using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Rastrear.Core.Video;

namespace Rastrear.Core.Ui;

/// <summary>
/// Búfer y lector de cuadros para el reproductor.
/// Soporta precarga secuencial rápida y búsqueda bajo demanda vía FFmpeg con límite de memoria.
/// </summary>
public sealed class PlayerFrameBuffer : IAsyncDisposable, IDisposable
{
    private readonly string _videoPath;
    private readonly VideoInfo _info;
    private readonly string _ffmpegPath;
    private readonly int _maxCachedFrames;
    private readonly ConcurrentDictionary<int, byte[]> _cache = new();
    private readonly Queue<int> _cacheOrder = new();
    private readonly Lock _lock = new();

    public VideoInfo Info => _info;
    public int CachedFrameCount => _cache.Count;

    public PlayerFrameBuffer(string videoPath, VideoInfo info, string? ffmpegPath = null, int maxCachedFrames = 60)
    {
        _videoPath = videoPath;
        _info = info;
        _ffmpegPath = FfmpegTool.FindFfmpeg(ffmpegPath);
        _maxCachedFrames = Math.Max(10, maxCachedFrames);
    }

    public bool HasFrame(int frameIndex) => _cache.ContainsKey(frameIndex);

    public byte[]? TryGetCachedFrame(int frameIndex)
    {
        return _cache.TryGetValue(frameIndex, out byte[]? frame) ? frame : null;
    }

    public void StoreFrame(int frameIndex, byte[] frameBgra)
    {
        lock (_lock)
        {
            if (_cache.ContainsKey(frameIndex))
            {
                return;
            }

            while (_cacheOrder.Count >= _maxCachedFrames)
            {
                int oldest = _cacheOrder.Dequeue();
                _cache.TryRemove(oldest, out _);
            }

            _cache[frameIndex] = frameBgra;
            _cacheOrder.Enqueue(frameIndex);
        }
    }

    public async Task<byte[]?> GetFrameAsync(int frameIndex, CancellationToken cancellationToken = default)
    {
        if (TryGetCachedFrame(frameIndex) is { } cached)
        {
            return cached;
        }

        byte[]? decoded = await FetchSingleFrameAsync(frameIndex, cancellationToken).ConfigureAwait(false);
        if (decoded is not null)
        {
            StoreFrame(frameIndex, decoded);
        }

        return decoded;
    }

    public async Task<int> PreloadRangeAsync(int startFrame, int count, CancellationToken cancellationToken = default)
    {
        int endFrame = Math.Min(startFrame + count, _info.EstimatedFrames > 0 ? _info.EstimatedFrames : startFrame + count);
        if (startFrame >= endFrame)
        {
            return 0;
        }

        double startTime = startFrame / _info.Fps;
        string timeStr = startTime.ToString("0.###", CultureInfo.InvariantCulture);

        ProcessStartInfo psi = new(_ffmpegPath)
        {
            Arguments = $"-hide_banner -loglevel error -ss {timeStr} -i \"{_videoPath}\" -vframes {endFrame - startFrame} -f rawvideo -pix_fmt bgra pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process proc = new() { StartInfo = psi };
        proc.Start();
        Stream stdout = proc.StandardOutput.BaseStream;
        int loaded = 0;

        try
        {
            for (int i = startFrame; i < endFrame; i++)
            {
                byte[] frame = new byte[_info.FrameBytesBgra];
                bool ok = await FfmpegTool.ReadExactBytesAsync(stdout, frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);
                if (!ok) break;

                StoreFrame(i, frame);
                loaded++;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelación intencional
        }
        finally
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }
        }

        return loaded;
    }

    private async Task<byte[]?> FetchSingleFrameAsync(int frameIndex, CancellationToken cancellationToken)
    {
        double time = frameIndex / _info.Fps;
        string timeStr = time.ToString("0.###", CultureInfo.InvariantCulture);

        ProcessStartInfo psi = new(_ffmpegPath)
        {
            Arguments = $"-hide_banner -loglevel error -ss {timeStr} -i \"{_videoPath}\" -vframes 1 -f rawvideo -pix_fmt bgra pipe:1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using Process proc = new() { StartInfo = psi };
        proc.Start();
        Stream stdout = proc.StandardOutput.BaseStream;

        byte[] frame = new byte[_info.FrameBytesBgra];
        bool ok = await FfmpegTool.ReadExactBytesAsync(stdout, frame, 0, frame.Length, cancellationToken).ConfigureAwait(false);
        await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return ok ? frame : null;
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _cacheOrder.Clear();
        }
    }

    public void Dispose()
    {
        ClearCache();
    }

    public ValueTask DisposeAsync()
    {
        ClearCache();
        return ValueTask.CompletedTask;
    }
}
