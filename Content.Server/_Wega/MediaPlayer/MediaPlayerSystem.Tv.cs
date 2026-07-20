using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using Content.Shared.MediaPlayer;
using Content.Shared.TvScreen;
using Robust.Shared.Player;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Server.MediaPlayer;

/// <summary>
/// Прототип «кино в игре»: сервер скачивает ролик через yt-dlp, ffmpeg режет его в PNG-кадры
/// низкого разрешения и ogg-звук; всё рассылается клиентам и крутится по кругу на экранах
/// (TvScreenComponent). Звук у клиента позиционный — играет из самой сущности телевизора.
/// Синхронизация — по серверным часам клипа (позиция в TvStartEvent + локальный ход у клиента).
/// </summary>
public sealed partial class MediaPlayerSystem
{
    private const int TvWidth = 160;      // ширина кадра; высота по аспекту (чётная)
    private const int TvFps = 15;         // кадров в секунду

    /// <summary>
    /// Размер чанка ТВ-аудио. Мельче общего ChunkSize (128 КиБ) намеренно: рассылка ограничена
    /// байтовым бюджетом на тик, и 128-КиБ куски давали бы рваную отправку далеко за бюджет.
    /// </summary>
    private const int TvChunkSize = 32 * 1024;

    private string? _tvClipId;
    private List<byte[]> _tvFrames = new();
    private byte[]? _tvAudio;
    private int _tvWidth;
    private int _tvHeight;
    private TimeSpan _tvStartedAt;
    private float _tvDuration;
    private bool _tvBusy;

    /// <summary>Клип на паузе (часы стоят, у клиентов замерший кадр и выключенный звук).</summary>
    private bool _tvPaused;

    /// <summary>Позиция, на которой поставили паузу.</summary>
    private float _tvPausedPosition;

    /// <summary>Незавершённые порционные рассылки клипа (broadcast + досылки поздним игрокам).</summary>
    private readonly List<TvSendJob> _tvSends = new();

    private sealed class TvSendJob
    {
        public Filter Filter = default!;
        public string ClipId = string.Empty;
        public int FrameCursor;
        public int AudioCursor;
        public int AudioTotal;
        public int TotalBytes;
        public int SentBytes;

        /// <summary>Когда последний раз сообщали адресату прогресс (шлём не чаще раза в ~2%).</summary>
        public float LastProgress = -1f;

        /// <summary>
        /// Первая (стартовая) рассылка клипа: по её завершении часы клипа (<see cref="_tvStartedAt"/>)
        /// перезапускаются с нуля — ролик начинает идти, когда зрители реально получили кадры,
        /// а не пока они качались.
        /// </summary>
        public bool ResetClock;
    }

    private void InitializeTv()
    {
        SubscribeLocalEvent<TvScreenComponent, ActivateInWorldEvent>(OnTvActivate);
        SubscribeNetworkEvent<TvPlayRequestEvent>(OnTvPlayRequest);
        SubscribeNetworkEvent<TvStopRequestEvent>(OnTvStopRequest);
        SubscribeNetworkEvent<TvPauseRequestEvent>(OnTvPauseRequest);
    }

    /// <summary>Текущая позиция клипа с учётом паузы, сек (клип зациклен).</summary>
    private float TvPosition()
    {
        if (_tvDuration <= 0f)
            return 0f;

        return _tvPaused
            ? _tvPausedPosition
            : (float)((_timing.RealTime - _tvStartedAt).TotalSeconds % _tvDuration);
    }

    /// <summary>Клик по телевизору открывает окно медиаплеера в ТВ-режиме: его кнопки управляют экраном.</summary>
    private void OnTvActivate(Entity<TvScreenComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp(args.User, out ActorComponent? actor))
            return;

