# Watch a shared screen (phase 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The watching machine joins a session with a code, receives the publisher's H.264 video, decodes it, and renders it in the app at full size without tearing or leaking memory.

**Architecture:** The mirror image of phase 2. `Media` gains a decoder contract; `Media.Windows` gains the FFmpeg decoder; `Rtc` gains a receive-only peer connection and the viewer half of negotiation; the App gains one Avalonia control that blits decoded frames into a recycled `WriteableBitmap`.

**Tech Stack:** .NET 10, SIPSorcery 10.0.16, SIPSorceryMedia.FFmpeg 10.0.16 (FFmpeg 8.1 ABI), Avalonia 12.1.1, xunit.

**Spec:** `docs/superpowers/specs/2026-08-23-sonicdesktoprelay-design.md`

**Depends on:** phase 2 (`2026-08-23-publish-video.md`). `IVideoEncoder`, `IPeerConnection`, `VideoQuality`, `EncodedVideoSample` and `IVideoPublishHost` all exist.

## Global Constraints

- **No audio in this phase.** The publisher does not send an audio track until phase 4, so a viewer-side audio path would have nothing to play. Video only.
- One decoder per session — a viewer has exactly one publisher.
- `Media`, `Rtc` and every other library stay `net10.0`. Only `Media.Windows`, `Media.Windows.Tests` and `App` carry `net10.0-windows10.0.19041.0` — the App must, because a `net10.0` project cannot reference a `net10.0-windows` one (NU1201).
- FFmpeg 8.1 shared libraries as in phase 2; `FFmpegLoader` already exists and must be reused, not reimplemented.
- **Never allocate a bitmap per frame.** At 30 fps and 1080p that is roughly 250 MB/s of garbage. One `WriteableBitmap`, reused, recreated only when the frame size changes.
- Rendering must happen on the UI thread; decoding must not.
- Never log SDP, ICE candidates, or frame contents.
- `TreatWarningsAsErrors` is on.
- Run all tests: `dotnet test SonicDesktopRelay.sln`

## File Structure

| File | Responsibility |
|---|---|
| `src/SonicDesktopRelay.Media/IVideoDecoder.cs` | Decoder contract |
| `src/SonicDesktopRelay.Media/ScreenWatchPipeline.cs` | sample → decode → one frame event, plus the stall watchdog |
| `src/SonicDesktopRelay.Media.Windows/FFmpegH264Decoder.cs` | H.264 decode, hardware when available |
| `src/SonicDesktopRelay.Rtc/ViewerPeerConnection.cs` | recvonly peer, answer side of negotiation |
| `src/SonicDesktopRelay.Rtc/VideoSubscriber.cs` | The viewer half: one publisher, offer→answer→ICE |
| `src/SonicDesktopRelay.App/Controls/VideoSurface.cs` | Avalonia control, recycled WriteableBitmap |
| `tests/SonicDesktopRelay.Media.Tests/ScreenWatchPipelineTests.cs` | Pipeline and watchdog, with fakes |
| `tests/SonicDesktopRelay.Rtc.Tests/VideoSubscriberTests.cs` | Negotiation, with fakes |
| `tests/SonicDesktopRelay.Media.Windows.Tests/FFmpegH264DecoderTests.cs` | Round-trip through the real codec |

---

### Task 1: Decoder contract and watch pipeline

**Files:**
- Create: `src/SonicDesktopRelay.Media/IVideoDecoder.cs`, `src/SonicDesktopRelay.Media/ScreenWatchPipeline.cs`
- Create: `tests/SonicDesktopRelay.Media.Tests/ScreenWatchPipelineTests.cs`

