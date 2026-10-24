using System.Diagnostics;
using System.Globalization;
using Rastrear.Core.Video;

namespace Rastrear.Tests;

internal static class TestMediaHelper
{
    public static string FindFfmpeg() => FfmpegTool.FindFfmpeg();

    public static string FindEchoModule()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "Rodaje.Module.Echo.exe");
        if (File.Exists(local)) return local;

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string[] candidates =
            [
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Echo", "bin", "Release", "net10.0-windows", "win-x64", "Rodaje.Module.Echo.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Echo", "bin", "Debug", "net10.0-windows", "win-x64", "Rodaje.Module.Echo.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Echo", "bin", "Release", "net10.0-windows", "Rodaje.Module.Echo.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Echo", "bin", "Debug", "net10.0-windows", "Rodaje.Module.Echo.exe"),
            ];
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
        }

        throw new FileNotFoundException("No se encontró Rodaje.Module.Echo.exe.");
    }

    public static string FindVisionModule()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string[] candidates =
            [
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Release", "net10.0-windows", "win-x64", "publish", "Rodaje.Module.Vision.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Release", "net10.0-windows", "win-x64", "Rodaje.Module.Vision.exe"),
                Path.Combine(dir.FullName, "externo", "rodaje", "modules", "Rodaje.Module.Vision", "bin", "Debug", "net10.0-windows", "win-x64", "Rodaje.Module.Vision.exe"),
            ];
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return Path.GetFullPath(c);
            }
        }

        throw new FileNotFoundException("No se encontró Rodaje.Module.Vision.exe.");
    }

    public static string? FindEnvironment()
    {
        if (Environment.GetEnvironmentVariable("RODAJE_ENTORNO") is { Length: > 0 } env && Directory.Exists(env))
        {
            return env;
        }

        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, ".entorno");
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    public static bool HasVisionModels()
    {
        string? env = FindEnvironment();
        if (env is null) return false;
        string modelsDir = Path.Combine(env, "modelos");
        return File.Exists(Path.Combine(modelsDir, "modnet.onnx")) &&
               File.Exists(Path.Combine(modelsDir, "face_detection_yunet_2023mar.onnx"));
    }

    public static bool IsLongTestsEnabled() =>
        Environment.GetEnvironmentVariable("RODAJE_PRUEBAS_LARGAS") == "1";

    /// <summary>
    /// Genera un archivo de video MP4 con un círculo blanco moviéndose sobre fondo oscuro
    /// con una trayectoria analítica conocida.
    /// </summary>
    public static async Task GenerateMovingCircleVideoAsync(
        string outputPath,
        int width,
        int height,
        int frames,
        double fps,
        Func<int, (double X, double Y)> getCenter,
        double radius = 16.0)
    {
        string ffmpeg = FindFfmpeg();
        using Process encoder = FfmpegTool.StartEncoder(outputPath, width, height, fps, OutputVideoFormat.H264Mp4, ffmpeg);
        Stream stream = encoder.StandardInput.BaseStream;

        byte[] frame = new byte[width * height * 4];
        double rSq = radius * radius;

        for (int f = 0; f < frames; f++)
        {
            (double cx, double cy) = getCenter(f);
            Array.Clear(frame);

            // Fondo con textura suave para que el rastreador tenga buen contraste y gradiente
            for (int y = 0; y < height; y++)
            {
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int o = row + (x * 4);
                    byte bg = (byte)(16 + ((x ^ y) & 15));
                    frame[o] = bg;
                    frame[o + 1] = bg;
                    frame[o + 2] = bg;
                    frame[o + 3] = 255;
                }
            }

            // Dibujar círculo blanco con antialiasing básico
            int minX = Math.Max(0, (int)(cx - radius - 2));
            int maxX = Math.Min(width - 1, (int)(cx + radius + 2));
            int minY = Math.Max(0, (int)(cy - radius - 2));
            int maxY = Math.Min(height - 1, (int)(cy + radius + 2));

            for (int y = minY; y <= maxY; y++)
            {
                int row = y * width * 4;
                for (int x = minX; x <= maxX; x++)
                {
                    double dSq = Math.Pow(x - cx, 2) + Math.Pow(y - cy, 2);
                    if (dSq <= rSq)
                    {
                        int o = row + (x * 4);
                        frame[o] = 240;
                        frame[o + 1] = 240;
                        frame[o + 2] = 240;
                    }
                    else if (dSq <= Math.Pow(radius + 1.0, 2))
                    {
                        double alpha = Math.Sqrt(dSq) - radius;
                        byte val = (byte)(240 - (int)(180 * alpha));
                        int o = row + (x * 4);
                        frame[o] = val;
                        frame[o + 1] = val;
                        frame[o + 2] = val;
                    }
                }
            }

            await stream.WriteAsync(frame);
        }

        await stream.FlushAsync();
        encoder.StandardInput.Close();
        await encoder.WaitForExitAsync();
    }

    /// <summary>
    /// Genera un archivo de video MP4 con temblor de cámara conocido sobre un fondo con patrones.
    /// </summary>
    public static async Task GenerateShakyVideoAsync(
        string outputPath,
        int width,
        int height,
        int frames,
        double fps,
        Func<int, (double Dx, double Dy)> getJitter)
    {
        string ffmpeg = FindFfmpeg();
        using Process encoder = FfmpegTool.StartEncoder(outputPath, width, height, fps, OutputVideoFormat.H264Mp4, ffmpeg);
        Stream stream = encoder.StandardInput.BaseStream;

        byte[] frame = new byte[width * height * 4];

        for (int f = 0; f < frames; f++)
        {
            (double jx, double jy) = getJitter(f);

            for (int y = 0; y < height; y++)
            {
                double sy = y - jy;
                int row = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    double sx = x - jx;
                    int o = row + (x * 4);

                    // Rejilla de cuadrados y patrones con altas frecuencias espaciales
                    int cellX = (int)Math.Floor(sx / 24.0);
                    int cellY = (int)Math.Floor(sy / 24.0);
                    bool check = ((cellX + cellY) & 1) == 0;

                    int pattern = (int)(Math.Sin(sx * 0.2) * Math.Cos(sy * 0.2) * 50);
                    byte val = check ? (byte)Math.Clamp(180 + pattern, 0, 255) : (byte)Math.Clamp(60 + pattern, 0, 255);

                    frame[o] = val;
                    frame[o + 1] = val;
                    frame[o + 2] = val;
                    frame[o + 3] = 255;
                }
            }

            await stream.WriteAsync(frame);
        }

        await stream.FlushAsync();
        encoder.StandardInput.Close();
        await encoder.WaitForExitAsync();
    }

    public static double ComputeIou(ReadOnlySpan<byte> maskA, ReadOnlySpan<byte> maskB)
    {
        long inter = 0;
        long union = 0;
        int len = Math.Min(maskA.Length, maskB.Length);

        for (int i = 0; i < len; i++)
        {
            bool a = maskA[i] >= 128;
            bool b = maskB[i] >= 128;
            if (a && b) inter++;
            if (a || b) union++;
        }

        return union == 0 ? 1.0 : (double)inter / union;
    }
}
