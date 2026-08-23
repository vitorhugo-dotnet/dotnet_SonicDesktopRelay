# Publish video (phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The sharing machine captures a chosen monitor, encodes it once as H.264, and publishes that single encoded stream to every viewer that joins the session.

**Architecture:** Three new projects. `Media` holds platform-neutral contracts and the fan-out pump; `Media.Windows` holds the two Windows adapters (Windows.Graphics.Capture, FFmpeg H.264); `Rtc` owns peer connections and negotiation over the existing signaling client. Capture and encode happen **once per session** regardless of viewer count — the encoded sample is handed to every peer connection.

**Tech Stack:** .NET 10, SIPSorcery 10.0.16, SIPSorceryMedia.FFmpeg 10.0.16 (FFmpeg 8.1 ABI), Windows.Graphics.Capture via CsWinRT, xunit.

**Spec:** `docs/superpowers/specs/2026-08-23-sonicdesktoprelay-design.md`

**Depends on:** phase 1 (`2026-08-23-app-skeleton.md`), merged. `SessionRuntime`, `ISignalingConnection` and `SessionSnapshot` already exist and are what this phase plugs into.

## Global Constraints

- **Encode once, send N times.** One capture pipeline and one encoder per session. An encoder per viewer is the failure mode this whole design exists to avoid — if a task seems to need one, you have misread it.
- The encoder and the capture source sit behind **this project's own interfaces**, never SIPSorcery types. Swapping FFmpeg for something else must be a new class.
- `Media`, `Rtc`: `net10.0`. **`Media.Windows` only**: `net10.0-windows10.0.19041.0` — the minimum that exposes `Windows.Graphics.Capture`. No other project may take a Windows TFM.
- FFmpeg 8.1 shared libraries are required at runtime and are already installed on this machine at:
  `C:\Users\vitor\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.2-full_build-shared\bin`
  (contains `avcodec-62.dll`, `avutil-60.dll`, `avformat-62.dll`). Version 9.x is **ABI-incompatible** — do not "upgrade" it.
- Never log SDP, ICE candidates, or frame contents.
- `TreatWarningsAsErrors` is on. Windows-only types need `[SupportedOSPlatform("windows")]`, not a suppression.
- Tests that need FFmpeg or a real GPU must **skip** when unavailable, never fail. Every other test runs with fakes.
- Run all tests: `dotnet test SonicDesktopRelay.sln`

## File Structure

| File | Responsibility |
|---|---|
| `src/SonicDesktopRelay.Media/VideoContracts.cs` | `VideoFrame`, `EncodedVideoSample`, `VideoQuality`, `MonitorInfo` |
| `src/SonicDesktopRelay.Media/IScreenCaptureSource.cs` | Capture contract + monitor enumeration |
| `src/SonicDesktopRelay.Media/IVideoEncoder.cs` | Encoder contract, keyframe request |
| `src/SonicDesktopRelay.Media/ScreenPublishPipeline.cs` | capture → encode → one event, started/stopped once |
| `src/SonicDesktopRelay.Rtc/IPeerConnection.cs` | Peer contract the runtime uses |
| `src/SonicDesktopRelay.Rtc/SipSorceryPeerConnection.cs` | SIPSorcery implementation |
| `src/SonicDesktopRelay.Rtc/VideoPublisher.cs` | One peer per viewer, fan-out, negotiation |
| `src/SonicDesktopRelay.Media.Windows/MonitorEnumerator.cs` | Connected monitors |
| `src/SonicDesktopRelay.Media.Windows/GraphicsCaptureScreenSource.cs` | WGC capture |
| `src/SonicDesktopRelay.Media.Windows/FFmpegH264Encoder.cs` | H.264 with hardware selection |
| `src/SonicDesktopRelay.Media.Windows/FFmpegLoader.cs` | Locates and initialises FFmpeg once |
| `tests/SonicDesktopRelay.Media.Tests/` | Pipeline, with fakes |
| `tests/SonicDesktopRelay.Rtc.Tests/` | Fan-out and negotiation, with fakes |
| `tests/SonicDesktopRelay.Media.Windows.Tests/` | Adapters, skipped without hardware/FFmpeg |

---

### Task 1: Media contracts

**Files:**
- Create: `src/SonicDesktopRelay.Media/SonicDesktopRelay.Media.csproj`, `VideoContracts.cs`, `IScreenCaptureSource.cs`, `IVideoEncoder.cs`
- Create: `tests/SonicDesktopRelay.Media.Tests/SonicDesktopRelay.Media.Tests.csproj`, `VideoContractsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `readonly record struct MonitorInfo(string Id, string Name, int Width, int Height, bool IsPrimary)`
  - `sealed record VideoQuality(int MaxHeight, int FramesPerSecond, int TargetBitsPerSecond)` with `static VideoQuality Default => new(1080, 30, 4_000_000)` and `VideoQuality Reduced()` returning the next step down (720/30/2_000_000, then 540/20/1_000_000, never below 360/15/600_000)
  - `sealed class VideoFrame(int width, int height, ReadOnlyMemory<byte> bgra, TimeSpan timestamp)`
  - `readonly record struct EncodedVideoSample(ReadOnlyMemory<byte> Data, TimeSpan Timestamp, bool IsKeyFrame, int Width, int Height)`
  - `interface IScreenCaptureSource : IAsyncDisposable { event Action<VideoFrame>? FrameCaptured; MonitorInfo Monitor { get; } Task StartAsync(MonitorInfo monitor, VideoQuality quality, CancellationToken ct); Task StopAsync(); }`
  - `interface IMonitorEnumerator { IReadOnlyList<MonitorInfo> List(); }`
  - `interface IVideoEncoder : IDisposable { string Name { get; } EncodedVideoSample? Encode(VideoFrame frame, VideoQuality quality); void RequestKeyFrame(); }`

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/SonicDesktopRelay.Media -n SonicDesktopRelay.Media
dotnet new xunit -o tests/SonicDesktopRelay.Media.Tests -n SonicDesktopRelay.Media.Tests
rm src/SonicDesktopRelay.Media/Class1.cs tests/SonicDesktopRelay.Media.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.Media tests/SonicDesktopRelay.Media.Tests
dotnet add tests/SonicDesktopRelay.Media.Tests reference src/SonicDesktopRelay.Media
```

- [ ] **Step 2: Write the failing tests**

`tests/SonicDesktopRelay.Media.Tests/VideoContractsTests.cs`:

```csharp
using SonicDesktopRelay.Media;
using Xunit;

namespace SonicDesktopRelay.Media.Tests;

public sealed class VideoContractsTests
{
    [Fact]
    public void The_default_quality_targets_1080p30()
    {
        var quality = VideoQuality.Default;

        Assert.Equal(1080, quality.MaxHeight);
        Assert.Equal(30, quality.FramesPerSecond);
        Assert.Equal(4_000_000, quality.TargetBitsPerSecond);
    }

    [Fact]
    public void Reducing_quality_steps_down_height_and_bitrate()
    {
        var reduced = VideoQuality.Default.Reduced();

        Assert.Equal(720, reduced.MaxHeight);
        Assert.True(reduced.TargetBitsPerSecond < VideoQuality.Default.TargetBitsPerSecond);
    }

    [Fact]
    public void Quality_never_degrades_below_the_floor_however_often_it_is_reduced()
    {
        var quality = VideoQuality.Default;

        for (var i = 0; i < 20; i++) quality = quality.Reduced();

        Assert.Equal(360, quality.MaxHeight);
        Assert.Equal(15, quality.FramesPerSecond);
        Assert.Equal(600_000, quality.TargetBitsPerSecond);
    }

    [Theory]
    [InlineData(1080, 1920, 1080, 1920, 1080)]
    [InlineData(720, 1920, 1080, 1280, 720)]
    [InlineData(1080, 1280, 720, 1280, 720)]
    public void Scaling_preserves_aspect_ratio_and_never_upscales(
        int maxHeight, int sourceWidth, int sourceHeight, int expectedWidth, int expectedHeight)
    {
        var (width, height) = new VideoQuality(maxHeight, 30, 1).ScaleFor(sourceWidth, sourceHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void Scaled_dimensions_are_even_because_h264_yuv420_requires_it()
    {
        var (width, height) = new VideoQuality(721, 30, 1).ScaleFor(1919, 1081);

        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~VideoContractsTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement the contracts**

`src/SonicDesktopRelay.Media/VideoContracts.cs`:

```csharp
namespace SonicDesktopRelay.Media;

