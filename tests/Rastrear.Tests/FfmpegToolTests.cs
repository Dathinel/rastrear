using Rastrear.Core;
using Rastrear.Core.Video;

namespace Rastrear.Tests;

public sealed class FfmpegToolTests
{
    /// <summary>Stream que entrega como mucho <paramref name="maxChunk"/> bytes por lectura.</summary>
    private sealed class ChunkedStream(byte[] data, int maxChunk) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, maxChunk));

        public override int Read(Span<byte> buffer) =>
            base.Read(buffer[..Math.Min(buffer.Length, maxChunk)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, maxChunk)], cancellationToken);
    }

    [Fact]
    public async Task ReadExactBytesAsync_empty_stream_returns_false()
    {
        using MemoryStream stream = new([]);
        byte[] buffer = new byte[8];
        Assert.False(await FfmpegTool.ReadExactBytesAsync(stream, buffer, 0, buffer.Length, CancellationToken.None));
    }

    [Fact]
    public async Task ReadExactBytesAsync_exact_and_split_reads_fill_the_buffer()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        using MemoryStream whole = new(data);
        byte[] buffer = new byte[10];
        Assert.True(await FfmpegTool.ReadExactBytesAsync(whole, buffer, 0, 10, CancellationToken.None));
        Assert.Equal(data, buffer);

        // A trompicones: el resultado es el mismo
        using ChunkedStream chunked = new(data, 3);
        Array.Clear(buffer);
        Assert.True(await FfmpegTool.ReadExactBytesAsync(chunked, buffer, 0, 10, CancellationToken.None));
        Assert.Equal(data, buffer);

        // Con offset: solo se toca la zona pedida
        using MemoryStream offsetStream = new(data);
        buffer = [99, 99, 0, 0, 0, 99];
        Assert.True(await FfmpegTool.ReadExactBytesAsync(offsetStream, buffer, 2, 3, CancellationToken.None));
        Assert.Equal([99, 99, 1, 2, 3, 99], buffer);

        // count 0: no lee nada y es éxito
        using MemoryStream any = new(data);
        Assert.True(await FfmpegTool.ReadExactBytesAsync(any, buffer, 0, 0, CancellationToken.None));
        Assert.Equal(0, any.Position);
    }

    [Fact]
    public async Task ReadExactBytesAsync_truncated_stream_throws_with_counts()
    {
        using MemoryStream stream = new([1, 2, 3]);
        byte[] buffer = new byte[10];
        EndOfStreamException ex = await Assert.ThrowsAsync<EndOfStreamException>(
            () => FfmpegTool.ReadExactBytesAsync(stream, buffer, 0, 10, CancellationToken.None));
        Assert.Contains("3", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    [Fact]
    public async Task ReadExactBytesAsync_cancellation_propagates()
    {
        using MemoryStream stream = new(new byte[64]);
        byte[] buffer = new byte[64];
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FfmpegTool.ReadExactBytesAsync(stream, buffer, 0, 64, cts.Token));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]       // negro
    [InlineData(255, 255, 255, 255)] // blanco
    [InlineData(0, 0, 255, 77)]    // rojo puro → 77 (BT.601)
    [InlineData(0, 255, 0, 149)]   // verde puro → 149
    [InlineData(255, 0, 0, 29)]    // azul puro → 29
    public void BgraToGray8_known_values(byte b, byte g, byte r, byte expected)
    {
        byte[] bgra = [b, g, r, 255];
        byte[] gray = new byte[1];
        FfmpegTool.BgraToGray8(bgra, gray, 1, 1);
        Assert.Equal(expected, gray[0]);
    }

    [Fact]
    public void FindFfmpeg_with_a_bad_custom_path_fails_clear()
    {
        var ex = Assert.Throws<RastrearUserException>(() => FfmpegTool.FindFfmpeg(@"C:\no\existe\este-ffmpeg.exe"));
        Assert.Contains("No se encontró el ejecutable de FFmpeg", ex.Message);
        Assert.Contains("este-ffmpeg.exe", ex.Message);
    }
}
