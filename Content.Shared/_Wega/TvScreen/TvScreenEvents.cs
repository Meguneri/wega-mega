using Robust.Shared.Serialization;

namespace Content.Shared.TvScreen;

/// <summary>
/// Server announces a new looping clip: metadata first, then video frames (<see cref="TvFrameEvent"/>)
/// and audio chunks (<see cref="TvAudioChunkEvent"/>). Clients start playback once everything arrived.
/// <see cref="Position"/> — позиция клипа на момент отправки (для поздно подключившихся), клип
/// зациклен, поэтому позиция берётся по модулю длительности.
/// </summary>
[Serializable, NetSerializable]
public sealed class TvStartEvent(string clipId, float fps, int frameCount, int width, int height, float position)
    : EntityEventArgs
{
    public string ClipId { get; } = clipId;
    public float Fps { get; } = fps;
    public int FrameCount { get; } = frameCount;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public float Position { get; } = position;
}

/// <summary>
/// Подводит часы клипа на клиенте к серверной позиции. Шлётся адресату по завершении порционной
/// передачи кадров/аудио: пока клип доезжал, локальные часы (запущенные TvStartEvent) утекли
/// вперёд — без подводки телевизор «включался» с середины ролика.
/// </summary>
[Serializable, NetSerializable]
public sealed class TvClockSyncEvent(string clipId, float position) : EntityEventArgs
{
    public string ClipId { get; } = clipId;
    public float Position { get; } = position;
}

/// <summary>
/// One PNG-encoded video frame of the current clip. PNG (не JPEG) — клиент в песочнице может
/// декодировать только через движковый IClyde.LoadTextureFromPNGStream.
/// </summary>
[Serializable, NetSerializable]
public sealed class TvFrameEvent(string clipId, int index, byte[] png) : EntityEventArgs
{
    public string ClipId { get; } = clipId;
    public int Index { get; } = index;
    public byte[] Png { get; } = png;
}

/// <summary>One chunk of the clip's ogg-vorbis audio track (позиционный звук из телевизора).</summary>
[Serializable, NetSerializable]
public sealed class TvAudioChunkEvent(string clipId, int index, int total, byte[] data) : EntityEventArgs
{
    public string ClipId { get; } = clipId;
    public int Index { get; } = index;
    public int Total { get; } = total;
    public byte[] Data { get; } = data;
}

/// <summary>Stop playback and blank every TV screen.</summary>
[Serializable, NetSerializable]
public sealed class TvStopEvent : EntityEventArgs;

/// <summary>Admin client asks the server to play a clip on the TV screens (id or URL).</summary>
[Serializable, NetSerializable]
public sealed class TvPlayRequestEvent(string idOrUrl) : EntityEventArgs
{
    public string IdOrUrl { get; } = idOrUrl;
}

/// <summary>Admin client asks the server to stop the TV clip.</summary>
[Serializable, NetSerializable]
public sealed class TvStopRequestEvent : EntityEventArgs;
