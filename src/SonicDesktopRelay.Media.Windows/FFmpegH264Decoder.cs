using System.Runtime.Versioning;
using FFmpeg.AutoGen;

namespace SonicDesktopRelay.Media.Windows;

/// <summary>
/// H.264 for the whole session. A viewer has exactly one publisher, so there is exactly one
/// of these — the mirror image of <see cref="FFmpegH264Encoder"/>.
/// <para>
/// Nothing here allocates per frame. The input staging buffer, the BGRA output buffer and the
/// scaler are created once and rebuilt only when the picture size changes: at 1080p30 a fresh
/// output buffer per frame is roughly 250 MB/s of garbage, which is exactly the cost the
/// capture side already refuses to pay.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class FFmpegH264Decoder : IVideoDecoder
{
    /// <summary>
    /// Tried in order, each by actually opening a context — the same discipline the encoder
    /// uses, because finding out a decoder will not open beats finding it out on the first
    /// frame of somebody's screen share.
    /// <para>
    /// <b>Software only, deliberately.</b> <c>h264_cuvid</c> opens and decodes correctly on
    /// this hardware but holds one picture back: NVDEC's parser will not release a frame until
    /// the <i>next</i> packet arrives to close it, and neither <c>AV_CODEC_FLAG_LOW_DELAY</c>
    /// nor <c>surfaces=1</c> changes that. On video that is fatal only to latency; on a shared
    /// screen it is fatal to correctness, because the picture is static for long stretches and
    /// the publisher only encodes when something moves. The viewer would sit on the
    /// second-to-last frame indefinitely — showing a stale window the moment the user stops
    /// moving. Software H.264 at 1080p30 costs a few percent of one core, which is a very
    /// cheap price for a picture that is actually current. <c>h264_qsv</c> buffers the same
    /// way through its async pipeline and is left out for the same reason.
    /// </para>
    /// </summary>
    private static readonly string[] Candidates = ["h264"];

    private readonly List<string> _rejections = [];
    private readonly Lock _gate = new();

    private AVCodecContext* _context;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _scaler;

    private byte[] _input = [];
    private byte[] _bgra = [];
    private int _scalerWidth;
    private int _scalerHeight;
    private AVPixelFormat _scalerFormat = AVPixelFormat.AV_PIX_FMT_NONE;
    private bool _disposed;

    public FFmpegH264Decoder()
    {
        if (!FFmpegLoader.TryInitialise(out var error))
            throw new InvalidOperationException(error ?? "FFmpeg is not available.");

        foreach (var candidate in Candidates)
        {
            if (TryOpen(candidate, out var reason))
            {
                Name = candidate;
                break;
            }

            _rejections.Add($"{candidate}: {reason}");
        }

        if (_context is null)
        {
            throw new InvalidOperationException(
                "No H.264 decoder could be opened. " + string.Join(" | ", _rejections));
        }

        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();
        if (_frame is null || _packet is null)
        {
            ReleaseNative();
            throw new InvalidOperationException("av_frame_alloc/av_packet_alloc failed.");
        }
    }

    /// <summary>The decoder that actually opened — "h264_cuvid", "h264", and so on.</summary>
    public string Name { get; } = string.Empty;

    /// <summary>Every candidate that was rejected, with the reason. Read by Diagnostics.</summary>
    public IReadOnlyList<string> RejectionLog => _rejections;

    public VideoFrame? Decode(EncodedVideoSample sample)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (sample.Data.Length == 0) return null;

            // FFmpeg reads past the end of a packet by up to AV_INPUT_BUFFER_PADDING_SIZE
            // bytes. Staging into one grown-on-demand buffer keeps that safe without a
            // per-frame allocation.
            EnsureInput(sample.Data.Length);
            sample.Data.Span.CopyTo(_input);
            Array.Clear(_input, sample.Data.Length, ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);

            int sent;
            fixed (byte* data = _input)
            {
                _packet->data = data;
                _packet->size = sample.Data.Length;
                sent = ffmpeg.avcodec_send_packet(_context, _packet);
                _packet->data = null;
                _packet->size = 0;
            }

            // A corrupt packet is ordinary weather on a lossy link. Swallowing it and waiting
            // for the next keyframe is the only behaviour that keeps a session alive.
            if (sent < 0 && sent != ffmpeg.AVERROR(ffmpeg.EAGAIN)) return null;

            var received = ffmpeg.avcodec_receive_frame(_context, _frame);
            if (received < 0) return null;

            try
            {
                return Convert(sample.Timestamp);
            }
            finally
            {
                ffmpeg.av_frame_unref(_frame);
            }
        }
    }

    private VideoFrame? Convert(TimeSpan timestamp)
    {
        var width = _frame->width;
        var height = _frame->height;
        if (width <= 0 || height <= 0) return null;

        var format = (AVPixelFormat)_frame->format;
        EnsureScaler(width, height, format);
        EnsureOutput(width, height);

        fixed (byte* target = _bgra)
        {
            var sourceData = new byte*[4];
            var sourceStride = new int[4];
            for (var plane = 0; plane < 4; plane++)
            {
                sourceData[plane] = _frame->data[(uint)plane];
                sourceStride[plane] = _frame->linesize[(uint)plane];
            }

            var targetData = new byte*[4];
            targetData[0] = target;
            var targetStride = new[] { width * 4, 0, 0, 0 };

            ffmpeg.sws_scale(_scaler, sourceData, sourceStride, 0, height, targetData, targetStride);
        }

        // The array is handed out again next frame, exactly as the capture source does on the
        // publishing side: consumers must copy or blit before returning.
        return new VideoFrame(width, height, _bgra.AsMemory(0, width * height * 4), timestamp);
    }

    private void EnsureInput(int length)
    {
        var needed = length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE;
        if (_input.Length >= needed) return;
        // Grown, never shrunk: packet sizes bounce around and a keyframe is the high-water
        // mark, so this settles after the first one.
        _input = new byte[needed];
    }

    private void EnsureOutput(int width, int height)
    {
        var needed = width * height * 4;
        if (_bgra.Length >= needed) return;
        _bgra = new byte[needed];
    }

    private void EnsureScaler(int width, int height, AVPixelFormat format)
    {
        if (_scaler is not null && _scalerWidth == width && _scalerHeight == height && _scalerFormat == format)
            return;

        if (_scaler is not null) ffmpeg.sws_freeContext(_scaler);

        _scaler = ffmpeg.sws_getContext(
            width, height, format,
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_scaler is null)
            throw new InvalidOperationException($"sws_getContext failed for {format} {width}x{height}.");

        _scalerWidth = width;
        _scalerHeight = height;
        _scalerFormat = format;
    }

    private bool TryOpen(string codecName, out string reason)
    {
        var codec = ffmpeg.avcodec_find_decoder_by_name(codecName);
        if (codec is null)
        {
            reason = "not built into this FFmpeg";
            return false;
        }

        var context = ffmpeg.avcodec_alloc_context3(codec);
        if (context is null)
        {
            reason = "avcodec_alloc_context3 failed";
            return false;
        }

        try
        {
            // Screen sharing is a latency budget, not a throughput one. Frame threading holds
            // several frames back before emitting the first, which on a mostly static screen
            // means the picture arrives seconds late or not at all.
            context->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            context->thread_type = ffmpeg.FF_THREAD_SLICE;

            // Timestamps arrive on the 90 kHz RTP clock; without this the decoder complains
            // about an invalid packet timebase on every open.
            context->pkt_timebase = new AVRational { num = 1, den = 90_000 };

            var opened = ffmpeg.avcodec_open2(context, codec, null);
            if (opened < 0)
            {
                reason = $"avcodec_open2 failed ({FFmpegError.Describe(opened)})";
                return false;
            }

            _context = context;
            context = null;
            reason = string.Empty;
            return true;
        }
        finally
        {
            if (context is not null) ffmpeg.avcodec_free_context(&context);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseNative();
        }
    }

    private void ReleaseNative()
    {
        if (_context is not null)
        {
            var context = _context;
            ffmpeg.avcodec_free_context(&context);
            _context = null;
        }

        if (_frame is not null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
        }

        if (_packet is not null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_scaler is not null)
        {
            ffmpeg.sws_freeContext(_scaler);
            _scaler = null;
        }
    }
}
