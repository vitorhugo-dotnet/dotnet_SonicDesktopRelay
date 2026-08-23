using SonicDesktopRelay.Media;
using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

public sealed class FFmpegH264DecoderTests
{
    private static bool Available => FFmpegLoader.TryInitialise(out _);

    [Fact]
    public void A_frame_encoded_by_the_publisher_decodes_back_to_the_same_size()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();
        using var decoder = new FFmpegH264Decoder();
        var bgra = new byte[1280 * 720 * 4];
        Random.Shared.NextBytes(bgra);
        var sample = encoder.Encode(new VideoFrame(1280, 720, bgra, TimeSpan.Zero),
            new VideoQuality(720, 30, 2_000_000));

        var frame = decoder.Decode(sample!.Value);

        Assert.NotNull(frame);
        Assert.Equal(1280, frame!.Width);
        Assert.Equal(720, frame.Height);
        Assert.Equal(1280 * 720 * 4, frame.Bgra.Length);
    }

    [Fact]
    public void A_decoder_handles_a_mid_stream_resolution_change()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();
        using var decoder = new FFmpegH264Decoder();
        Decode(encoder, decoder, 1280, 720);

        var frame = Decode(encoder, decoder, 640, 360);

        Assert.Equal(640, frame!.Width);
    }

    [Fact]
    public void Garbage_input_returns_null_rather_than_throwing()
    {
        if (!Available) return;

        using var decoder = new FFmpegH264Decoder();

        var frame = decoder.Decode(new EncodedVideoSample(new byte[] { 1, 2, 3, 4 }, TimeSpan.Zero, false, 16, 16));

        // A corrupt packet is a normal event on a lossy link; it must not end the session.
        Assert.Null(frame);
    }

    [Fact]
    public void The_decoder_is_named_for_diagnostics()
    {
        if (!Available) return;

        using var decoder = new FFmpegH264Decoder();

        Assert.Contains("264", decoder.Name);
    }

    [Fact]
    public void The_conversion_buffer_is_reused_between_frames_of_the_same_size()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();
        using var decoder = new FFmpegH264Decoder();
        var first = Decode(encoder, decoder, 1280, 720);

        var second = Decode(encoder, decoder, 1280, 720);

        // At 1080p30 a fresh buffer per frame is roughly 250 MB/s of garbage. The one buffer
        // is handed out again, exactly as the capture source does on the publishing side.
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(first!.Bgra, out var a));
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(second!.Bgra, out var b));
        Assert.Same(a.Array, b.Array);
    }

    private static VideoFrame? Decode(
        FFmpegH264Encoder encoder, FFmpegH264Decoder decoder, int width, int height)
    {
        var bgra = new byte[width * height * 4];
        Random.Shared.NextBytes(bgra);
        var sample = encoder.Encode(
            new VideoFrame(width, height, bgra, TimeSpan.Zero),
            new VideoQuality(height, 30, 2_000_000));
        return sample is null ? null : decoder.Decode(sample.Value);
    }
}
