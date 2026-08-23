using Avalonia;

namespace SonicDesktopRelay.Presentation;

/// <summary>
/// Where a decoded picture goes inside the space the window gives it. Pure arithmetic, kept
/// out of the control on purpose: this is where the bugs live, and here it can be tested
/// without a window, a compositor or a frame.
/// </summary>
public static class LetterboxGeometry
{
    /// <summary>
    /// The largest centred rectangle of the source's aspect ratio that fits in
    /// <paramref name="available"/>. Never stretches: a distorted screen share is worse than
    /// black bars, because text stops being readable and nobody can tell why.
    /// </summary>
    public static Rect Fit(Size source, Size available)
    {
        if (source.Width <= 0 || source.Height <= 0) return default;
        if (available.Width <= 0 || available.Height <= 0) return default;

        var scale = Math.Min(available.Width / source.Width, available.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Rect((available.Width - width) / 2, (available.Height - height) / 2, width, height);
    }
}
