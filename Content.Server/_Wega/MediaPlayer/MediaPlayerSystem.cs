using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Interaction.Events;
using Content.Shared.MediaPlayer;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.MediaPlayer;

/// <summary>
/// Global admin-controlled music player: searches and downloads tracks from the online
/// source via yt-dlp, transcodes them to ogg-vorbis, and broadcasts the audio to every
/// connected client. Playback state is kept on server time so late joiners hear the
/// track from the correct position.
/// </summary>
public sealed partial class MediaPlayerSystem : EntitySystem
{
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IResourceManager _resource = default!;

    private ISawmill _sawmill = default!;

    private const int ChunkSize = 128 * 1024;
    private const string CacheFolder = "media_player";

    private CurrentTrack? _current;
    private bool _busy;

    private sealed class CurrentTrack
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public float Duration;
        public byte[] Data = [];

        /// <summary>
        /// Real time the track would have started at position 0. Position = now - StartedAt.
        /// </summary>
        public TimeSpan StartedAt;

        public bool Paused;

        /// <summary>
        /// Frozen playback position while paused.
        /// </summary>
        public float PausedPosition;
    }

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("media_player");

        SubscribeNetworkEvent<MediaPlayerSearchRequestEvent>(OnSearchRequest);
        SubscribeNetworkEvent<MediaPlayerPlayRequestEvent>(OnPlayRequest);
        SubscribeNetworkEvent<MediaPlayerStopRequestEvent>(OnStopRequest);
        SubscribeNetworkEvent<MediaPlayerPauseRequestEvent>(OnPauseRequest);

        SubscribeLocalEvent<MediaPlayerControllerComponent, UseInHandEvent>(OnControllerUse);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Track finished — clear the state so late joiners don't get stale data.
        if (_current is { } track && GetPosition(track) > track.Duration + 3f)
        {
            _current = null;
            BroadcastState();
        }
    }

    private bool CheckAdmin(EntitySessionEventArgs args)
    {
        return _adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Fun);
    }

    private void SendStatus(ICommonSession session, string message, bool isError = false)
    {
        RaiseNetworkEvent(new MediaPlayerStatusEvent(message, isError), session);
    }

    private float GetPosition(CurrentTrack track)
    {
        if (track.Paused)
            return track.PausedPosition;

        return (float)(_timing.RealTime - track.StartedAt).TotalSeconds;
    }

    private MediaPlayerStateEvent BuildState()
    {
        if (_current == null)
            return new MediaPlayerStateEvent(null, string.Empty, 0f, 0f, false);

        return new MediaPlayerStateEvent(_current.Id, _current.Title, _current.Duration,
            GetPosition(_current), !_current.Paused);
    }

    private void BroadcastState()
    {
        RaiseNetworkEvent(BuildState(), Filter.Broadcast());
    }

    private void SendTrackData(CurrentTrack track, Filter filter)
    {
        var total = (track.Data.Length + ChunkSize - 1) / ChunkSize;
        for (var i = 0; i < total; i++)
        {
            var offset = i * ChunkSize;
            var size = Math.Min(ChunkSize, track.Data.Length - offset);
            var chunk = new byte[size];
            Array.Copy(track.Data, offset, chunk, 0, size);
            RaiseNetworkEvent(new MediaPlayerTrackChunkEvent(track.Id, i, total, chunk), filter);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            return;

        if (_current is not { } track)
            return;

        var filter = Filter.SinglePlayer(args.Session);
        SendTrackData(track, filter);
        RaiseNetworkEvent(BuildState(), args.Session);
    }

    /// <summary>
    /// Starts broadcasting an already-available ogg-vorbis track to every client.
    /// </summary>
    public void PlayData(string id, string title, float duration, byte[] data)
    {
        _current = new CurrentTrack
        {
            Id = id,
            Title = title,
            Duration = duration,
            Data = data,
            StartedAt = _timing.RealTime,
        };

        SendTrackData(_current, Filter.Broadcast());
        BroadcastState();
    }

    /// <summary>
    /// Stops playback for everyone.
    /// </summary>
    public void Stop()
    {
        _current = null;
        BroadcastState();
    }

    private void OnStopRequest(MediaPlayerStopRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        _sawmill.Info($"{args.SenderSession.Name} stopped the media player");
        Stop();
    }

    /// <summary>
    /// Toggles pause/resume for everyone.
    /// </summary>
    public void TogglePause()
    {
        if (_current is not { } track)
            return;

        if (track.Paused)
        {
            // Resume: shift the virtual start so position continues from where it froze.
            track.StartedAt = _timing.RealTime - TimeSpan.FromSeconds(track.PausedPosition);
            track.Paused = false;
        }
        else
        {
            track.PausedPosition = GetPosition(track);
            track.Paused = true;
        }

        BroadcastState();
    }

    private void OnPauseRequest(MediaPlayerPauseRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        TogglePause();
    }

    private void OnControllerUse(Entity<MediaPlayerControllerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !TryComp(args.User, out ActorComponent? actor))
            return;

        RaiseNetworkEvent(new OpenMediaPlayerEvent(), actor.PlayerSession);
        args.Handled = true;
    }

    private async void OnSearchRequest(MediaPlayerSearchRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        var query = ev.Query.Trim();
        if (query.Length is 0 or > 200)
            return;

        var session = args.SenderSession;

        if (!await EnsureToolsAsync(session))
            return;

        try
        {
            var (exitCode, stdout, stderr) = await RunYtdlp(
                $"\"ytsearch10:{Sanitize(query)}\" --flat-playlist -J --no-warnings");

            if (exitCode != 0)
            {
                _sawmill.Error($"yt-dlp search failed: {stderr}");
                RaiseNetworkEvent(new MediaPlayerSearchResponseEvent([], Loc.GetString("media-player-error-search")), session);
                return;
            }

            var results = ParseSearchResults(stdout);
            RaiseNetworkEvent(new MediaPlayerSearchResponseEvent(results, null), session);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Media search error: {e}");
            RaiseNetworkEvent(new MediaPlayerSearchResponseEvent([], Loc.GetString("media-player-error-ytdlp")), session);
        }
    }

    private async void OnPlayRequest(MediaPlayerPlayRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        if (_busy)
        {
            SendStatus(args.SenderSession, Loc.GetString("media-player-status-busy"), isError: true);
            return;
        }

        var idOrUrl = ev.IdOrUrl.Trim();
        if (idOrUrl.Length is 0 or > 300)
            return;

        var session = args.SenderSession;
        _busy = true;

        try
        {
            if (!await EnsureToolsAsync(session))
                return;

            _sawmill.Info($"{session.Name} requested track: {idOrUrl}");

            // Resolve metadata first so we can refuse over-long tracks before downloading.
            SendStatus(session, Loc.GetString("media-player-status-resolving"));
            var (metaExit, metaOut, metaErr) = await RunYtdlp(
                $"--no-playlist -J --no-warnings -- \"{Sanitize(idOrUrl)}\"");

            if (metaExit != 0)
            {
                _sawmill.Error($"yt-dlp metadata failed: {metaErr}");
                SendStatus(session, Loc.GetString("media-player-error-resolve"), isError: true);
                return;
            }

            using var meta = JsonDocument.Parse(metaOut);
            var root = meta.RootElement;
            var id = root.GetProperty("id").GetString() ?? string.Empty;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? id : id;
            var duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                ? (float)d.GetDouble()
                : 0f;

            var maxDuration = _cfg.GetCVar(WegaCVars.MediaPlayerMaxDuration);
            if (duration <= 0 || duration > maxDuration)
            {
                SendStatus(session, Loc.GetString("media-player-error-duration",
                    ("max", maxDuration / 60)), isError: true);
                return;
            }

            var data = await GetOrDownloadTrack(id, session);
            if (data == null)
                return;

            SendStatus(session, Loc.GetString("media-player-status-broadcasting"));
            PlayData(id, title, duration, data);
            _sawmill.Info($"Now playing: {title} ({id}), {data.Length} bytes");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Media play error: {e}");
            SendStatus(session, Loc.GetString("media-player-error-ytdlp"), isError: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<byte[]?> GetOrDownloadTrack(string id, ICommonSession session)
    {
        var cacheDir = Path.Combine(_resource.UserData.RootDir ?? ".", CacheFolder);
        Directory.CreateDirectory(cacheDir);
        var oggPath = Path.Combine(cacheDir, $"{id}.ogg");

        if (!File.Exists(oggPath))
        {
            SendStatus(session, Loc.GetString("media-player-status-downloading"));
            var outTemplate = Path.Combine(cacheDir, "%(id)s.%(ext)s");
            var baseArgs = $"--no-playlist -f bestaudio -x --audio-format vorbis --audio-quality 96K " +
                           $"--max-filesize 100M -o \"{outTemplate}\" --no-warnings -- \"{Sanitize(id)}\"";

            var (exitCode, _, stderr) = await RunYtdlp(baseArgs);

            // Some ffmpeg builds ship without libvorbis; retry with the native experimental encoder.
            if (exitCode != 0 && stderr.Contains("Encoder not found"))
            {
                _sawmill.Warning("libvorbis missing, retrying with the native vorbis encoder");
                (exitCode, _, stderr) = await RunYtdlp(
                    $"--postprocessor-args \"ExtractAudio:-c:a vorbis -strict -2\" {baseArgs}");
            }

            if (exitCode != 0 || !File.Exists(oggPath))
            {
                _sawmill.Error($"yt-dlp download failed: {stderr}");
                SendStatus(session, Loc.GetString("media-player-error-download"), isError: true);
                return null;
            }
        }

        return await File.ReadAllBytesAsync(oggPath);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunYtdlp(string arguments)
    {
        // Prefer the path resolved by provisioning; fall back to the configured one.
        var ytdlpPath = _resolvedYtdlp ?? ResolveYtdlpPath(_cfg.GetCVar(WegaCVars.MediaPlayerYtdlpPath));

        // Point yt-dlp at our downloaded ffmpeg when it isn't on PATH.
        if (_resolvedFfmpegDir != null)
            arguments = $"--ffmpeg-location \"{_resolvedFfmpegDir}\" " + arguments;

        var psi = new ProcessStartInfo
        {
            FileName = ytdlpPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // yt-dlp emits UTF-8; without this non-ASCII titles decode into replacement chars.
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // yt-dlp looks for ffmpeg in PATH and in the current directory. Set the working
        // directory to the folder containing yt-dlp so the bundled ffmpeg is found.
        var ytdlpDir = Path.GetDirectoryName(Path.GetFullPath(ytdlpPath));
        if (!string.IsNullOrEmpty(ytdlpDir) && Directory.Exists(ytdlpDir))
        {
            psi.WorkingDirectory = ytdlpDir;
        }

        Process proc;
        try
        {
            proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yt-dlp");
        }
        catch (Win32Exception)
        {
            var path = psi.FileName;
            _sawmill.Error(
                $"yt-dlp executable not found at '{path}'. Install yt-dlp and add it to PATH, " +
                "or set the CVar 'wega.media_player_ytdlp_path' to its full path. " +
                "ffmpeg must also be available in PATH.");
            return (1, "", $"yt-dlp not found: {path}");
        }

        using (proc)
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return (proc.ExitCode, await stdoutTask, await stderrTask);
        }
    }

    private List<MediaSearchResult> ParseSearchResults(string json)
    {
        var results = new List<MediaSearchResult>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("entries", out var entries))
            return results;

        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrEmpty(id))
                continue;

            results.Add(new MediaSearchResult
            {
                Id = id,
                Title = entry.TryGetProperty("title", out var title) ? title.GetString() ?? id : id,
                Uploader = entry.TryGetProperty("uploader", out var up) ? up.GetString() ?? "" : "",
                DurationSeconds = entry.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number
                    ? (int)dur.GetDouble()
                    : 0,
            });
        }

        return results;
    }

    private static string ResolveYtdlpPath(string configuredPath)
    {
        // If the user supplied an absolute path, trust it.
        if (Path.IsPathRooted(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        // Otherwise, try to resolve it relative to the server executable's directory.
        // This lets you simply drop yt-dlp.exe and ffmpeg.exe next to the server binary.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var localPath = Path.Combine(baseDir, configuredPath);
        if (File.Exists(localPath))
            return localPath;

        var localExePath = Path.Combine(baseDir, configuredPath + ".exe");
        if (File.Exists(localExePath))
            return localExePath;

        return configuredPath;
    }

    /// <summary>
    /// Strips characters that could escape the quoted process argument.
    /// </summary>
    private static string Sanitize(string input)
    {
        return input.Replace("\"", "").Replace("\\", "").Replace("\n", " ").Replace("\r", " ");
    }
}
