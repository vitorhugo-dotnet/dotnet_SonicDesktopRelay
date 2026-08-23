namespace SonicDesktopRelay.Media;

public interface IVideoEncoder : IDisposable
{
    /// <summary>
    /// Which encoder actually got selected — "h264_nvenc", "libx264", and so on. Surfaced on
    /// the Diagnostics page, because "why is my CPU pinned" is answered here first.
    /// </summary>
    string Name { get; }

    /// <summary>Returns null when this frame produced no output (encoder buffering).</summary>
    EncodedVideoSample? Encode(VideoFrame frame, VideoQuality quality);

    /// <summary>
    /// Makes the next encode a keyframe. Called on PLI from a viewer — screen content is
    /// mostly static, so periodic keyframes would waste bandwidth and only on-demand ones
    /// are emitted.
    /// </summary>
    void RequestKeyFrame();
}
