using System.IO;
using Content.Shared.TvScreen;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Utility;

namespace Content.Client.MediaPlayer;

/// <summary>
/// Клиентская часть ТВ-прототипа: собирает PNG-кадры и ogg-звук клипа, декодирует кадры движковым
/// LoadTextureFromPNGStream (единственный разрешённый песочницей декодер) и крутит клип по кругу.
/// Звук — позиционный: отдельный зацикленный стрим привязывается к КАЖДОЙ сущности-телевизору,
/// поэтому громкость падает с расстоянием, как у любого источника звука в мире.
/// Часы клипа приходят с сервера (TvStartEvent.Position) и дальше идут локально.
/// </summary>
public sealed partial class MediaPlayerSystem
{
    private const string TvLayerKey = "tv-screen";

    /// <summary>Ширина картинки на спрайте в пикселях (3 тайла). Масштаб слоя считается от неё и
    /// фактической ширины кадра, поэтому смена TvWidth на сервере не меняет размер экрана.</summary>
    private const float TvScreenPx = 96f;

    /// <summary>
    /// Сколько декодированных кадров держим в видеопамяти одновременно.
    /// Раньше клип декодировался ЦЕЛИКОМ: четырёхминутный ролик занимал ~200 МБ видеопамяти на
    /// 160px и ~800 МБ на 320px — то есть качество упиралось не в сеть, а в память. Теперь это
    /// скользящее окно, и расход постоянен независимо от длины ролика и разрешения.
    /// </summary>
    private const int TvFrameCacheSize = 60;

    private string? _tvClipId;
    private int _tvFrameCount;
    private float _tvFps;
    private byte[]?[] _tvPngs = [];
    private int _tvFramesReceived;
    private byte[]?[] _tvAudioChunks = [];
    private int _tvAudioReceived = -1; // -1 = TvStartEvent ещё не пришёл / аудио не анонсировано

    /// <summary>Декодированные кадры (скользящее окно) и порядок их появления для вытеснения.</summary>
    private readonly Dictionary<int, OwnedTexture> _tvFrameCache = new();
    private readonly Queue<int> _tvFrameCacheOrder = new();

    /// <summary>Масштаб слоя, посчитанный от фактической ширины кадра из TvStartEvent.</summary>
    private float _tvLayerScale = 0.6f;

    private AudioResource? _tvAudioRes;
    private ResPath? _tvAudioFile;
    private float _tvClock;
    private float _tvDuration;
    private int _tvLastIndex = -1;

    /// <summary>
    /// Клип полностью доехал и синхронизирован — только теперь можно показывать и озвучивать.
    /// Без этого флага звук (он приезжает первым) шёл бы задолго до появления картинки.
    /// </summary>
    private bool _tvReady;

    /// <summary>Клип на паузе: часы стоят, кадр замер, звук выключен.</summary>
    private bool _tvPaused;

    /// <summary>Доля доставленного клипа (0..1) — для строки состояния в окне плеера.</summary>
    public float TvProgress { get; private set; }

    /// <summary>Клип есть и готов к показу (можно ставить на паузу).</summary>
    public bool TvHasClip => _tvReady;

    /// <summary>Клип сейчас на паузе.</summary>
    public bool TvPaused => _tvPaused;

    /// <summary>Клип объявлен, но ещё едет по сети.</summary>
    public bool TvLoading => _tvClipId != null && !_tvReady;

    /// <summary>Состояние ТВ изменилось (готовность/пауза/прогресс) — окну пора обновиться.</summary>
    public event Action? TvStateUpdated;

    /// <summary>Звуковой стрим на каждом телевизоре: ТВ → сущность аудио-стрима.</summary>
    private readonly Dictionary<EntityUid, EntityUid> _tvStreams = new();

