using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Rastrear.Core.Model;

namespace Rastrear.Core.Video;

public enum OutputVideoFormat
{
    H264Mp4,
    Vp9AlphaWebm,
    ProRes4444Mov,
    GrayMp4,
}

public static class FfmpegTool
{
    public static string FindFfmpeg(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            if (File.Exists(customPath))
            {
                return Path.GetFullPath(customPath);
            }
            if (Directory.Exists(customPath))
            {
                string inDir = Path.Combine(customPath, "ffmpeg.exe");
                if (File.Exists(inDir))
                {
                    return Path.GetFullPath(inDir);
                }
            }
            throw new RastrearUserException($"No se encontró el ejecutable de FFmpeg en: {customPath}");
        }

        // Variable RODAJE_ENTORNO
        if (Environment.GetEnvironmentVariable("RODAJE_ENTORNO") is { Length: > 0 } env && Directory.Exists(env))
        {
            string[] candidates =
            [
                Path.Combine(env, "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(env, "ffmpeg-lgpl", "bin", "ffmpeg.exe"),
                Path.Combine(env, "ffmpeg-gpl", "bin", "ffmpeg.exe"),
                Path.Combine(env, "bin", "ffmpeg.exe"),
            ];
            foreach (string c in candidates)
            {
                if (File.Exists(c))
                {
                    return Path.GetFullPath(c);
                }
            }
        }

        // Directorios superiores (.entorno/ffmpeg/bin/ffmpeg.exe)
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, ".entorno", "ffmpeg", "bin", "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        // PATH
        if (Environment.GetEnvironmentVariable("PATH") is { Length: > 0 } pathVar)
        {
            string[] paths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in paths)
            {
                string candidate = Path.Combine(p.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new RastrearUserException("No se encontró ffmpeg.exe. Pasa --ffmpeg <ruta>, define la variable RODAJE_ENTORNO o agrégalo al PATH.");
    }

    public static string FindFfprobe(string? ffmpegPath = null)
    {
        string ffmpeg = FindFfmpeg(ffmpegPath);
        string? dir = Path.GetDirectoryName(ffmpeg);
        if (!string.IsNullOrEmpty(dir))
        {
            string ffprobe = Path.Combine(dir, "ffprobe.exe");
            if (File.Exists(ffprobe))
            {
                return ffprobe;
            }
        }

        // Buscar ffprobe en PATH
        if (Environment.GetEnvironmentVariable("PATH") is { Length: > 0 } pathVar)
        {
            string[] paths = pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in paths)
            {
                string candidate = Path.Combine(p.Trim(), "ffprobe.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new RastrearUserException("No se encontró ffprobe.exe junto a ffmpeg ni en el PATH.");
    }

    public static async Task<VideoInfo> ProbeAsync(string videoPath, string? ffmpegPath = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoPath))
        {
            throw new RastrearUserException($"No se encontró el archivo de video: {videoPath}");
        }

        string ffprobe = FindFfprobe(ffmpegPath);
        ProcessStartInfo startInfo = new(ffprobe)
        {
            Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,avg_frame_rate,nb_frames,duration:format=duration -of json \"{videoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new RastrearModuleException($"Error al iniciar ffprobe ({ffprobe}): {ex.Message}", ex);
        }

        string json = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new RastrearModuleException($"ffprobe terminó con error ({process.ExitCode}): {stderr}");
        }

        FfprobeOutput? probe;
        try
        {
            probe = JsonSerializer.Deserialize(json, RastrearJsonContext.Default.FfprobeOutput);
        }
        catch (Exception ex)
        {
            throw new RastrearModuleException($"Error al analizar la información del video de ffprobe: {ex.Message}", ex);
        }

        FfprobeStream? stream = probe?.Streams?.Count > 0 ? probe.Streams[0] : null;
        if (stream is null || stream.Width is null or <= 0 || stream.Height is null or <= 0)
        {
            throw new RastrearUserException($"El archivo «{Path.GetFileName(videoPath)}» no contiene un flujo de video válido.");
        }

        int width = stream.Width.Value;
        int height = stream.Height.Value;

        double fps = 30.0;
        string? rateStr = !string.IsNullOrEmpty(stream.AvgFrameRate) && stream.AvgFrameRate != "0/0"
            ? stream.AvgFrameRate
            : stream.RFrameRate;

        if (!string.IsNullOrEmpty(rateStr))
        {
            string[] parts = rateStr.Split('/');
            if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out double num) && double.TryParse(parts[1], CultureInfo.InvariantCulture, out double den) && den > 0)
            {
                fps = num / den;
            }
            else if (double.TryParse(rateStr, CultureInfo.InvariantCulture, out double singleRate) && singleRate > 0)
            {
                fps = singleRate;
            }
        }