public readonly record struct MonitorInfo(string Id, string Name, int Width, int Height, bool IsPrimary);

/// <summary>
/// The session's single quality target. There is one for the whole session, not one per
/// viewer: the screen is encoded once and handed to everyone, so quality is a property of
/// the encode, not of a connection.
/// </summary>
public sealed record VideoQuality(int MaxHeight, int FramesPerSecond, int TargetBitsPerSecond)
{
    private static readonly VideoQuality[] Ladder =
    [
        new(1080, 30, 4_000_000),
        new(720, 30, 2_000_000),
        new(540, 20, 1_000_000),
        new(360, 15, 600_000)
    ];

    public static VideoQuality Default => Ladder[0];

    /// <summary>
    /// The next rung down, or the floor. Degrading is driven by the worst viewer's RTCP, so
    /// it must terminate: a session on a bad link settles at 360p rather than spiralling.
    /// </summary>
    public VideoQuality Reduced()
    {
        var index = Array.FindIndex(Ladder, x => x.MaxHeight == MaxHeight);
        if (index < 0) return Ladder[^1];
        return index >= Ladder.Length - 1 ? Ladder[^1] : Ladder[index + 1];
    }

    /// <summary>
    /// Output dimensions for a source of this size: never upscaled, aspect preserved, and
    /// both values even — H.264 4:2:0 chroma subsampling cannot represent odd dimensions.
    /// </summary>
    public (int Width, int Height) ScaleFor(int sourceWidth, int sourceHeight)
    {
        var height = Math.Min(MaxHeight, sourceHeight);
        var width = (int)Math.Round(sourceWidth * (height / (double)sourceHeight));
        return (MakeEven(width), MakeEven(height));
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;
}

/// <summary>One captured frame, BGRA8888, top-down, tightly packed.</summary>
public sealed class VideoFrame(int width, int height, ReadOnlyMemory<byte> bgra, TimeSpan timestamp)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public ReadOnlyMemory<byte> Bgra { get; } = bgra;

    public TimeSpan Timestamp { get; } = timestamp;
}

public readonly record struct EncodedVideoSample(
    ReadOnlyMemory<byte> Data,
    TimeSpan Timestamp,
    bool IsKeyFrame,
    int Width,
    int Height);
```

`src/SonicDesktopRelay.Media/IScreenCaptureSource.cs`:

```csharp
namespace SonicDesktopRelay.Media;

public interface IMonitorEnumerator
{
    IReadOnlyList<MonitorInfo> List();
}

public interface IScreenCaptureSource : IAsyncDisposable
{
    MonitorInfo Monitor { get; }

    event Action<VideoFrame>? FrameCaptured;

    Task StartAsync(MonitorInfo monitor, VideoQuality quality, CancellationToken ct);

    Task StopAsync();
}
```

`src/SonicDesktopRelay.Media/IVideoEncoder.cs`:

```csharp
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
```

- [ ] **Step 5: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~VideoContractsTests"`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(media): video contracts, quality ladder and scaling rules"
```

---

### Task 2: The publish pipeline

**Files:**
- Create: `src/SonicDesktopRelay.Media/ScreenPublishPipeline.cs`
- Create: `tests/SonicDesktopRelay.Media.Tests/ScreenPublishPipelineTests.cs`

**Interfaces:**
- Consumes: everything from Task 1.
- Produces: `sealed class ScreenPublishPipeline(IScreenCaptureSource capture, IVideoEncoder encoder) : IAsyncDisposable` with `event Action<EncodedVideoSample>? SampleEncoded`, `VideoQuality Quality { get; }`, `string EncoderName { get; }`, `Task StartAsync(MonitorInfo monitor, CancellationToken ct)`, `Task StopAsync()`, `void RequestKeyFrame()`, `void ReportPoorReception()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using SonicDesktopRelay.Media;
using Xunit;

namespace SonicDesktopRelay.Media.Tests;

public sealed class ScreenPublishPipelineTests
{
    private static readonly MonitorInfo Monitor = new("\\\\.\\DISPLAY1", "Primary", 1920, 1080, true);

    [Fact]
    public async Task Each_captured_frame_produces_one_encoded_sample()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        var samples = new List<EncodedVideoSample>();
        pipeline.SampleEncoded += samples.Add;
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        capture.Emit();
        capture.Emit();

        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public async Task One_encode_serves_every_subscriber()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        var first = 0;
        var second = 0;
        var third = 0;
        pipeline.SampleEncoded += _ => first++;
        pipeline.SampleEncoded += _ => second++;
        pipeline.SampleEncoded += _ => third++;
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        capture.Emit();

