using Rastrear.Core;
using Rastrear.Core.Model;
using Rastrear.Core.Ui;
using Rastrear.Core.Video;
using Xunit;

namespace Rastrear.Tests;

public class UiTests
{
    [Fact]
    public void PlayerTimeModel_Conversions_AreAccurate()
    {
        // 30 fps, 150 cuadros = 5.0 segundos
        PlayerTimeModel model = new(150, 30.0, 5.0);

        Assert.Equal(150, model.TotalFrames);
        Assert.Equal(30.0, model.Fps);
        Assert.Equal(5.0, model.DurationSeconds);
        Assert.Equal(0, model.CurrentFrame);
        Assert.Equal(0.0, model.CurrentTimeSeconds);

        // Cuadro a tiempo
        Assert.Equal(0.0, model.TimeFromFrame(0));
        Assert.Equal(1.0, model.TimeFromFrame(30));
        Assert.Equal(4.966666666666667, model.TimeFromFrame(149), 4);

        // Tiempo a cuadro
        Assert.Equal(0, model.FrameFromTime(0.0));
        Assert.Equal(15, model.FrameFromTime(0.5));
        Assert.Equal(30, model.FrameFromTime(1.0));
        Assert.Equal(149, model.FrameFromTime(5.0)); // Límites
        Assert.Equal(149, model.FrameFromTime(100.0)); // Clamp superior
        Assert.Equal(0, model.FrameFromTime(-10.0)); // Clamp inferior

        // Pasos y saltos
        Assert.True(model.CanStepForward);
        Assert.False(model.CanStepBackward);

        Assert.Equal(1, model.StepForward());
        Assert.True(model.CanStepBackward);

        Assert.Equal(0, model.StepBackward());
        Assert.False(model.CanStepBackward);

        Assert.Equal(75, model.SeekFrame(75));
        Assert.Equal(75, model.CurrentFrame);
        Assert.Equal(2.5, model.CurrentTimeSeconds);

        Assert.Equal(90, model.SeekTime(3.0));
        Assert.Equal(90, model.CurrentFrame);

        // Formato
        Assert.Equal("00:00.00", PlayerTimeModel.FormatTime(0.0));
        Assert.Equal("01:15.50", PlayerTimeModel.FormatTime(75.5));
        Assert.Contains("00:03.00 / 00:05.00", model.FormatPositionString());
    }

    [Fact]
    public void ViewportTransformer_Pillarbox_And_Letterbox_CalculateCorrectly()
    {
        // Video 1920x1080 (16:9 = 1.777)
        ViewportCoordinateTransformer transformer = new(1920, 1080);

        // Caso 1: Visor más ancho (Letterbox horizontal -> barras laterales o pillarbox)
        // Visor 2000 x 1000 (aspecto 2.0).
        // scale = min(2000/1920=1.0416, 1000/1080=0.9259) = 0.9259259
        transformer.UpdateViewport(2000, 1000);
        Assert.Equal(1000.0, transformer.DestinationHeight, 2);
        Assert.True(transformer.DestinationWidth < 2000.0);
        Assert.True(transformer.OffsetX > 0.0);
        Assert.Equal(0.0, transformer.OffsetY, 2);

        // Clic fuera del video a la izquierda
        bool insideLeft = transformer.TryViewportToVideo(10, 500, out double vx1, out double vy1);
        Assert.False(insideLeft);
        Assert.Equal(0.0, vx1); // Clamped

        // Clic en el centro exacto del video
        double centerX = transformer.OffsetX + (transformer.DestinationWidth / 2.0);
        double centerY = transformer.OffsetY + (transformer.DestinationHeight / 2.0);
        bool insideCenter = transformer.TryViewportToVideo(centerX, centerY, out double vCenterX, out double vCenterY);
        Assert.True(insideCenter);
        Assert.Equal(960.0, vCenterX, 1);
        Assert.Equal(540.0, vCenterY, 1);

        // Inversa: VideoToViewport
        (double viewX, double viewY) = transformer.VideoToViewport(960.0, 540.0);
        Assert.Equal(centerX, viewX, 1);
        Assert.Equal(centerY, viewY, 1);

        // Caso 2: Visor más alto (Letterbox vertical -> barras arriba y abajo)
        // Visor 1000 x 1000 (aspecto 1.0)
        transformer.UpdateViewport(1000, 1000);
        Assert.Equal(1000.0, transformer.DestinationWidth, 2);
        Assert.Equal(0.0, transformer.OffsetX, 2);
        Assert.True(transformer.OffsetY > 0.0);
        Assert.True(transformer.DestinationHeight < 1000.0);
    }