    private void InitializeTv()
    {
        SubscribeNetworkEvent<TvStartEvent>(OnTvStart);
        SubscribeNetworkEvent<TvFrameEvent>(OnTvFrame);
        SubscribeNetworkEvent<TvAudioChunkEvent>(OnTvAudioChunk);
        SubscribeNetworkEvent<TvClockSyncEvent>(OnTvClockSync);
        SubscribeNetworkEvent<TvPauseEvent>(OnTvPause);
        SubscribeNetworkEvent<TvProgressEvent>(OnTvProgress);
        SubscribeNetworkEvent<TvStopEvent>(_ =>
        {
            TvReset();
            TvStateUpdated?.Invoke();
        });
    }

    /// <summary>Просит сервер запустить ролик на ТВ-экранах (окно в ТВ-режиме).</summary>
    public void RequestTvPlay(string idOrUrl)
    {
        RaiseNetworkEvent(new TvPlayRequestEvent(idOrUrl));
    }

    /// <summary>Просит сервер остановить ТВ-клип.</summary>
    public void RequestTvStop()
    {
        RaiseNetworkEvent(new TvStopRequestEvent());
    }

    /// <summary>Просит сервер поставить ТВ-клип на паузу / снять с паузы.</summary>
    public void RequestTvPause()
    {
        RaiseNetworkEvent(new TvPauseRequestEvent());
    }

    private void OnTvStart(TvStartEvent ev)
    {
        TvReset();
        _tvClipId = ev.ClipId;
        _tvFrameCount = ev.FrameCount;
        _tvFps = ev.Fps;
        _tvDuration = ev.FrameCount / ev.Fps;
        _tvClock = ev.Position;
        _tvPngs = new byte[]?[ev.FrameCount];
        _tvFramesReceived = 0;
        // Держим экран одного размера при любом разрешении кадра.
        _tvLayerScale = ev.Width > 0 ? TvScreenPx / ev.Width : 0.6f;
        _sawmill.Info($"TV clip incoming: {ev.ClipId}, {ev.FrameCount} frames {ev.Width}x{ev.Height} @ {ev.Fps} fps, pos {ev.Position:0.0}s");
        TvStateUpdated?.Invoke();
    }

    /// <summary>
    /// «Клип доехал»: до этого события ничего не играет и часы не идут — иначе звук, который
    /// приезжает первым, шёл бы на чёрном экране, а часы за время передачи утекли бы вперёд.
    /// </summary>
    private void OnTvClockSync(TvClockSyncEvent ev)
    {
        if (ev.ClipId != _tvClipId)
            return;

        _tvClock = ev.Position;
        _tvPaused = ev.Paused;
        _tvLastIndex = -1;
        _tvReady = true;
        TvProgress = 1f;
        _sawmill.Info($"TV clip ready to play at {ev.Position:0.0}s (paused: {ev.Paused})");
        TvStateUpdated?.Invoke();
    }

    private void OnTvProgress(TvProgressEvent ev)
    {
        if (ev.ClipId != _tvClipId)
            return;

        TvProgress = ev.Progress;
        TvStateUpdated?.Invoke();
    }

    /// <summary>Пауза от сервера: часы замирают, звук глохнет, кадр остаётся на экране.</summary>
    private void OnTvPause(TvPauseEvent ev)
    {
        if (_tvClipId == null)
            return;

        _tvPaused = ev.Paused;
        _tvClock = ev.Position;

        if (_tvPaused)
        {
            // Стримы гасим целиком: при снятии паузы FrameUpdate создаст их заново с нужной позиции.
            foreach (var stream in _tvStreams.Values)
            {
                if (Exists(stream))
                    _audio.Stop(stream);
            }
            _tvStreams.Clear();
        }

        TvStateUpdated?.Invoke();
    }