        // Three viewers, one encode. This is the whole point of the design.
        Assert.Equal(1, encoder.EncodeCalls);
        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, third);
    }

    [Fact]
    public async Task A_frame_the_encoder_swallows_publishes_nothing()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder { ReturnNull = true };
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        var samples = 0;
        pipeline.SampleEncoded += _ => samples++;
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        capture.Emit();

        Assert.Equal(0, samples);
    }

    [Fact]
    public async Task A_frame_arriving_before_start_is_ignored()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        var samples = 0;
        pipeline.SampleEncoded += _ => samples++;

        capture.Emit();

        Assert.Equal(0, samples);
        Assert.Equal(0, encoder.EncodeCalls);
    }

    [Fact]
    public async Task Stopping_stops_publishing()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        var samples = 0;
        pipeline.SampleEncoded += _ => samples++;
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        await pipeline.StopAsync();
        capture.Emit();

        Assert.Equal(0, samples);
    }

    [Fact]
    public async Task A_keyframe_request_reaches_the_encoder()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        pipeline.RequestKeyFrame();

        Assert.Equal(1, encoder.KeyFrameRequests);
    }

    [Fact]
    public async Task Poor_reception_degrades_the_session_quality()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        pipeline.ReportPoorReception();

        Assert.Equal(720, pipeline.Quality.MaxHeight);
    }

    [Fact]
    public async Task Degrading_also_forces_a_keyframe_so_viewers_resync_at_the_new_size()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        pipeline.ReportPoorReception();

        Assert.Equal(1, encoder.KeyFrameRequests);
    }

    [Fact]
    public async Task An_encoder_that_throws_stops_the_pipeline_instead_of_spinning()
    {
        var capture = new FakeCapture();
        var encoder = new FakeEncoder { Throw = true };
        await using var pipeline = new ScreenPublishPipeline(capture, encoder);
        Exception? failure = null;
        pipeline.Failed += e => failure = e;
        await pipeline.StartAsync(Monitor, CancellationToken.None);

        capture.Emit();
        capture.Emit();

        Assert.NotNull(failure);
        // One throw is enough; the pipeline must not keep feeding a broken encoder.
        Assert.Equal(1, encoder.EncodeCalls);
    }

    private sealed class FakeCapture : IScreenCaptureSource
    {
        public MonitorInfo Monitor { get; private set; }

        public event Action<VideoFrame>? FrameCaptured;

        public Task StartAsync(MonitorInfo monitor, VideoQuality quality, CancellationToken ct)
        {
            Monitor = monitor;
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public void Emit() => FrameCaptured?.Invoke(
            new VideoFrame(1920, 1080, new byte[16], TimeSpan.Zero));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeEncoder : IVideoEncoder
    {
        public string Name => "fake";

        public int EncodeCalls { get; private set; }

        public int KeyFrameRequests { get; private set; }

        public bool ReturnNull { get; init; }

        public bool Throw { get; init; }

        public EncodedVideoSample? Encode(VideoFrame frame, VideoQuality quality)
        {
            EncodeCalls++;
            if (Throw) throw new InvalidOperationException("encoder failed");
            return ReturnNull
                ? null
                : new EncodedVideoSample(new byte[8], frame.Timestamp, true, frame.Width, frame.Height);
        }

        public void RequestKeyFrame() => KeyFrameRequests++;

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~ScreenPublishPipelineTests"`
Expected: compile error — `ScreenPublishPipeline` does not exist.

- [ ] **Step 3: Implement**

`src/SonicDesktopRelay.Media/ScreenPublishPipeline.cs`:

```csharp
namespace SonicDesktopRelay.Media;

/// <summary>
/// capture → encode → one event, for the whole session. Every viewer subscribes to the same
/// <see cref="SampleEncoded"/>, so adding the fourth viewer costs a subscription rather than
/// a fourth encoder.
/// </summary>
public sealed class ScreenPublishPipeline(IScreenCaptureSource capture, IVideoEncoder encoder)
    : IAsyncDisposable
{
    private bool _running;

    public event Action<EncodedVideoSample>? SampleEncoded;

    public event Action<Exception>? Failed;

    public VideoQuality Quality { get; private set; } = VideoQuality.Default;

    public string EncoderName => encoder.Name;

    public async Task StartAsync(MonitorInfo monitor, CancellationToken ct)
    {
        if (_running) return;
        capture.FrameCaptured += OnFrame;
        await capture.StartAsync(monitor, Quality, ct);
        _running = true;
    }

    public async Task StopAsync()
    {
        if (!_running) return;
        _running = false;
        capture.FrameCaptured -= OnFrame;
        await capture.StopAsync();
    }

    public void RequestKeyFrame() => encoder.RequestKeyFrame();

    /// <summary>
    /// Called when any viewer's RTCP shows sustained loss. Quality is global, so the worst
    /// connection sets it for everyone — the alternative is a second encode per viewer.
    /// </summary>
    public void ReportPoorReception()
    {
        var reduced = Quality.Reduced();
        if (reduced == Quality) return;
        Quality = reduced;
        // The next sample changes dimensions; without a keyframe every viewer would decode
        // garbage until one happened to arrive.
        encoder.RequestKeyFrame();
    }

    private void OnFrame(VideoFrame frame)
    {
        if (!_running) return;

        EncodedVideoSample? sample;
        try
        {
            sample = encoder.Encode(frame, Quality);
        }
        catch (Exception e)
        {
            // Stop before reporting: a failing encoder called once per frame at 30 Hz turns
            // one fault into a flood, and the session is over either way.
            _running = false;
            capture.FrameCaptured -= OnFrame;
            Failed?.Invoke(e);
            return;
        }

        if (sample is not null) SampleEncoded?.Invoke(sample.Value);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await capture.DisposeAsync();
        encoder.Dispose();
    }
}
```

- [ ] **Step 4: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~ScreenPublishPipelineTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(media): encode-once publish pipeline with global quality degradation"
```

---

### Task 3: Peer connections and fan-out

**Files:**
- Create: `src/SonicDesktopRelay.Rtc/SonicDesktopRelay.Rtc.csproj`, `IPeerConnection.cs`, `VideoPublisher.cs`
- Create: `tests/SonicDesktopRelay.Rtc.Tests/SonicDesktopRelay.Rtc.Tests.csproj`, `VideoPublisherTests.cs`

**Interfaces:**
- Consumes: `EncodedVideoSample` (Task 1), `ScreenPublishPipeline` (Task 2), `ISignalingConnection`, `SignalingEnvelope`, `SignalingMessageTypes` (phase 1).
- Produces:
  - `interface IPeerConnection : IAsyncDisposable { Guid ParticipantId { get; } Task<string> CreateOfferAsync(CancellationToken ct); Task ApplyAnswerAsync(string sdp, CancellationToken ct); Task AddIceCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct); void SendVideo(EncodedVideoSample sample); event Action<string, string?, int?>? IceCandidateGathered; event Action? KeyFrameRequested; event Action<double>? PacketLossReported; }`
  - `interface IPeerConnectionFactory { IPeerConnection Create(Guid participantId); }`
  - `sealed class VideoPublisher(ScreenPublishPipeline pipeline, IPeerConnectionFactory peers, ISignalingConnection signaling) : IAsyncDisposable` with `int PeerCount { get; }`, `Task HandleAsync(SignalingEnvelope envelope, CancellationToken ct)`, `Task AddViewerAsync(Guid participantId, CancellationToken ct)`, `Task RemoveViewerAsync(Guid participantId)`.

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/SonicDesktopRelay.Rtc -n SonicDesktopRelay.Rtc
dotnet new xunit -o tests/SonicDesktopRelay.Rtc.Tests -n SonicDesktopRelay.Rtc.Tests
rm src/SonicDesktopRelay.Rtc/Class1.cs tests/SonicDesktopRelay.Rtc.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.Rtc tests/SonicDesktopRelay.Rtc.Tests
dotnet add src/SonicDesktopRelay.Rtc reference src/SonicDesktopRelay.Core src/SonicDesktopRelay.Media src/SonicDesktopRelay.Signaling
dotnet add src/SonicDesktopRelay.Rtc package SIPSorcery --version 10.0.16
dotnet add tests/SonicDesktopRelay.Rtc.Tests reference src/SonicDesktopRelay.Rtc
```

- [ ] **Step 2: Write the failing tests**

```csharp
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Rtc;
using SonicDesktopRelay.Signaling;
using Xunit;

namespace SonicDesktopRelay.Rtc.Tests;

public sealed class VideoPublisherTests
{
    private static readonly Guid ViewerA = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96401");
    private static readonly Guid ViewerB = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc96402");
    private static readonly MonitorInfo Monitor = new("\\\\.\\DISPLAY1", "Primary", 1920, 1080, true);

    [Fact]
    public async Task Adding_a_viewer_creates_one_peer_and_sends_it_an_offer()
    {
        var harness = await Harness.StartedAsync();

        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);

        Assert.Equal(1, harness.Publisher.PeerCount);
        var sent = Assert.Single(harness.Signaling.Sent);
        Assert.Equal(SignalingMessageTypes.WebRtcOffer, sent.Type);
        Assert.Equal(ViewerA, sent.To);
    }

    [Fact]
    public async Task Every_viewer_receives_the_same_encoded_sample()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);
        await harness.Publisher.AddViewerAsync(ViewerB, CancellationToken.None);

        harness.Capture.Emit();

        Assert.Equal(1, harness.Encoder.EncodeCalls);
        Assert.All(harness.Peers.Created, peer => Assert.Equal(1, peer.SentSamples.Count));
    }

    [Fact]
    public async Task A_removed_viewer_stops_receiving_and_is_disposed()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);
        var peer = harness.Peers.Created[0];

        await harness.Publisher.RemoveViewerAsync(ViewerA);
        harness.Capture.Emit();

        Assert.Equal(0, harness.Publisher.PeerCount);
        Assert.True(peer.Disposed);
        Assert.Empty(peer.SentSamples);
    }

    [Fact]
    public async Task An_answer_is_applied_to_the_peer_that_sent_it()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);

        await harness.Publisher.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcAnswer, ViewerA, """{"type":"answer","sdp":"the-sdp"}"""),
            CancellationToken.None);

        Assert.Equal("the-sdp", harness.Peers.Created[0].AppliedAnswer);
    }

    [Fact]
    public async Task An_answer_from_an_unknown_participant_is_ignored()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);

        await harness.Publisher.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcAnswer, ViewerB, """{"type":"answer","sdp":"the-sdp"}"""),
            CancellationToken.None);

        Assert.Null(harness.Peers.Created[0].AppliedAnswer);
    }

    [Fact]
    public async Task An_ice_candidate_is_routed_to_its_peer()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);

        await harness.Publisher.HandleAsync(
            Frame(SignalingMessageTypes.WebRtcIceCandidate, ViewerA,
                """{"candidate":"candidate:1","sdpMid":"0","sdpMLineIndex":0}"""),
            CancellationToken.None);

        Assert.Single(harness.Peers.Created[0].RemoteCandidates);
    }

    [Fact]
    public async Task A_gathered_candidate_is_signalled_to_that_viewer()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);
        harness.Signaling.Sent.Clear();

        harness.Peers.Created[0].GatherCandidate("candidate:2", "0", 0);

        var sent = Assert.Single(harness.Signaling.Sent);
        Assert.Equal(SignalingMessageTypes.WebRtcIceCandidate, sent.Type);
        Assert.Equal(ViewerA, sent.To);
    }

    [Fact]
    public async Task A_keyframe_request_from_any_viewer_reaches_the_single_encoder()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);
        await harness.Publisher.AddViewerAsync(ViewerB, CancellationToken.None);

        harness.Peers.Created[1].RequestKeyFrame();

        Assert.Equal(1, harness.Encoder.KeyFrameRequests);
    }

    [Fact]
    public async Task Sustained_loss_on_one_viewer_degrades_quality_for_the_session()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);
        await harness.Publisher.AddViewerAsync(ViewerB, CancellationToken.None);

        harness.Peers.Created[0].ReportPacketLoss(0.15);

        Assert.Equal(720, harness.Pipeline.Quality.MaxHeight);
    }

    [Fact]
    public async Task Mild_loss_does_not_degrade_quality()
    {
        var harness = await Harness.StartedAsync();
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);

        harness.Peers.Created[0].ReportPacketLoss(0.01);

        Assert.Equal(1080, harness.Pipeline.Quality.MaxHeight);
    }

    [Fact]
    public async Task Adding_the_same_viewer_twice_does_not_create_a_second_peer()
    {
        var harness = await Harness.StartedAsync();

        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);
        await harness.Publisher.AddViewerAsync(ViewerA, CancellationToken.None);

        Assert.Equal(1, harness.Publisher.PeerCount);
    }

    private static SignalingEnvelope Frame(string type, Guid from, string payloadJson) =>
        new(type, null, null, from, null, null,
            System.Text.Json.JsonDocument.Parse(payloadJson).RootElement.Clone());

    private sealed class Harness
    {
        public required FakeCapture Capture { get; init; }

        public required FakeEncoder Encoder { get; init; }

        public required ScreenPublishPipeline Pipeline { get; init; }

        public required FakePeerFactory Peers { get; init; }

        public required FakeSignaling Signaling { get; init; }

        public required VideoPublisher Publisher { get; init; }

        public static async Task<Harness> StartedAsync()
        {
            var capture = new FakeCapture();
            var encoder = new FakeEncoder();
            var pipeline = new ScreenPublishPipeline(capture, encoder);
            var peers = new FakePeerFactory();
            var signaling = new FakeSignaling();
            var publisher = new VideoPublisher(pipeline, peers, signaling);
            await pipeline.StartAsync(Monitor, CancellationToken.None);
            return new Harness
            {
                Capture = capture, Encoder = encoder, Pipeline = pipeline,
                Peers = peers, Signaling = signaling, Publisher = publisher
            };
        }
    }

    private sealed class FakeCapture : IScreenCaptureSource
    {
        public MonitorInfo Monitor { get; private set; }

        public event Action<VideoFrame>? FrameCaptured;

        public Task StartAsync(MonitorInfo monitor, VideoQuality quality, CancellationToken ct)
        {
            Monitor = monitor;
            return Task.CompletedTask;
        }

        public Task StopAsync() => Task.CompletedTask;

        public void Emit() => FrameCaptured?.Invoke(new VideoFrame(1920, 1080, new byte[16], TimeSpan.Zero));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeEncoder : IVideoEncoder
    {
        public string Name => "fake";

        public int EncodeCalls { get; private set; }

        public int KeyFrameRequests { get; private set; }

        public EncodedVideoSample? Encode(VideoFrame frame, VideoQuality quality)
        {
            EncodeCalls++;
            return new EncodedVideoSample(new byte[8], frame.Timestamp, true, frame.Width, frame.Height);
        }

        public void RequestKeyFrame() => KeyFrameRequests++;

        public void Dispose()
        {
        }
    }

    private sealed class FakePeerFactory : IPeerConnectionFactory
    {
        public List<FakePeer> Created { get; } = [];

        public IPeerConnection Create(Guid participantId)
        {
            var peer = new FakePeer(participantId);
            Created.Add(peer);
            return peer;
        }
    }

    private sealed class FakePeer(Guid participantId) : IPeerConnection
    {
        public Guid ParticipantId { get; } = participantId;

        public List<EncodedVideoSample> SentSamples { get; } = [];

        public List<string> RemoteCandidates { get; } = [];

        public string? AppliedAnswer { get; private set; }

        public bool Disposed { get; private set; }

        public event Action<string, string?, int?>? IceCandidateGathered;

        public event Action? KeyFrameRequested;

        public event Action<double>? PacketLossReported;

        public Task<string> CreateOfferAsync(CancellationToken ct) => Task.FromResult("offer-sdp");

        public Task ApplyAnswerAsync(string sdp, CancellationToken ct)
        {
            AppliedAnswer = sdp;
            return Task.CompletedTask;
        }

        public Task AddIceCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct)
        {
            RemoteCandidates.Add(candidate);
            return Task.CompletedTask;
        }

        public void SendVideo(EncodedVideoSample sample) => SentSamples.Add(sample);

        public void GatherCandidate(string candidate, string? mid, int? index) =>
            IceCandidateGathered?.Invoke(candidate, mid, index);

        public void RequestKeyFrame() => KeyFrameRequested?.Invoke();

        public void ReportPacketLoss(double loss) => PacketLossReported?.Invoke(loss);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSignaling : ISignalingConnection
    {
        public List<(string Type, Guid? To, object? Payload)> Sent { get; } = [];

        public SignalingState State => SignalingState.Connected;

        public event Action<SignalingEnvelope>? FrameReceived;

        public event Action<SignalingState>? StateChanged;

        public Task StartAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task SendAsync(string type, Guid? to, object? payload, CancellationToken ct)
        {
            Sent.Add((type, to, payload));
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~VideoPublisherTests"`
Expected: compile error — `IPeerConnection` and `VideoPublisher` do not exist.

- [ ] **Step 4: Implement the peer contract**

`src/SonicDesktopRelay.Rtc/IPeerConnection.cs`:

```csharp
using SonicDesktopRelay.Media;

namespace SonicDesktopRelay.Rtc;

/// <summary>
/// One WebRTC connection to one viewer. Declared here rather than using SIPSorcery's types
/// directly so the fan-out and negotiation logic can be tested without a network stack.
/// </summary>
public interface IPeerConnection : IAsyncDisposable
{
    Guid ParticipantId { get; }

    event Action<string, string?, int?>? IceCandidateGathered;

    /// <summary>The viewer asked for a keyframe (PLI), usually because it just joined or lost sync.</summary>
    event Action? KeyFrameRequested;

    /// <summary>Inbound-loss ratio this viewer reported over RTCP, 0..1.</summary>
    event Action<double>? PacketLossReported;

    Task<string> CreateOfferAsync(CancellationToken ct);

    Task ApplyAnswerAsync(string sdp, CancellationToken ct);

    Task AddIceCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct);

    void SendVideo(EncodedVideoSample sample);
}

public interface IPeerConnectionFactory
{
    IPeerConnection Create(Guid participantId);
}
```

- [ ] **Step 5: Implement the publisher**

`src/SonicDesktopRelay.Rtc/VideoPublisher.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Signaling;

namespace SonicDesktopRelay.Rtc;

/// <summary>
/// Owns one peer connection per viewer and feeds all of them from a single encode. Everything
/// that scales with viewer count lives here; everything that must not is in the pipeline.
/// </summary>
public sealed class VideoPublisher(
    ScreenPublishPipeline pipeline,
    IPeerConnectionFactory peers,
    ISignalingConnection signaling) : IAsyncDisposable
{
    /// <summary>
    /// Below this, loss is ordinary internet weather and reacting to it would make the picture
    /// worse for everyone over nothing.
    /// </summary>
    private const double PoorReceptionLossRatio = 0.05;

    private readonly ConcurrentDictionary<Guid, IPeerConnection> _peers = new();
    private bool _subscribed;

    public int PeerCount => _peers.Count;

    public async Task AddViewerAsync(Guid participantId, CancellationToken ct)
    {
        if (_peers.ContainsKey(participantId)) return;

        var peer = peers.Create(participantId);
        if (!_peers.TryAdd(participantId, peer))
        {
            await peer.DisposeAsync();
            return;
        }

        peer.IceCandidateGathered += (candidate, mid, index) =>
            _ = signaling.SendAsync(SignalingMessageTypes.WebRtcIceCandidate, participantId,
                new { candidate, sdpMid = mid, sdpMLineIndex = index }, CancellationToken.None);
        peer.KeyFrameRequested += pipeline.RequestKeyFrame;
        peer.PacketLossReported += loss =>
        {
            if (loss >= PoorReceptionLossRatio) pipeline.ReportPoorReception();
        };

        EnsureSubscribed();

        var offer = await peer.CreateOfferAsync(ct);
        await signaling.SendAsync(SignalingMessageTypes.WebRtcOffer, participantId,
            new { type = "offer", sdp = offer }, ct);
    }

    public async Task RemoveViewerAsync(Guid participantId)
    {
        if (!_peers.TryRemove(participantId, out var peer)) return;
        await peer.DisposeAsync();
    }

    public async Task HandleAsync(SignalingEnvelope envelope, CancellationToken ct)
    {
        if (envelope.From is not { } from) return;
        if (!_peers.TryGetValue(from, out var peer)) return;
        if (envelope.Payload is not { } payload) return;

        switch (envelope.Type)
        {
            case SignalingMessageTypes.WebRtcAnswer:
                if (payload.TryGetProperty("sdp", out var sdp) && sdp.GetString() is { } sdpText)
                    await peer.ApplyAnswerAsync(sdpText, ct);
                break;

            case SignalingMessageTypes.WebRtcIceCandidate:
                if (payload.TryGetProperty("candidate", out var candidate)
                    && candidate.GetString() is { } candidateText)
                {
                    await peer.AddIceCandidateAsync(
                        candidateText,
                        payload.TryGetProperty("sdpMid", out var mid) ? mid.GetString() : null,
                        payload.TryGetProperty("sdpMLineIndex", out var index)
                            && index.ValueKind == JsonValueKind.Number
                                ? index.GetInt32()
                                : null,
                        ct);
                }
                break;
        }
    }

    // Subscribed on the first viewer rather than at construction: with nobody watching there
    // is nothing to send, and an unsubscribed pipeline is the cheap idle state.
    private void EnsureSubscribed()
    {
        if (_subscribed) return;
        pipeline.SampleEncoded += Broadcast;
        _subscribed = true;
    }

    private void Broadcast(EncodedVideoSample sample)
    {
        foreach (var peer in _peers.Values) peer.SendVideo(sample);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscribed) pipeline.SampleEncoded -= Broadcast;
        foreach (var participantId in _peers.Keys) await RemoveViewerAsync(participantId);
    }
}
```

- [ ] **Step 6: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~VideoPublisherTests"`
Expected: PASS, 11 tests.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(rtc): one peer per viewer fed from a single encode"
```

---

### Task 4: SIPSorcery peer connection

**Files:**
- Create: `src/SonicDesktopRelay.Rtc/SipSorceryPeerConnection.cs`, `SipSorceryPeerConnectionFactory.cs`, `IceServerSettings.cs`
- Create: `tests/SonicDesktopRelay.Rtc.Tests/SipSorceryPeerConnectionTests.cs`

**Interfaces:**
- Consumes: `IPeerConnection`, `IPeerConnectionFactory` (Task 3).
- Produces: `sealed record IceServerSettings(IReadOnlyList<IceServer> Servers, bool ForceRelay)`, `sealed record IceServer(string Url, string? Username, string? Credential)`, `sealed class SipSorceryPeerConnectionFactory(IceServerSettings ice) : IPeerConnectionFactory`.

- [ ] **Step 1: Write the failing tests**

These exercise construction and SDP shape only — they need no network. Anything needing a real
peer belongs to manual verification in Task 8.

```csharp
using SonicDesktopRelay.Rtc;
using Xunit;

namespace SonicDesktopRelay.Rtc.Tests;

public sealed class SipSorceryPeerConnectionTests
{
    private static readonly IceServerSettings Ice = new(
        [new IceServer("stun:stun.example.com:3478", null, null)], ForceRelay: false);

    [Fact]
    public async Task An_offer_advertises_a_sendonly_h264_video_track()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice);
        await using var peer = factory.Create(Guid.NewGuid());

        var sdp = await peer.CreateOfferAsync(CancellationToken.None);

        Assert.Contains("m=video", sdp);
        Assert.Contains("H264", sdp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a=sendonly", sdp);
    }

    [Fact]
    public async Task The_offer_contains_no_audio_track_in_this_phase()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice);
        await using var peer = factory.Create(Guid.NewGuid());

        var sdp = await peer.CreateOfferAsync(CancellationToken.None);

        Assert.DoesNotContain("m=audio", sdp);
    }

    [Fact]
    public async Task The_peer_reports_the_participant_it_was_created_for()
    {
        var participantId = Guid.NewGuid();
        var factory = new SipSorceryPeerConnectionFactory(Ice);

        await using var peer = factory.Create(participantId);

        Assert.Equal(participantId, peer.ParticipantId);
    }

    [Fact]
    public async Task Forcing_relay_produces_a_relay_only_offer()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice with { ForceRelay = true });
        await using var peer = factory.Create(Guid.NewGuid());

        var sdp = await peer.CreateOfferAsync(CancellationToken.None);

        // With relay forced and no TURN server configured, no host candidates may leak.
        Assert.DoesNotContain("typ host", sdp);
    }

    [Fact]
    public async Task Sending_video_before_the_answer_arrives_does_not_throw()
    {
        var factory = new SipSorceryPeerConnectionFactory(Ice);
        await using var peer = factory.Create(Guid.NewGuid());
        await peer.CreateOfferAsync(CancellationToken.None);

        // Frames keep arriving from the pipeline while negotiation is still in flight; the
        // peer must drop them quietly rather than take down the capture loop.
        var exception = Record.Exception(() => peer.SendVideo(
            new Media.EncodedVideoSample(new byte[8], TimeSpan.Zero, true, 1920, 1080)));

        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SipSorceryPeerConnectionTests"`
Expected: compile error — the factory does not exist.

- [ ] **Step 3: Implement**

`src/SonicDesktopRelay.Rtc/IceServerSettings.cs`:

```csharp
namespace SonicDesktopRelay.Rtc;

public sealed record IceServer(string Url, string? Username, string? Credential);

/// <summary>
/// ICE configuration from <c>GET /api/webrtc/ice-servers</c>. <see cref="ForceRelay"/> is the
/// Settings toggle: useful for proving a relay path works, expensive to leave on.
/// </summary>
public sealed record IceServerSettings(IReadOnlyList<IceServer> Servers, bool ForceRelay);
```

`src/SonicDesktopRelay.Rtc/SipSorceryPeerConnection.cs`:

```csharp
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SonicDesktopRelay.Media;

namespace SonicDesktopRelay.Rtc;

/// <summary>
/// A viewer's connection, backed by SIPSorcery. The video track is <c>sendonly</c>: this phase
/// publishes a screen and receives nothing back.
/// </summary>
public sealed class SipSorceryPeerConnection : IPeerConnection
{
    private readonly RTCPeerConnection _connection;
    private readonly MediaStreamTrack _videoTrack;
    private bool _negotiated;

    public SipSorceryPeerConnection(Guid participantId, IceServerSettings ice)
    {
        ParticipantId = participantId;

        var configuration = new RTCConfiguration
        {
            iceServers = ice.Servers
                .Select(x => new RTCIceServer { urls = x.Url, username = x.Username, credential = x.Credential })
                .ToList(),
            iceTransportPolicy = ice.ForceRelay ? RTCIceTransportPolicy.relay : RTCIceTransportPolicy.all
        };
        _connection = new RTCPeerConnection(configuration);

        _videoTrack = new MediaStreamTrack(
            new VideoFormat(VideoCodecsEnum.H264, 96),
            MediaStreamStatusEnum.SendOnly);
        _connection.addTrack(_videoTrack);

        _connection.onicecandidate += candidate =>
        {
            if (candidate is null) return;
            IceCandidateGathered?.Invoke(candidate.candidate, candidate.sdpMid, candidate.sdpMLineIndex);
        };

        // A PLI is a viewer saying "I cannot decode what you are sending" — the answer is a
        // keyframe, and with on-demand-only keyframes this is the sole trigger.
        _connection.OnVideoFrameReceived += (_, _, _, _) => { };
        _connection.OnReceiveReport += (_, mediaType, report) =>
        {
            if (mediaType != SDPMediaTypesEnum.video) return;
            var block = report?.ReceptionReports?.FirstOrDefault();
            if (block is null) return;
            PacketLossReported?.Invoke(block.FractionLost / 256.0);
        };
        _connection.OnRtcpBye += (_, _) => { };
        _connection.onconnectionstatechange += state =>
        {
            if (state == RTCPeerConnectionState.connected) KeyFrameRequested?.Invoke();
        };
    }

    public Guid ParticipantId { get; }

    public event Action<string, string?, int?>? IceCandidateGathered;

    public event Action? KeyFrameRequested;

    public event Action<double>? PacketLossReported;

    public Task<string> CreateOfferAsync(CancellationToken ct)
    {
        var offer = _connection.createOffer();
        _connection.setLocalDescription(offer);
        return Task.FromResult(offer.sdp);
    }

    public Task ApplyAnswerAsync(string sdp, CancellationToken ct)
    {
        var result = _connection.setRemoteDescription(
            new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
        if (result != SetDescriptionResultEnum.OK)
            throw new InvalidOperationException($"The viewer's answer was rejected: {result}.");
        _negotiated = true;
        return Task.CompletedTask;
    }

    public Task AddIceCandidateAsync(string candidate, string? sdpMid, int? sdpMLineIndex, CancellationToken ct)
    {
        _connection.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = (ushort)(sdpMLineIndex ?? 0)
        });
        return Task.CompletedTask;
    }

    public void SendVideo(EncodedVideoSample sample)
    {
        // Frames keep coming from the shared pipeline while this particular viewer is still
        // negotiating. Dropping them is correct: the viewer has no decoder yet, and throwing
        // would take down the capture loop that every other viewer depends on.
        if (!_negotiated || _connection.connectionState != RTCPeerConnectionState.connected) return;

        var durationTicks = (uint)(90_000 / 30);
        _connection.SendVideo(durationTicks, sample.Data.ToArray());
    }

    public ValueTask DisposeAsync()
    {
        _connection.close();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class SipSorceryPeerConnectionFactory(IceServerSettings ice) : IPeerConnectionFactory
{
    public IPeerConnection Create(Guid participantId) => new SipSorceryPeerConnection(participantId, ice);
}
```

- [ ] **Step 4: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SipSorceryPeerConnectionTests"`
Expected: PASS, 5 tests.

If SIPSorcery 10's API differs from what is written here — the event names and the
`SendVideo`/`createOffer` signatures are the likely places — adapt to the real API and keep
the tests' intent. Do not weaken an assertion to make a signature fit.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(rtc): SIPSorcery peer connection with a sendonly H.264 track"
```

---

### Task 5: Monitor enumeration and screen capture

**Files:**
- Create: `src/SonicDesktopRelay.Media.Windows/SonicDesktopRelay.Media.Windows.csproj`, `MonitorEnumerator.cs`, `GraphicsCaptureScreenSource.cs`, `CaptureInterop.cs`
- Create: `tests/SonicDesktopRelay.Media.Windows.Tests/SonicDesktopRelay.Media.Windows.Tests.csproj`, `MonitorEnumeratorTests.cs`, `GraphicsCaptureScreenSourceTests.cs`

**Interfaces:**
- Consumes: `IMonitorEnumerator`, `IScreenCaptureSource`, `VideoFrame`, `MonitorInfo`, `VideoQuality` (Task 1).
- Produces: `sealed class MonitorEnumerator : IMonitorEnumerator`, `sealed class GraphicsCaptureScreenSource : IScreenCaptureSource`. Both `[SupportedOSPlatform("windows10.0.19041.0")]`.

- [ ] **Step 1: Create the project with the Windows TFM**

```bash
dotnet new classlib -o src/SonicDesktopRelay.Media.Windows -n SonicDesktopRelay.Media.Windows
dotnet new xunit -o tests/SonicDesktopRelay.Media.Windows.Tests -n SonicDesktopRelay.Media.Windows.Tests
rm src/SonicDesktopRelay.Media.Windows/Class1.cs tests/SonicDesktopRelay.Media.Windows.Tests/UnitTest1.cs
dotnet sln add src/SonicDesktopRelay.Media.Windows tests/SonicDesktopRelay.Media.Windows.Tests
dotnet add src/SonicDesktopRelay.Media.Windows reference src/SonicDesktopRelay.Media
dotnet add tests/SonicDesktopRelay.Media.Windows.Tests reference src/SonicDesktopRelay.Media.Windows
```

Both csproj files need the Windows TFM, overriding `Directory.Build.props`:

```xml
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>
  </PropertyGroup>
```

Add the Direct3D interop package to the source project:

```bash
dotnet add src/SonicDesktopRelay.Media.Windows package Vortice.Direct3D11 --version 3.6.2
```

- [ ] **Step 2: Write the failing tests**

`tests/SonicDesktopRelay.Media.Windows.Tests/MonitorEnumeratorTests.cs`:

```csharp
using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

public sealed class MonitorEnumeratorTests
{
    [Fact]
    public void A_machine_with_a_display_reports_at_least_one_monitor()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var monitors = new MonitorEnumerator().List();

        Assert.NotEmpty(monitors);
    }

    [Fact]
    public void Exactly_one_monitor_is_primary()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var monitors = new MonitorEnumerator().List();

        Assert.Single(monitors, x => x.IsPrimary);
    }

    [Fact]
    public void Every_monitor_has_an_id_a_name_and_a_positive_size()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        foreach (var monitor in new MonitorEnumerator().List())
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.Id));
            Assert.False(string.IsNullOrWhiteSpace(monitor.Name));
            Assert.True(monitor.Width > 0);
            Assert.True(monitor.Height > 0);
        }
    }

    [Fact]
    public void Monitor_ids_are_unique()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var monitors = new MonitorEnumerator().List();

        Assert.Equal(monitors.Count, monitors.Select(x => x.Id).Distinct().Count());
    }
}
```

`tests/SonicDesktopRelay.Media.Windows.Tests/GraphicsCaptureScreenSourceTests.cs`:

```csharp
using SonicDesktopRelay.Media;
using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

