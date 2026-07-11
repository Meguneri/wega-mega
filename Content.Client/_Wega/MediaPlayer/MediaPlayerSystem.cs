using System.IO;
using System.Text;
using Content.Client._Wega.MediaPlayer.UI;
using Content.Shared.CCVar;
using Content.Shared.MediaPlayer;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.MediaPlayer;

/// <summary>
/// Client side of the global media player: assembles track chunks sent by the server,
/// plays them with the player's personal volume, and syncs the playback position.
/// </summary>
public sealed class MediaPlayerSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private AudioSystem _audio = default!;

    private ISawmill _sawmill = default!;

    private static readonly MemoryContentRoot ContentRoot = new();
    private static readonly ResPath Prefix = ResPath.Root / "MediaPlayer";

    // Tracks which resource managers already have our root mounted. In production this
    // only ever holds one entry; multiple entries appear when several clients share a
    // process (integration tests).
    private static readonly HashSet<IResourceManager> RootedManagers = new();

    // Chunk assembly.
    private string? _assemblingId;
    private byte[]?[]? _chunks;
    private int _receivedChunks;

    // Completed track data by id.
    private readonly Dictionary<string, byte[]> _tracks = new();

    // Cached thumbnail textures by track id.
    private readonly Dictionary<string, Texture> _thumbnails = new();

    // Active playback.
    private EntityUid? _streamEntity;
    private string? _playingId;
    private ResPath? _currentFilePath;
    private MediaPlayerStateEvent? _pendingState;

    private float _volume;

    /// <summary>
    /// Last search results received from the server, kept so reopening the window restores the list.
    /// </summary>
    public List<MediaSearchResult> LastSearchResults { get; private set; } = [];

    /// <summary>
    /// Last playback state received from the server, for UI display.
    /// </summary>
    public MediaPlayerStateEvent? LastState { get; private set; }

    /// <summary>
    /// Whether a track is currently loaded (playing or paused).
    /// </summary>
    public bool IsPlaying => _streamEntity != null;

    /// <summary>
    /// Whether the current track is paused.
    /// </summary>
    public bool IsTrackPaused => LastState is { TrackId: not null, Playing: false };

    /// <summary>
    /// Title of the currently playing track, or null.
    /// </summary>
    public string? CurrentTitle => IsPlaying ? LastState?.Title : null;

    /// <summary>
    /// Total duration of the current track in seconds.
    /// </summary>
    public float CurrentDuration => LastState?.Duration ?? 0f;

    /// <summary>
    /// Live playback position in seconds, read straight from the audio source.
    /// </summary>
    public float CurrentPosition =>
        _streamEntity is { } ent && TryComp<Robust.Shared.Audio.Components.AudioComponent>(ent, out var comp)
            ? comp.PlaybackPosition
            : 0f;

    public event Action? StateUpdated;
    public event Action<MediaPlayerSearchResponseEvent>? SearchReceived;
    public event Action<MediaPlayerStatusEvent>? StatusReceived;

    /// <summary>
    /// Fired when a thumbnail finishes downloading. Argument is the track id.
    /// </summary>
    public event Action<string>? ThumbnailLoaded;

    public override void Initialize()
    {
        base.Initialize();

        if (RootedManagers.Add(_res))
            _res.AddRoot(Prefix, ContentRoot);

        _sawmill = Logger.GetSawmill("media_player");
        _cfg.OnValueChanged(WegaCVars.MediaPlayerVolume, OnVolumeChanged, true);

        SubscribeNetworkEvent<MediaPlayerTrackChunkEvent>(OnTrackChunk);
        SubscribeNetworkEvent<MediaPlayerStateEvent>(OnState);
        SubscribeNetworkEvent<MediaPlayerSearchResponseEvent>(ev =>
        {
            LastSearchResults = [.. ev.Results];
            SearchReceived?.Invoke(ev);
        });
        SubscribeNetworkEvent<MediaPlayerStatusEvent>(ev => StatusReceived?.Invoke(ev));
        SubscribeNetworkEvent<MediaPlayerThumbnailEvent>(OnThumbnail);
        SubscribeNetworkEvent<OpenMediaPlayerEvent>(_ => OpenWindow());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cfg.UnsubValueChanged(WegaCVars.MediaPlayerVolume, OnVolumeChanged);
        StopPlayback();
    }

    private MediaPlayerWindow? _window;

    /// <summary>
    /// Opens the media player window, reusing the existing one if already open.
    /// </summary>
    public void OpenWindow()
    {
        if (_window is { Disposed: false })
        {
            _window.MoveToFront();
            return;
        }

        _window = new MediaPlayerWindow();
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }

    public void RequestSearch(string query)
    {
        RaiseNetworkEvent(new MediaPlayerSearchRequestEvent(query));
    }

    public void RequestPlay(string idOrUrl)
    {
        RaiseNetworkEvent(new MediaPlayerPlayRequestEvent(idOrUrl));
    }

    public void RequestStop()
    {
        RaiseNetworkEvent(new MediaPlayerStopRequestEvent());
    }

    public void RequestPause()
    {
        RaiseNetworkEvent(new MediaPlayerPauseRequestEvent());
    }

    /// <summary>
    /// Returns a cached thumbnail texture for the given track id, or null if it hasn't loaded yet.
    /// </summary>
    public Texture? GetThumbnail(string id)
    {
        _thumbnails.TryGetValue(id, out var texture);
        return texture;
    }

    internal void OnThumbnail(MediaPlayerThumbnailEvent ev)
    {
        if (_thumbnails.ContainsKey(ev.TrackId))
            return;

        try
        {
            using var stream = new MemoryStream(ev.PngData);
            var texture = _clyde.LoadTextureFromPNGStream(stream);
            _thumbnails[ev.TrackId] = texture;
            ThumbnailLoaded?.Invoke(ev.TrackId);
        }
        catch (Exception e)
        {
            _sawmill.Debug($"Failed to load thumbnail texture: {e.Message}");
        }
    }

    /// <summary>
    /// Strips characters the game font can't render (CJK, emoji, replacement chars) so titles
    /// don't show up as a row of unreadable boxes. Latin, Cyrillic and common punctuation stay.
    /// </summary>
    public static string SanitizeTitle(string title)
    {
        var sb = new StringBuilder(title.Length);
        var lastWasSpace = false;

        foreach (var ch in title)
        {
            if (IsRenderable(ch))
            {
                sb.Append(ch);
                lastWasSpace = ch == ' ';
            }
            else if (!lastWasSpace && sb.Length > 0)
            {
                // Collapse a run of unrenderable chars into a single space.
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        var result = sb.ToString().Trim();
        return result.Length == 0 ? "?" : result;
    }

    private static bool IsRenderable(char c)
    {
        if (c is >= ' ' and <= '~')                 // ASCII printable
            return true;
        if (c is >= '\u00A0' and <= '\u024F')       // Latin-1 Supplement + Latin Extended A/B
            return true;
        if (c is >= '\u0400' and <= '\u04FF')       // Cyrillic
            return true;

        return c switch
        {
            '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' => true, // dashes
            '\u2018' or '\u2019' or '\u201C' or '\u201D' => true,                           // curly quotes
            '\u2026' or '\u2022' => true,                                                     // ellipsis, bullet
            _ => false,
        };
    }

    private void OnVolumeChanged(float volume)
    {
        _volume = volume;

        if (_streamEntity != null)
            _audio.SetGain(_streamEntity, volume);
    }

    private void OnTrackChunk(MediaPlayerTrackChunkEvent ev)
    {
        if (ev.Total <= 0 || ev.Index < 0 || ev.Index >= ev.Total)
            return;

        if (_assemblingId != ev.TrackId || _chunks == null || _chunks.Length != ev.Total)
        {
            _assemblingId = ev.TrackId;
            _chunks = new byte[]?[ev.Total];
            _receivedChunks = 0;
        }

        if (_chunks[ev.Index] == null)
            _receivedChunks++;

        _chunks[ev.Index] = ev.Data;

        if (_receivedChunks < ev.Total)
            return;

        // All chunks in: stitch the track together.
        var totalLength = 0;
        foreach (var chunk in _chunks)
            totalLength += chunk!.Length;

        var data = new byte[totalLength];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            Array.Copy(chunk!, 0, data, offset, chunk!.Length);
            offset += chunk.Length;
        }

        // Keep only the freshest couple of tracks in memory.
        if (_tracks.Count > 2)
            _tracks.Clear();

        _tracks[ev.TrackId] = data;
        _assemblingId = null;
        _chunks = null;

        _sawmill.Debug($"Assembled track {ev.TrackId}: {data.Length} bytes");
        TryStartPlayback();
    }

    private void OnState(MediaPlayerStateEvent ev)
    {
        LastState = ev;

        // Stopped entirely.
        if (ev.TrackId == null)
        {
            StopPlayback();
            _pendingState = null;
            StateUpdated?.Invoke();
            return;
        }

        // Same track already loaded: just pause/resume + resync, don't restart.
        if (_playingId == ev.TrackId && _streamEntity is { } ent)
        {
            if (ev.Playing)
            {
                _audio.SetState(ent, AudioState.Playing);
                _audio.SetPlaybackPosition(ent, ev.Position);
            }
            else
            {
                _audio.SetState(ent, AudioState.Paused);
            }

            StateUpdated?.Invoke();
            return;
        }

        // New track: start it once the data has arrived.
        _pendingState = ev;
        TryStartPlayback();
        StateUpdated?.Invoke();
    }

    private void TryStartPlayback()
    {
        if (_pendingState is not { TrackId: { } trackId } state)
            return;

        if (!_tracks.TryGetValue(trackId, out var data))
            return;

        StopPlayback();

        var filePath = new ResPath($"{trackId}.ogg");
        // Keep the file mounted for the whole playback: pausing/resuming makes the engine
        // reload the source from this path, so it must not be removed until we stop.
        ContentRoot.AddOrUpdateFile(filePath, data);

        var audioResource = new AudioResource();
        audioResource.Load(IoCManager.Instance!, Prefix / filePath);

        var soundSpecifier = new ResolvedPathSpecifier(Prefix / filePath);
        var audioParams = AudioParams.Default.WithVolume(SharedAudioSystem.GainToVolume(_volume));

        var stream = _audio.PlayGlobal(audioResource.AudioStream, soundSpecifier, audioParams);

        if (stream == null)
        {
            ContentRoot.RemoveFile(filePath);
            _sawmill.Error($"Failed to start playback of {trackId}");
            return;
        }

        _streamEntity = stream.Value.Entity;
        _playingId = trackId;
        _currentFilePath = filePath;
        _pendingState = null;

        if (state.Position > 1f)
            _audio.SetPlaybackPosition(stream.Value.Entity, state.Position);

        // Joined while the track was paused — start it paused at the frozen position.
        if (!state.Playing)
            _audio.SetState(stream.Value.Entity, AudioState.Paused);

        _sawmill.Info($"Playing '{state.Title}' from {state.Position:0.0}s (playing: {state.Playing})");
    }

    private void StopPlayback()
    {
        if (_streamEntity != null)
        {
            _audio.Stop(_streamEntity);
            _streamEntity = null;
        }

        if (_currentFilePath is { } path)
        {
            ContentRoot.RemoveFile(path);
            _currentFilePath = null;
        }

        _playingId = null;
    }
}
