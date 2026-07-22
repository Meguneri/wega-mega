using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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
    [Dependency] private ITaskManager _taskManager = default!;
    [Dependency] private IRobustRandom _random = default!;

    private ISawmill _sawmill = default!;

    private const int ChunkSize = 128 * 1024;
    private const string CacheFolder = "media_player";
    private const int ThumbnailWidth = 120;

    private readonly Dictionary<string, byte[]> _thumbnailCache = new();
    private readonly object _thumbnailCacheLock = new();

    private CurrentTrack? _current;
    private bool _busy;

    /// <summary>Tracks waiting to play after the current one, in order.</summary>
    private readonly List<MediaQueueItem> _queue = new();

    /// <summary>Repeat the current track instead of advancing when it ends.</summary>
    private bool _repeat;

    /// <summary>Pick the next queue entry at random instead of in order.</summary>
    private bool _shuffle;

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
        SubscribeNetworkEvent<MediaPlayerSeekRequestEvent>(OnSeekRequest);
        SubscribeNetworkEvent<MediaPlayerEnqueueRequestEvent>(OnEnqueueRequest);
        SubscribeNetworkEvent<MediaPlayerQueueRemoveRequestEvent>(OnQueueRemoveRequest);
        SubscribeNetworkEvent<MediaPlayerModeRequestEvent>(OnModeRequest);

        SubscribeLocalEvent<MediaPlayerControllerComponent, UseInHandEvent>(OnControllerUse);

        InitializeTv();

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

        // Порционная досылка ТВ-клипа — независимо от состояния музыки, поэтому до ранних return.
        TvTickSend();

        // A manual play or a queue advance is already fetching a track — don't re-trigger.
        if (_busy)
            return;

        // Not finished yet (3s grace so the tail isn't cut).
        if (_current is not { } track || GetPosition(track) <= track.Duration + 3f)
            return;

        // Repeat: restart the same track from the top, no re-download needed.
        if (_repeat)
        {
            PlayData(track.Id, track.Title, track.Duration, track.Data);
            return;
        }

        // Clear first so this branch doesn't fire again while the next track downloads.
        _current = null;

        if (TryDequeueNext(out var next))
            AdvanceToQueued(next); // async fetch → PlayData when ready
        else
            BroadcastState(); // queue empty: nothing playing
    }

    /// <summary>
    /// Pops the next queue entry: the front of the queue, or a random one when shuffle is on.
    /// </summary>
    private bool TryDequeueNext(out MediaQueueItem item)
    {
        item = default!;
        if (_queue.Count == 0)
            return false;

        var index = _shuffle ? _random.Next(_queue.Count) : 0;
        item = _queue[index];
        _queue.RemoveAt(index);
        BroadcastQueueState();
        return true;
    }

    /// <summary>
    /// Fire-and-forget fetch + play of a queued entry. The synchronous prefix of
    /// <see cref="PlayFromSourceAsync"/> sets <see cref="_busy"/>, so <see cref="Update"/> won't
    /// try to advance again while the download is in flight.
    /// </summary>
    private void AdvanceToQueued(MediaQueueItem item)
    {
        _ = PlayFromSourceAsync(item.IdOrUrl, null);
    }

    private bool CheckAdmin(EntitySessionEventArgs args)
    {
        return _adminManager.HasAdminFlag(args.SenderSession, AdminFlags.Fun);
    }

    private void SendStatus(ICommonSession? session, string message, bool isError = false)
    {
        // Queue auto-advance has no requesting session — status lines are simply skipped.
        if (session == null)
            return;

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

    private MediaPlayerQueueStateEvent BuildQueueState()
    {
        // Copy so the networked event doesn't alias the live list.
        var items = _queue.Select(q => new MediaQueueItem { IdOrUrl = q.IdOrUrl, Title = q.Title }).ToList();
        return new MediaPlayerQueueStateEvent(items, _repeat, _shuffle);
    }

    private void BroadcastQueueState()
    {
        RaiseNetworkEvent(BuildQueueState(), Filter.Broadcast());
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

        // Sync the queue + modes even when nothing is currently playing.
        RaiseNetworkEvent(BuildQueueState(), args.Session);

        // Идущий ТВ-клип тоже досылаем новичку.
        TvSyncNewPlayer(args.Session);

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
    /// Stops playback for everyone and clears the queue. Repeat/shuffle modes are kept.
    /// </summary>
    public void Stop()
    {
        _current = null;
        _queue.Clear();
        BroadcastState();
        BroadcastQueueState();
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

    /// <summary>
    /// Jumps the playhead to <paramref name="position"/> seconds for everyone.
    /// </summary>
    public void Seek(float position)
    {
        if (_current is not { } track)
            return;

        position = Math.Clamp(position, 0f, track.Duration);

        if (track.Paused)
            track.PausedPosition = position;
        else
            track.StartedAt = _timing.RealTime - TimeSpan.FromSeconds(position);

        BroadcastState();
    }

    private void OnSeekRequest(MediaPlayerSeekRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        Seek(ev.Position);
    }

    private void OnEnqueueRequest(MediaPlayerEnqueueRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        var idOrUrl = ev.IdOrUrl.Trim();
        if (idOrUrl.Length is 0 or > 300)
            return;

        // Nothing playing and not busy → start immediately instead of parking in the queue.
        if (_current == null && !_busy)
        {
            _ = PlayFromSourceAsync(idOrUrl, args.SenderSession);
            return;
        }

        _queue.Add(new MediaQueueItem { IdOrUrl = idOrUrl, Title = ev.Title.Trim() });
        BroadcastQueueState();
    }

    private void OnQueueRemoveRequest(MediaPlayerQueueRemoveRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        if (ev.Index < 0)
            _queue.Clear();
        else if (ev.Index < _queue.Count)
            _queue.RemoveAt(ev.Index);
        else
            return;

        BroadcastQueueState();
    }

    private void OnModeRequest(MediaPlayerModeRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        _repeat = ev.Repeat;
        _shuffle = ev.Shuffle;
        BroadcastQueueState();
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
            BeginThumbnailDownloads(results, session);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Media search error: {e}");
            RaiseNetworkEvent(new MediaPlayerSearchResponseEvent([], Loc.GetString("media-player-error-ytdlp")), session);
        }
    }

    private void OnPlayRequest(MediaPlayerPlayRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        if (_busy)
        {
            SendStatus(args.SenderSession, Loc.GetString("media-player-status-busy"), isError: true);
            return;
        }

        _ = PlayFromSourceAsync(ev.IdOrUrl, args.SenderSession);
    }

    /// <summary>
    /// Resolves, downloads and starts a track. Shared by manual play requests and by queue
    /// auto-advance — pass <paramref name="session"/> null for the latter (no status feedback).
    /// </summary>
    private async Task PlayFromSourceAsync(string idOrUrl, ICommonSession? session)
    {
        idOrUrl = idOrUrl.Trim();
        if (idOrUrl.Length is 0 or > 300)
            return;

        _busy = true;

        try
        {
            if (!await EnsureToolsAsync(session))
                return;

            _sawmill.Info($"{session?.Name ?? "queue"} requested track: {idOrUrl}");

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

    private async Task<byte[]?> GetOrDownloadTrack(string id, ICommonSession? session)
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

            // Лимит потоков уходит внутрь ffmpeg-постпроцессора yt-dlp: иначе извлечение звука
            // тоже забирает все ядра хоста (см. MediaPlayerFfmpegThreads).
            var threads = _cfg.GetCVar(WegaCVars.MediaPlayerFfmpegThreads);
            var ppThreads = threads > 0 ? $"-threads {threads}" : string.Empty;

            var (exitCode, _, stderr) = await RunYtdlp(
                ppThreads.Length > 0
                    ? $"--postprocessor-args \"ExtractAudio:{ppThreads}\" {baseArgs}"
                    : baseArgs);

            // Some ffmpeg builds ship without libvorbis; retry with the native experimental encoder.
            if (exitCode != 0 && stderr.Contains("Encoder not found"))
            {
                _sawmill.Warning("libvorbis missing, retrying with the native vorbis encoder");
                (exitCode, _, stderr) = await RunYtdlp(
                    $"--postprocessor-args \"ExtractAudio:-c:a vorbis -strict -2 {ppThreads}\" {baseArgs}");
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
                ThumbnailUrl = GetThumbnailUrl(entry),
                DurationSeconds = entry.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number
                    ? (int)dur.GetDouble()
                    : 0,
            });
        }

        return results;
    }

    private static string GetThumbnailUrl(JsonElement entry)
    {
        // Prefer the largest thumbnail from the dedicated array.
        if (entry.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
        {
            string? bestUrl = null;
            var bestArea = 0;

            foreach (var thumb in thumbs.EnumerateArray())
            {
                if (!thumb.TryGetProperty("url", out var urlProp))
                    continue;

                var url = urlProp.GetString();
                if (string.IsNullOrEmpty(url))
                    continue;

                var width = thumb.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number
                    ? w.GetInt32()
                    : 0;
                var height = thumb.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number
                    ? h.GetInt32()
                    : 0;
                var area = width * height;

                if (area <= bestArea)
                    continue;

                bestArea = area;
                bestUrl = url;
            }

            if (!string.IsNullOrEmpty(bestUrl))
                return bestUrl;
        }

        // Fallback to the single default thumbnail.
        if (entry.TryGetProperty("thumbnail", out var thumbUrl) && thumbUrl.ValueKind == JsonValueKind.String)
            return thumbUrl.GetString() ?? string.Empty;

        return string.Empty;
    }

    private void BeginThumbnailDownloads(List<MediaSearchResult> results, ICommonSession session)
    {
        foreach (var result in results)
        {
            var id = result.Id;
            var url = result.ThumbnailUrl;

            // Don't skip on an empty primary URL: flat-playlist search sometimes omits the
            // thumbnail for the first/channel result, but DownloadThumbnailAsync falls back to the
            // id-based hq/mq/default thumbnails every YouTube video has. Only skip if there's
            // nothing to work with at all.
            if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(id))
                continue;

            _ = Task.Run(async () => await DownloadThumbnailAsync(id, url, session));
        }
    }

    private async Task DownloadThumbnailAsync(string trackId, string url, ICommonSession session)
    {
        // Перебираем кандидатов по очереди: сначала лучший превью от yt-dlp (часто maxresdefault.jpg),
        // затем ГАРАНТИРОВАННО существующие у любого ролика hq/mq/default. Раньше брали только «самый
        // крупный» URL, а maxresdefault есть далеко не у всех видео (YouTube отдаёт 404) — из-за этого
        // превью показывалось лишь у части треков. Первый успешно скачавшийся кандидат и уходит клиенту.
        foreach (var candidate in ThumbnailCandidates(trackId, url))
        {
            byte[]? pngData;
            try
            {
                pngData = await TryGetThumbnailPngAsync(candidate);
            }
            catch (Exception e)
            {
                _sawmill.Debug($"Thumbnail candidate failed for {trackId} ({candidate}): {e.Message}");
                continue;
            }

            if (pngData == null)
                continue;

            var data = pngData;
            _taskManager.RunOnMainThread(() =>
            {
                try
                {
                    RaiseNetworkEvent(new MediaPlayerThumbnailEvent(trackId, data), session);
                }
                catch (Exception e)
                {
                    _sawmill.Debug($"Failed to send thumbnail to {session.Name}: {e.Message}");
                }
            });
            return;
        }

        _sawmill.Debug($"No working thumbnail candidate for {trackId}");
    }

    /// <summary>
    /// Скачивает и нормализует (ресайз до <see cref="ThumbnailWidth"/>, PNG) превью по одному URL.
    /// Возвращает null, если ответ не 2xx (напр. 404 у отсутствующего maxresdefault) или картинка пустая —
    /// вызывающий переходит к следующему кандидату. Успех кэшируется по URL.
    /// </summary>
    private async Task<byte[]?> TryGetThumbnailPngAsync(string url)
    {
        lock (_thumbnailCacheLock)
        {
            if (_thumbnailCache.TryGetValue(url, out var cached))
                return cached;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("wega-mega-mediaplayer");

        // GetAsync (а не GetByteArrayAsync): 404 не бросает исключение, а даёт мягко перейти к фолбэку.
        using var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return null;

        var data = await response.Content.ReadAsByteArrayAsync();
        if (data.Length == 0)
            return null;

        using var image = Image.Load<Rgba32>(data);
        if (image.Width <= 0 || image.Height <= 0)
            return null;

        if (image.Width > ThumbnailWidth)
        {
            var height = Math.Max(1, (int)(image.Height * (ThumbnailWidth / (float)image.Width)));
            image.Mutate(x => x.Resize(ThumbnailWidth, height));
        }

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        var pngData = ms.ToArray();

        lock (_thumbnailCacheLock)
        {
            _thumbnailCache[url] = pngData;
        }

        return pngData;
    }

    /// <summary>
    /// URL-кандидаты превью в порядке предпочтения: сначала выбранный yt-dlp (лучшее качество), затем
    /// собранные по id ролика hq/mq/default.jpg — они есть у КАЖДОГО видео YouTube, поэтому служат
    /// надёжным запасным вариантом, когда основной (напр. maxresdefault) отсутствует.
    /// </summary>
    private static IEnumerable<string> ThumbnailCandidates(string trackId, string primaryUrl)
    {
        var seen = new HashSet<string>();

        if (!string.IsNullOrEmpty(primaryUrl) && seen.Add(primaryUrl))
            yield return primaryUrl;

        if (!IsYoutubeId(trackId))
            yield break;

        foreach (var name in new[] { "hqdefault", "mqdefault", "default" })
        {
            var url = $"https://i.ytimg.com/vi/{trackId}/{name}.jpg";
            if (seen.Add(url))
                yield return url;
        }
    }

    /// <summary>Похож ли id на 11-символьный идентификатор видео YouTube (для сборки i.ytimg.com URL).</summary>
    private static bool IsYoutubeId(string id)
    {
        if (id.Length != 11)
            return false;

        foreach (var c in id)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }

        return true;
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
