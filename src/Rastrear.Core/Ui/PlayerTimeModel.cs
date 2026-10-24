using System.Globalization;

namespace Rastrear.Core.Ui;

/// <summary>
/// Modelo de tiempo y cuadros para el reproductor de video.
/// Maneja conversiones bidireccionales exactas entre índice de cuadro y tiempo en segundos,
/// formateo legible y navegación (paso adelante, atrás, salto).
/// </summary>
public sealed class PlayerTimeModel
{
    public int TotalFrames { get; }
    public double Fps { get; }
    public double DurationSeconds { get; }

    public int CurrentFrame { get; private set; }

    public double CurrentTimeSeconds => TimeFromFrame(CurrentFrame);

    public bool CanStepForward => CurrentFrame < TotalFrames - 1;
    public bool CanStepBackward => CurrentFrame > 0;

    public PlayerTimeModel(int totalFrames, double fps, double durationSeconds)
    {
        TotalFrames = Math.Max(0, totalFrames);
        Fps = fps > 0 ? fps : 30.0;
        DurationSeconds = durationSeconds > 0
            ? durationSeconds
            : (TotalFrames > 0 ? TotalFrames / Fps : 0.0);
        CurrentFrame = 0;
    }

    public static PlayerTimeModel FromVideo(int totalFrames, double fps, double durationSeconds) =>
        new(totalFrames, fps, durationSeconds);

    public int FrameFromTime(double seconds)
    {
        if (TotalFrames <= 0 || seconds <= 0)
        {
            return 0;
        }

        int frame = (int)Math.Round(seconds * Fps);
        return Math.Clamp(frame, 0, Math.Max(0, TotalFrames - 1));
    }

    public double TimeFromFrame(int frame)
    {
        if (TotalFrames <= 0 || frame <= 0)
        {
            return 0.0;
        }

        int clamped = Math.Clamp(frame, 0, Math.Max(0, TotalFrames - 1));
        return clamped / Fps;
    }

    public int SeekFrame(int targetFrame)
    {
        if (TotalFrames <= 0)
        {
            CurrentFrame = 0;
            return 0;
        }

        CurrentFrame = Math.Clamp(targetFrame, 0, TotalFrames - 1);
        return CurrentFrame;
    }

    public int SeekTime(double targetSeconds)
    {
        int frame = FrameFromTime(targetSeconds);
        return SeekFrame(frame);
    }

    public int StepForward()
    {
        if (CanStepForward)
        {
            CurrentFrame++;
        }
        return CurrentFrame;
    }

    public int StepBackward()
    {
        if (CanStepBackward)
        {
            CurrentFrame--;
        }
        return CurrentFrame;
    }

    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        int totalMinutes = (int)ts.TotalMinutes;
        int secs = ts.Seconds;
        int ms10 = ts.Milliseconds / 10;
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}.{2:00}", totalMinutes, secs, ms10);
    }

    public string FormatPositionString()
    {
        string curTime = FormatTime(CurrentTimeSeconds);
        string totalTime = FormatTime(DurationSeconds);
        int displayFrame = TotalFrames > 0 ? CurrentFrame + 1 : 0;
        return $"{curTime} / {totalTime} (cuadro {displayFrame}/{TotalFrames})";
    }
}