**Interfaces:**
- Consumes: `EncodedVideoSample`, `VideoFrame` (phase 2 Task 1).
- Produces:
  - `interface IVideoDecoder : IDisposable { string Name { get; } VideoFrame? Decode(EncodedVideoSample sample); }`
  - `enum WatchState { Waiting, Receiving, Stalled, Failed }`
  - `sealed class ScreenWatchPipeline(IVideoDecoder decoder, TimeProvider time) : IDisposable` with `event Action<VideoFrame>? FrameDecoded`, `event Action<WatchState>? StateChanged`, `event Action? KeyFrameNeeded`, `WatchState State { get; }`, `string DecoderName { get; }`, `void Submit(EncodedVideoSample sample)`, `void CheckForStall()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Time.Testing;
using SonicDesktopRelay.Media;
using Xunit;

namespace SonicDesktopRelay.Media.Tests;

public sealed class ScreenWatchPipelineTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_fresh_pipeline_is_waiting_for_the_first_frame()
    {
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), new FakeTimeProvider(Start));

        Assert.Equal(WatchState.Waiting, pipeline.State);
    }

    [Fact]
    public void A_decoded_sample_is_published_and_moves_the_state_to_receiving()
    {
        var time = new FakeTimeProvider(Start);
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), time);
        var frames = new List<VideoFrame>();
        pipeline.FrameDecoded += frames.Add;

        pipeline.Submit(Sample());

        Assert.Single(frames);
        Assert.Equal(WatchState.Receiving, pipeline.State);
    }

    [Fact]
    public void A_sample_the_decoder_swallows_publishes_nothing()
    {
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder { ReturnNull = true },
            new FakeTimeProvider(Start));
        var frames = 0;
        pipeline.FrameDecoded += _ => frames++;

        pipeline.Submit(Sample());

        Assert.Equal(0, frames);
        // Still waiting: a decoder buffering its first frames has not failed.
        Assert.Equal(WatchState.Waiting, pipeline.State);
    }

    [Fact]
    public void No_frame_for_five_seconds_is_reported_as_stalled_not_as_disconnected()
    {
        var time = new FakeTimeProvider(Start);
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), time);
        pipeline.Submit(Sample());

        time.Advance(TimeSpan.FromSeconds(5));
        pipeline.CheckForStall();

        // The peer connection can be perfectly healthy while the media has stopped. Calling
        // that "disconnected" sends the user to debug the wrong thing.
        Assert.Equal(WatchState.Stalled, pipeline.State);
    }

    [Fact]
    public void A_stall_asks_the_publisher_for_a_keyframe()
    {
        var time = new FakeTimeProvider(Start);
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), time);
        var requests = 0;
        pipeline.KeyFrameNeeded += () => requests++;
        pipeline.Submit(Sample());

        time.Advance(TimeSpan.FromSeconds(5));
        pipeline.CheckForStall();

        Assert.Equal(1, requests);
    }

    [Fact]
    public void A_frame_arriving_after_a_stall_returns_to_receiving()
    {
        var time = new FakeTimeProvider(Start);
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), time);
        pipeline.Submit(Sample());
        time.Advance(TimeSpan.FromSeconds(5));
        pipeline.CheckForStall();

        pipeline.Submit(Sample());

        Assert.Equal(WatchState.Receiving, pipeline.State);
    }

    [Fact]
    public void A_brief_gap_is_not_a_stall()
    {
        var time = new FakeTimeProvider(Start);
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), time);
        pipeline.Submit(Sample());

        time.Advance(TimeSpan.FromSeconds(2));
        pipeline.CheckForStall();

        Assert.Equal(WatchState.Receiving, pipeline.State);
    }

    [Fact]
    public void Repeated_stall_checks_ask_for_only_one_keyframe()
    {
        var time = new FakeTimeProvider(Start);
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), time);
        var requests = 0;
        pipeline.KeyFrameNeeded += () => requests++;
        pipeline.Submit(Sample());
        time.Advance(TimeSpan.FromSeconds(5));

        pipeline.CheckForStall();
        pipeline.CheckForStall();
        pipeline.CheckForStall();

        // Asking once per tick would flood the publisher with PLIs precisely when the link
        // is already struggling.
        Assert.Equal(1, requests);
    }

    [Fact]
    public void A_decoder_that_throws_fails_the_pipeline_once()
    {
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder { Throw = true },
            new FakeTimeProvider(Start));
        var decoder = 0;
        pipeline.StateChanged += s => { if (s == WatchState.Failed) decoder++; };

        pipeline.Submit(Sample());
        pipeline.Submit(Sample());

        Assert.Equal(WatchState.Failed, pipeline.State);
        Assert.Equal(1, decoder);
    }

    [Fact]
    public void The_decoder_name_is_exposed_for_diagnostics()
    {
        using var pipeline = new ScreenWatchPipeline(new FakeDecoder(), new FakeTimeProvider(Start));

        Assert.Equal("fake", pipeline.DecoderName);
    }

    private static EncodedVideoSample Sample() =>
        new(new byte[8], TimeSpan.Zero, true, 1920, 1080);

    private sealed class FakeDecoder : IVideoDecoder
    {
        public string Name => "fake";

        public bool ReturnNull { get; init; }

        public bool Throw { get; init; }

        public VideoFrame? Decode(EncodedVideoSample sample)
        {
            if (Throw) throw new InvalidOperationException("decoder failed");
            return ReturnNull ? null : new VideoFrame(sample.Width, sample.Height, new byte[16], sample.Timestamp);
        }

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~ScreenWatchPipelineTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 3: Implement the contract**

`src/SonicDesktopRelay.Media/IVideoDecoder.cs`:

```csharp
namespace SonicDesktopRelay.Media;

