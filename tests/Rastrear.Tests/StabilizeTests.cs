using Rodaje.Analysis.Video;
using Rastrear.Core;
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

    [Fact]
    public void Identity_input_yields_exact_identity_corrections()
    {
        Similarity[] motions = Enumerable.Repeat(Similarity.Identity, 40).ToArray();

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(0.8, AutoCrop: true));

        Assert.Equal(40, r.Corrections.Count);
        Assert.All(r.Corrections, c => Assert.Equal(Similarity.Identity, c));
        Assert.Equal(1.0, r.Zoom);
        Assert.Equal(0.0, r.ShakeBefore);
        Assert.Equal(0.0, r.ShakeAfter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Fewer_than_three_frames_never_corrects(int frames)
    {
        Similarity[] motions = Enumerable.Repeat(new Similarity(1, 0, 0.05, -0.03), frames).ToArray();

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(1.0, AutoCrop: true));

        Assert.Equal(frames, r.Corrections.Count);
        Assert.All(r.Corrections, c => Assert.Equal(Similarity.Identity, c));
        Assert.Equal(1.0, r.Zoom);
        Assert.Equal(0.0, r.ShakeBefore);
        Assert.Equal(0.0, r.ShakeAfter);
    }

    [Fact]
    public void Uniform_pan_is_not_treated_as_shake()
    {
        // Panorámica constante: la segunda diferencia es cero; el suavizado conserva rectas.
        var motions = new Similarity[60];
        for (int i = 0; i < motions.Length; i++)
        {
            motions[i] = new Similarity(1, 0, 0.005, 0.0);
        }

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(1.0, AutoCrop: true));

        Assert.Equal(0.0, r.ShakeBefore, 10);
        // Regresión de Rodaje #497. Antes, con el núcleo gaussiano más ancho que la señal (radio 90 en 60 cuadros) la reflexión
        // impar de Smooth recorta el índice espejo a n-1; la rampa se curva en los extremos, salen
        // correcciones que no son cero y el temblor sube (4,5e-6) en vez de «quedar igual» (CameraPath).
        Assert.True(r.ShakeAfter <= r.ShakeBefore + 1e-10, $"El temblor subió: {r.ShakeAfter} > {r.ShakeBefore}");
        Assert.Equal(1.0, r.Zoom, 6);
        Assert.All(r.Corrections, c =>
        {
            Assert.True(c.IsFinite);
            Assert.Equal(0.0, c.X, 6);
            Assert.Equal(0.0, c.Y, 6);
        });
    }

    [Fact]
    public void Sudden_jump_stays_finite_and_zoom_is_capped()
    {
        var motions = new Similarity[60];
        for (int i = 0; i < motions.Length; i++)
        {
            motions[i] = Similarity.Identity;
        }
        motions[20] = new Similarity(1, 0, 0.4, 0.0); // salto brusco del 40 % del alto

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(0.9, AutoCrop: true, MaxZoom: 1.5));

        Assert.All(r.Corrections, c => Assert.True(c.IsFinite, $"Corrección no finita: {c}"));
        Assert.InRange(r.Zoom, 1.0, 1.5);
        Assert.True(r.ShakeBefore > 0);
        Assert.True(r.ShakeAfter <= r.ShakeBefore, $"El temblor subió: {r.ShakeAfter} > {r.ShakeBefore}");
    }

    [Fact]
    public void Smoothing_window_larger_than_frame_count_still_works()
    {
        // 5 cuadros a 30 fps con intensidad 1: la ventana gaussiana (σ = 30 cuadros) es mucho mayor que la serie.
        var motions = new Similarity[5];
        for (int i = 0; i < motions.Length; i++)
        {
            motions[i] = new Similarity(1, 0, 0.01 * (i % 2 == 0 ? 1 : -1), 0.002);
        }

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(1.0, AutoCrop: true));

        Assert.Equal(5, r.Corrections.Count);
        Assert.All(r.Corrections, c => Assert.True(c.IsFinite, $"Corrección no finita: {c}"));
        Assert.True(double.IsFinite(r.Zoom) && r.Zoom >= 1.0);
        Assert.True(double.IsFinite(r.ShakeBefore) && double.IsFinite(r.ShakeAfter));
    }

    [Fact]
    public void Non_finite_motion_samples_are_treated_as_identity()
    {
        var motions = new Similarity[40];
        for (int i = 0; i < motions.Length; i++)
        {
            motions[i] = Similarity.Identity;
        }
        motions[10] = new Similarity(double.NaN, 0, 0, 0);
        motions[11] = new Similarity(1, 0, double.PositiveInfinity, 0);
        motions[12] = new Similarity(0, 0, 0, 0); // escala cero no es una similitud válida

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(1.0, AutoCrop: true));

        Assert.All(r.Corrections, c => Assert.True(c.IsFinite, $"Corrección no finita: {c}"));
        Assert.Equal(0.0, r.ShakeBefore);
        Assert.Equal(1.0, r.Zoom, 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public async Task Intensity_outside_0_1_fails_clear_before_touching_the_video(double intensity)
    {
        // La Cli acepta «--intensidad NaN»: se rechaza aquí, antes de buscar ffmpeg o abrir el video
        RastrearUserException ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => StabilizeService.StabilizeAsync("no-existe.mp4", "salida.mp4", intensity));
        Assert.Contains("intensidad", ex.Message);
    }

    [Fact]
    public void Nan_intensity_does_not_corrupt_corrections()
    {
        var motions = new Similarity[60];
        for (int i = 0; i < motions.Length; i++)
        {
            motions[i] = new Similarity(1, 0, 0.01 * Math.Sin(i * 0.7), 0.008 * Math.Cos(i * 0.5));
        }

        StabilizationResult r = CameraPath.Stabilize(motions, 30.0, 16.0 / 9.0, new StabilizeOptions(double.NaN, AutoCrop: true));

        // Regresión de Rodaje #497. Antes, con intensidad NaN la sigma queda NaN, el núcleo gaussiano degenera y todas las
        // correcciones salen NaN (además ShakeAfter = NaN): el video estabilizado sale negro sin avisar.
        Assert.All(r.Corrections, c => Assert.True(c.IsFinite, $"Corrección no finita: {c}"));
        Assert.True(double.IsFinite(r.ShakeAfter), $"ShakeAfter no finito: {r.ShakeAfter}");
    }

    [Fact]
    public void Warp_identity_copies_every_pixel()
    {
        const int w = 32, h = 24;
        byte[] input = new byte[w * h * 4];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = (byte)(i % 251);
        }
        byte[] output = new byte[w * h * 4];

        StabilizeService.WarpFrame(input, output, w, h, Similarity.Identity);

        Assert.Equal(input, output);
    }

    [Fact]
    public void Warp_translation_moves_content_by_whole_pixels()
    {
        // Una corrección de +4 píxeles en X (en unidades del alto) desplaza el contenido a la derecha.
        const int w = 32, h = 24;
        byte[] input = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int o = ((y * w) + 5) * 4;
            input[o] = input[o + 1] = input[o + 2] = 200;
            input[o + 3] = 255;
        }
        byte[] output = new byte[w * h * 4];

        StabilizeService.WarpFrame(input, output, w, h, new Similarity(1, 0, 4.0 / h, 0));

        for (int y = 0; y < h; y++)
        {
            Assert.Equal(200, output[((y * w) + 9) * 4]); // el píxel de la columna 5 llegó a la 9
            Assert.Equal(255, output[((y * w) + 9) * 4 + 3]);
            Assert.Equal(0, output[((y * w) + 5) * 4]);   // donde estaba queda el contenido de la 1
        }
    }

    [Fact]
    public void Warp_extreme_correction_only_samples_inside()
    {
        const int w = 16, h = 16;
        byte[] input = new byte[w * h * 4];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = 77;
        }
        byte[] output = new byte[w * h * 4];

        // Giro de 90°, escala 2 y un desplazamiento de 10 altos: todo muestrea el borde, sin salirse.
        Similarity wild = Similarity.FromParts(Math.PI / 2, 2.0, 10.0, -7.0);
        StabilizeService.WarpFrame(input, output, w, h, wild);

        Assert.All(output, b => Assert.Equal(77, b));
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