        RaiseNetworkEvent(new OpenMediaPlayerEvent(tvMode: true), actor.PlayerSession);
        args.Handled = true;
    }

    private void OnTvPlayRequest(TvPlayRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        TvPlay(ev.IdOrUrl, args.SenderSession);
    }

    private void OnTvStopRequest(TvStopRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        TvStop();
    }

    private void OnTvPauseRequest(TvPauseRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckAdmin(args))
            return;

        TvTogglePause();
    }

    /// <summary>Пауза/продолжение клипа: часы стоят, у клиентов замирает кадр и глохнет звук.</summary>
    public void TvTogglePause()
    {
        if (_tvClipId == null)
            return;

        if (_tvPaused)
        {
            // Снимаем паузу: сдвигаем точку отсчёта так, чтобы клип пошёл с той же позиции.
            _tvStartedAt = _timing.RealTime - TimeSpan.FromSeconds(_tvPausedPosition);
            _tvPaused = false;
        }
        else
        {
            _tvPausedPosition = TvPosition();
            _tvPaused = true;
        }

        RaiseNetworkEvent(new TvPauseEvent(_tvPaused, TvPosition()), Filter.Broadcast());
    }

    /// <summary>Запускает клип на всех ТВ-экранах. Вызывается командой tvplay.</summary>
    public async void TvPlay(string idOrUrl, ICommonSession? session)
    {
        if (_tvBusy)
        {
            SendStatus(session, Loc.GetString("media-player-status-busy"), isError: true);
            return;
        }

        idOrUrl = idOrUrl.Trim();
        if (idOrUrl.Length is 0 or > 300)
            return;

        _tvBusy = true;
        try
        {
            if (!await EnsureToolsAsync(session))
                return;

            _sawmill.Info($"{session?.Name ?? "?"} requested TV clip: {idOrUrl}");
            SendStatus(session, Loc.GetString("media-player-status-resolving"));

            // Метаданные: id для кэша.
            var (metaExit, metaOut, metaErr) = await RunYtdlp(
                $"--no-playlist -J --no-warnings -- \"{Sanitize(idOrUrl)}\"");
            if (metaExit != 0)
            {
                _sawmill.Error($"yt-dlp tv metadata failed: {metaErr}");
                SendStatus(session, Loc.GetString("media-player-error-resolve"), isError: true);
                return;
            }

            using var meta = JsonDocument.Parse(metaOut);
            var id = meta.RootElement.GetProperty("id").GetString() ?? string.Empty;
            if (id.Length == 0)
                return;

            var cacheDir = Path.Combine(_resource.UserData.RootDir ?? ".", CacheFolder);
            Directory.CreateDirectory(cacheDir);

            // Миграция кэша: старые файлы tv_* качались 30-секундными обрезками (до фикса полного
            // видео) и переиспользовались вечно — «телевизор показывает только фрагмент». Сносим.
            foreach (var stale in Directory.EnumerateFiles(cacheDir, "tv_*"))
                File.Delete(stale);

            // Качаем видео ЦЕЛИКОМ (≤360p — файл небольшой). Потолок 2 ГБ как защита от диска на
            // случай многочасового ролика; для обычных клипов/клипов-песен запас огромный.
            var videoPath = Directory.EnumerateFiles(cacheDir, $"tvfull_{id}.*")
                .FirstOrDefault(f => !f.EndsWith(".ogg"));
            if (videoPath == null)
            {
                SendStatus(session, Loc.GetString("media-player-status-downloading"));
                var outTemplate = Path.Combine(cacheDir, "tvfull_%(id)s.%(ext)s");

                var (dlExit, _, dlErr) = await RunYtdlp(
                    "--no-playlist -f \"best[height<=360][ext=mp4]/best[height<=360]/best\" " +
                    $"--max-filesize 2G -o \"{outTemplate}\" --no-warnings -- \"{Sanitize(id)}\"");
                videoPath = Directory.EnumerateFiles(cacheDir, $"tvfull_{id}.*")
                    .FirstOrDefault(f => !f.EndsWith(".ogg"));

                if (dlExit != 0 || videoPath == null)
                {
                    _sawmill.Error($"yt-dlp tv download failed: {dlErr}");
                    SendStatus(session, Loc.GetString("media-player-error-tv-video"), isError: true);
                    return;
                }
            }

            // Режем ВСЁ видео в PNG-кадры во временную папку.
            // PNG, не JPEG: клиентская песочница разрешает декодировать только через
            // IClyde.LoadTextureFromPNGStream (Image.Load из ImageSharp в вайтлисте нет).
            // %06d — до миллиона кадров (полное видео на 15 fps): 4 цифры хватило бы лишь на ~11 мин.
            SendStatus(session, Loc.GetString("media-player-status-broadcasting"));
            var framesDir = Path.Combine(cacheDir, $"tvframes_{id}");
            if (Directory.Exists(framesDir))
                Directory.Delete(framesDir, recursive: true);
            Directory.CreateDirectory(framesDir);

            // -pix_fmt pal8: палитровый PNG (256 цветов) — вдвое легче truecolor при том же виде на
            // экране 160px, а весь клип едет по сети целиком, так что каждый килобайт на кадре
            // умножается на тысячи. Движковый LoadTextureFromPNGStream палитру понимает.
            var pattern = Path.Combine(framesDir, "f_%06d.png");
            var (ffExit, ffErr) = await RunFfmpeg(
                $"-i \"{videoPath}\" -vf \"fps={TvFps},scale={TvWidth}:-2\" -pix_fmt pal8 \"{pattern}\"");
            var frameFiles = Directory.EnumerateFiles(framesDir, "f_*.png").OrderBy(f => f).ToList();
            if (ffExit != 0 || frameFiles.Count == 0)
            {
                _sawmill.Error($"ffmpeg tv frames failed: {ffErr}");
                SendStatus(session, Loc.GetString("media-player-error-tv-frames"), isError: true);
                return;
            }

            var frames = new List<byte[]>(frameFiles.Count);
            foreach (var f in frameFiles)
                frames.Add(await File.ReadAllBytesAsync(f));
            Directory.Delete(framesDir, recursive: true);

            // Размер кадра берём из первого PNG (scale=-2 мог слегка подогнать высоту).
            using (var img = SixLabors.ImageSharp.Image.Load<Rgba32>(frames[0]))
            {
                _tvWidth = img.Width;
                _tvHeight = img.Height;
            }

            // Звуковая дорожка того же отрезка — ogg-vorbis. Клиент привяжет её к сущностям
            // телевизоров (позиционный звук), а не к глобальному плееру.
            var audioPath = Path.Combine(cacheDir, $"tvfull_{id}.ogg");
            // -ac 1: ОБЯЗАТЕЛЬНО моно — позиционный источник движка не умеет позиционировать
            // стерео (assert «Make sure the audio is MONO» и краш клиента).
            var (aExit, aErr) = await RunFfmpeg(
                $"-i \"{videoPath}\" -vn -ac 1 -c:a libvorbis -b:a 96k -f ogg \"{audioPath}\"");
            if (aExit != 0 && aErr.Contains("Encoder not found"))
            {
                _sawmill.Warning("libvorbis missing, retrying TV audio with the native vorbis encoder");
                (aExit, aErr) = await RunFfmpeg(
                    $"-i \"{videoPath}\" -vn -ac 1 -c:a vorbis -strict -2 -f ogg \"{audioPath}\"");
            }
            if (aExit != 0 || !File.Exists(audioPath))
            {
                _sawmill.Error($"ffmpeg tv audio failed: {aErr}");
                SendStatus(session, Loc.GetString("media-player-error-tv-audio"), isError: true);
                return;
            }

            _tvClipId = id;
            _tvFrames = frames;
            _tvAudio = await File.ReadAllBytesAsync(audioPath);
            _tvDuration = frames.Count / (float)TvFps;
            _tvStartedAt = _timing.RealTime;
            _tvPaused = false;
            _tvPausedPosition = 0f;

            var total = frames.Sum(f => f.Length);
            _sawmill.Info($"TV clip {id}: {frames.Count} frames {_tvWidth}x{_tvHeight}, " +
                          $"{total / 1024} KiB video, {_tvAudio.Length / 1024} KiB audio, {_tvDuration:0.0}s loop");

            TvBroadcast(Filter.Broadcast(), resetClock: true);
        }
        catch (Exception e)
        {
            _sawmill.Error($"TV play error: {e}");
            SendStatus(session, Loc.GetString("media-player-error-ytdlp"), isError: true);
        }
        finally
        {
            _tvBusy = false;
        }
    }

    /// <summary>Останавливает клип и гасит все экраны.</summary>
    public void TvStop()
    {
        _tvClipId = null;
        _tvFrames = new List<byte[]>();
        _tvAudio = null;
        _tvPaused = false;
        _tvPausedPosition = 0f;
        _tvSends.Clear();
        RaiseNetworkEvent(new TvStopEvent(), Filter.Broadcast());
    }

    /// <summary>
    /// Шлёт стартовое событие сразу (оно маленькое), а объёмные кадры/аудио ставит в порционную
    /// рассылку — их дотачивает <see cref="TvTickSend"/> по несколько за тик, чтобы не завалить
    /// сервер и сетевой канал разом.
    /// </summary>
    private void TvBroadcast(Filter filter, bool resetClock = false)
    {
        if (_tvClipId is not { } id || _tvFrames.Count == 0 || _tvAudio is not { } audio)
            return;

        // Позиция клипа на момент отправки — чтобы поздно зашедший попал в тот же кадр цикла.
        RaiseNetworkEvent(new TvStartEvent(id, TvFps, _tvFrames.Count, _tvWidth, _tvHeight, TvPosition()), filter);

        var totalBytes = audio.Length;
        foreach (var frame in _tvFrames)
            totalBytes += frame.Length;

        _tvSends.Add(new TvSendJob
        {
            Filter = filter,
            ClipId = id,
            AudioTotal = (audio.Length + TvChunkSize - 1) / TvChunkSize,
            TotalBytes = totalBytes,
            ResetClock = resetClock,
        });
    }

    /// <summary>
    /// Дотачивает порционные рассылки в рамках БАЙТОВОГО бюджета на тик (cvar
    /// <c>wega.media_player_tv_kbps</c>). Раньше лимит был в штуках (20 кадров + 4 чанка по 128 КиБ),
    /// то есть попытка пропихнуть ~20 МБ/с в надёжный канал: очередь разбухала, кадры ползли
    /// минутами, а игровые пакеты (тот же клик по телевизору) стояли в очереди за ними.
    /// Сначала уходит аудио (оно небольшое), затем кадры; играть клиент начнёт только когда
    /// приедет всё — см. <see cref="TvClockSyncEvent"/>.
    /// </summary>
    private void TvTickSend()
    {
        if (_tvSends.Count == 0)
            return;

        // Бюджет на тик = КиБ/с из cvar, делённые на частоту тиков.
        var perSecond = Math.Max(16, _cfg.GetCVar(WegaCVars.MediaPlayerTvKbps)) * 1024;
        var tickRate = Math.Max(1, (int)_timing.TickRate);
        var budget = Math.Max(1024, perSecond / tickRate);

        for (var j = _tvSends.Count - 1; j >= 0; j--)
        {
            var job = _tvSends[j];

            // Клип сменился/остановлен, пока досылали — задача устарела, выкидываем.
            if (job.ClipId != _tvClipId || _tvAudio is not { } audio)
            {
                _tvSends.RemoveAt(j);
                continue;
            }

            var spent = 0;

            // Аудио вперёд: клиенту нужно время собрать и смонтировать ogg-ресурс.
            while (spent < budget && job.AudioCursor < job.AudioTotal)
            {
                var offset = job.AudioCursor * TvChunkSize;
                var size = Math.Min(TvChunkSize, audio.Length - offset);
                var chunk = new byte[size];
                Array.Copy(audio, offset, chunk, 0, size);
                RaiseNetworkEvent(new TvAudioChunkEvent(job.ClipId, job.AudioCursor, job.AudioTotal, chunk), job.Filter);
                job.AudioCursor++;
                spent += size;
                job.SentBytes += size;
            }

            while (spent < budget && job.FrameCursor < _tvFrames.Count)
            {
                var frame = _tvFrames[job.FrameCursor];
                RaiseNetworkEvent(new TvFrameEvent(job.ClipId, job.FrameCursor, frame), job.Filter);
                job.FrameCursor++;
                spent += frame.Length;
                job.SentBytes += frame.Length;
            }

            var done = job.FrameCursor >= _tvFrames.Count && job.AudioCursor >= job.AudioTotal;

            // Прогресс адресату (не чаще, чем раз в 2%) — окно показывает «Загрузка ролика… 45%».
            if (!done && job.TotalBytes > 0)
            {
                var progress = Math.Clamp(job.SentBytes / (float)job.TotalBytes, 0f, 1f);
                if (progress - job.LastProgress >= 0.02f)
                {
                    job.LastProgress = progress;
                    RaiseNetworkEvent(new TvProgressEvent(job.ClipId, progress), job.Filter);
                }
            }

            if (done)
            {
                // Передача доехала: стартовая рассылка перезапускает часы клипа с нуля, а адресату
                // в любом случае подводим локальные часы — пока кадры качались, они утекли вперёд.
                if (job.ResetClock)
                {
                    _tvStartedAt = _timing.RealTime;
                    _tvPausedPosition = 0f;
                }

                RaiseNetworkEvent(new TvClockSyncEvent(job.ClipId, TvPosition(), _tvPaused), job.Filter);
                _tvSends.RemoveAt(j);
            }
        }
    }

    /// <summary>Досылает текущий клип поздно подключившемуся игроку (вызов из OnPlayerStatusChanged).</summary>
    private void TvSyncNewPlayer(ICommonSession session)
    {
        TvBroadcast(Filter.SinglePlayer(session));
    }

    /// <summary>Запускает ffmpeg с теми же правилами поиска бинарника, что и yt-dlp-провижн.</summary>
    private async Task<(int ExitCode, string Stderr)> RunFfmpeg(string arguments)
    {
        var fileName = _resolvedFfmpegDir != null
            ? Path.Combine(_resolvedFfmpegDir, FfmpegFileName)
            : FfmpegFileName; // на PATH

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = "-y -hide_banner -loglevel error " + arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi)!;
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, stderr);
    }
}