public sealed class GraphicsCaptureScreenSourceTests
{
    [Fact]
    public async Task Capturing_the_primary_monitor_delivers_frames()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        if (!GraphicsCaptureScreenSource.IsSupported) return;

        var monitor = new MonitorEnumerator().List().Single(x => x.IsPrimary);
        await using var source = new GraphicsCaptureScreenSource();
        var frames = 0;
        VideoFrame? last = null;
        source.FrameCaptured += frame => { frames++; last = frame; };

        await source.StartAsync(monitor, VideoQuality.Default, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await source.StopAsync();

        Assert.True(frames > 0, "no frames were captured in two seconds");
        Assert.Equal(monitor.Width, last!.Width);
        Assert.Equal(monitor.Height, last.Height);
        // BGRA8888: four bytes per pixel, tightly packed.
        Assert.Equal(last.Width * last.Height * 4, last.Bgra.Length);
    }

    [Fact]
    public async Task Stopping_ends_frame_delivery()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        if (!GraphicsCaptureScreenSource.IsSupported) return;

        var monitor = new MonitorEnumerator().List().Single(x => x.IsPrimary);
        await using var source = new GraphicsCaptureScreenSource();
        await source.StartAsync(monitor, VideoQuality.Default, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await source.StopAsync();
        var after = 0;
        source.FrameCaptured += _ => after++;
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.Equal(0, after);
    }
}
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~Media.Windows"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement monitor enumeration**

