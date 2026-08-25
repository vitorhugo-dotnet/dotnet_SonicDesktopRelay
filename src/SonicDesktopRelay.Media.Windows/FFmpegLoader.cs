using System.Runtime.Versioning;
using FFmpeg.AutoGen;

namespace SonicDesktopRelay.Media.Windows;

/// <summary>
/// Finds the FFmpeg 8.1 shared libraries and binds to them exactly once per process.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FFmpegLoader
{
    /// <summary>
    /// The FFmpeg 8.x SONAMEs. A directory is only accepted if it holds these exact files:
    /// FFmpeg 9 ships <c>avcodec-63.dll</c>, is ABI-incompatible with these bindings, and
    /// loading it fails later as an opaque native crash rather than a clean error.
    /// </summary>
    private static readonly string[] RequiredLibraries = ["avcodec-62.dll", "avutil-60.dll"];

    private const string OverrideVariable = "SONICDESKTOPRELAY_FFMPEG_PATH";

    private static readonly Lock Gate = new();
    private static bool _attempted;
    private static bool _succeeded;
    private static string? _error;

    /// <summary>The directory the libraries were loaded from, once initialisation succeeded.</summary>
    public static string? LibraryPath { get; private set; }

    /// <summary>
    /// Idempotent and thread-safe. Returns false with a human-readable reason rather than
    /// throwing, because "no FFmpeg" is a supported state: the app still runs, it just cannot
    /// share a screen, and the Diagnostics page has to be able to say why.
    /// </summary>
    public static bool TryInitialise(out string? error)
    {
        lock (Gate)
        {
            if (_attempted)
            {
                error = _error;
                return _succeeded;
            }

            _attempted = true;

            var searched = new List<string>();
            var directory = FindLibraryDirectory(searched);
            if (directory is null)
            {
                // A release build carries these beside the executable, so reaching this point
                // means either a source build without the bundled runtime or a stripped
                // install directory — the message has to distinguish the two for the user.
                _error =
                    $"FFmpeg 8.1 shared libraries ({string.Join(", ", RequiredLibraries)}) were not found. "
                    + "A packaged build ships them beside the executable; if this is one, the "
                    + "installation is incomplete and reinstalling restores it. "
                    + $"Otherwise install a shared FFmpeg 8.1 build, or set {OverrideVariable} "
                    + "to the folder holding them. "
                    + $"Searched: {string.Join("; ", searched)}.";
                error = _error;
                return false;
            }

            try
            {
                ffmpeg.RootPath = directory;
                SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(libPath: directory);

                // Force one real call across the boundary: a wrong ABI shows up here rather
                // than inside the first encode.
                _ = ffmpeg.avcodec_version();

                LibraryPath = directory;
                _succeeded = true;
            }
            catch (Exception e)
            {
                _error = $"FFmpeg is present at '{directory}' but could not be initialised: {e.Message}";
            }

            error = _error;
            return _succeeded;
        }
    }

    private static string? FindLibraryDirectory(List<string> searched)
    {
        foreach (var candidate in Candidates())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            // PATH routinely repeats entries, and the bundled runtime is reachable under more
            // than one name; reporting the same folder twice only makes the failure harder to read.
            if (!searched.Contains(candidate, StringComparer.OrdinalIgnoreCase)) searched.Add(candidate);
            if (IsUsable(candidate)) return candidate;
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        // 1. The escape hatch for a non-standard install.
        var overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden)) yield return overridden;

        // 2. The runtime the build embedded: beside the app for a normal or portable build.
        yield return AppContext.BaseDirectory;

        // 3. The same runtime inside a single-file EXE. The host extracts bundled native
        //    libraries to a temporary folder and names it here; AppContext.BaseDirectory
        //    still points at the EXE, which is why probing that alone is not enough.
        foreach (var path in NativeSearchDirectories()) yield return path;

        // 4. Beside a single-file EXE, and the ffmpeg subfolder either layout may use.
        var processDirectory = ProcessDirectory();
        if (processDirectory is not null)
        {
            yield return processDirectory;
            yield return Path.Combine(processDirectory, "ffmpeg");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg");

        // 5. The winget shared package.
        foreach (var path in WingetCandidates()) yield return path;

        // 6. Anything already on PATH.
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return entry.Trim();
    }

    /// <summary>
    /// The directories the .NET host resolves native libraries from. For a single-file
    /// publish this is the bundle's extraction directory, which is the only place the
    /// embedded FFmpeg exists on disk.
    /// </summary>
    private static IEnumerable<string> NativeSearchDirectories()
    {
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is not string directories) yield break;

        foreach (var entry in directories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return entry.Trim();
    }

    private static string? ProcessDirectory()
    {
        try
        {
            return Path.GetDirectoryName(Environment.ProcessPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> WingetCandidates()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) yield break;

        var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packages)) yield break;

        string[] packageDirectories;
        try
        {
            packageDirectories = Directory.GetDirectories(packages, "Gyan.FFmpeg.Shared_*");
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var packageDirectory in packageDirectories)
        {
            string[] builds;
            try
            {
                builds = Directory.GetDirectories(packageDirectory, "ffmpeg-*-full_build-shared");
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var build in builds) yield return Path.Combine(build, "bin");
        }
    }

    private static bool IsUsable(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                   && RequiredLibraries.All(x => File.Exists(Path.Combine(directory, x)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
