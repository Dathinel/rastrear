using System.Diagnostics;
using System.Runtime.Versioning;
using Rodaje.Analysis.Video;
using Rastrear.Core.Model;
using Rastrear.Core.Video;

namespace Rastrear.Core.Stabilization;

public static class StabilizeService
{
    public static async Task<StabilizationResult> StabilizeAsync(
        string inputVideo,
        string outputVideo,
        double intensity = 0.5,
        bool autoCrop = true,
        string? ffmpegPath = null,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string ffmpeg = FfmpegTool.FindFfmpeg(ffmpegPath);
        VideoInfo info = await FfmpegTool.ProbeAsync(inputVideo, ffmpeg, cancellationToken).ConfigureAwait(false);

        // Pase 1: Análisis de movimiento
        var estimator = new MotionEstimator(info.Width, info.Height);
        byte[] bgraFrame = new byte[info.FrameBytesBgra];
        byte[] grayFrame = new byte[info.Width * info.Height];

        using (Process pass1Decoder = FfmpegTool.StartDecoder(inputVideo, ffmpeg))
        {
            try
            {
                Stream stdout = pass1Decoder.StandardOutput.BaseStream;
                int frameIdx = 0;
                while (await FfmpegTool.ReadExactBytesAsync(stdout, bgraFrame, 0, bgraFrame.Length, cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FfmpegTool.BgraToGray8(bgraFrame, grayFrame, info.Width, info.Height);
                    estimator.Push(new LumaFrame(grayFrame, info.Width, info.Height, info.Width, LumaFormat.Gray8));
                    frameIdx++;
                    if (info.EstimatedFrames > 0)
                    {
                        double frac = Math.Clamp((double)frameIdx / (info.EstimatedFrames * 2), 0, 0.5);
                        progress?.Report(new VideoProgress(frac, "Analizando movimiento"));
                    }
                }
            }
            finally
            {
                if (!pass1Decoder.HasExited)
                {
                    try { pass1Decoder.Kill(entireProcessTree: true); } catch { }
                }
            }
        }

        int totalFrames = estimator.Motions.Count;
        if (totalFrames == 0)
        {
            throw new RastrearUserException("El video de entrada no contiene cuadros para estabilizar.");
        }

        StabilizationResult result = CameraPath.Stabilize(
            [.. estimator.Motions.Select(m => m.Motion)],
            info.Fps,
            info.Width / (double)info.Height,
            new StabilizeOptions(intensity, autoCrop));

        // Asegurar que el directorio de salida existe
        string? outDir = Path.GetDirectoryName(outputVideo);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        // Pase 2: Deformar cuadros y codificar
        using Process pass2Decoder = FfmpegTool.StartDecoder(inputVideo, ffmpeg);
        using Process encoder = FfmpegTool.StartEncoder(outputVideo, info.Width, info.Height, info.Fps, OutputVideoFormat.H264Mp4, ffmpeg);

        try
        {
            Stream decodeStream = pass2Decoder.StandardOutput.BaseStream;
            Stream encodeStream = encoder.StandardInput.BaseStream;
            byte[] warpedBgra = new byte[info.FrameBytesBgra];

            int frameIdx = 0;
            while (await FfmpegTool.ReadExactBytesAsync(decodeStream, bgraFrame, 0, bgraFrame.Length, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Similarity correction = frameIdx < result.Corrections.Count ? result.Corrections[frameIdx] : Similarity.Identity;

                WarpFrame(bgraFrame, warpedBgra, info.Width, info.Height, correction);

                await encodeStream.WriteAsync(warpedBgra, cancellationToken).ConfigureAwait(false);

                frameIdx++;
                double frac = 0.5 + Math.Clamp((double)frameIdx / (totalFrames * 2), 0, 0.5);
                progress?.Report(new VideoProgress(frac, "Estabilizando"));
            }

            await encodeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            encoder.StandardInput.Close();

            await encoder.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (encoder.ExitCode != 0)
            {
                string err = await encoder.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                throw new RastrearModuleException($"FFmpeg falló al codificar el video estabilizado ({encoder.ExitCode}): {err}");
            }

            return result;
        }
        finally
        {
            if (!pass2Decoder.HasExited)
            {
                try { pass2Decoder.Kill(entireProcessTree: true); } catch { }
            }
            if (!encoder.HasExited)
            {
                try { encoder.Kill(entireProcessTree: true); } catch { }
            }
        }
    }

    public static void WarpFrame(byte[] input, byte[] output, int width, int height, Similarity correction)
    {
        Similarity inv = correction.Inverse();
        double halfW = width / 2.0;
        double halfH = height / 2.0;
        double invH = 1.0 / height;

        Parallel.For(0, height, y =>
        {
            double yn = (y - halfH) * invH;
            int rowOffset = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                double xn = (x - halfW) * invH;
                (double origXn, double origYn) = inv.Apply(xn, yn);
                double srcX = (origXn * height) + halfW;
                double srcY = (origYn * height) + halfH;

                int x0 = (int)Math.Floor(srcX);
                int y0 = (int)Math.Floor(srcY);
                int x1 = x0 + 1;
                int y1 = y0 + 1;

                double fx = srcX - x0;
                double fy = srcY - y0;

                x0 = Math.Clamp(x0, 0, width - 1);
                x1 = Math.Clamp(x1, 0, width - 1);
                y0 = Math.Clamp(y0, 0, height - 1);
                y1 = Math.Clamp(y1, 0, height - 1);

                int idx00 = (y0 * width + x0) * 4;
                int idx10 = (y0 * width + x1) * 4;
                int idx01 = (y1 * width + x0) * 4;
                int idx11 = (y1 * width + x1) * 4;

                double w00 = (1.0 - fx) * (1.0 - fy);
                double w10 = fx * (1.0 - fy);
                double w01 = (1.0 - fx) * fy;
                double w11 = fx * fy;

                int outIdx = rowOffset + (x * 4);
                output[outIdx] = (byte)Math.Clamp((int)Math.Round((input[idx00] * w00) + (input[idx10] * w10) + (input[idx01] * w01) + (input[idx11] * w11)), 0, 255);
                output[outIdx + 1] = (byte)Math.Clamp((int)Math.Round((input[idx00 + 1] * w00) + (input[idx10 + 1] * w10) + (input[idx01 + 1] * w01) + (input[idx11 + 1] * w11)), 0, 255);
                output[outIdx + 2] = (byte)Math.Clamp((int)Math.Round((input[idx00 + 2] * w00) + (input[idx10 + 2] * w10) + (input[idx01 + 2] * w01) + (input[idx11 + 2] * w11)), 0, 255);
                output[outIdx + 3] = (byte)Math.Clamp((int)Math.Round((input[idx00 + 3] * w00) + (input[idx10 + 3] * w10) + (input[idx01 + 3] * w01) + (input[idx11 + 3] * w11)), 0, 255);
            }
        });
    }
}
