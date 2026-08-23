# Screen publishing

How SonicDesktopRelay gets a monitor onto other people's screens, and what has to be true on
the machine for it to work.

## One capture, one encode, N viewers

```
GraphicsCaptureScreenSource  ──frames──▶  ScreenPublishPipeline  ──encoded sample──▶  VideoPublisher
       (one per session)                   (FFmpegH264Encoder,             │            (one peer per viewer)
                                            one per session)               ├──▶ peer A
                                                                           ├──▶ peer B
                                                                           └──▶ peer C
```

The capture source and the encoder are created **once per session**, never once per viewer.
`ScreenPublishPipeline` raises a single `SampleEncoded` event and `VideoPublisher` hands that
one `EncodedVideoSample` to every peer connection it owns. The fourth viewer therefore costs a
subscription and an RTP send, not a fourth GPU encode session — which matters because consumer
NVENC caps concurrent encode sessions, and because a second 1080p30 encode is roughly a second
CPU or GPU core.

`ScreenPublishPipelineTests.One_encode_serves_every_subscriber` and
`VideoPublisherTests.Every_viewer_receives_the_same_encoded_sample` assert this directly: three
subscribers, one `Encode` call. If either ever fails, the design has been broken.

The consequence is that **quality is a property of the session, not of a connection**. There is
one target for everyone, so the worst link sets it for all of them (see the ladder below). The
alternative — a per-viewer target — is a per-viewer encode, which is exactly what this design
exists to avoid. Simulcast and SVC are a later phase.

## Capture

`GraphicsCaptureScreenSource` uses Windows.Graphics.Capture:

- `GraphicsCaptureSession.IsSupported()` gates everything; it is exposed as
  `GraphicsCaptureScreenSource.IsSupported` and every caller must check it.
- The monitor is resolved from `MonitorInfo.Id` (the device name, `\\.\DISPLAY1`) back to an
  `HMONITOR`, and a `GraphicsCaptureItem` is created through `IGraphicsCaptureItemInterop`.
- The frame pool is free-threaded with two buffers. Frames arrive on a pool thread.
- Each frame's D3D surface is copied into a reused staging texture, mapped, and copied out row
  by row. **The mapped row pitch is not `width * 4`** — D3D pads rows — so copying the block
  whole produces a sheared image.
- The `byte[]` handed to `FrameCaptured` is reused between frames. At 1080p30 a fresh array per
  frame is about 250 MB/s of garbage. Subscribers must consume it before returning; the pipeline
  encodes synchronously, which is what makes that safe.
- A resolution change mid-session recreates the pool rather than ending the session.
- The cursor is captured (`IsCursorCaptureEnabled = true`) — a screen share without the pointer
  is markedly harder to follow.
- Delivery is throttled to the session's frame rate: the compositor can deliver well above it,
  and dropping here is far cheaper than encoding and discarding later.

Monitors come from `MonitorEnumerator`, which is `EnumDisplayMonitors` + `GetMonitorInfoW`. The
device name is the id because it is stable across restarts and round-trips to an `HMONITOR`.

## Encoder selection

`FFmpegH264Encoder` tries these in order and takes the first that **actually opens**:

1. `h264_nvenc`
2. `h264_qsv`
3. `h264_amf`
4. `libx264`

Each candidate is tried by allocating and opening a real 1280×720 encoder context. Merely
finding the codec is not enough: a machine can ship NVENC and still fail to open it (no driver,
or every session slot already in use), and finding that out at construction is far better than
at the first frame.

The winner is `IVideoEncoder.Name`. Every rejected candidate, with the reason FFmpeg gave, is in
`FFmpegH264Encoder.RejectionLog`. Both are shown on the **Diagnostics** page, together with the
FFmpeg directory that was loaded — "why is my CPU pinned" is answered there first (`libx264`
means every hardware path was rejected).

Measured on the development machine (RTX-class NVIDIA GPU, Intel CPU without a usable QSV
device, no AMD runtime):

