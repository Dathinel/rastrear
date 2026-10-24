namespace Rastrear.Core.Video;

public sealed record VideoInfo(
    int Width,
    int Height,
    double Fps,
    double DurationSeconds,
    int EstimatedFrames)
{
    public long FrameBytesBgra => (long)Width * Height * 4;
}