public interface IVideoDecoder : IDisposable
{
    /// <summary>The decoder actually in use, for the Diagnostics page.</summary>
    string Name { get; }

    /// <summary>Returns null when this sample produced no frame yet (decoder buffering).</summary>
    VideoFrame? Decode(EncodedVideoSample sample);
}
```

- [ ] **Step 4: Implement the pipeline**

`src/SonicDesktopRelay.Media/ScreenWatchPipeline.cs`:

```csharp
namespace SonicDesktopRelay.Media;

public enum WatchState
{
    Waiting,
    Receiving,

    /// <summary>
    /// The connection is up but no frame has arrived for a while. Distinct from disconnected
    /// on purpose: the two have different causes and different fixes, and conflating them
    /// sends the user looking in the wrong place.
    /// </summary>
    Stalled,

    Failed
}

public sealed class ScreenWatchPipeline(IVideoDecoder decoder, TimeProvider time) : IDisposable
{
    private static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(4);

    private DateTimeOffset? _lastFrameAt;
    private bool _keyFrameAsked;
    private WatchState _state = WatchState.Waiting;

    public event Action<VideoFrame>? FrameDecoded;

    public event Action<WatchState>? StateChanged;

    public event Action? KeyFrameNeeded;

    public WatchState State => _state;

    public string DecoderName => decoder.Name;

    public void Submit(EncodedVideoSample sample)
    {
        if (_state == WatchState.Failed) return;

        VideoFrame? frame;
        try
        {
            frame = decoder.Decode(sample);
        }
        catch (Exception)
        {
            SetState(WatchState.Failed);
            return;
        }

        if (frame is null) return;

        _lastFrameAt = time.GetUtcNow();
        _keyFrameAsked = false;
        SetState(WatchState.Receiving);
        FrameDecoded?.Invoke(frame);
    }

    /// <summary>Called on a timer by the host; keeps the clock out of this class.</summary>
    public void CheckForStall()
    {
        if (_state is WatchState.Failed or WatchState.Waiting) return;
        if (_lastFrameAt is not { } last) return;
        if (time.GetUtcNow() - last < StallAfter) return;

        SetState(WatchState.Stalled);

        // One PLI per stall, not one per tick: flooding the publisher with keyframe requests
        // is the worst thing to do to a link that is already failing to deliver.
        if (_keyFrameAsked) return;
        _keyFrameAsked = true;
        KeyFrameNeeded?.Invoke();
    }