| Candidate | Result |
|---|---|
| `h264_nvenc` | opened — **selected** |
| `h264_qsv` | `avcodec_open2` → -22 (Invalid argument); no usable QSV device |
| `h264_amf` | `amfrt64.dll` failed to open; no AMD runtime installed |
| `libx264` | opens; the fallback, never reached here |

### Tuning

The GOP is effectively infinite and scene-change detection is off. Screen content is static for
long stretches, so periodic keyframes are wasted bytes; keyframes are emitted **on demand only**,
triggered by a viewer's PLI/FIR or by a quality change.

`RequestKeyFrame()` sets `AV_PICTURE_TYPE_I` on the next frame. NVENC needs `forced-idr=1` for
that to produce a real IDR — without it you get an I-frame that is not an IDR, viewers that lost
sync stay broken, and the packet is not even flagged as a keyframe. QSV and AMF get the
equivalent `forced_idr`.

BGRA → YUV420P conversion and scaling happen in one `sws_scale` pass. Output dimensions are
always even: H.264 4:2:0 chroma subsampling cannot represent odd ones.

## FFmpeg requirement

**FFmpeg 8.1, shared build.** `SIPSorceryMedia.FFmpeg` 10.0.16 binds to the FFmpeg 8.1 ABI
through `FFmpeg.AutoGen` 8.1.0.

A directory is accepted only if it contains **`avcodec-62.dll` and `avutil-60.dll`** — the
FFmpeg 8.x SONAMEs. This check is on the file names rather than the folder name on purpose:
FFmpeg 9 ships `avcodec-63.dll`, is ABI-incompatible, and loading it fails later as an opaque
`DllNotFoundException` or a crash inside native code. **Do not "upgrade" to 9.x.**

`FFmpegLoader` searches, in order:

1. `SONICDESKTOPRELAY_FFMPEG_PATH` — the escape hatch for a non-standard install.
2. An `ffmpeg` folder beside the executable — where the installer will put them.
3. `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_*\ffmpeg-*-full_build-shared\bin`.
4. Every entry on `PATH`.

It is idempotent and thread-safe, and returns `false` with a human-readable reason rather than
throwing: "no FFmpeg" is a supported state. The app still runs, it just cannot share, and
Diagnostics has to be able to say why.

To install the expected build:

```powershell
winget install Gyan.FFmpeg.Shared
```

## Quality ladder

One global target per session, degraded by the worst viewer's RTCP:

| Rung | Height | FPS | Target bitrate |
|---|---|---|---|
| 0 (default) | 1080 | 30 | 4 Mbit/s |
| 1 | 720 | 30 | 2 Mbit/s |
| 2 | 540 | 20 | 1 Mbit/s |
| 3 (floor) | 360 | 15 | 600 kbit/s |

A viewer reporting an RTCP inbound-loss fraction of **5% or more** steps the session down one
rung. Below that, loss is ordinary internet weather and reacting to it would make the picture
worse for everyone over nothing. Degrading terminates at the floor: a session on a bad link
settles at 360p rather than spiralling. Every step down also forces a keyframe, because the
dimensions change and viewers would otherwise decode garbage until one happened to arrive.

Scaling never upscales, preserves aspect ratio, and rounds both dimensions down to even.

## Negotiation

`SipSorceryPeerConnection` wraps one SIPSorcery `RTCPeerConnection` per viewer with a single
**`sendonly`** H.264 track (payload type 96, `packetization-mode=1`). This phase publishes and
receives nothing back. ICE servers come from `GET /api/webrtc/ice-servers`;
`IceServerSettings.ForceRelay` maps to `RTCIceTransportPolicy.relay`.

Frames that arrive while a particular viewer is still negotiating are dropped quietly. That
viewer has no decoder yet, and throwing would take down the capture loop every other viewer
depends on.

Signaling flow, per viewer, over the existing session socket:

1. `session.joined` → `VideoPublisher.AddViewerAsync` creates the peer, sends `publisher.ready`,
   then `webrtc.offer`. That order is what `dotnet_SonicRelay/docs/protocol.md` specifies: the
   `publisher.ready` frame is how a viewer learns which participant is the publisher, from the
   server-authenticated `from` rather than from anything a peer claims about itself.
