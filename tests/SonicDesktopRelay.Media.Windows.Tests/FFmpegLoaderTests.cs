using SonicDesktopRelay.Media.Windows;
using Xunit;

namespace SonicDesktopRelay.Media.Windows.Tests;

/// <summary>
/// The encoder and decoder tests skip themselves when FFmpeg is missing, which is exactly the
/// wrong behaviour for the guarantee that the build embeds it: a runtime that silently stopped
/// shipping would read as a green run. These fail instead.
/// </summary>
public sealed class FFmpegLoaderTests
{
#if FFMPEG_RUNTIME_EMBEDDED
    [Fact]
    public void The_build_puts_the_ffmpeg_runtime_beside_the_assembly()
    {
        foreach (var library in new[] { "avcodec-62.dll", "avutil-60.dll", "swresample-6.dll", "swscale-9.dll" })
            Assert.True(
                File.Exists(Path.Combine(AppContext.BaseDirectory, library)),
                $"{library} was not copied to {AppContext.BaseDirectory}.");
    }

    [Fact]
    public void The_embedded_runtime_initialises()
    {
        // Also the check that the embedded set is complete: a missing transitive dependency
        // surfaces here as a load failure rather than as a crash on the first shared screen.
        Assert.True(FFmpegLoader.TryInitialise(out var error), error);
        Assert.NotNull(FFmpegLoader.LibraryPath);
    }
#else
    [Fact]
    public void Initialisation_reports_a_reason_when_it_fails()
    {
        // EmbedFFmpegRuntime=false: whether FFmpeg is installed on this machine is unknown,
        // but "no FFmpeg" must stay a described state rather than an exception.
        if (FFmpegLoader.TryInitialise(out var error))
        {
            Assert.NotNull(FFmpegLoader.LibraryPath);
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(error));
    }
#endif
}
