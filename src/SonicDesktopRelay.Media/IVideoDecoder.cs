namespace SonicDesktopRelay.Media;

public interface IVideoDecoder : IDisposable
{
    /// <summary>The decoder actually in use, for the Diagnostics page.</summary>
    string Name { get; }

    /// <summary>Returns null when this sample produced no frame yet (decoder buffering).</summary>
    VideoFrame? Decode(EncodedVideoSample sample);
}