2. Gathered ICE candidates go out as `webrtc.ice_candidate`.
3. `webrtc.answer` and inbound `webrtc.ice_candidate` are routed back to that participant's peer.
4. `session.left` disposes the peer. `participant.disconnected` does **not** — that means
   "transiently unreachable", and tearing the peer down would force a full renegotiation for a
   viewer that is about to come back.

## Known limits of this phase

- One monitor at a time. No window or region capture.
- No audio. `WASAPI` is a later phase.
- No simulcast or SVC — one encode, one quality, for everyone.
- The offer advertises `transport-cc` but not `nack pli` as an `rtcp-fb` attribute, because
  SIPSorcery generates the media line. PLI-driven keyframes therefore depend on the viewer
  sending one anyway; the `connected` transition also forces a keyframe, which covers the join
  case.
- Never log SDP, ICE candidates, or frame contents.

---

# Watching a shared screen

The mirror image of everything above: receive, decode, render.

```
SipSorceryViewerPeerConnection ──encoded sample──▶ ScreenWatchPipeline ──VideoFrame──▶ VideoSurface
      (one per session)                            (FFmpegH264Decoder,                 (one recycled
                                                    one per session)                    WriteableBitmap)
```

One publisher means one peer connection, one decoder and one pipeline. Nothing here fans out,
which is why the viewer side is the simpler half.

## Negotiation, from the viewer's end

`VideoSubscriber` owns the single peer and the whole viewer half of signaling:

1. `publisher.ready` → learn the publisher's `participantId` **from the authenticated `from`
   field**, never from the payload, and reply `viewer.ready`.
2. `webrtc.offer` → create the peer on first use, `setRemoteDescription`, `createAnswer`,
   `setLocalDescription`, send `webrtc.answer` back to the publisher.
3. Gathered candidates go out as `webrtc.ice_candidate`; inbound ones are applied.
4. A later offer is a **renegotiation** and lands on the same peer. The publisher renegotiates
   when the monitor resolution changes, and building a second peer would leak the first.

Every frame whose `from` is not the publisher is dropped. A session can hold other viewers, and
none of them may drive this connection.

The subscriber also accepts the authenticated sender of the **first offer** as the publisher,
when no `publisher.ready` has arrived. That tolerance exists because phase 2 originally shipped
a publisher that skipped `publisher.ready` and offered straight off `session.joined` — a viewer
built strictly to the documented handshake would have ignored every offer this app's own
publisher sent, while every unit test on both sides passed. The publisher was fixed to send it;
the fallback stays, because a contract you can only satisfy by reading the other half's source
is not a contract, and some future publisher will get this wrong again.

`SipSorceryViewerPeerConnection` holds a single **`recvonly`** H.264 track (payload type 96,
`packetization-mode=1`) and takes frames from `OnVideoFrameReceived` — SIPSorcery reassembles
RTP into whole access units and hands them over **still encoded**. Decoding is this project's
job: the decoder has to be ours for the Diagnostics page to be able to name it.

There is **no audio track** in this phase. The publisher does not send one until phase 4, so a
viewer-side audio path would have nothing to play.

## Decoder selection

`FFmpegH264Decoder` uses the **software `h264` decoder**, and that is a deliberate choice
rather than a missing feature.

| Candidate | Result |
|---|---|
| `h264_cuvid` | opens and decodes correctly — **rejected anyway**, see below |
| `h264_qsv` | same buffering behaviour through its async pipeline; rejected for the same reason |
| `h264` | **selected** |

NVDEC's parser will not release a picture until the *next* packet arrives to close it. Neither
`AV_CODEC_FLAG_LOW_DELAY` nor `surfaces=1` changes that; it was measured, not assumed. On
ordinary video that costs latency only. On a **shared screen** it costs correctness: the picture
is static for long stretches and the publisher only encodes when something moves, so the viewer
would sit on the second-to-last frame indefinitely — showing a stale window the moment the user
stopped moving, and only catching up when they moved again. Software H.264 at 1080p30 costs a
few percent of one core, which is a very cheap price for a picture that is actually current.

