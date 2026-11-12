using System.Globalization;
using System.Text.Json;
using Rastrear.Core;
using Rastrear.Core.Model;
using Rastrear.Core.Tracking;
using Rastrear.Core.Ui;
using Rastrear.Core.Vision;
using Xunit.Abstractions;

namespace Rastrear.Tests;

public sealed class FollowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Point_tracking_mean_error_is_under_2_pixels()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "RastrearTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string videoPath = Path.Combine(tempDir, "circulo_movil.mp4");

        try
        {
            const int width = 320;
            const int height = 240;
            const int frames = 30;
            const double fps = 30.0;

            static (double X, double Y) TrueCenter(int f) => (60.0 + (3.0 * f), 50.0 + (2.0 * f));

            await TestMediaHelper.GenerateMovingCircleVideoAsync(
                videoPath,
                width,
                height,
                frames,
                fps,
                TrueCenter,
                radius: 16.0);

            (double startX, double startY) = TrueCenter(0);
            FollowResult result = await FollowService.FollowAsync(
                videoPath,
                point: new PointTargetSpec(startX, startY),
                region: null);

            Assert.Equal(width, result.Ancho);
            Assert.Equal(height, result.Alto);
            Assert.True(result.Cuadros.Count >= frames - 2, $"Se esperaban al menos {frames - 2} cuadros, se obtuvieron {result.Cuadros.Count}");

            List<double> errors = [];
            for (int i = 0; i < result.Cuadros.Count; i++)
            {
                FollowFrame f = result.Cuadros[i];
                (double tx, double ty) = TrueCenter(f.Cuadro);
                double err = Math.Sqrt(Math.Pow(f.X - tx, 2) + Math.Pow(f.Y - ty, 2));
                errors.Add(err);
            }

            double meanError = errors.Average();
            double maxError = errors.Max();

            output.WriteLine($"Seguimiento de punto sintético: error medio = {meanError:F3} px, error máximo = {maxError:F3} px (en {errors.Count} cuadros)");
            Assert.True(meanError < 2.0, $"El error medio ({meanError:F3} px) debe ser menor a 2.0 px.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Region_tracking_follows_moving_region()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "RastrearTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string videoPath = Path.Combine(tempDir, "region_movil.mp4");

        try
        {
            const int width = 320;
            const int height = 240;
            const int frames = 20;
            const double fps = 25.0;

            static (double X, double Y) TrueCenter(int f) => (80.0 + (2.5 * f), 60.0 + (1.5 * f));

            await TestMediaHelper.GenerateMovingCircleVideoAsync(
                videoPath,
                width,
                height,
                frames,
                fps,
                TrueCenter,
                radius: 20.0);

            (double cx0, double cy0) = TrueCenter(0);
            const double w = 40.0;
            const double h = 40.0;

            FollowResult result = await FollowService.FollowAsync(
                videoPath,
                point: null,
                region: new RegionTargetSpec(cx0 - (w / 2.0), cy0 - (h / 2.0), w, h));

            Assert.NotEmpty(result.Cuadros);
            FollowFrame last = result.Cuadros[^1];
            (double txLast, double tyLast) = TrueCenter(last.Cuadro);
            double errLast = Math.Sqrt(Math.Pow(last.X - txLast, 2) + Math.Pow(last.Y - tyLast, 2));

            output.WriteLine($"Seguimiento de región: error en último cuadro = {errLast:F3} px");
            Assert.True(errLast < 5.0, $"El error en el último cuadro ({errLast:F3} px) excede lo esperado.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Csv_and_json_writers_work_properly()
    {
        FollowFrame[] sampleFrames =
        [
            new(0, 0.0, 100.25, 200.5, 1.0, "Good"),
            new(1, 0.033, 103.5, 202.1, 0.98, "Good"),
        ];
        FollowResult result = new(1920, 1080, 30.0, sampleFrames);

        using StringWriter csvWriter = new();
        FollowService.WriteCsv(result, csvWriter);
        string csv = csvWriter.ToString();

        Assert.Contains("cuadro,tiempo,x,y,confianza,estado", csv);
        Assert.Contains("0,0,100.25,200.5,1,Good", csv);

        using StringWriter jsonWriter = new();
        FollowService.WriteJson(result, jsonWriter);
        string json = jsonWriter.ToString();

        FollowResult? roundtrip = JsonSerializer.Deserialize(json, RastrearJsonContext.Default.FollowResult);
        Assert.NotNull(roundtrip);
        Assert.Equal(1920, roundtrip.Ancho);
        Assert.Equal(2, roundtrip.Cuadros.Count);
        Assert.Equal(100.25, roundtrip.Cuadros[0].X);
    }

    [Fact]
    public void InterpolateKeyframe_empty_single_and_bounds()
    {
        Assert.Equal((0.5, 0.5), VisionService.InterpolateKeyframe([], 3.0));

        ReframeKeyframe[] single = [new(2.0, 0.2, 0.8, 1.5)];
        Assert.Equal((0.2, 0.8), VisionService.InterpolateKeyframe(single, 0.0));
        Assert.Equal((0.2, 0.8), VisionService.InterpolateKeyframe(single, 2.0));
        Assert.Equal((0.2, 0.8), VisionService.InterpolateKeyframe(single, 99.0));

        ReframeKeyframe[] keys =
        [
            new(0.0, 0.1, 0.2, 1.0),
            new(1.0, 0.5, 0.4, 1.0),
            new(3.0, 0.9, 0.8, 1.0),
        ];

        // Antes del primero y después del último se queda en el extremo
        Assert.Equal((0.1, 0.2), VisionService.InterpolateKeyframe(keys, -5.0));
        Assert.Equal((0.1, 0.2), VisionService.InterpolateKeyframe(keys, 0.0));
        Assert.Equal((0.9, 0.8), VisionService.InterpolateKeyframe(keys, 3.0));
        Assert.Equal((0.9, 0.8), VisionService.InterpolateKeyframe(keys, 99.0));

        // En un keyframe exacto y en puntos medios interpolados
        Assert.Equal((0.5, 0.4), VisionService.InterpolateKeyframe(keys, 1.0));
        (double x, double y) = VisionService.InterpolateKeyframe(keys, 0.5);
        Assert.Equal(0.3, x, 10);
        Assert.Equal(0.3, y, 10);
        (x, y) = VisionService.InterpolateKeyframe(keys, 2.0);
        Assert.Equal(0.7, x, 10);
        Assert.Equal(0.6, y, 10);
    }

    [Fact]
    public void InterpolateKeyframe_same_time_uses_next_key_without_dividing_by_zero()
    {
        ReframeKeyframe[] keys =
        [
            new(0.0, 0.1, 0.2, 1.0),
            new(1.0, 0.3, 0.3, 1.0),
            new(1.0, 0.7, 0.7, 1.0), // mismo t que la anterior
            new(2.0, 0.9, 0.9, 1.0),
        ];

        // t == 1.0 cae en el primer tramo (f = 1): el valor exacto del key 1
        Assert.Equal((0.3, 0.3), VisionService.InterpolateKeyframe(keys, 1.0));

        // Entre 1.0 y 2.0 interpola desde el segundo key de t = 1.0
        (double x, double y) = VisionService.InterpolateKeyframe(keys, 1.5);
        Assert.Equal(0.8, x, 10);
        Assert.Equal(0.8, y, 10);
    }

    [Theory]
    [InlineData(1920, 1080, 0.5625)]               // 9:16
    [InlineData(1920, 1080, 1.0)]                  // 1:1
    [InlineData(1920, 1080, 0.8)]                  // 4:5
    [InlineData(1920, 1080, 2.3333333333333335)]   // 21:9, más ancho que la fuente
    [InlineData(1080, 1920, 1.7777777777777777)]   // 16:9 sobre fuente vertical
    [InlineData(1080, 1920, 0.5625)]               // 9:16 sobre 9:16: el cuadro entero
    [InlineData(333, 333, 0.5625)]                 // cuadro impar
    [InlineData(1919, 1079, 0.8)]                  // impar
    [InlineData(16, 16, 0.5625)]                   // diminuto
    [InlineData(640, 360, 2.3333333333333335)]
    public void Crop_rect_stays_inside_the_frame_for_extreme_aspects(int width, int height, double aspect)
    {
        UiRect r = OverlayGeometry.ComputeCropRect(width, height, aspect);

        Assert.True(r.Width > 0 && r.Height > 0, $"El recorte quedó vacío: {r.Width}×{r.Height}");
        Assert.True(r.X >= 0 && r.Y >= 0, $"Origen negativo: ({r.X}, {r.Y})");
        Assert.True(r.Right <= width + 1e-9, $"Se sale por la derecha: {r.Right} > {width}");
        Assert.True(r.Bottom <= height + 1e-9, $"Se sale por abajo: {r.Bottom} > {height}");
        Assert.Equal(aspect, r.Width / r.Height, 6);
    }

    [Fact]
    public void Crop_rect_with_focus_off_the_frame_still_stays_inside()
    {
        double aspect = 9.0 / 16.0;

        // Foco muy a la izquierda y abajo: el recorte se pega a los bordes, no se sale
        UiRect r = OverlayGeometry.ComputeCropRect(1920, 1080, aspect, focusX: -500, focusY: 5000);
        Assert.Equal(0.0, r.X);
        Assert.Equal(0.0, r.Y); // cropH ocupa todo el alto, así que no hay margen vertical
        Assert.True(r.Right <= 1920);
        Assert.True(r.Bottom <= 1080);

        // Foco muy a la derecha: pegado al borde derecho
        r = OverlayGeometry.ComputeCropRect(1920, 1080, aspect, focusX: 5000, focusY: 540);
        Assert.Equal(1920.0, r.Right, 6);
        Assert.True(r.X >= 0);

        // Foco justo en la esquina
        r = OverlayGeometry.ComputeCropRect(640, 480, 1.0, focusX: 0, focusY: 0);
        Assert.Equal(0.0, r.X);
        Assert.Equal(0.0, r.Y);
        Assert.Equal(480.0, r.Width);
        Assert.Equal(480.0, r.Height);
    }

    [Fact]
    public void Trajectory_overlay_empty_null_and_out_of_range_index()
    {
        ViewportCoordinateTransformer t = new(100, 100);
        t.UpdateViewport(200, 200);

        (List<OverlayPoint> pts, OverlayPoint? cur) = OverlayGeometry.BuildTrajectoryOverlay(null, 0, t);
        Assert.Empty(pts);
        Assert.Null(cur);

        (pts, cur) = OverlayGeometry.BuildTrajectoryOverlay([], 0, t);
        Assert.Empty(pts);
        Assert.Null(cur);

        List<FollowFrame> frames = [new(0, 0.0, 50, 50, 1.0, "Good")];

        // Índice negativo: no hay nada que dibujar
        (pts, cur) = OverlayGeometry.BuildTrajectoryOverlay(frames, -1, t);
        Assert.Empty(pts);
        Assert.Null(cur);

        // Índice más allá de la lista: se dibuja todo pero no hay punto «actual»
        (pts, cur) = OverlayGeometry.BuildTrajectoryOverlay(frames, 99, t);
        Assert.Single(pts);
        Assert.False(pts[0].IsCurrent);
        Assert.Null(cur);
    }

    [Theory]
    [InlineData("es-CO")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Csv_writer_is_culture_independent_and_keeps_odd_values(string culture)
    {
        FollowResult result = new(320, 240, 30.0,
        [
            new(0, 0.0, -5.5, 300.125, 0.5, "Good"),     // fuera del cuadro: se apunta igual
            new(5, 0.166, 33.33333, 0.00001, 0.0, "Lost"), // hueco en los cuadros
        ]);

        CultureInfo previous = CultureInfo.CurrentCulture;
        string csv;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using StringWriter writer = new();
            FollowService.WriteCsv(result, writer);
            csv = writer.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("cuadro,tiempo,x,y,confianza,estado", lines[0].TrimEnd('\r'));
        Assert.Equal("0,0,-5.5,300.125,0.5,Good", lines[1].TrimEnd('\r'));
        Assert.Equal("5,0.166,33.333,0,0,Lost", lines[2].TrimEnd('\r'));
    }

    [Fact]
    public void A_non_finite_frame_becomes_lost_at_the_last_valid_position_and_exports_cleanly()
    {
        // Un seguimiento que diverge (NaN o infinito) no llega al resultado: System.Text.Json lanza al escribirlo
        // y el CSV lo dejaría como texto. Queda como los demás cuadros perdidos: donde estaba y con confianza 0.
        FollowFrame bueno = new(0, 0.0, 10, 20, 1.0, "Good");
        Assert.Same(bueno, FollowService.LostIfNotFinite(bueno, bueno));

        List<FollowFrame> cuadros = [bueno];
        FollowFrame[] raros =
        [
            new(1, 0.033, double.NaN, 21, 0.9, "Good"),
            new(2, 0.066, 11, double.PositiveInfinity, 0.9, "Good"),
            new(3, 0.1, 11, 21, double.NaN, "Good"),
        ];
        foreach (FollowFrame raro in raros)
        {
            FollowFrame f = FollowService.LostIfNotFinite(raro, cuadros[^1]);
            Assert.Equal(raro with { X = 10, Y = 20, Confianza = 0.0, Estado = "Lost" }, f);
            cuadros.Add(f);
        }

        FollowResult result = new(320, 240, 30.0, cuadros);
        using StringWriter json = new();
        FollowService.WriteJson(result, json);
        using JsonDocument doc = JsonDocument.Parse(json.ToString());
        Assert.Equal(4, doc.RootElement.GetProperty("cuadros").GetArrayLength());

        using StringWriter csv = new();
        FollowService.WriteCsv(result, csv);
        Assert.Contains("1,0.033,10,20,0,Lost", csv.ToString());
        Assert.DoesNotContain("NaN", csv.ToString());
    }

    [Theory]
    [InlineData(double.NaN, 5.0)]
    [InlineData(5.0, double.PositiveInfinity)]
    public async Task Non_finite_target_fails_clear_before_touching_the_video(double a, double b)
    {
        // La Cli acepta «--punto NaN,5»: se rechaza antes de buscar ffmpeg o abrir el video
        RastrearUserException ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => FollowService.FollowAsync("no-existe.mp4", new PointTargetSpec(a, b), region: null));
        Assert.Contains("finitos", ex.Message);

        ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => FollowService.FollowAsync("no-existe.mp4", point: null, new RegionTargetSpec(0, 0, a, b)));
        Assert.Contains("finitos", ex.Message);
    }

    [Fact]
    public async Task Region_whose_far_corner_overflows_fails_clear_before_touching_the_video()
    {
        // «--region 3e38,0,3e38,10»: cada número cabe en un float pero x + w no; el centro salía infinito y el JSON lanzaba
        RastrearUserException ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => FollowService.FollowAsync("no-existe.mp4", point: null, new RegionTargetSpec(3e38, 0, 3e38, 10)));
        Assert.Contains("finitos", ex.Message);

        ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => FollowService.FollowAsync("no-existe.mp4", point: null, new RegionTargetSpec(0, -3e38, 10, -3e38)));
        Assert.Contains("finitos", ex.Message);
    }

    [Theory]
    [InlineData(FollowService.MaxPointWindowSide + 1, 121)]
    [InlineData(int.MaxValue, 121)]
    [InlineData(41, FollowService.MaxPointWindowSide + 1)]
    [InlineData(41, int.MaxValue)]
    public async Task Oversized_pattern_or_search_fails_clear_before_touching_the_video(int patternSize, int searchSize)
    {
        // «--patron 100000» pedía gigas de memoria y acababa en IndexOutOfRangeException; con int.MaxValue, OverflowException
        RastrearUserException ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => FollowService.FollowAsync("no-existe.mp4", new PointTargetSpec(40, 40), region: null, patternSize, searchSize));
        Assert.Contains("1600", ex.Message);

        // Con una región no se usan: el error es el del video que falta, no el de los lados
        ex = await Assert.ThrowsAsync<RastrearUserException>(
            () => FollowService.FollowAsync("no-existe.mp4", point: null, new RegionTargetSpec(0, 0, 10, 10), patternSize, searchSize));
        Assert.DoesNotContain("1600", ex.Message);
    }
}