    private void SetState(WatchState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose() => decoder.Dispose();
}
```

- [ ] **Step 5: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~ScreenWatchPipelineTests"`
Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(media): watch pipeline with decode and stall detection"
```

---

### Task 2: The viewer half of negotiation

**Files:**
- Create: `src/SonicDesktopRelay.Rtc/IViewerPeerConnection.cs`, `src/SonicDesktopRelay.Rtc/VideoSubscriber.cs`
- Create: `tests/SonicDesktopRelay.Rtc.Tests/VideoSubscriberTests.cs`

**Interfaces:**
- Consumes: `ScreenWatchPipeline` (Task 1), `ISignalingConnection`, `SignalingEnvelope`, `SignalingMessageTypes`.
- Produces:
  - `interface IViewerPeerConnection : IAsyncDisposable { Task<string> CreateAnswerAsync(string offerSdp, CancellationToken ct); Task AddIceCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct); void RequestKeyFrame(); event Action<string, string?, int?>? IceCandidateGathered; event Action<EncodedVideoSample>? VideoSampleReceived; }`
  - `interface IViewerPeerConnectionFactory { IViewerPeerConnection Create(); }`
  - `sealed class VideoSubscriber(ScreenWatchPipeline pipeline, IViewerPeerConnectionFactory peers, ISignalingConnection signaling) : IAsyncDisposable` with `Guid? PublisherId { get; }`, `Task HandleAsync(SignalingEnvelope envelope, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Signaling;
using Xunit;

namespace SonicDesktopRelay.Rtc.Tests;

public sealed class VideoSubscriberTests
{
    private static readonly Guid Publisher = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96401");
    private static readonly Guid Stranger = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96402");

    [Fact]
    public async Task Publisher_ready_learns_the_publisher_and_answers_viewer_ready()
    {
        var harness = new Harness();

        await harness.Subscriber.HandleAsync(Frame(SignalingMessageTypes.PublisherReady, Publisher, "{}"),
            CancellationToken.None);

        Assert.Equal(Publisher, harness.Subscriber.PublisherId);
        var sent = Assert.Single(harness.Signaling.Sent);
        Assert.Equal(SignalingMessageTypes.ViewerReady, sent.Type);
        Assert.Equal(Publisher, sent.To);
    }

    [Fact]
    public async Task The_publisher_identity_comes_from_the_authenticated_from_field()
    {
        var harness = new Harness();

        // The payload is attacker-controlled; `from` is the server's word. Only `from` counts.
        await harness.Subscriber.HandleAsync(
            Frame(SignalingMessageTypes.PublisherReady, Publisher, $$"""{"participantId":"{{Stranger}}"}"""),
            CancellationToken.None);

        Assert.Equal(Publisher, harness.Subscriber.PublisherId);
    }

    [Fact]
    public async Task An_offer_produces_an_answer_addressed_to_the_publisher()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        harness.Signaling.Sent.Clear();

        await harness.Subscriber.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcOffer, Publisher, """{"type":"offer","sdp":"offer-sdp"}"""),
            CancellationToken.None);

        var sent = Assert.Single(harness.Signaling.Sent);
        Assert.Equal(SignalingMessageTypes.WebRtcAnswer, sent.Type);
        Assert.Equal(Publisher, sent.To);
        Assert.Equal("offer-sdp", harness.Peers.Created!.ReceivedOffer);
    }

    [Fact]
    public async Task An_offer_from_someone_who_is_not_the_publisher_is_ignored()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        harness.Signaling.Sent.Clear();

        await harness.Subscriber.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcOffer, Stranger, """{"type":"offer","sdp":"offer-sdp"}"""),
            CancellationToken.None);

        Assert.Empty(harness.Signaling.Sent);
    }

    [Fact]
    public async Task An_ice_candidate_from_the_publisher_reaches_the_peer()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        await harness.OfferAsync();

        await harness.Subscriber.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcIceCandidate, Publisher,
                """{"candidate":"candidate:1","sdpMid":"0","sdpMLineIndex":0}"""),
            CancellationToken.None);

        Assert.Single(harness.Peers.Created!.RemoteCandidates);
    }

    [Fact]
    public async Task A_gathered_candidate_is_signalled_to_the_publisher()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        await harness.OfferAsync();
        harness.Signaling.Sent.Clear();

        harness.Peers.Created!.GatherCandidate("candidate:2", "0", 0);

        var sent = Assert.Single(harness.Signaling.Sent);
        Assert.Equal(SignalingMessageTypes.WebRtcIceCandidate, sent.Type);
        Assert.Equal(Publisher, sent.To);
    }

    [Fact]
    public async Task A_received_video_sample_is_decoded_and_rendered()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        await harness.OfferAsync();
        var frames = 0;
        harness.Pipeline.FrameDecoded += _ => frames++;

        harness.Peers.Created!.ReceiveVideo(new EncodedVideoSample(new byte[8], TimeSpan.Zero, true, 1920, 1080));

        Assert.Equal(1, frames);
    }

    [Fact]
    public async Task A_stalled_pipeline_sends_a_keyframe_request_to_the_peer()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        await harness.OfferAsync();

        harness.Pipeline.RaiseKeyFrameNeeded();

        Assert.Equal(1, harness.Peers.Created!.KeyFrameRequests);
    }

    [Fact]
    public async Task A_renegotiation_offer_replaces_the_previous_description_on_the_same_peer()
    {
        var harness = new Harness();
        await harness.ReadyAsync();
        await harness.OfferAsync();
        var first = harness.Peers.Created;

        await harness.Subscriber.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcOffer, Publisher, """{"type":"offer","sdp":"second-sdp"}"""),
            CancellationToken.None);

        // The publisher renegotiates when the monitor resolution changes; that must not build
        // a second peer connection and leak the first.
        Assert.Same(first, harness.Peers.Created);
        Assert.Equal("second-sdp", harness.Peers.Created!.ReceivedOffer);
    }
}
```

Write `Harness`, `FakeViewerPeerFactory`, `FakeViewerPeer` and `FakeSignaling` in the same file,
following the shapes already used in `VideoPublisherTests`. `Harness.ReadyAsync()` sends
`publisher.ready`; `Harness.OfferAsync()` sends an offer. Expose
`ScreenWatchPipeline.RaiseKeyFrameNeeded()` for the test by having the harness hold a fake
decoder and drive `CheckForStall` through a `FakeTimeProvider`, rather than adding a test-only
method to production code.

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~VideoSubscriberTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 3: Implement**

`VideoSubscriber` mirrors `VideoPublisher`:

- Learns `PublisherId` from the **authenticated `from`** of `publisher.ready` and replies `viewer.ready`.
- Creates its single peer lazily on the first offer; a later offer is a renegotiation applied to that same peer.
- Ignores every frame whose `from` is not `PublisherId` — a session can hold other viewers, and none of them may drive this connection.
- Forwards received samples into the pipeline, and the pipeline's `KeyFrameNeeded` back to the peer.

- [ ] **Step 4: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~VideoSubscriberTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(rtc): viewer-side negotiation and inbound video"
```

