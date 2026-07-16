using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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

    // Рассылка кадров/аудио размазана по тикам: полное видео — это тысячи кадров, и слать их
    // одним тиком значит застопорить сервер и переполнить надёжный сетевой канал. За тик уходит
    // не больше этого числа кадров и чанков (на каждого адресата).
    private const int TvFramesPerTick = 20;
    private const int TvAudioChunksPerTick = 4;

    private string? _tvClipId;
    private List<byte[]> _tvFrames = new();
    private byte[]? _tvAudio;
    private int _tvWidth;
    private int _tvHeight;
    private TimeSpan _tvStartedAt;
    private float _tvDuration;
    private bool _tvBusy;

    /// <summary>Незавершённые порционные рассылки клипа (broadcast + досылки поздним игрокам).</summary>
    private readonly List<TvSendJob> _tvSends = new();

    private sealed class TvSendJob
    {
        public Filter Filter = default!;
        public string ClipId = string.Empty;
        public int FrameCursor;
        public int AudioCursor;
        public int AudioTotal;
    }

    private void InitializeTv()
    {
        SubscribeLocalEvent<TvScreenComponent, ActivateInWorldEvent>(OnTvActivate);
        SubscribeNetworkEvent<TvPlayRequestEvent>(OnTvPlayRequest);
        SubscribeNetworkEvent<TvStopRequestEvent>(OnTvStopRequest);
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

            // Качаем видео ЦЕЛИКОМ (≤360p — файл небольшой). Потолок 2 ГБ как защита от диска на
            // случай многочасового ролика; для обычных клипов/клипов-песен запас огромный.
            var videoPath = Directory.EnumerateFiles(cacheDir, $"tv_{id}.*")
                .FirstOrDefault(f => !f.EndsWith(".ogg"));
            if (videoPath == null)
            {
                SendStatus(session, Loc.GetString("media-player-status-downloading"));
                var outTemplate = Path.Combine(cacheDir, "tv_%(id)s.%(ext)s");

                var (dlExit, _, dlErr) = await RunYtdlp(
                    "--no-playlist -f \"best[height<=360][ext=mp4]/best[height<=360]/best\" " +
                    $"--max-filesize 2G -o \"{outTemplate}\" --no-warnings -- \"{Sanitize(id)}\"");
                videoPath = Directory.EnumerateFiles(cacheDir, $"tv_{id}.*")
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

            var pattern = Path.Combine(framesDir, "f_%06d.png");
            var (ffExit, ffErr) = await RunFfmpeg(
                $"-i \"{videoPath}\" -vf \"fps={TvFps},scale={TvWidth}:-2\" \"{pattern}\"");
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
            var audioPath = Path.Combine(cacheDir, $"tv_{id}.ogg");
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

            var total = frames.Sum(f => f.Length);
            _sawmill.Info($"TV clip {id}: {frames.Count} frames {_tvWidth}x{_tvHeight}, " +
                          $"{total / 1024} KiB video, {_tvAudio.Length / 1024} KiB audio, {_tvDuration:0.0}s loop");

            TvBroadcast(Filter.Broadcast());
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
        _tvSends.Clear();
        RaiseNetworkEvent(new TvStopEvent(), Filter.Broadcast());
    }

    /// <summary>
    /// Шлёт стартовое событие сразу (оно маленькое), а объёмные кадры/аудио ставит в порционную
    /// рассылку — их дотачивает <see cref="TvTickSend"/> по несколько за тик, чтобы не завалить
    /// сервер и сетевой канал разом.
    /// </summary>
    private void TvBroadcast(Filter filter)
    {
        if (_tvClipId is not { } id || _tvFrames.Count == 0 || _tvAudio is not { } audio)
            return;

        // Позиция клипа на момент отправки — чтобы поздно зашедший попал в тот же кадр цикла.
        var position = (float)((_timing.RealTime - _tvStartedAt).TotalSeconds % _tvDuration);
        RaiseNetworkEvent(new TvStartEvent(id, TvFps, _tvFrames.Count, _tvWidth, _tvHeight, position), filter);

        _tvSends.Add(new TvSendJob
        {
            Filter = filter,
            ClipId = id,
            AudioTotal = (audio.Length + ChunkSize - 1) / ChunkSize,
        });
    }

    /// <summary>Дотачивает порционные рассылки: за тик — до TvFramesPerTick кадров и TvAudioChunksPerTick чанков на задачу.</summary>
    private void TvTickSend()
    {
        if (_tvSends.Count == 0)
            return;

        for (var j = _tvSends.Count - 1; j >= 0; j--)
        {
            var job = _tvSends[j];

            // Клип сменился/остановлен, пока досылали — задача устарела, выкидываем.
            if (job.ClipId != _tvClipId || _tvAudio is not { } audio)
            {
                _tvSends.RemoveAt(j);
                continue;
            }

            var frameEnd = Math.Min(job.FrameCursor + TvFramesPerTick, _tvFrames.Count);
            for (; job.FrameCursor < frameEnd; job.FrameCursor++)
                RaiseNetworkEvent(new TvFrameEvent(job.ClipId, job.FrameCursor, _tvFrames[job.FrameCursor]), job.Filter);

            var chunkEnd = Math.Min(job.AudioCursor + TvAudioChunksPerTick, job.AudioTotal);
            for (; job.AudioCursor < chunkEnd; job.AudioCursor++)
            {
                var offset = job.AudioCursor * ChunkSize;
                var size = Math.Min(ChunkSize, audio.Length - offset);
                var chunk = new byte[size];
                Array.Copy(audio, offset, chunk, 0, size);
                RaiseNetworkEvent(new TvAudioChunkEvent(job.ClipId, job.AudioCursor, job.AudioTotal, chunk), job.Filter);
            }

            if (job.FrameCursor >= _tvFrames.Count && job.AudioCursor >= job.AudioTotal)
                _tvSends.RemoveAt(j);
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
