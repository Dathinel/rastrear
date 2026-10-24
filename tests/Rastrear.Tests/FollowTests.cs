using System.Globalization;
using System.Text.Json;
using Rastrear.Core.Model;
using Rastrear.Core.Tracking;
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
}