---

### Task 3: SIPSorcery receive-only peer

**Files:**
- Create: `src/SonicDesktopRelay.Rtc/SipSorceryViewerPeerConnection.cs`
- Create: `tests/SonicDesktopRelay.Rtc.Tests/SipSorceryViewerPeerConnectionTests.cs`

**Interfaces:**
- Consumes: `IViewerPeerConnection`, `IceServerSettings` (phase 2 Task 4).
- Produces: `sealed class SipSorceryViewerPeerConnectionFactory(IceServerSettings ice) : IViewerPeerConnectionFactory`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task The_answer_accepts_a_recvonly_h264_video_track()
{
    var factory = new SipSorceryViewerPeerConnectionFactory(Ice);
    await using var peer = factory.Create();

    var answer = await peer.CreateAnswerAsync(PublisherOfferSdp, CancellationToken.None);

    Assert.Contains("m=video", answer);
    Assert.Contains("H264", answer, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("a=recvonly", answer);
}

[Fact]
public async Task A_malformed_offer_is_rejected_with_a_clear_failure()
{
    var factory = new SipSorceryViewerPeerConnectionFactory(Ice);
    await using var peer = factory.Create();

    await Assert.ThrowsAnyAsync<Exception>(
        () => peer.CreateAnswerAsync("this is not sdp", CancellationToken.None));
}

[Fact]
public async Task Requesting_a_keyframe_before_connection_does_not_throw()
{
    var factory = new SipSorceryViewerPeerConnectionFactory(Ice);
    await using var peer = factory.Create();

    Assert.Null(Record.Exception(peer.RequestKeyFrame));
}
```

`PublisherOfferSdp` is a `const string` holding a real H.264 sendonly offer. Generate it once
with `SipSorceryPeerConnectionFactory` from phase 2 and paste it in, rather than hand-writing
SDP — a hand-written one tests the test, not the code.

- [ ] **Step 2: Run and verify failure, then implement**

The peer sets a `MediaStreamTrack(VideoFormat(H264, 96), MediaStreamStatusEnum.RecvOnly)`,
answers with `createAnswer`, and raises `VideoSampleReceived` from
`OnVideoFrameReceived`/`OnRtpPacketReceived` (whichever SIPSorcery 10 exposes for encoded
access — prefer the encoded callback; decoding is this project's job, not the library's).
`RequestKeyFrame` sends a PLI through the peer connection's RTCP path.

- [ ] **Step 3: Run, verify, commit**

```bash
git add src tests
git commit -m "feat(rtc): SIPSorcery receive-only viewer peer"
```

---

### Task 4: FFmpeg H.264 decoder

**Files:**
- Create: `src/SonicDesktopRelay.Media.Windows/FFmpegH264Decoder.cs`
- Create: `tests/SonicDesktopRelay.Media.Windows.Tests/FFmpegH264DecoderTests.cs`

**Interfaces:**
- Consumes: `IVideoDecoder` (Task 1), `FFmpegLoader` (phase 2 Task 6).
- Produces: `sealed class FFmpegH264Decoder : IVideoDecoder`.

- [ ] **Step 1: Write the failing tests**

The strongest test available is a round trip through the real codec: encode with the phase-2
encoder, decode with this one, and check the frame comes back at the right size.

```csharp
[Fact]
public void A_frame_encoded_by_the_publisher_decodes_back_to_the_same_size()
{
    if (!FFmpegLoader.TryInitialise(out _)) return;

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
    if (!FFmpegLoader.TryInitialise(out _)) return;

    using var encoder = new FFmpegH264Encoder();
    using var decoder = new FFmpegH264Decoder();
    Decode(encoder, decoder, 1280, 720);

    var frame = Decode(encoder, decoder, 640, 360);

    Assert.Equal(640, frame!.Width);
}

[Fact]
public void Garbage_input_returns_null_rather_than_throwing()
{
    if (!FFmpegLoader.TryInitialise(out _)) return;

    using var decoder = new FFmpegH264Decoder();

    var frame = decoder.Decode(new EncodedVideoSample(new byte[] { 1, 2, 3, 4 }, TimeSpan.Zero, false, 16, 16));

    // A corrupt packet is a normal event on a lossy link; it must not end the session.
    Assert.Null(frame);
}

[Fact]
public void The_decoder_is_named_for_diagnostics()
{
    if (!FFmpegLoader.TryInitialise(out _)) return;

    using var decoder = new FFmpegH264Decoder();

    Assert.Contains("264", decoder.Name);
}
```

- [ ] **Step 2: Implement**

Opens an H.264 decoder context, preferring a hardware decoder (`d3d11va`) and falling back to
software. Converts the decoded YUV to BGRA with `sws_scale` into a **reused** buffer sized to
the current frame — reallocating per frame is the same 250 MB/s mistake as reallocating
bitmaps. Recreates the scaler when dimensions change. Frees every unmanaged handle in
`Dispose`.

- [ ] **Step 3: Run, verify, commit**

```bash
git add src tests
git commit -m "feat(media): FFmpeg H.264 decoder with reused conversion buffers"
```

---

### Task 5: The Avalonia video surface

**Files:**
- Create: `src/SonicDesktopRelay.App/Controls/VideoSurface.cs`
- Modify: `src/SonicDesktopRelay.App/Views/WatchView.axaml`, `Shell.cs`
- Create: `tests/SonicDesktopRelay.Presentation.Tests/VideoSurfaceGeometryTests.cs`

**Interfaces:**
- Consumes: `VideoFrame` (phase 2 Task 1).
- Produces: `sealed class VideoSurface : Control` with `void Present(VideoFrame frame)`; and a pure `static class LetterboxGeometry` with `static Rect Fit(Size source, Size available)` in `Presentation` so the maths is testable without a UI.

- [ ] **Step 1: Write the failing tests**

Test the geometry, not the rendering — the maths is where the bugs live, and it needs no window.

```csharp
[Theory]
// 16:9 source into a wider viewport: bars left and right.
[InlineData(1920, 1080, 1000, 500, 888.88, 500, 55.55, 0)]
// 16:9 source into a taller viewport: bars top and bottom.
[InlineData(1920, 1080, 800, 800, 800, 450, 0, 175)]
// Exact match: no bars.
[InlineData(1920, 1080, 1920, 1080, 1920, 1080, 0, 0)]
public void The_picture_is_letterboxed_never_stretched(
    double sourceWidth, double sourceHeight, double availableWidth, double availableHeight,
    double expectedWidth, double expectedHeight, double expectedX, double expectedY)
{
    var rect = LetterboxGeometry.Fit(
        new Size(sourceWidth, sourceHeight), new Size(availableWidth, availableHeight));

    Assert.Equal(expectedWidth, rect.Width, 1);
    Assert.Equal(expectedHeight, rect.Height, 1);
    Assert.Equal(expectedX, rect.X, 1);
    Assert.Equal(expectedY, rect.Y, 1);
}

[Fact]
public void A_zero_sized_viewport_produces_an_empty_rect_rather_than_dividing_by_zero()
{
    var rect = LetterboxGeometry.Fit(new Size(1920, 1080), new Size(0, 0));

    Assert.Equal(0, rect.Width);
    Assert.Equal(0, rect.Height);
}
```

- [ ] **Step 2: Implement**

`LetterboxGeometry.Fit` picks the smaller of the width and height scale factors and centres
the result. `VideoSurface` holds one `WriteableBitmap`, recreated only when the incoming frame
size differs from the current one; `Present` locks the bitmap, copies the BGRA rows in, and
calls `InvalidateVisual`. `Render` draws the bitmap into `LetterboxGeometry.Fit(...)`.

`Present` must be called on the UI thread; `Shell` marshals with `Dispatcher.UIThread.Post`,
as it already does for snapshots.

`WatchView` hosts the surface, with `F11` toggling full screen and `Esc` leaving it.

- [ ] **Step 3: Run, verify, commit**

```bash
git add src tests
git commit -m "feat(app): letterboxed video surface with a recycled bitmap"
```

---

### Task 6: Wire watching into the runtime

**Files:**
- Modify: `src/SonicDesktopRelay.Presentation/SessionRuntime.cs`, `SessionSnapshot.cs`, `MainWindowViewModel.cs`
- Modify: `src/SonicDesktopRelay.App/AppComposition.cs`, `Shell.cs`
- Test: `tests/SonicDesktopRelay.Presentation.Tests/SessionRuntimeTests.cs`

**Interfaces:**
- Produces: `interface IVideoWatchHost` in Presentation mirroring `IVideoPublishHost`; `SessionSnapshot` gains `WatchState? Watching` and `string? DecoderName`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Watching_starts_the_watch_host()
{
    // ... asserts host.Started
}

[Fact]
public async Task The_snapshot_reports_the_watch_state()
{
    // ... host raises Receiving; snapshot.Watching == WatchState.Receiving
}

[Fact]
public async Task A_stall_is_visible_in_the_snapshot_without_leaving_the_watching_phase()
{
    // Phase stays Watching; Watching == WatchState.Stalled. The session is fine; the media is not.
}

[Fact]
public async Task Session_ended_stops_the_watch_host_and_returns_to_idle()
{
}

[Fact]
public async Task Stopping_a_watched_session_disposes_the_host_without_ending_the_session()
{
    // Only the publisher may end a session; a viewer leaving is a local teardown.
}
```

- [ ] **Step 2: Implement, run, verify**

`StartWatchingAsync` joins, attaches signaling, starts the host, and forwards
`publisher.ready`, `webrtc.offer` and `webrtc.ice_candidate` to it. A stall changes
`Snapshot.Watching`, never `Snapshot.Phase`.

- [ ] **Step 3: Commit**

```bash
git add src tests
git commit -m "feat(app): watch a shared screen end to end"
```

---

### Task 7: Documentation and manual verification

- [ ] **Step 1: Extend `docs/screen-publishing.md` with the viewer path**

Cover the receive → decode → render chain, the stall state and why it is not "disconnected",
and the decoder selection.

- [ ] **Step 2: Verify by hand what can be verified**

With the phase-0 backend deployed, two machines: one shares, one enters the code and sees the
screen. Report the observed frame rate and whether the connection went direct or via relay.
If the backend is not deployed, say so plainly instead of implying the check passed.

- [ ] **Step 3: Commit**

```bash
git add docs
git commit -m "docs: viewer path, stall handling and decoder selection"
```

---

## Done when

- `dotnet test SonicDesktopRelay.sln` passes; build is warning-free.
- A machine entering a valid code renders the other machine's screen.
- The picture is letterboxed, never stretched, at every window size.
- No bitmap or conversion buffer is allocated per frame.
- A media stall shows as stalled, distinct from disconnected, and asks for exactly one keyframe.
- The Diagnostics page names the decoder in use.
