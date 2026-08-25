# Third-party notices

SonicDesktopRelay itself is MIT licensed (see [LICENSE](LICENSE)). Its release assets also
carry third-party binaries, listed here with the terms they arrive under.

## FFmpeg

- **Component**: FFmpeg 8.1.1, 64-bit shared Windows build from <https://www.gyan.dev/ffmpeg/builds/>,
  mirrored at <https://github.com/GyanD/codexffmpeg/releases/tag/8.1.1>.
- **Files shipped**: `avcodec-62.dll`, `avutil-60.dll`, `swresample-6.dll`, `swscale-9.dll`.
  The rest of the build (avformat, avfilter, avdevice, the command-line tools) is left out;
  the app only encodes, decodes and scales.
- **Licence**: **GPL v3**. The full text ships beside the binaries as `FFMPEG-LICENSE.txt` and
  lives at <https://www.gnu.org/licenses/gpl-3.0.html>.
- **Source code**: <https://github.com/FFmpeg/FFmpeg/commit/239f2c733d>, the commit this build
  was made from. Build configuration is in the upstream release's `README.txt`.
- **Why this build**: it is the one the project has always asked users to install
  (`winget install Gyan.FFmpeg.Shared`), and it is the only one of the two common variants that
  carries `libx264` — the software H.264 encoder `FFmpegH264Encoder` falls back to when no
  hardware encoder opens.

### What GPL v3 means for a release here

The FFmpeg build is GPL v3, and SonicDesktopRelay links against it dynamically and distributes
it in the same package. Anyone redistributing that package is bound by the GPL for the
combination, which in practice means offering the corresponding source of the whole work under
GPL v3 terms. The app's own source stays MIT for anyone who takes it without these binaries.

Two ways out, if that is not the intent:

- Build with `-p:EmbedFFmpegRuntime=false` and let each machine install FFmpeg itself. That is
  exactly the behaviour this repository had before the runtime was embedded.
- Point the build at an **LGPL** shared FFmpeg 8.1 build with
  `-p:FFmpegRuntimeDirectory=<folder>`. LGPL builds ship no `libx264`, so machines without a
  working hardware encoder lose the software fallback and cannot share a screen.
