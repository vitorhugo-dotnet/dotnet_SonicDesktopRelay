using System.Runtime.Versioning;
using SonicDesktopRelay.ApiClient;
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Media.Windows;
using SonicDesktopRelay.Presentation;
using SonicDesktopRelay.Rtc;
using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.App;

/// <summary>
/// The real media stack behind <see cref="IVideoWatchHost"/>: one decoder, one pipeline and
/// one receive-only peer per session, because a viewer has exactly one publisher.
/// <para>
/// Built here rather than in Presentation for the same reason as the publish host — this is
/// the only assembly that may know about FFmpeg and SIPSorcery.
/// </para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class RtcVideoWatchHost(
    IceApiClient iceApi,
    Func<ISignalingConnection?> signaling) : IVideoWatchHost
{
    /// <summary>
    /// How often the watchdog looks. The pipeline decides what counts as a stall; this only
    /// decides how quickly it notices, and a second is well under the four it waits for.
    /// </summary>
    private static readonly TimeSpan StallCheckInterval = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private ScreenWatchPipeline? _pipeline;
    private VideoSubscriber? _subscriber;
    private ITimer? _watchdog;

    public string? DecoderName { get; private set; }

    /// <summary>Why the media stack could not start, when it could not. Shown in Diagnostics.</summary>
    public string? StartFailure { get; private set; }

    /// <summary>Each decoder candidate that was rejected, with the reason FFmpeg gave.</summary>
    public IReadOnlyList<string> DecoderRejections { get; private set; } = [];

    public event Action<WatchState>? WatchStateChanged;

    /// <summary>
    /// One decoded frame, on the decode thread. The shell marshals it to the UI thread before
    /// anything touches a bitmap.
    /// </summary>
    public event Action<VideoFrame>? FrameDecoded;

    public async Task StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_pipeline is not null) return;

            StartFailure = null;

            var connection = signaling()
                             ?? throw new InvalidOperationException(
                                 "Signaling must be connected before watching starts.");

            var decoder = new FFmpegH264Decoder();
            DecoderName = decoder.Name;
            DecoderRejections = decoder.RejectionLog;

            var pipeline = new ScreenWatchPipeline(decoder, TimeProvider.System);
            pipeline.FrameDecoded += OnFrame;
            pipeline.StateChanged += OnState;

            var subscriber = new VideoSubscriber(
                pipeline,
                new SipSorceryViewerPeerConnectionFactory(await LoadIceAsync(ct)),
                connection);

            // The pipeline deliberately holds no clock of its own; something outside has to
            // ask it whether the media has gone quiet.
            _watchdog = TimeProvider.System.CreateTimer(
                _ => pipeline.CheckForStall(), null, StallCheckInterval, StallCheckInterval);

            _pipeline = pipeline;
            _subscriber = subscriber;
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException
                                      or HttpRequestException or ApiException)
        {
            // Watching without a decoder is not recoverable, but the session is already up:
            // record why and let Diagnostics say it out loud rather than taking the app down.
            StartFailure = e.Message;
            await DisposeStackAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DisposeStackAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task HandleSignalingAsync(SignalingEnvelope envelope, CancellationToken ct) =>
        _subscriber?.HandleAsync(envelope, ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

    private void OnFrame(VideoFrame frame) => FrameDecoded?.Invoke(frame);

    private void OnState(WatchState state) => WatchStateChanged?.Invoke(state);

    private async Task<IceServerSettings> LoadIceAsync(CancellationToken ct)
    {
        var response = await iceApi.GetIceServersAsync(ct);
        var servers = response.IceServers
            .SelectMany(x => x.Urls.Select(url => new IceServer(url, x.Username, x.Credential)))
            .ToList();
        return new IceServerSettings(servers, ForceRelay: false);
    }

    private async Task DisposeStackAsync()
    {
        if (_watchdog is not null)
        {
            await _watchdog.DisposeAsync();
            _watchdog = null;
        }

        if (_subscriber is not null)
        {
            await _subscriber.DisposeAsync();
            _subscriber = null;
        }

        if (_pipeline is not null)
        {
            _pipeline.FrameDecoded -= OnFrame;
            _pipeline.StateChanged -= OnState;
            // Disposing the pipeline disposes the decoder with it.
            _pipeline.Dispose();
            _pipeline = null;
        }
    }
}
