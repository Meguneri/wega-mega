using Robust.Shared.Serialization;

namespace Content.Shared.MediaPlayer;

/// <summary>
/// One search result from the online source (YouTube via yt-dlp).
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaSearchResult
{
    public string Id = string.Empty;
    public string Title = string.Empty;
    public string Uploader = string.Empty;
    public string ThumbnailUrl = string.Empty;
    public int DurationSeconds;
}

/// <summary>
/// Admin client asks the server to search the online source.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerSearchRequestEvent(string query) : EntityEventArgs
{
    public string Query { get; } = query;
}

/// <summary>
/// Server returns search results (or an error) to the requesting admin.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerSearchResponseEvent(List<MediaSearchResult> results, string? error) : EntityEventArgs
{
    public List<MediaSearchResult> Results { get; } = results;
    public string? Error { get; } = error;
}

/// <summary>
/// Admin client asks the server to play a track: a search-result id or a direct URL.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerPlayRequestEvent(string idOrUrl) : EntityEventArgs
{
    public string IdOrUrl { get; } = idOrUrl;
}

/// <summary>
/// Admin client asks the server to stop playback for everyone.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerStopRequestEvent : EntityEventArgs;

/// <summary>
/// Admin client asks the server to toggle pause/resume for everyone.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerPauseRequestEvent : EntityEventArgs;

/// <summary>
/// Admin client asks the server to jump the playhead to <see cref="Position"/> seconds.
/// Applies to everyone, since playback is server-synced.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerSeekRequestEvent(float position) : EntityEventArgs
{
    public float Position { get; } = position;
}

/// <summary>
/// Admin client asks the server to append a track to the queue. <see cref="Title"/> is the
/// display name known at enqueue time (before the track is downloaded), used for the queue UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerEnqueueRequestEvent(string idOrUrl, string title) : EntityEventArgs
{
    public string IdOrUrl { get; } = idOrUrl;
    public string Title { get; } = title;
}

/// <summary>
/// Admin client asks the server to remove a queue entry by index, or clear the whole queue
/// when <see cref="Index"/> is negative.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerQueueRemoveRequestEvent(int index) : EntityEventArgs
{
    public int Index { get; } = index;
}

/// <summary>
/// Admin client asks the server to set the repeat / shuffle playback modes for everyone.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerModeRequestEvent(bool repeat, bool shuffle) : EntityEventArgs
{
    public bool Repeat { get; } = repeat;
    public bool Shuffle { get; } = shuffle;
}

/// <summary>
/// Server tells a client to open the media player window (e.g. from using the player item).
/// <see cref="TvMode"/> — окно открыто с телевизора: кнопки «Играть»/«По ссылке»/«Стоп»
/// управляют ТВ-клипом, а не глобальной музыкой.
/// </summary>
[Serializable, NetSerializable]
public sealed class OpenMediaPlayerEvent(bool tvMode = false) : EntityEventArgs
{
    public bool TvMode { get; } = tvMode;
}

/// <summary>
/// Server sends a progress/status line ("downloading...", errors) to the requesting admin.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerStatusEvent(string message, bool isError) : EntityEventArgs
{
    public string Message { get; } = message;
    public bool IsError { get; } = isError;
}

/// <summary>
/// One chunk of the ogg-vorbis track data, broadcast to every client.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerTrackChunkEvent(string trackId, int index, int total, byte[] data) : EntityEventArgs
{
    public string TrackId { get; } = trackId;
    public int Index { get; } = index;
    public int Total { get; } = total;
    public byte[] Data { get; } = data;
}

/// <summary>
/// Current playback state, broadcast to every client. TrackId null means nothing is playing.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerStateEvent(string? trackId, string title, float duration, float position, bool playing)
    : EntityEventArgs
{
    public string? TrackId { get; } = trackId;
    public string Title { get; } = title;
    public float Duration { get; } = duration;

    /// <summary>
    /// Playback position in seconds at the moment the event was sent.
    /// </summary>
    public float Position { get; } = position;

    public bool Playing { get; } = playing;
}

/// <summary>
/// Server sends a processed thumbnail (PNG) for a single search result to the requesting client.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerThumbnailEvent(string trackId, byte[] pngData) : EntityEventArgs
{
    public string TrackId { get; } = trackId;
    public byte[] PngData { get; } = pngData;
}

/// <summary>
/// One entry in the playback queue, as shown in the client UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaQueueItem
{
    public string IdOrUrl = string.Empty;
    public string Title = string.Empty;
}

/// <summary>
/// Current queue and playback modes, broadcast to every client whenever they change.
/// </summary>
[Serializable, NetSerializable]
public sealed class MediaPlayerQueueStateEvent(List<MediaQueueItem> items, bool repeat, bool shuffle)
    : EntityEventArgs
{
    public List<MediaQueueItem> Items { get; } = items;
    public bool Repeat { get; } = repeat;
    public bool Shuffle { get; } = shuffle;
}
