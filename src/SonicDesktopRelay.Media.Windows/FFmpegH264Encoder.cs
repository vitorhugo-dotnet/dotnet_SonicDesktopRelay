using System.Runtime.Versioning;
using FFmpeg.AutoGen;

namespace SonicDesktopRelay.Media.Windows;

/// <summary>
/// H.264 for the whole session. One of these exists per share, never one per viewer: the
/// encoded packet it produces is handed to every peer connection unchanged.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class FFmpegH264Encoder : IVideoEncoder
{
    /// <summary>
    /// Hardware first, software last. Each is tried by actually opening a context — a machine
    /// can advertise NVENC and still fail to open it (no driver, or every session slot in use),
    /// and finding that out here beats finding it out on the first frame.
    /// </summary>
    private static readonly string[] Candidates = ["h264_nvenc", "h264_qsv", "h264_amf", "libx264"];

    /// <summary>
    /// Screen content is static for long stretches, so periodic keyframes are wasted bytes.
    /// Keyframes are produced on demand only, which means the GOP is effectively unbounded.
    /// </summary>
    private const int EffectivelyInfiniteGop = 1_000_000;

    private const int ProbeWidth = 1280;
    private const int ProbeHeight = 720;

    private readonly List<string> _rejections = [];
    private readonly Lock _gate = new();

    private AVCodec* _codec;
    private AVCodecContext* _context;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _scaler;

    private int _sourceWidth;
    private int _sourceHeight;
    private int _outputWidth;
    private int _outputHeight;
    private int _configuredFps;
    private long _configuredBitrate;
    private long _pts;
    private bool _forceKeyFrame;
    private bool _disposed;

    public FFmpegH264Encoder()
    {
        if (!FFmpegLoader.TryInitialise(out var error))
            throw new InvalidOperationException(error ?? "FFmpeg is not available.");

        foreach (var candidate in Candidates)
        {
            if (TryOpenProbe(candidate, out var reason))
            {
                Name = candidate;
                return;
            }

            _rejections.Add($"{candidate}: {reason}");
        }

        throw new InvalidOperationException(
            "No H.264 encoder could be opened. " + string.Join(" | ", _rejections));
    }

    /// <summary>
    /// The codec that actually opened — "h264_nvenc", "libx264", and so on. Surfaced on the
    /// Diagnostics page, because "why is my CPU pinned" is answered here first.
    /// </summary>
    public string Name { get; } = string.Empty;

    /// <summary>Every candidate that was rejected, with the reason. Read by Diagnostics.</summary>
    public IReadOnlyList<string> RejectionLog => _rejections;

    public EncodedVideoSample? Encode(VideoFrame frame, VideoQuality quality)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            var (width, height) = quality.ScaleFor(frame.Width, frame.Height);
            if (width <= 0 || height <= 0) return null;

            EnsureContext(width, height, quality);
            EnsureScaler(frame.Width, frame.Height, width, height);

            var expected = (long)frame.Width * frame.Height * 4;
            if (frame.Bgra.Length < expected) return null;

            FFmpegError.Check(ffmpeg.av_frame_make_writable(_frame), "av_frame_make_writable");
            Scale(frame);

            _frame->pts = _pts++;
            _frame->pict_type = _forceKeyFrame ? AVPictureType.AV_PICTURE_TYPE_I : AVPictureType.AV_PICTURE_TYPE_NONE;
            if (_forceKeyFrame)
            {
                // Some hardware encoders look at key_frame rather than pict_type.
                _frame->flags |= ffmpeg.AV_FRAME_FLAG_KEY;
                _forceKeyFrame = false;
            }
            else
            {
                _frame->flags &= ~ffmpeg.AV_FRAME_FLAG_KEY;
            }

            FFmpegError.Check(ffmpeg.avcodec_send_frame(_context, _frame), "avcodec_send_frame");

            var received = ffmpeg.avcodec_receive_packet(_context, _packet);
            if (received == ffmpeg.AVERROR(ffmpeg.EAGAIN) || received == ffmpeg.AVERROR_EOF) return null;
            FFmpegError.Check(received, "avcodec_receive_packet");

            try
            {
                if (_packet->size <= 0) return null;

                var data = new byte[_packet->size];
                new ReadOnlySpan<byte>(_packet->data, _packet->size).CopyTo(data);
                var isKeyFrame = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
                return new EncodedVideoSample(data, frame.Timestamp, isKeyFrame, _outputWidth, _outputHeight);
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }

    public void RequestKeyFrame()
    {
        lock (_gate) _forceKeyFrame = true;
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

    private void Scale(VideoFrame frame)
    {
        var span = frame.Bgra.Span;
        fixed (byte* source = span)
        {
            var sourceData = new byte*[4];
            sourceData[0] = source;
            var sourceStride = new[] { frame.Width * 4, 0, 0, 0 };

            var targetData = new byte*[4];
            targetData[0] = _frame->data[0];
            targetData[1] = _frame->data[1];
            targetData[2] = _frame->data[2];
            var targetStride = new[] { _frame->linesize[0], _frame->linesize[1], _frame->linesize[2], 0 };

            ffmpeg.sws_scale(_scaler, sourceData, sourceStride, 0, frame.Height, targetData, targetStride);
        }
    }

    private void EnsureScaler(int sourceWidth, int sourceHeight, int width, int height)
    {
        if (_scaler is not null
            && _sourceWidth == sourceWidth && _sourceHeight == sourceHeight
            && _outputWidth == width && _outputHeight == height)
        {
            return;
        }

        if (_scaler is not null) ffmpeg.sws_freeContext(_scaler);

        _scaler = ffmpeg.sws_getContext(
            sourceWidth, sourceHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
            width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_scaler is null) throw new InvalidOperationException("sws_getContext failed.");

        _sourceWidth = sourceWidth;
        _sourceHeight = sourceHeight;
    }

    private void EnsureContext(int width, int height, VideoQuality quality)
    {
        if (_context is not null
            && _outputWidth == width && _outputHeight == height
            && _configuredFps == quality.FramesPerSecond
            && _configuredBitrate == quality.TargetBitsPerSecond)
        {
            return;
        }

        ReleaseCodec();

        _context = OpenContext(Name, width, height, quality.FramesPerSecond, quality.TargetBitsPerSecond);
        _outputWidth = width;
        _outputHeight = height;
        _configuredFps = quality.FramesPerSecond;
        _configuredBitrate = quality.TargetBitsPerSecond;
        _pts = 0;

        // Whatever the reason for the new context, viewers are holding reference frames for the
        // old one. The next sample has to be decodable on its own.
        _forceKeyFrame = true;

        _frame = ffmpeg.av_frame_alloc();
        if (_frame is null) throw new InvalidOperationException("av_frame_alloc failed.");
        _frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        _frame->width = width;
        _frame->height = height;
        FFmpegError.Check(ffmpeg.av_frame_get_buffer(_frame, 32), "av_frame_get_buffer");

        _packet = ffmpeg.av_packet_alloc();
        if (_packet is null) throw new InvalidOperationException("av_packet_alloc failed.");

        // Dimensions changed, so the scaler's target is stale.
        if (_scaler is not null)
        {
            ffmpeg.sws_freeContext(_scaler);
            _scaler = null;
        }
    }

    private bool TryOpenProbe(string codecName, out string reason)
    {
        AVCodecContext* probe = null;
        try
        {
            probe = OpenContext(codecName, ProbeWidth, ProbeHeight, 30, 4_000_000);
            reason = string.Empty;
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or DllNotFoundException
                                      or EntryPointNotFoundException)
        {
            reason = e.Message;
            return false;
        }
        finally
        {
            if (probe is not null) ffmpeg.avcodec_free_context(&probe);
        }
    }

    private AVCodecContext* OpenContext(string codecName, int width, int height, int fps, int bitrate)
    {
        var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
        if (codec is null) throw new InvalidOperationException("not built into this FFmpeg");

        var context = ffmpeg.avcodec_alloc_context3(codec);
        if (context is null) throw new InvalidOperationException("avcodec_alloc_context3 failed");

        try
        {
            context->width = width;
            context->height = height;
            context->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            context->time_base = new AVRational { num = 1, den = fps };
            context->framerate = new AVRational { num = fps, den = 1 };
            context->bit_rate = bitrate;
            context->rc_max_rate = bitrate;
            context->rc_buffer_size = bitrate / 2;
            context->gop_size = EffectivelyInfiniteGop;
            context->max_b_frames = 0;
            context->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;

            ApplyLowLatencyOptions(codecName, context);

            var opened = ffmpeg.avcodec_open2(context, codec, null);
            if (opened < 0)
                throw new InvalidOperationException($"avcodec_open2 failed ({FFmpegError.Describe(opened)})");

            _codec = codec;
            var result = context;
            context = null;
            return result;
        }
        finally
        {
            if (context is not null) ffmpeg.avcodec_free_context(&context);
        }
    }

    private static void ApplyLowLatencyOptions(string codecName, AVCodecContext* context)
    {
        // Every option here is best-effort: av_opt_set on an unknown key is an error we ignore,
        // because the alternative is refusing an encoder over a tuning knob.
        var options = context->priv_data;
        switch (codecName)
        {
            case "h264_nvenc":
                ffmpeg.av_opt_set(options, "preset", "p1", 0);
                ffmpeg.av_opt_set(options, "tune", "ull", 0);
                ffmpeg.av_opt_set(options, "rc", "cbr", 0);
                ffmpeg.av_opt_set(options, "delay", "0", 0);
                ffmpeg.av_opt_set(options, "zerolatency", "1", 0);
                // Without this NVENC treats AV_PICTURE_TYPE_I as "force intra", which produces
                // an I-frame that is not an IDR: viewers that lost sync stay broken and the
                // packet is not even flagged as a keyframe.
                ffmpeg.av_opt_set(options, "forced-idr", "1", 0);
                break;

            case "h264_qsv":
                ffmpeg.av_opt_set(options, "preset", "veryfast", 0);
                ffmpeg.av_opt_set(options, "async_depth", "1", 0);
                ffmpeg.av_opt_set(options, "forced_idr", "1", 0);
                break;

            case "h264_amf":
                ffmpeg.av_opt_set(options, "usage", "ultralowlatency", 0);
                ffmpeg.av_opt_set(options, "quality", "speed", 0);
                ffmpeg.av_opt_set(options, "forced_idr", "1", 0);
                break;

            case "libx264":
                ffmpeg.av_opt_set(options, "preset", "veryfast", 0);
                ffmpeg.av_opt_set(options, "tune", "zerolatency", 0);
                // Scene-change detection would insert keyframes nobody asked for; on a screen
                // every window switch looks like a cut.
                ffmpeg.av_opt_set(options, "x264-params", "scenecut=0:open-gop=0", 0);
                break;
        }
    }

    private void ReleaseCodec()
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
    }

    private void ReleaseNative()
    {
        ReleaseCodec();

        if (_scaler is not null)
        {
            ffmpeg.sws_freeContext(_scaler);
            _scaler = null;
        }

        _codec = null;
    }
}

internal static unsafe class FFmpegError
{
    internal static void Check(int code, string operation)
    {
        if (code >= 0) return;
        throw new InvalidOperationException($"{operation} failed ({Describe(code)}).");
    }

    internal static string Describe(int code)
    {
        const int bufferSize = 256;
        var buffer = stackalloc byte[bufferSize];
        return ffmpeg.av_strerror(code, buffer, bufferSize) == 0
            ? $"{code}: {new string((sbyte*)buffer)}"
            : code.ToString();
    }
}