Use `EnumDisplayMonitors` + `GetMonitorInfoW` via `LibraryImport`. `MonitorInfo.Id` is the
device name (`\\.\DISPLAY1`), which is stable enough to persist as a preference and is what
`MonitorFromPoint`-style lookups round-trip.

`src/SonicDesktopRelay.Media.Windows/MonitorEnumerator.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SonicDesktopRelay.Media.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class MonitorEnumerator : IMonitorEnumerator
{
    public IReadOnlyList<MonitorInfo> List()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEXW { cbSize = Marshal.SizeOf<MONITORINFOEXW>() };
            if (GetMonitorInfoW(hMonitor, ref info))
            {
                var deviceName = info.szDevice.TrimEnd('\0');
                var width = info.rcMonitor.right - info.rcMonitor.left;
                var height = info.rcMonitor.bottom - info.rcMonitor.top;
                var isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
                var label = isPrimary
                    ? $"{deviceName} — primary ({width}×{height})"
                    : $"{deviceName} ({width}×{height})";
                monitors.Add(new MonitorInfo(deviceName, label, width, height, isPrimary));
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    private const int MONITORINFOF_PRIMARY = 1;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcClip, IntPtr data);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr lprcClip,
        [MarshalAs(UnmanagedType.FunctionPtr)] MonitorEnumProc callback, IntPtr data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEXW info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
```

