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

1. `session.joined` → `VideoPublisher.AddViewerAsync` creates the peer and sends `webrtc.offer`.
2. Gathered ICE candidates go out as `webrtc.ice_candidate`.
3. `webrtc.answer` and inbound `webrtc.ice_candidate` are routed back to that participant's peer.
4. `session.left` disposes the peer. `participant.disconnected` does **not** — that means
   "transiently unreachable", and tearing the peer down would force a full renegotiation for a
   viewer that is about to come back.

## Known limits of this phase

- One monitor at a time. No window or region capture.
- No audio. `WASAPI` is a later phase.
- **No viewer-side rendering.** This phase only publishes; a SonicDesktopRelay viewer cannot
  display the stream until phase 3.
- No simulcast or SVC — one encode, one quality, for everyone.
- The offer advertises `transport-cc` but not `nack pli` as an `rtcp-fb` attribute, because
  SIPSorcery generates the media line. PLI-driven keyframes therefore depend on the viewer
  sending one anyway; the `connected` transition also forces a keyframe, which covers the join
  case.
- Never log SDP, ICE candidates, or frame contents.