    private void OnTvFrame(TvFrameEvent ev)
    {
        if (ev.ClipId != _tvClipId || ev.Index < 0 || ev.Index >= _tvPngs.Length)
            return;

        if (_tvPngs[ev.Index] == null)
            _tvFramesReceived++;
        _tvPngs[ev.Index] = ev.Png;

        if (_tvFramesReceived < _tvFrameCount)
            return;

        // Кадры НЕ разворачиваем в текстуры заранее — это и съедало сотни мегабайт видеопамяти.
        // PNG-байты всего клипа весят единицы МБ; декодируем по одному кадру в FrameUpdate.
        // Готовность к показу приходит отдельно, в TvClockSyncEvent.
        _tvLastIndex = -1;
        _sawmill.Info($"TV clip received: {_tvFrameCount} frames (декодируются на лету)");
    }

    /// <summary>
    /// Текстура кадра: из кэша либо декодируем прямо сейчас. Кэш — скользящее окно фиксированного
    /// размера, поэтому видеопамять не зависит ни от длины клипа, ни от разрешения. Вытесняем
    /// всегда самый старый кадр: он заведомо уже не висит ни на одном слое, потому что слои
    /// переставляются на новую текстуру при каждой смене кадра (15 раз в секунду).
    /// </summary>
    private Texture? GetTvFrame(int index)
    {
        if (_tvFrameCache.TryGetValue(index, out var cached))
            return cached;

        if (index < 0 || index >= _tvPngs.Length || _tvPngs[index] is not { } png)
            return null;

        try
        {
            using var stream = new MemoryStream(png);
            var texture = _clyde.LoadTextureFromPNGStream(stream);
            _tvFrameCache[index] = texture;
            _tvFrameCacheOrder.Enqueue(index);

            while (_tvFrameCacheOrder.Count > TvFrameCacheSize)
            {
                var oldest = _tvFrameCacheOrder.Dequeue();
                if (oldest != index && _tvFrameCache.Remove(oldest, out var old))
                    old.Dispose();
            }

            return texture;
        }
        catch (Exception e)
        {
            _sawmill.Error($"TV frame {index} decode failed: {e.Message}");
            return null;
        }
    }

    private void OnTvAudioChunk(TvAudioChunkEvent ev)
    {
        if (ev.ClipId != _tvClipId || ev.Total <= 0 || ev.Index < 0 || ev.Index >= ev.Total)
            return;

        if (_tvAudioChunks.Length != ev.Total)
        {
            _tvAudioChunks = new byte[]?[ev.Total];
            _tvAudioReceived = 0;
        }

        if (_tvAudioChunks[ev.Index] == null)
            _tvAudioReceived++;
        _tvAudioChunks[ev.Index] = ev.Data;

        if (_tvAudioReceived < ev.Total)
            return;

        // Аудио собрано: монтируем ogg в контент-рут (как треки плеера) и готовим ресурс.
        try
        {
            var length = 0;
            foreach (var c in _tvAudioChunks)
                length += c!.Length;
            var data = new byte[length];
            var offset = 0;
            foreach (var c in _tvAudioChunks)
            {
                Array.Copy(c!, 0, data, offset, c!.Length);
                offset += c.Length;
            }

            var file = new ResPath($"tv_{_tvClipId}.ogg");
            ContentRoot.AddOrUpdateFile(file, data);
            var res = new AudioResource();
            res.Load(IoCManager.Instance!, Prefix / file);

            _tvAudioFile = file;
            _tvAudioRes = res;
            _tvAudioChunks = [];
            _sawmill.Info($"TV audio ready: {length / 1024} KiB");
        }
        catch (Exception e)
        {
            _sawmill.Error($"TV audio failed: {e.Message}");
            _tvAudioRes = null;
        }
    }

