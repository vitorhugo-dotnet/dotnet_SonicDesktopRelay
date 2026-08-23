using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Presentation;

namespace SonicDesktopRelay.App.Controls;

/// <summary>
/// Draws decoded frames. One <see cref="WriteableBitmap"/> lives for the whole session and is
/// rebuilt only when the picture size changes — at 1080p30 a bitmap per frame is roughly
/// 250 MB/s of garbage, and the GC pauses that buys show up as stutter in exactly the content
/// people notice it in.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class VideoSurface : Control
{
    private WriteableBitmap? _bitmap;

    /// <summary>The staging copy used only when a frame is not array-backed. Reused, never per frame.</summary>
    private byte[] _staging = [];

    private int _width;
    private int _height;

    /// <summary>
    /// Blits one frame into the recycled bitmap. UI thread only: <see cref="Shell"/> marshals
    /// with <c>Dispatcher.UIThread.Post</c>, as it already does for snapshots. Decoding
    /// happens off this thread; only the blit belongs here.
    /// </summary>
    public void Present(VideoFrame frame)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (frame.Width <= 0 || frame.Height <= 0) return;

        var rowBytes = frame.Width * 4;
        var total = rowBytes * frame.Height;
        if (frame.Bgra.Length < total) return;

        if (_bitmap is null || _width != frame.Width || _height != frame.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(frame.Width, frame.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            _width = frame.Width;
            _height = frame.Height;
        }

        var (source, offset) = Borrow(frame.Bgra, total);

        using (var locked = _bitmap.Lock())
        {
            // The lock's row pitch is the platform's, not width*4. Copying the block whole
            // would shear the picture the same way an unpadded copy does on the capture side.
            if (locked.RowBytes == rowBytes)
            {
                Marshal.Copy(source, offset, locked.Address, total);
            }
            else
            {
                for (var y = 0; y < frame.Height; y++)
                {
                    Marshal.Copy(source, offset + (y * rowBytes),
                        locked.Address + ((nint)y * locked.RowBytes), rowBytes);
                }
            }
        }

        InvalidateVisual();
    }

    /// <summary>Drops the picture, and the bitmap with it, when a session ends.</summary>
    public void Clear()
    {
        Dispatcher.UIThread.VerifyAccess();
        _bitmap?.Dispose();
        _bitmap = null;
        _width = 0;
        _height = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var available = Bounds.Size;

        // Black, not transparent: the bars around a letterboxed picture are part of it, and a
        // window-coloured border reads as a rendering bug.
        context.FillRectangle(Brushes.Black, new Rect(available));

        if (_bitmap is null) return;

        var destination = LetterboxGeometry.Fit(new Size(_width, _height), available);
        if (destination.Width <= 0 || destination.Height <= 0) return;

        context.DrawImage(_bitmap, new Rect(0, 0, _width, _height), destination);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
        _width = 0;
        _height = 0;
    }

    /// <summary>
    /// The decoder hands out its own reused array, so the common path takes it directly. The
    /// staging copy exists for any other producer and is itself reused.
    /// </summary>
    private (byte[] Buffer, int Offset) Borrow(ReadOnlyMemory<byte> frame, int total)
    {
        if (MemoryMarshal.TryGetArray(frame, out var segment) && segment.Array is { } array)
            return (array, segment.Offset);

        if (_staging.Length < total) _staging = new byte[total];
        frame.Span[..total].CopyTo(_staging);
        return (_staging, 0);
    }
}
