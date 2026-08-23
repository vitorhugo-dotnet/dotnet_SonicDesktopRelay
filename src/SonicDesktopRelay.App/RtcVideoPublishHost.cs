using System.Runtime.Versioning;
using SonicDesktopRelay.ApiClient;
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Media.Windows;
using SonicDesktopRelay.Presentation;
using SonicDesktopRelay.Rtc;
using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.App;

/// <summary>
/// The real media stack behind <see cref="IVideoPublishHost"/>: one capture source, one
/// encoder and one pipeline per session, with a <see cref="VideoPublisher"/> fanning the
/// single encoded stream out to however many viewers turn up.
/// <para>
/// Built here rather than in Presentation because this is the only assembly that may know
/// about Windows.Graphics.Capture, FFmpeg and SIPSorcery.
/// </para>
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class RtcVideoPublishHost(
    IceApiClient iceApi,
    Func<ISignalingConnection?> signaling) : IVideoPublishHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ScreenPublishPipeline? _pipeline;
    private VideoPublisher? _publisher;

    public string? EncoderName { get; private set; }

    /// <summary>Why the media stack could not start, when it could not. Shown in Diagnostics.</summary>
    public string? StartFailure { get; private set; }

    /// <summary>Each encoder candidate that was rejected, with the reason FFmpeg gave.</summary>
    public IReadOnlyList<string> EncoderRejections { get; private set; } = [];

    public async Task StartAsync(MonitorInfo monitor, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_pipeline is not null) return;

            StartFailure = null;

            var connection = signaling()
                             ?? throw new InvalidOperationException(
                                 "Signaling must be connected before publishing starts.");

            var encoder = new FFmpegH264Encoder();
            EncoderName = encoder.Name;
            EncoderRejections = encoder.RejectionLog;

            var capture = new GraphicsCaptureScreenSource();
            var pipeline = new ScreenPublishPipeline(capture, encoder);
            var publisher = new VideoPublisher(pipeline, new SipSorceryPeerConnectionFactory(
                await LoadIceAsync(ct)), connection);

            await pipeline.StartAsync(monitor, ct);
            _pipeline = pipeline;
            _publisher = publisher;
        }
        catch (Exception e) when (e is InvalidOperationException or PlatformNotSupportedException
                                      or HttpRequestException or ApiException)
        {
            // Sharing without a working capture or encoder is not recoverable, but the session
            // itself is already up: record why and let Diagnostics say it out loud rather than
            // taking the app down.
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

    public Task AddViewerAsync(Guid participantId, CancellationToken ct) =>
        _publisher?.AddViewerAsync(participantId, ct) ?? Task.CompletedTask;

    public Task RemoveViewerAsync(Guid participantId) =>
        _publisher?.RemoveViewerAsync(participantId) ?? Task.CompletedTask;

    public Task HandleSignalingAsync(SignalingEnvelope envelope, CancellationToken ct) =>
        _publisher?.HandleAsync(envelope, ct) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }

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
        if (_publisher is not null)
        {
            await _publisher.DisposeAsync();
            _publisher = null;
        }

        if (_pipeline is not null)
        {
            // Disposing the pipeline disposes the capture source and the encoder with it, so
            // the GPU encode session is released the moment the share stops.
            await _pipeline.DisposeAsync();
            _pipeline = null;
        }
    }
}
