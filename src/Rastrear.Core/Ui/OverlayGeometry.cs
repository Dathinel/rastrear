using System.Globalization;
using Rastrear.Core.Model;

namespace Rastrear.Core.Ui;

public readonly record struct OverlayPoint(double X, double Y, bool IsCurrent, double Confidence, string State);

public readonly record struct OverlayBox(UiRect Rect, string Label, double Confidence);

public readonly record struct OverlayFrameData(
    IReadOnlyList<OverlayPoint> Trajectory,
    OverlayPoint? CurrentPoint,
    IReadOnlyList<OverlayBox> Boxes,
    UiRect? ReframeCropRect,
    UiRect? DragSelectionRect);

public static class OverlayGeometry
{
    public static double AspectRatioFromPreset(string preset) => preset switch
    {
        "9:16" => 9.0 / 16.0,
        "1:1" => 1.0,
        "4:5" => 4.0 / 5.0,
        _ => 9.0 / 16.0,
    };

    /// <summary>
    /// Calcula el rectángulo de recorte centrado para una relación de aspecto objetivo sobre el video nativo.
    /// Si se proporciona un punto focal (ej. sujeto o rostro), centra el recorte allí manteniendo los límites.
    /// </summary>
    public static UiRect ComputeCropRect(double videoWidth, double videoHeight, double targetAspect, double? focusX = null, double? focusY = null)
    {
        double sourceAspect = videoWidth / videoHeight;
        double cropW;
        double cropH;

        if (targetAspect <= sourceAspect)
        {
            // Más estrecho que la fuente: recortamos a los lados
            cropH = videoHeight;
            cropW = cropH * targetAspect;
        }
        else
        {
            // Más ancho que la fuente: recortamos arriba y abajo
            cropW = videoWidth;
            cropH = cropW / targetAspect;
        }

        double targetCenterX = focusX ?? (videoWidth / 2.0);
        double targetCenterY = focusY ?? (videoHeight / 2.0);

        double x = targetCenterX - (cropW / 2.0);
        double y = targetCenterY - (cropH / 2.0);

        x = Math.Clamp(x, 0.0, Math.Max(0.0, videoWidth - cropW));
        y = Math.Clamp(y, 0.0, Math.Max(0.0, videoHeight - cropH));

        return new UiRect(x, y, cropW, cropH);
    }

    /// <summary>
    /// Construye la trayectoria y punto actual para la superposición en coordenadas de viewport.
    /// </summary>
    public static (List<OverlayPoint> Trajectory, OverlayPoint? Current) BuildTrajectoryOverlay(
        IReadOnlyList<FollowFrame>? frames,
        int currentFrameIndex,
        ViewportCoordinateTransformer transformer)
    {
        if (frames is null || frames.Count == 0)
        {
            return ([], null);
        }

        List<OverlayPoint> points = new(Math.Min(frames.Count, currentFrameIndex + 1));
        OverlayPoint? current = null;

        for (int i = 0; i <= currentFrameIndex && i < frames.Count; i++)
        {
            FollowFrame f = frames[i];
            (double vx, double vy) = transformer.VideoToViewport(f.X, f.Y);
            bool isCur = i == currentFrameIndex;
            OverlayPoint p = new(vx, vy, isCur, f.Confianza, f.Estado);
            points.Add(p);
            if (isCur)
            {
                current = p;
            }
        }

        return (points, current);
    }

    /// <summary>
    /// Convierte las cajas detectadas del cuadro actual a coordenadas del visor.
    /// </summary>
    public static List<OverlayBox> BuildDetectionsOverlay(
        DetectionResult? detections,
        int currentFrameIndex,
        ViewportCoordinateTransformer transformer)
    {
        if (detections is null ||
            detections.Cuadros is null ||
            currentFrameIndex < 0 ||
            currentFrameIndex >= detections.Cuadros.Count)
        {
            return [];
        }

        DetectionFrame frame = detections.Cuadros[currentFrameIndex];
        if (frame.Cajas is null || frame.Cajas.Count == 0)
        {
            return [];
        }

        List<OverlayBox> boxes = new(frame.Cajas.Count);
        foreach (DetectionBox b in frame.Cajas)
        {
            UiRect videoRect = new(b.X, b.Y, b.W, b.H);
            UiRect viewRect = transformer.VideoRectToViewport(videoRect);
            string label = $"Detectado ({(int)Math.Round(b.P * 100)}%)";
            boxes.Add(new OverlayBox(viewRect, label, b.P));
        }

        return boxes;
    }
}