- [ ] **Step 5: Implement capture**

`GraphicsCaptureScreenSource` follows the documented Windows.Graphics.Capture flow:

1. `GraphicsCaptureSession.IsSupported()` gates everything — expose it as `static bool IsSupported`.
2. Create a D3D11 device (Vortice, `DeviceCreationFlags.BgraSupport`) and wrap it as an
   `IDirect3DDevice` through `CreateDirect3D11DeviceFromDXGIDevice`.
3. Build a `GraphicsCaptureItem` for the monitor's `HMONITOR` through the
   `IGraphicsCaptureItemInterop` COM interface (`CreateForMonitor`). Resolve the `HMONITOR`
   from `MonitorInfo.Id` with `EnumDisplayMonitors` again, matching `szDevice`.
4. `Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size)`,
   then `CreateCaptureSession(item)` and `StartCapture()`.
5. On `FrameArrived`, copy the frame's surface into a CPU-readable staging texture, map it,
   copy row by row into a pooled `byte[]` (the mapped row pitch is **not** `width * 4`;
   copying the whole block without honouring the pitch produces a sheared image), and raise
   `FrameCaptured`.
6. Set `session.IsCursorCaptureEnabled = true` — a screen share without the pointer is
   markedly harder to follow.
7. Handle `item.Closed` and a frame whose `ContentSize` differs from the pool's by recreating
   the pool: a resolution change mid-session must not end the session.

