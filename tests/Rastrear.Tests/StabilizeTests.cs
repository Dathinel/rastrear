using Rodaje.Analysis.Video;
using Rastrear.Core.Stabilization;
using Rastrear.Core.Video;
using Xunit.Abstractions;

namespace Rastrear.Tests;

public sealed class StabilizeTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Synthetic_shake_motion_variance_drops_by_at_least_50_percent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "RastrearTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string shakyPath = Path.Combine(tempDir, "temblor_sintetico.mp4");
        string stabPath = Path.Combine(tempDir, "estabilizado.mp4");

        try
        {
            const int width = 320;
            const int height = 240;
            const int frames = 60;
            const double fps = 30.0;

            static (double Dx, double Dy) Jitter(int f)
            {
                // Temblor de alta frecuencia
                double jx = 10.0 * Math.Sin(f * 0.9);
                double jy = 8.0 * Math.Cos(f * 0.75);
                return (jx, jy);
            }

            await TestMediaHelper.GenerateShakyVideoAsync(
                shakyPath,
                width,
                height,
                frames,
                fps,
                Jitter);

            StabilizationResult result = await StabilizeService.StabilizeAsync(
                shakyPath,
                stabPath,
                intensity: 0.8,
                autoCrop: true);

            // Medir la varianza del movimiento real decodificando ambos videos con MotionEstimator
            double varBefore = await MeasureMotionVarianceAsync(shakyPath, width, height);
            double varAfter = await MeasureMotionVarianceAsync(stabPath, width, height);

            double drop = 1.0 - (varAfter / varBefore);
            double shakeDrop = 1.0 - (result.ShakeAfter / result.ShakeBefore);

            output.WriteLine($"Estabilización de clip sintético:");
            output.WriteLine($"  Varianza de desplazamiento: {varBefore:F4} → {varAfter:F4} (reducción: {drop:P1})");
            output.WriteLine($"  Temblor CameraPath: {result.ShakeBefore:F4} → {result.ShakeAfter:F4} (reducción: {shakeDrop:P1})");
            output.WriteLine($"  Zoom aplicado por autoCrop: ×{result.Zoom:F3}");

            Assert.True(File.Exists(stabPath), "El archivo estabilizado debe haberse creado.");
            Assert.True(drop >= 0.50, $"La varianza del movimiento debe bajar al menos un 50 % (bajó {drop:P1}).");
            Assert.True(shakeDrop >= 0.50, $"El temblor de CameraPath debe bajar al menos un 50 % (bajó {shakeDrop:P1}).");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task<double> MeasureMotionVarianceAsync(string videoPath, int width, int height)
    {
        string ffmpeg = TestMediaHelper.FindFfmpeg();
        using var decoder = FfmpegTool.StartDecoder(videoPath, ffmpeg);
        Stream stdout = decoder.StandardOutput.BaseStream;

        var estimator = new MotionEstimator(width, height);
        byte[] bgra = new byte[width * height * 4];
        byte[] gray = new byte[width * height];

        while (await FfmpegTool.ReadExactBytesAsync(stdout, bgra, 0, bgra.Length, CancellationToken.None))
        {
            FfmpegTool.BgraToGray8(bgra, gray, width, height);
            estimator.Push(new LumaFrame(gray, width, height, width, LumaFormat.Gray8));
        }

        // Varianza de las traslaciones entre cuadros sucesivos
        List<double> dx = [];
        List<double> dy = [];

        foreach (FrameMotion m in estimator.Motions.Skip(1))
        {
            if (m.Motion.IsFinite)
            {
                dx.Add(m.Motion.X);
                dy.Add(m.Motion.Y);
            }
        }

        if (dx.Count < 2) return 0;

        double meanX = dx.Average();
        double meanY = dy.Average();
        double varX = dx.Select(x => Math.Pow(x - meanX, 2)).Average();
        double varY = dy.Select(y => Math.Pow(y - meanY, 2)).Average();

        return varX + varY;
    }
}
