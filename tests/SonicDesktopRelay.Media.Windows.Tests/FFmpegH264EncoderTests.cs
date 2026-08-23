using SonicDesktopRelay.Media;
using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

public sealed class FFmpegH264EncoderTests
{
    private static bool Available => FFmpegLoader.TryInitialise(out _);

    [Fact]
    public void The_selected_encoder_is_named()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();

        Assert.False(string.IsNullOrWhiteSpace(encoder.Name));
        Assert.Contains("264", encoder.Name);
    }

    [Fact]
    public void The_first_encoded_frame_is_a_keyframe()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();

        var sample = EncodeOne(encoder, 1280, 720);

        Assert.NotNull(sample);
        Assert.True(sample!.Value.IsKeyFrame);
        Assert.True(sample.Value.Data.Length > 0);
    }

    [Fact]
    public void Encoding_honours_the_quality_height()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();

        var sample = EncodeOne(encoder, 1920, 1080, new VideoQuality(720, 30, 2_000_000));

        Assert.Equal(720, sample!.Value.Height);
        Assert.Equal(1280, sample.Value.Width);
    }

    [Fact]
    public void A_requested_keyframe_is_produced()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();
        EncodeOne(encoder, 1280, 720);
        for (var i = 0; i < 5; i++) EncodeOne(encoder, 1280, 720);

        encoder.RequestKeyFrame();
        var sample = EncodeOne(encoder, 1280, 720);

        Assert.True(sample!.Value.IsKeyFrame);
    }

    [Fact]
    public void A_resolution_change_is_absorbed_rather_than_throwing()
    {
        if (!Available) return;

        using var encoder = new FFmpegH264Encoder();
        EncodeOne(encoder, 1280, 720);

        var sample = EncodeOne(encoder, 1920, 1080);

        Assert.NotNull(sample);
        Assert.True(sample!.Value.IsKeyFrame);
    }

    private static EncodedVideoSample? EncodeOne(
        FFmpegH264Encoder encoder, int width, int height, VideoQuality? quality = null)
    {
        var bgra = new byte[width * height * 4];
        Random.Shared.NextBytes(bgra);
        return encoder.Encode(
            new VideoFrame(width, height, bgra, TimeSpan.Zero),
            quality ?? new VideoQuality(height, 30, 4_000_000));
    }
}