        if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps))
        {
            fps = 30.0;
        }

        double duration = 0;
        if (!string.IsNullOrEmpty(stream.Duration) && double.TryParse(stream.Duration, CultureInfo.InvariantCulture, out double dStream) && dStream > 0)
        {
            duration = dStream;
        }
        else if (probe?.Format?.Duration is { Length: > 0 } dFmtStr && double.TryParse(dFmtStr, CultureInfo.InvariantCulture, out double dFmt) && dFmt > 0)
        {
            duration = dFmt;
        }

        int frames = 0;
        if (!string.IsNullOrEmpty(stream.NbFrames) && int.TryParse(stream.NbFrames, CultureInfo.InvariantCulture, out int nb) && nb > 0)
        {
            frames = nb;
        }
        else if (duration > 0)
        {
            frames = (int)Math.Round(duration * fps);
        }

        return new VideoInfo(width, height, fps, duration, frames);
    }

    public static Process StartDecoder(string videoPath, string ffmpegPath)
    {
        ProcessStartInfo startInfo = new(ffmpegPath)
        {
            Arguments = $"-hide_banner -loglevel error -i \"{videoPath}\" -f rawvideo -pix_fmt bgra pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
            return process;
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new RastrearModuleException($"Error al iniciar la decodificación con FFmpeg ({ffmpegPath}): {ex.Message}", ex);
        }
    }

    public static Process StartEncoder(string outputPath, int width, int height, double fps, OutputVideoFormat format, string ffmpegPath)
    {
        string fpsStr = fps.ToString("0.###", CultureInfo.InvariantCulture);
        string codecArgs = format switch
        {
            OutputVideoFormat.H264Mp4 => "-c:v h264_mf -pix_fmt yuv420p",
            OutputVideoFormat.Vp9AlphaWebm => "-c:v libvpx-vp9 -pix_fmt yuva420p",
            OutputVideoFormat.ProRes4444Mov => "-c:v prores_ks -profile:v 4 -pix_fmt yuva444p10le",
            OutputVideoFormat.GrayMp4 => "-c:v h264_mf -pix_fmt yuv420p",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

        ProcessStartInfo startInfo = new(ffmpegPath)
        {
            Arguments = $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgra -s {width}x{height} -r {fpsStr} -i pipe:0 {codecArgs} \"{outputPath}\"",
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
            return process;
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new RastrearModuleException($"Error al iniciar la codificación con FFmpeg ({ffmpegPath}): {ex.Message}", ex);
        }
    }

    public static void BgraToGray8(ReadOnlySpan<byte> bgra, Span<byte> gray, int width, int height)
    {
        int pixelCount = width * height;
        for (int i = 0; i < pixelCount; i++)
        {
            int offset = i * 4;
            byte b = bgra[offset];
            byte g = bgra[offset + 1];
            byte r = bgra[offset + 2];
            gray[i] = (byte)((r * 77 + g * 150 + b * 29 + 128) >> 8);
        }
    }

    public static async Task<bool> ReadExactBytesAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (totalRead == 0)
                {
                    return false; // Fin de flujo limpio entre cuadros
                }
                throw new EndOfStreamException($"Se leyeron solo {totalRead} de los {count} bytes esperados del cuadro.");
            }
            totalRead += read;
        }
        return true;
    }
}
