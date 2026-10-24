namespace Rastrear.Core.Ui;

/// <summary>
/// Rectángulo 2D para coordenadas de video o visor.
/// </summary>
public readonly record struct UiRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static UiRect FromPoints(double x1, double y1, double x2, double y2)
    {
        double x = Math.Min(x1, x2);
        double y = Math.Min(y1, y2);
        double w = Math.Abs(x2 - x1);
        double h = Math.Abs(y2 - y1);
        return new UiRect(x, y, w, h);
    }
}

/// <summary>
/// Conversión bidireccional de coordenadas entre el contenedor visual (viewport de WinUI)
/// y el espacio nativo de píxeles del video, preservando relación de aspecto uniforme (Uniform).
/// </summary>
public sealed class ViewportCoordinateTransformer
{
    public double VideoWidth { get; }
    public double VideoHeight { get; }
    public double ViewportWidth { get; private set; }
    public double ViewportHeight { get; private set; }

    public double Scale { get; private set; }
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public double DestinationWidth => VideoWidth * Scale;
    public double DestinationHeight => VideoHeight * Scale;
    public UiRect VideoContentRect => new(OffsetX, OffsetY, DestinationWidth, DestinationHeight);

    public ViewportCoordinateTransformer(double videoWidth, double videoHeight)
    {
        VideoWidth = Math.Max(1.0, videoWidth);
        VideoHeight = Math.Max(1.0, videoHeight);
        UpdateViewport(VideoWidth, VideoHeight);
    }

    public void UpdateViewport(double viewportWidth, double viewportHeight)
    {
        ViewportWidth = Math.Max(0.0, viewportWidth);
        ViewportHeight = Math.Max(0.0, viewportHeight);

        if (ViewportWidth <= 0 || ViewportHeight <= 0 || VideoWidth <= 0 || VideoHeight <= 0)
        {
            Scale = 1.0;
            OffsetX = 0.0;
            OffsetY = 0.0;
            return;
        }

        double scaleX = ViewportWidth / VideoWidth;
        double scaleY = ViewportHeight / VideoHeight;
        Scale = Math.Min(scaleX, scaleY);

        OffsetX = (ViewportWidth - DestinationWidth) / 2.0;
        OffsetY = (ViewportHeight - DestinationHeight) / 2.0;
    }

    public bool IsInsideVideo(double viewportX, double viewportY)
    {
        return viewportX >= OffsetX &&
               viewportX <= OffsetX + DestinationWidth &&
               viewportY >= OffsetY &&
               viewportY <= OffsetY + DestinationHeight;
    }

    public bool TryViewportToVideo(double viewportX, double viewportY, out double videoX, out double videoY)
    {
        if (Scale <= 0)
        {
            videoX = 0;
            videoY = 0;
            return false;
        }

        bool inside = IsInsideVideo(viewportX, viewportY);
        videoX = Math.Clamp((viewportX - OffsetX) / Scale, 0.0, VideoWidth);
        videoY = Math.Clamp((viewportY - OffsetY) / Scale, 0.0, VideoHeight);
        return inside;
    }

    public (double ViewportX, double ViewportY) VideoToViewport(double videoX, double videoY)
    {
        double vx = OffsetX + (videoX * Scale);
        double vy = OffsetY + (videoY * Scale);
        return (vx, vy);
    }

    public UiRect VideoRectToViewport(UiRect videoRect)
    {
        double vx = OffsetX + (videoRect.X * Scale);
        double vy = OffsetY + (videoRect.Y * Scale);
        double vw = videoRect.Width * Scale;
        double vh = videoRect.Height * Scale;
        return new UiRect(vx, vy, vw, vh);
    }

    public bool TryViewportDragToVideoRect(
        double startViewportX,
        double startViewportY,
        double curViewportX,
        double curViewportY,
        out UiRect videoRect)
    {
        _ = TryViewportToVideo(startViewportX, startViewportY, out double vx1, out double vy1);
        _ = TryViewportToVideo(curViewportX, curViewportY, out double vx2, out double vy2);

        videoRect = UiRect.FromPoints(vx1, vy1, vx2, vy2);
        return videoRect.Width >= 1.0 && videoRect.Height >= 1.0;
    }
}