    private void TvReset()
    {
        _tvClipId = null;
        _tvPngs = [];
        _tvFramesReceived = 0;
        _tvFrameCount = 0;
        _tvLastIndex = -1;
        _tvReady = false;
        _tvPaused = false;
        TvProgress = 0f;

        foreach (var stream in _tvStreams.Values)
            _audio.Stop(stream);
        _tvStreams.Clear();

        _tvAudioChunks = [];
        _tvAudioReceived = -1;
        _tvAudioRes = null;
        if (_tvAudioFile is { } file)
        {
            ContentRoot.RemoveFile(file);
            _tvAudioFile = null;
        }

        // Гасим слой на всех экранах.
        var query = EntityQueryEnumerator<TvScreenComponent, SpriteComponent>();
        while (query.MoveNext(out _, out _, out var sprite))
        {
            if (sprite.LayerMapTryGet(TvLayerKey, out var idx))
                sprite.LayerSetVisible(idx, false);
        }

        // Освобождаем кадры ПОСЛЕ гашения слоёв: иначе слой остался бы со ссылкой на
        // уже освобождённую текстуру.
        foreach (var texture in _tvFrameCache.Values)
            texture.Dispose();
        _tvFrameCache.Clear();
        _tvFrameCacheOrder.Clear();
    }

    /// <summary>Пробрасывает личную громкость плеера на все ТВ-стримы (вызов из OnVolumeChanged).</summary>
    private void TvUpdateVolume(float volume)
    {
        foreach (var stream in _tvStreams.Values)
        {
            if (Exists(stream))
                _audio.SetGain(stream, volume);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Пока клип не доехал целиком — экран тёмный и тишина: иначе звук (он приезжает первым)
        // играл бы задолго до картинки.
        if (_tvClipId == null || !_tvReady)
            return;

        if (!_tvPaused)
            _tvClock += frameTime;

        // Видео: кадр по часам клипа.
        var frameChanged = false;
        var index = 0;
        if (_tvFrameCount > 0)
        {
            index = (int)(_tvClock * _tvFps) % _tvFrameCount;
            frameChanged = index != _tvLastIndex;
            _tvLastIndex = index;
        }

        // Текущий кадр разворачиваем по требованию (кэш держит окно вокруг него).
        var texture = _tvFrameCount > 0 ? GetTvFrame(index) : null;

        var query = EntityQueryEnumerator<TvScreenComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            // Слой с кадром: создаём при первом появлении экрана, дальше только меняем текстуру.
            if (texture != null)
            {
                if (!sprite.LayerMapTryGet(TvLayerKey, out var idx))
                {
                    idx = sprite.LayerMapReserveBlank(TvLayerKey);
                    sprite.LayerSetScale(idx, new System.Numerics.Vector2(_tvLayerScale, _tvLayerScale));
                    sprite.LayerSetOffset(idx, new System.Numerics.Vector2(0f, 0.0625f));
                    sprite.LayerSetShader(idx, "unshaded");
                    sprite.LayerSetTexture(idx, texture);
                    sprite.LayerSetVisible(idx, true);
                }
                else if (frameChanged)
                {
                    sprite.LayerSetTexture(idx, texture);
                    sprite.LayerSetVisible(idx, true);
                }
            }

            // Позиционный звук: свой зацикленный стрим на каждом телевизоре (на паузе — молчим).
            if (!_tvPaused && _tvAudioRes is { } audioRes
                && (!_tvStreams.TryGetValue(uid, out var streamEnt) || !Exists(streamEnt)))
            {
                var audioParams = AudioParams.Default
                    .WithVolume(SharedAudioSystem.GainToVolume(_volume))
                    .WithLoop(true);
                var stream = _audio.PlayEntity(audioRes.AudioStream,
                    uid, new ResolvedPathSpecifier(Prefix / _tvAudioFile!.Value), audioParams);
                if (stream != null)
                {
                    _audio.SetPlaybackPosition(stream.Value.Entity, _tvClock % _tvDuration);
                    _tvStreams[uid] = stream.Value.Entity;
                }
            }
        }

        // Чистим записи о стримах телевизоров, которых больше нет (стрим удаляется вместе с ТВ).
        if (_tvStreams.Count > 0)
        {
            List<EntityUid>? dead = null;
            foreach (var (tv, stream) in _tvStreams)
            {
                if (!Exists(tv) || !Exists(stream))
                    (dead ??= new List<EntityUid>()).Add(tv);
            }

            if (dead != null)
                foreach (var tv in dead)
                    _tvStreams.Remove(tv);
        }
    }
}