The decoder is opened by actually opening a context, as the encoder is, and the rejection log is
on the Diagnostics page beside the encoder's.

Frame threading is off (`FF_THREAD_SLICE` only) for the same reason: frame threading holds
several pictures back before emitting the first.

## No allocation per frame

This is the phase's defining constraint, the way encode-once was phase 2's. At 1080p30 a fresh
1080p BGRA buffer per frame is roughly **250 MB/s of garbage**, and the GC pauses that buys show
up as stutter in exactly the content people notice it in.

Three buffers exist, all reused and rebuilt only when the picture size changes:

- the decoder's **input staging buffer** — FFmpeg reads up to `AV_INPUT_BUFFER_PADDING_SIZE`
  bytes past the end of a packet, so packets are staged rather than pinned in place;
- the decoder's **BGRA output buffer**, the `sws_scale` target, handed out inside the
  `VideoFrame` exactly as `GraphicsCaptureScreenSource` hands out its capture buffer — consumers
  must blit before returning;
- the surface's **`WriteableBitmap`**, recreated only when the frame size differs from the
  current one.

Because the decoder's output buffer is reused, the hand-off to the UI thread is
`Dispatcher.UIThread.**Invoke**`, not `Post`. Posting would let the decode thread write the next
frame over the buffer before the UI thread had blitted this one — tearing under exactly the load
that makes it hardest to diagnose. Blocking there costs one memcpy of decode throughput and
applies backpressure to the receive side, which is the right thing to give up.

`FFmpegH264DecoderTests.The_conversion_buffer_is_reused_between_frames_of_the_same_size` asserts
the middle one directly, by identity. If it ever fails, the design has been broken.

## Threads

Decoding must not happen on the UI thread; rendering must. Samples arrive on a SIPSorcery
receive thread, are decoded there, and cross to the UI thread exactly once, at
`Shell.PublishFrame`, which posts through `Dispatcher.UIThread` the way the shell already does
for snapshots. `VideoSurface.Present` asserts it with `Dispatcher.UIThread.VerifyAccess()`.

## Stalled is not disconnected

`WatchState` has four values: `Waiting`, `Receiving`, `Stalled`, `Failed`.

A **stall** is four seconds without a decoded frame. It is reported as its own state and never
as a disconnection, because the peer connection can be perfectly healthy while the media has
stopped — a frozen publisher, a wedged encoder, a path that has quietly stopped delivering. The
two have different causes and different fixes, and calling a stall "disconnected" sends the user
to check their network when the publisher's screen is the thing that has gone quiet.

A stall also changes `SessionSnapshot.Watching`, **never** `SessionSnapshot.Phase`: the session
is fine, the media is not.

Every stall asks the publisher for exactly **one** keyframe, not one per watchdog tick.
Flooding a publisher with PLIs is the worst thing to do to a link that is already failing to
deliver. The ask is re-armed by the next frame that actually decodes.

The pipeline holds no clock of its own — `RtcVideoWatchHost` ticks `CheckForStall()` once a
second — which is what makes the whole watchdog testable over a `FakeTimeProvider`.

## The picture

`LetterboxGeometry.Fit` lives in `Presentation`, not in the control: it is pure arithmetic, it
is where the bugs live, and there it is testable without a window. It picks the smaller of the
width and height scale factors and centres the result, so the picture is letterboxed and
**never stretched** — a distorted screen share is worse than black bars, because text stops
being readable and nobody can tell why.

`F11` fills the window with the picture and hides the navigation rail; `Esc` comes back. The key
is handled on the window rather than on the surface, because a video surface is not focusable
and nobody expects to have to click the picture first.

## Known limits of this phase

- Software decode only, for the reason above. A zero-delay hardware decoder would be a drop-in
  addition to the candidate list.
- No audio, either direction. Phase 4.
- No jitter buffer and no reordering beyond what SIPSorcery's depacketiser does. A lost packet
  produces a dropped frame and, eventually, a stall and a PLI.
- The viewer never renegotiates on its own; it only answers.
- Never log SDP, ICE candidates, or frame contents.