Two frames of pool depth, and a single reused staging texture, are deliberate: at 30 Hz and
1080p a fresh allocation per frame is 250 MB/s of garbage.

- [ ] **Step 6: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~Media.Windows"`
Expected: PASS, 6 tests, on a Windows machine with a display. The guards make them no-ops
elsewhere.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(media): monitor enumeration and Windows.Graphics.Capture screen source"
```

---

### Task 6: FFmpeg H.264 encoder

**Files:**
- Create: `src/SonicDesktopRelay.Media.Windows/FFmpegLoader.cs`, `FFmpegH264Encoder.cs`
- Create: `tests/SonicDesktopRelay.Media.Windows.Tests/FFmpegH264EncoderTests.cs`

**Interfaces:**
- Consumes: `IVideoEncoder`, `VideoFrame`, `VideoQuality`, `EncodedVideoSample` (Task 1).
- Produces: `static class FFmpegLoader` with `static bool TryInitialise(out string? error)` and `static string? LibraryPath { get; }`; `sealed class FFmpegH264Encoder : IVideoEncoder` whose `Name` is the codec actually selected.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/SonicDesktopRelay.Media.Windows package SIPSorceryMedia.FFmpeg --version 10.0.16
```

- [ ] **Step 2: Write the failing tests**

```csharp
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
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~FFmpegH264EncoderTests"`
Expected: compile error — the types do not exist.

- [ ] **Step 4: Implement the loader**

`FFmpegLoader` finds the FFmpeg 8.1 shared binaries and calls
`SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise` exactly once, in this order:

1. `SONICDESKTOPRELAY_FFMPEG_PATH` if set — the escape hatch for a non-standard install.
2. An `ffmpeg` folder beside the executable — where the installer will put them in phase 4.
3. The winget shared package:
   `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_*\ffmpeg-*-full_build-shared\bin`
4. `PATH`.

A directory counts only if it contains `avcodec-62.dll` **and** `avutil-60.dll` — the FFmpeg
8.x SONAMEs. Checking the file names rather than trusting the folder is what stops a
FFmpeg 9 install (`avcodec-63`) from being loaded and failing later as an opaque
`DllNotFoundException` or, worse, a crash inside native code.

`TryInitialise` must be idempotent, thread-safe, and return `false` with a human-readable
reason rather than throwing.

- [ ] **Step 5: Implement the encoder**

`FFmpegH264Encoder`:

- Selects a codec at construction, first that opens successfully:
  `h264_nvenc` → `h264_qsv` → `h264_amf` → `libx264`. Each candidate is tried by actually
  opening an encoder context; a machine can advertise NVENC and still fail to open it (no
  driver, or every session slot in use), and finding that out at construction is far better
  than at the first frame.
- Records the winner in `Name`, and the rejected candidates with their reasons in a
  `RejectionLog` the Diagnostics page reads.
- Configures: `TargetBitsPerSecond`, `FramesPerSecond`, a very long GOP with scene-change
  detection off, and zero-latency/low-latency tuning where the codec supports it. Screen
  content is static for long stretches; periodic keyframes are wasted bytes.
- `Encode` converts BGRA → YUV420P with `sws_scale`, scaling to `quality.ScaleFor(...)` in the
  same pass, and returns the encoded packet.
- Recreates the codec context when the output dimensions change, and forces a keyframe on the
  first frame afterwards.
- `RequestKeyFrame` sets `pict_type = AV_PICTURE_TYPE_I` on the next frame.
- `Dispose` frees the context, frames and sws context. This is unmanaged memory; a leak here
  is a leak per session.

- [ ] **Step 6: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~FFmpegH264EncoderTests"`
Expected: PASS, 5 tests. Report which encoder was selected on this machine — that is the
single most useful fact this task produces.

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(media): FFmpeg H.264 encoder with hardware selection and fallback"
```

---

### Task 7: Wire sharing into the runtime and the UI

**Files:**
- Modify: `src/SonicDesktopRelay.Presentation/SessionRuntime.cs`, `SessionSnapshot.cs`, `MainWindowViewModel.cs`
- Modify: `src/SonicDesktopRelay.App/AppComposition.cs`, `Shell.cs`, `Views/ShareView.axaml`, `Views/DiagnosticsView.axaml`
- Modify: `src/SonicDesktopRelay.App/SonicDesktopRelay.App.csproj` (reference `Media.Windows` and `Rtc`)
- Test: `tests/SonicDesktopRelay.Presentation.Tests/SessionRuntimeTests.cs`, `MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `VideoPublisher` (Task 3), `MonitorInfo` (Task 1).
- Produces: `SessionRuntime.StartSharingAsync(MonitorInfo monitor, int maxViewers, CancellationToken ct)`; `SessionSnapshot` gains `string? EncoderName`, `int FramesPerSecond`, `int VideoHeight`; `interface IVideoPublishHost` in Presentation so the runtime drives publishing without referencing `Rtc`.