    [Fact]
    public void ViewportTransformer_DragSelection_ProducesNormalizedRect()
    {
        ViewportCoordinateTransformer transformer = new(1000, 1000);
        transformer.UpdateViewport(1000, 1000); // 1:1 scale

        // Arrastre diagonal inverso (de abajo-derecha a arriba-izquierda)
        bool ok = transformer.TryViewportDragToVideoRect(400, 300, 100, 150, out UiRect rect);
        Assert.True(ok);
        Assert.Equal(100.0, rect.X);
        Assert.Equal(150.0, rect.Y);
        Assert.Equal(300.0, rect.Width);
        Assert.Equal(150.0, rect.Height);
    }

    [Fact]
    public void OverlayGeometry_CropRect_CalculatesExactAspectRatios()
    {
        // Video 1920x1080 (16:9)
        // Relación 9:16 (0.5625)
        double aspect916 = OverlayGeometry.AspectRatioFromPreset("9:16");
        UiRect crop916 = OverlayGeometry.ComputeCropRect(1920, 1080, aspect916);
        Assert.Equal(1080.0, crop916.Height);
        Assert.Equal(1080.0 * (9.0 / 16.0), crop916.Width, 2); // 607.5
        Assert.Equal((1920 - crop916.Width) / 2.0, crop916.X, 2); // Centrado

        // Relación 1:1
        double aspect11 = OverlayGeometry.AspectRatioFromPreset("1:1");
        UiRect crop11 = OverlayGeometry.ComputeCropRect(1920, 1080, aspect11);
        Assert.Equal(1080.0, crop11.Height);
        Assert.Equal(1080.0, crop11.Width);

        // Relación 4:5
        double aspect45 = OverlayGeometry.AspectRatioFromPreset("4:5");
        UiRect crop45 = OverlayGeometry.ComputeCropRect(1920, 1080, aspect45);
        Assert.Equal(1080.0, crop45.Height);
        Assert.Equal(1080.0 * 0.8, crop45.Width, 2);

        // Con punto focal personalizado (ej. sujeto a la izquierda)
        UiRect cropFocal = OverlayGeometry.ComputeCropRect(1920, 1080, aspect916, focusX: 200, focusY: 540);
        Assert.Equal(0.0, cropFocal.X); // Clamped al borde izquierdo
    }

    [Fact]
    public void OverlayGeometry_TrajectoryAndDetections_ConvertCorrectly()
    {
        ViewportCoordinateTransformer transformer = new(1000, 1000);
        transformer.UpdateViewport(500, 500); // Scale = 0.5

        List<FollowFrame> frames =
        [
            new(0, 0.0, 100, 200, 1.0, "Good"),
            new(1, 0.1, 110, 210, 0.95, "Good"),
            new(2, 0.2, 120, 220, 0.90, "Good"),
        ];

        (List<OverlayPoint> points, OverlayPoint? current) = OverlayGeometry.BuildTrajectoryOverlay(frames, 1, transformer);
        Assert.Equal(2, points.Count);
        Assert.NotNull(current);
        Assert.Equal(55.0, current.Value.X, 1); // 110 * 0.5
        Assert.Equal(105.0, current.Value.Y, 1); // 210 * 0.5

        // Cajas de detección
        DetectionResult detResult = new(
        [
            new([new(100, 100, 200, 200, 0.95)]),
        ]);

        List<OverlayBox> boxes = OverlayGeometry.BuildDetectionsOverlay(detResult, 0, transformer);
        Assert.Single(boxes);
        Assert.Equal(50.0, boxes[0].Rect.X, 1);
        Assert.Equal(50.0, boxes[0].Rect.Y, 1);
        Assert.Equal(100.0, boxes[0].Rect.Width, 1);
        Assert.Equal(100.0, boxes[0].Rect.Height, 1);
        Assert.Contains("95%", boxes[0].Label);
    }

