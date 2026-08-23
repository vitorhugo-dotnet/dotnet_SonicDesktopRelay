# dotnet_SonicDesktopRelay

Share a Windows screen with other Windows machines over the SonicRelay backend. An Avalonia
desktop app: pick a monitor, get a six-character code, and everyone who enters it watches the
same encoded stream.

## Requirements

- Windows 10 build 19041 or later.
- .NET 10 SDK.
- **FFmpeg 8.1, shared build** for encoding and decoding — `winget install Gyan.FFmpeg.Shared`. Version 9.x is
  ABI-incompatible and is deliberately rejected. See
  [docs/screen-publishing.md](docs/screen-publishing.md#ffmpeg-requirement).

## Build and test

```bash
dotnet build SonicDesktopRelay.sln
dotnet test SonicDesktopRelay.sln
dotnet run --project src/SonicDesktopRelay.App
```

Tests that need a display or FFmpeg skip themselves when neither is available; everything else
runs on fakes.

## Projects

| Project | TFM | Responsibility |
|---|---|---|
| `SonicDesktopRelay.Core` | `net10.0` | Device identity, credential storage, settings |
| `SonicDesktopRelay.ApiClient` | `net10.0` | Typed HTTP: devices, sessions, ICE servers |
| `SonicDesktopRelay.Signaling` | `net10.0` | WebSocket signaling, envelope, reconnection |
| `SonicDesktopRelay.Media` | `net10.0` | Platform-neutral media contracts, the publish and watch pipelines |
| `SonicDesktopRelay.Media.Windows` | `net10.0-windows10.0.19041.0` | Windows.Graphics.Capture and the FFmpeg H.264 encoder and decoder |
| `SonicDesktopRelay.Rtc` | `net10.0` | Peer connections, both halves of negotiation, fan-out to N viewers |
| `SonicDesktopRelay.Presentation` | `net10.0` | Session state machine and view models |
| `SonicDesktopRelay.App` | `net10.0-windows10.0.19041.0` | Avalonia shell and composition root |

The App carries a Windows TFM because MSBuild cannot reference a `net10.0-windows` project from
a `net10.0` one, and the App is the only assembly that composes `Media.Windows`. Every library
below it stays platform-neutral, which is what lets the whole presentation layer be tested
without a GPU.

## Documentation

- [Screen publishing and watching](docs/screen-publishing.md) — capture, encoder and decoder
  selection, the FFmpeg requirement, the quality ladder, and why a stall is not a
  disconnection.
- [Design spec](docs/superpowers/specs/2026-08-23-sonicdesktoprelay-design.md).
