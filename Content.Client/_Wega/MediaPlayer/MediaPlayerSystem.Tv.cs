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

    /// <summary>Масштаб слоя: кадр 160px шириной → 96px на спрайте (3 тайла).</summary>
    private const float TvLayerScale = 0.6f;

    private string? _tvClipId;
    private int _tvFrameCount;
    private float _tvFps;
    private byte[]?[] _tvPngs = [];
    private int _tvFramesReceived;
    private byte[]?[] _tvAudioChunks = [];
    private int _tvAudioReceived = -1; // -1 = TvStartEvent ещё не пришёл / аудио не анонсировано

    private List<Texture>? _tvTextures;
    private AudioResource? _tvAudioRes;
    private ResPath? _tvAudioFile;
    private float _tvClock;
    private float _tvDuration;
    private int _tvLastIndex = -1;

    /// <summary>Звуковой стрим на каждом телевизоре: ТВ → сущность аудио-стрима.</summary>
    private readonly Dictionary<EntityUid, EntityUid> _tvStreams = new();

    private void InitializeTv()
    {
        SubscribeNetworkEvent<TvStartEvent>(OnTvStart);
        SubscribeNetworkEvent<TvFrameEvent>(OnTvFrame);
        SubscribeNetworkEvent<TvAudioChunkEvent>(OnTvAudioChunk);
        SubscribeNetworkEvent<TvStopEvent>(_ => TvReset());
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
        _sawmill.Info($"TV clip incoming: {ev.ClipId}, {ev.FrameCount} frames {ev.Width}x{ev.Height} @ {ev.Fps} fps, pos {ev.Position:0.0}s");
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

        // Все кадры на месте — декодируем одним заходом.
        try
        {
            var textures = new List<Texture>(_tvFrameCount);
            foreach (var png in _tvPngs)
            {
                using var stream = new MemoryStream(png!);
                textures.Add(_clyde.LoadTextureFromPNGStream(stream));
            }

            _tvTextures = textures;
            _tvLastIndex = -1;
            _tvPngs = []; // сырые байты больше не нужны
            _sawmill.Info($"TV clip ready: {textures.Count} frames decoded");
        }
        catch (Exception e)
        {
            _sawmill.Error($"TV decode failed: {e.Message}");
            TvReset();
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
        _tvTextures = null; // текстуры освободит GC
        _tvLastIndex = -1;

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

        if (_tvClipId == null)
            return;

        _tvClock += frameTime;

        // Видео: кадр по часам клипа.
        var frameChanged = false;
        var index = 0;
        if (_tvTextures is { Count: > 0 } textures)
        {
            index = (int)(_tvClock * _tvFps) % textures.Count;
            frameChanged = index != _tvLastIndex;
            _tvLastIndex = index;
        }

        var query = EntityQueryEnumerator<TvScreenComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            // Слой с кадром: создаём при первом появлении экрана, дальше только меняем текстуру.
            if (_tvTextures is { Count: > 0 } frames2)
            {
                if (!sprite.LayerMapTryGet(TvLayerKey, out var idx))
                {
                    idx = sprite.LayerMapReserveBlank(TvLayerKey);
                    sprite.LayerSetScale(idx, new System.Numerics.Vector2(TvLayerScale, TvLayerScale));
                    sprite.LayerSetOffset(idx, new System.Numerics.Vector2(0f, 0.0625f));
                    sprite.LayerSetShader(idx, "unshaded");
                    sprite.LayerSetTexture(idx, frames2[index]);
                    sprite.LayerSetVisible(idx, true);
                }
                else if (frameChanged)
                {
                    sprite.LayerSetTexture(idx, frames2[index]);
                    sprite.LayerSetVisible(idx, true);
                }
            }

            // Позиционный звук: свой зацикленный стрим на каждом телевизоре.
            if (_tvAudioRes is { } audioRes && (!_tvStreams.TryGetValue(uid, out var streamEnt) || !Exists(streamEnt)))
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
