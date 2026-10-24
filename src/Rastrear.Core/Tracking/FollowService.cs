using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Rodaje.Analysis.Video;
using Rastrear.Core.Model;
using Rastrear.Core.Video;

namespace Rastrear.Core.Tracking;

public readonly record struct PointTargetSpec(double X, double Y);

public readonly record struct RegionTargetSpec(double X, double Y, double W, double H);

public static class FollowService
{
    public static async Task<FollowResult> FollowAsync(
        string videoPath,
        PointTargetSpec? point,
        RegionTargetSpec? region,
        int patternSize = 41,
        int searchSize = 121,
        float threshold = 0.7f,
        string? ffmpegPath = null,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (point is null && region is null)
        {
            throw new RastrearUserException("Debe especificar un punto (--punto x,y) o una región (--region x,y,w,h) a seguir.");
        }

        string ffmpeg = FfmpegTool.FindFfmpeg(ffmpegPath);
        VideoInfo info = await FfmpegTool.ProbeAsync(videoPath, ffmpeg, cancellationToken).ConfigureAwait(false);

        using Process decoder = FfmpegTool.StartDecoder(videoPath, ffmpeg);
        try
        {
            Stream stdout = decoder.StandardOutput.BaseStream;
            byte[] bgraFrame = new byte[info.FrameBytesBgra];
            byte[] grayFrame = new byte[info.Width * info.Height];

            List<FollowFrame> frames = [];
            int frameIdx = 0;

            PointTracker? pointTracker = null;
            RegionTracker? regionTracker = null;
            Vector2 initialCenter = Vector2.Zero;

            while (await FfmpegTool.ReadExactBytesAsync(stdout, bgraFrame, 0, bgraFrame.Length, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                double t = frameIdx / info.Fps;
                FfmpegTool.BgraToGray8(bgraFrame, grayFrame, info.Width, info.Height);
                LumaFrame luma = new(grayFrame, info.Width, info.Height, info.Width, LumaFormat.Gray8);

                if (frameIdx == 0)
                {
                    if (point is { } p)
                    {
                        pointTracker = new PointTracker(info.Width, info.Height, threshold);
                        PointTarget target = new(new Vector2((float)p.X, (float)p.Y), new Vector2(patternSize), new Vector2(searchSize));
                        pointTracker.Start(luma, [target]);
                        frames.Add(new FollowFrame(0, 0.0, p.X, p.Y, 1.0, "Good"));
                    }
                    else if (region is { } r)
                    {
                        regionTracker = new RegionTracker(info.Width, info.Height);
                        Vector2[] corners =
                        [
                            new((float)r.X, (float)r.Y),
                            new((float)(r.X + r.W), (float)r.Y),
                            new((float)(r.X + r.W), (float)(r.Y + r.H)),
                            new((float)r.X, (float)(r.Y + r.H)),
                        ];
                        initialCenter = new Vector2((float)(r.X + (r.W / 2.0)), (float)(r.Y + (r.H / 2.0)));
                        regionTracker.Start(luma, corners);
                        frames.Add(new FollowFrame(0, 0.0, initialCenter.X, initialCenter.Y, 1.0, "Good"));
                    }
                }
                else
                {
                    if (pointTracker is not null)
                    {
                        PointResult[] results = pointTracker.Step(luma);
                        PointResult res = results[0];
                        frames.Add(new FollowFrame(frameIdx, t, res.Position.X, res.Position.Y, res.Confidence, res.State.ToString()));
                    }
                    else if (regionTracker is not null)
                    {
                        RegionStep step = regionTracker.Step(luma);
                        (double cx, double cy) = step.Motion.Apply(initialCenter.X, initialCenter.Y);
                        double conf = step.Lost ? 0.0 : Math.Clamp(step.Inliers / 24.0, 0.3, 1.0);
                        string state = step.Lost ? "Lost" : "Good";
                        frames.Add(new FollowFrame(frameIdx, t, cx, cy, conf, state));
                    }
                }

                frameIdx++;
                if (info.EstimatedFrames > 0)
                {
                    progress?.Report(new VideoProgress(Math.Clamp((double)frameIdx / info.EstimatedFrames, 0, 1), "Siguiendo"));
                }
            }

            return new FollowResult(info.Width, info.Height, info.Fps, frames);
        }
        finally
        {
            if (!decoder.HasExited)
            {
                try { decoder.Kill(entireProcessTree: true); } catch { }
            }
        }
    }

    public static void WriteCsv(FollowResult result, TextWriter writer)
    {
        writer.WriteLine("cuadro,tiempo,x,y,confianza,estado");
        foreach (FollowFrame f in result.Cuadros)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1:0.####},{2:0.###},{3:0.###},{4:0.###},{5}",
                f.Cuadro, f.Tiempo, f.X, f.Y, f.Confianza, f.Estado));
        }
    }

    public static void WriteJson(FollowResult result, TextWriter writer)
    {
        string json = JsonSerializer.Serialize(result, RastrearJsonContext.Default.FollowResult);
        writer.Write(json);
    }
}
