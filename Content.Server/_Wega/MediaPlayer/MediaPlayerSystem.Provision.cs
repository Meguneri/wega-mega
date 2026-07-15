using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Robust.Shared.Player;

namespace Content.Server.MediaPlayer;

/// <summary>
/// Provisions the external helper tools the media player needs (yt-dlp + ffmpeg). If they aren't
/// already installed, downloads them into the server's data folder on first use so the feature
/// works out of the box, especially on Windows where these tools are rarely on PATH.
/// </summary>
public sealed partial class MediaPlayerSystem
{
    private const string YtdlpWindows = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private const string YtdlpLinux = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp";
    private const string YtdlpMacos = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos";
    private const string FfmpegWindowsZip = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

    // Resolved once, then reused.
    private string? _resolvedYtdlp;
    private string? _resolvedFfmpegDir; // dir passed via --ffmpeg-location; null means "on PATH".
    private Task<bool>? _provisionTask;

    private string ToolsDir => Path.Combine(_resource.UserData.RootDir ?? ".", CacheFolder, "bin");
    private static string YtdlpFileName => OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
    private static string FfmpegFileName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>
    /// Ensures yt-dlp and ffmpeg are available, downloading them if needed. Sends progress/errors
    /// to the requesting admin. Returns false if the tools couldn't be made available.
    /// </summary>
    private Task<bool> EnsureToolsAsync(ICommonSession? session)
    {
        if (_resolvedYtdlp != null)
            return Task.FromResult(true);

        return _provisionTask ??= ProvisionAsync(session);
    }

    private async Task<bool> ProvisionAsync(ICommonSession? session)
    {
        try
        {
            var ytdlp = await ResolveOrDownloadYtdlp(session);
            if (ytdlp == null)
                return false;

            if (!await ResolveOrDownloadFfmpeg(session))
                return false;

            _resolvedYtdlp = ytdlp;
            _sawmill.Info($"Media tools ready: yt-dlp='{ytdlp}', ffmpeg-dir='{_resolvedFfmpegDir ?? "PATH"}'");
            return true;
        }
        catch (Exception e)
        {
            _sawmill.Error($"Media tool provisioning failed: {e}");
            SendStatus(session, Loc.GetString("media-player-error-tools"), isError: true);
            return false;
        }
        finally
        {
            // Allow another attempt later if this one failed (on success we short-circuit above).
            _provisionTask = null;
        }
    }

    private async Task<string?> ResolveOrDownloadYtdlp(ICommonSession? session)
    {
        // 1. Configured path / next to the server binary.
        var configured = ResolveYtdlpPath(_cfg.GetCVar(WegaCVars.MediaPlayerYtdlpPath));
        if (await CanRun(configured, "--version"))
            return configured;

        // 2. Previously downloaded copy.
        var local = Path.Combine(ToolsDir, YtdlpFileName);
        if (await CanRun(local, "--version"))
            return local;

        // 3. Download it.
        if (!_cfg.GetCVar(WegaCVars.MediaPlayerAutoDownload))
        {
            SendStatus(session, Loc.GetString("media-player-error-ytdlp"), isError: true);
            return null;
        }

        SendStatus(session, Loc.GetString("media-player-status-fetching-ytdlp"));
        Directory.CreateDirectory(ToolsDir);

        var url = OperatingSystem.IsWindows() ? YtdlpWindows
            : OperatingSystem.IsMacOS() ? YtdlpMacos
            : YtdlpLinux;

        await DownloadFile(url, local);
        MakeExecutable(local);

        if (await CanRun(local, "--version"))
            return local;

        SendStatus(session, Loc.GetString("media-player-error-tools"), isError: true);
        return null;
    }

    private async Task<bool> ResolveOrDownloadFfmpeg(ICommonSession? session)
    {
        // 1. Explicit ffmpeg path from config.
        var configured = _cfg.GetCVar(WegaCVars.MediaPlayerFfmpegPath);
        if (!string.IsNullOrWhiteSpace(configured) && await CanRun(configured, "-version"))
        {
            _resolvedFfmpegDir = Path.GetDirectoryName(Path.GetFullPath(configured));
            return true;
        }

        // 2. ffmpeg on PATH.
        if (await CanRun("ffmpeg", "-version"))
        {
            _resolvedFfmpegDir = null; // yt-dlp will find it in PATH
            return true;
        }

        // 3. Previously downloaded copy.
        var ffmpegDir = Path.Combine(ToolsDir, "ffmpeg");
        var ffmpegExe = Path.Combine(ffmpegDir, FfmpegFileName);
        if (await CanRun(ffmpegExe, "-version"))
        {
            _resolvedFfmpegDir = ffmpegDir;
            return true;
        }

        // 4. Auto-download — currently only the Windows static build is fetched automatically.
        if (_cfg.GetCVar(WegaCVars.MediaPlayerAutoDownload) && OperatingSystem.IsWindows())
        {
            SendStatus(session, Loc.GetString("media-player-status-fetching-ffmpeg"));
            if (await DownloadFfmpegWindows(ffmpegDir) && await CanRun(ffmpegExe, "-version"))
            {
                _resolvedFfmpegDir = ffmpegDir;
                return true;
            }
        }

        SendStatus(session, Loc.GetString("media-player-error-ffmpeg"), isError: true);
        return false;
    }

    private async Task<bool> DownloadFfmpegWindows(string destDir)
    {
        Directory.CreateDirectory(destDir);
        var zipPath = Path.Combine(ToolsDir, "ffmpeg.zip");
        var extractDir = Path.Combine(ToolsDir, "ffmpeg_extract");

        try
        {
            await DownloadFile(FfmpegWindowsZip, zipPath);

            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // The archive nests the exes under "<name>/bin/". Grab everything from that bin folder.
            var binFolder = Directory.GetDirectories(extractDir, "bin", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (binFolder == null)
                return false;

            foreach (var file in Directory.GetFiles(binFolder))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);

            return File.Exists(Path.Combine(destDir, FfmpegFileName));
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
        }
    }

    private static async Task DownloadFile(string url, string dest)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("wega-mega-mediaplayer");

        await using var stream = await http.GetStreamAsync(url);
        await using var file = File.Create(dest);
        await stream.CopyToAsync(file);
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Non-fatal: if chmod fails the CanRun check will catch it.
        }
    }

    /// <summary>
    /// Returns true if the given executable runs and exits with code 0 for the given argument.
    /// </summary>
    private static async Task<bool> CanRun(string exe, string arg)
    {
        if (string.IsNullOrWhiteSpace(exe))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return false;

            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false; // not found / not executable
        }
        catch (Exception)
        {
            return false;
        }
    }
}