    [Fact]
    public async Task TaskExecutionController_Lifecycle_And_Cancellation_UnderOneSecond()
    {
        TaskExecutionController controller = new();
        Assert.Equal(ExecutionState.Idle, controller.State);
        Assert.False(controller.IsBusy);

        CancellationToken ct = controller.BeginExecution();
        Assert.Equal(ExecutionState.Running, controller.State);
        Assert.True(controller.IsBusy);
        Assert.False(ct.IsCancellationRequested);

        controller.ReportProgress(0.5, "Procesando cuadro 50");
        UiProgressInfo info = controller.GetProgressInfo();
        Assert.Equal(0.5, info.Fraction);
        Assert.Contains("50% - Procesando cuadro 50", info.FormattedProgress);

        // Cancelación rápida < 1 s
        bool cancelled = await controller.CancelAsync();
        Assert.True(cancelled);
        Assert.True(ct.IsCancellationRequested);
        Assert.True(controller.LastCancelDuration < TimeSpan.FromSeconds(1.0));

        controller.MarkCancelled();
        Assert.Equal(ExecutionState.Cancelled, controller.State);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public void TaskExecutionController_ErrorFormatting_SpanishWithoutTraces()
    {
        RastrearUserException userEx = new("No se encontró el archivo de video: clip.mp4");
        string msg1 = TaskExecutionController.FormatErrorMessage(userEx);
        Assert.Equal("No se encontró el archivo de video: clip.mp4", msg1);
        Assert.DoesNotContain("at ", msg1);

        RastrearModuleException modEx = new("No se encontró el módulo de visión en: C:\\modulos\\vision.exe");
        string msg2 = TaskExecutionController.FormatErrorMessage(modEx);
        Assert.Equal("No se encontró el módulo de visión en: C:\\modulos\\vision.exe", msg2);

        OperationCanceledException cancelEx = new();
        string msg3 = TaskExecutionController.FormatErrorMessage(cancelEx);
        Assert.Equal("Operación cancelada por el usuario.", msg3);

        Exception genericWithTrace = new("Fallo de conexión interno\r\n   at System.IO.Stream.Read()");
        string msg4 = TaskExecutionController.FormatErrorMessage(genericWithTrace);
        Assert.Equal("Fallo de conexión interno", msg4);
        Assert.DoesNotContain("at ", msg4);
    }

    [Fact]
    public void PlayerFrameBuffer_StoreAndRetrieve_EvictsOldest()
    {
        VideoInfo info = new(10, 10, 30.0, 1.0, 30);
        // Capacidad mínima permitida: 10
        using PlayerFrameBuffer buffer = new("dummy.mp4", info, maxCachedFrames: 10);

        Assert.Equal(0, buffer.CachedFrameCount);

        byte[] frame0 = [1, 2, 3];
        buffer.StoreFrame(0, frame0);
        Assert.True(buffer.HasFrame(0));
        Assert.Same(frame0, buffer.TryGetCachedFrame(0));

        // Insertar 11 cuadros para forzar desalojo del más antiguo (cuadro 0)
        for (int i = 1; i <= 10; i++)
        {
            buffer.StoreFrame(i, [(byte)i]);
        }

        Assert.Equal(10, buffer.CachedFrameCount);
        Assert.False(buffer.HasFrame(0)); // Cuadro 0 fue desalojado
        Assert.True(buffer.HasFrame(10));

        buffer.ClearCache();
        Assert.Equal(0, buffer.CachedFrameCount);
    }
}