- [ ] **Step 1: Write the failing tests**

Add to `SessionRuntimeTests`:

```csharp
    [Fact]
    public async Task Sharing_starts_publishing_on_the_chosen_monitor()
    {
        var api = new FakeSessionApi();
        var host = new FakeVideoPublishHost();
        var runtime = new SessionRuntime(api, () => new FakeConnection(), host);
        var monitor = new MonitorInfo("\\\\.\\DISPLAY2", "Second", 2560, 1440, false);

        await runtime.StartSharingAsync(monitor, 3, CancellationToken.None);

        Assert.Equal(monitor, host.StartedOn);
        Assert.Equal(SessionPhase.Sharing, runtime.Snapshot.Phase);
    }

    [Fact]
    public async Task A_viewer_joining_is_added_to_the_publisher()
    {
        var api = new FakeSessionApi();
        var host = new FakeVideoPublishHost();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection, host);
        await runtime.StartSharingAsync(Monitor, 3, CancellationToken.None);
        var viewer = Guid.NewGuid();

        connection.EmitJoined(viewer);

        Assert.Contains(viewer, host.Viewers);
    }

    [Fact]
    public async Task A_viewer_leaving_is_removed_from_the_publisher()
    {
        var api = new FakeSessionApi();
        var host = new FakeVideoPublishHost();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection, host);
        await runtime.StartSharingAsync(Monitor, 3, CancellationToken.None);
        var viewer = Guid.NewGuid();
        connection.EmitJoined(viewer);

        connection.EmitLeft(viewer);

        Assert.DoesNotContain(viewer, host.Viewers);
    }

    [Fact]
    public async Task A_disconnecting_viewer_keeps_its_peer_through_the_grace_period()
    {
        var api = new FakeSessionApi();
        var host = new FakeVideoPublishHost();
        var connection = new FakeConnection();
        var runtime = new SessionRuntime(api, () => connection, host);
        await runtime.StartSharingAsync(Monitor, 3, CancellationToken.None);
        var viewer = Guid.NewGuid();
        connection.EmitJoined(viewer);

        connection.EmitDisconnected(viewer);

        // participant.disconnected means "transiently unreachable"; tearing the peer down here
        // would force a full renegotiation for a viewer that is about to come back.
        Assert.Contains(viewer, host.Viewers);
    }

    [Fact]
    public async Task Stopping_a_shared_session_stops_publishing()
    {
        var api = new FakeSessionApi();
        var host = new FakeVideoPublishHost();
        var runtime = new SessionRuntime(api, () => new FakeConnection(), host);
        await runtime.StartSharingAsync(Monitor, 3, CancellationToken.None);

        await runtime.StopAsync(CancellationToken.None);

        Assert.True(host.Stopped);
    }

    [Fact]
    public async Task The_snapshot_reports_the_encoder_in_use()
    {
        var api = new FakeSessionApi();
        var host = new FakeVideoPublishHost { EncoderName = "h264_nvenc" };
        var runtime = new SessionRuntime(api, () => new FakeConnection(), host);

        await runtime.StartSharingAsync(Monitor, 3, CancellationToken.None);

        Assert.Equal("h264_nvenc", runtime.Snapshot.EncoderName);
    }
```

Write `FakeVideoPublishHost` implementing `IVideoPublishHost`, recording `StartedOn`,
`Viewers`, `Stopped`, and exposing a settable `EncoderName`. Extend `FakeConnection` with
`EmitJoined`, `EmitLeft` and `EmitDisconnected` helpers that raise `session.joined`,
`session.left` and `participant.disconnected` with a `participantId` payload.

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test SonicDesktopRelay.sln --filter "FullyQualifiedName~SessionRuntimeTests"`
Expected: compile error — `IVideoPublishHost` does not exist and `StartSharingAsync` has the
old signature.

- [ ] **Step 3: Implement**

Add to `SessionSnapshot.cs`:

```csharp
/// <summary>
/// What the runtime needs from the media stack, declared here so Presentation never
/// references Rtc or Media.Windows. The App composes the real one.
/// </summary>
public interface IVideoPublishHost : IAsyncDisposable
{
    string? EncoderName { get; }

    Task StartAsync(MonitorInfo monitor, CancellationToken ct);

    Task StopAsync();

    Task AddViewerAsync(Guid participantId, CancellationToken ct);

    Task RemoveViewerAsync(Guid participantId);

    Task HandleSignalingAsync(SignalingEnvelope envelope, CancellationToken ct);
}
```

`Presentation` needs a reference to `Media` for `MonitorInfo`; that is allowed — `Media` is
platform-neutral contracts. It must **not** reference `Media.Windows` or `Rtc`.

In `SessionRuntime`:

- `StartSharingAsync(MonitorInfo, int, CancellationToken)` creates the session, attaches
  signaling, then `await host.StartAsync(monitor, ct)`.
- `OnFrame` routes `session.joined`/`participant.reconnected` with a viewer role to
  `AddViewerAsync`, `session.left` to `RemoveViewerAsync`, and forwards
  `webrtc.answer`/`webrtc.ice_candidate` to `HandleSignalingAsync`.
- `participant.disconnected` updates nothing but the count — the peer stays.
- `StopAsync` calls `host.StopAsync()` before ending the session.

- [ ] **Step 4: Run and verify pass**

Run: `dotnet test SonicDesktopRelay.sln`
Expected: PASS, including every phase-1 test unchanged except the `StartSharingAsync`
signature.

- [ ] **Step 5: Compose the real host in the App**

`RtcVideoPublishHost` in the App project implements `IVideoPublishHost` over
`ScreenPublishPipeline` + `VideoPublisher`, composing `GraphicsCaptureScreenSource`,
`FFmpegH264Encoder` and `SipSorceryPeerConnectionFactory`. ICE servers come from
`GET /api/webrtc/ice-servers`; add `IceApiClient` to `ApiClient` for it, following the shape
of `SessionApiClient`.

`ShareView` gains a real monitor picker bound to `IMonitorEnumerator.List()`, replacing the
phase-1 placeholder. `DiagnosticsView` gains encoder name, resolution, fps and viewer count.

- [ ] **Step 6: Verify the build and the whole suite**

Run: `dotnet build SonicDesktopRelay.sln` — zero warnings under `TreatWarningsAsErrors`.
Run: `dotnet test SonicDesktopRelay.sln`

- [ ] **Step 7: Commit**

```bash
git add src tests
git commit -m "feat(app): share a chosen monitor over WebRTC to every viewer"
```

---

### Task 8: Manual verification and documentation

**Files:**
- Create: `docs/screen-publishing.md`
- Modify: `README.md`

- [ ] **Step 1: Record what could and could not be verified**

Run the app. On a machine with a display, confirm: the monitor picker lists the real monitors;
starting a share produces a code; the Diagnostics page names the selected encoder.

The two-machine check needs the phase-0 backend deployed. If it is not, say so plainly rather
than implying it passed.

- [ ] **Step 2: Write `docs/screen-publishing.md`**

Cover: the capture → encode → fan-out path and why the encode is shared; the encoder selection
chain and how to read it in Diagnostics; the FFmpeg 8.1 requirement, the exact SONAMEs, and
why version 9 does not work; the quality ladder and what triggers a step down; and the known
limits of this phase (one monitor, no audio yet, no viewer-side rendering until phase 3).

- [ ] **Step 3: Commit**

```bash
git add docs README.md
git commit -m "docs: screen publishing pipeline and FFmpeg requirements"
```

---

## Done when

- `dotnet test SonicDesktopRelay.sln` passes; build is warning-free.
- Exactly one encoder exists per session no matter how many viewers — proven by
  `Every_viewer_receives_the_same_encoded_sample`.
- The monitor picker lists real monitors and capture delivers correctly-sized BGRA frames.
- The Diagnostics page names the encoder actually selected on this machine.
- No project except `Media.Windows` and its test project carries a Windows TFM.
