using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

[CVarDefs]
public sealed partial class WegaCVars
{
    /*
        Ghost Respawn CVars
    */
    /// <summary>
    /// Whether or not respawning is enabled.
    /// </summary>
    public static readonly CVarDef<bool> GhostRespawnEnabled =
        CVarDef.Create("wega.respawn_enabled", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// Respawn time, how long the player has to wait in seconds after death.
    /// </summary>
    public static readonly CVarDef<float> GhostRespawnTime =
        CVarDef.Create("wega.respawn_time", 1200.0f, CVar.SERVER | CVar.REPLICATED);

    /*
        Barks CVars
    */
    /// <summary>
    /// Responsible for turning on and off the bark system.
    /// </summary>
    public static readonly CVarDef<bool> BarksEnabled =
        CVarDef.Create("wega.barks_enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of Barks sound.
    /// </summary>
    public static readonly CVarDef<float> BarksVolume =
        CVarDef.Create("wega.barks_volume", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /*
        Media Player CVars
    */
    /// <summary>
    /// Personal volume (gain) of the global media player, per client.
    /// </summary>
    public static readonly CVarDef<float> MediaPlayerVolume =
        CVarDef.Create("wega.media_player_volume", 0.5f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Path to the yt-dlp executable on the server host. If not found, the server auto-downloads it
    /// (see <see cref="MediaPlayerAutoDownload"/>).
    /// </summary>
    public static readonly CVarDef<string> MediaPlayerYtdlpPath =
        CVarDef.Create("wega.media_player_ytdlp_path", "yt-dlp", CVar.SERVERONLY);

    /// <summary>
    /// Path to ffmpeg on the server host. Empty = look in PATH, then auto-download (Windows).
    /// </summary>
    public static readonly CVarDef<string> MediaPlayerFfmpegPath =
        CVarDef.Create("wega.media_player_ffmpeg_path", "", CVar.SERVERONLY);

    /// <summary>
    /// If yt-dlp/ffmpeg aren't found, auto-download them into the server's data folder on first use.
    /// </summary>
    public static readonly CVarDef<bool> MediaPlayerAutoDownload =
        CVarDef.Create("wega.media_player_auto_download", true, CVar.SERVERONLY);

    /// <summary>
    /// Maximum allowed track duration in seconds. Longer tracks are refused before download.
    /// </summary>
    public static readonly CVarDef<int> MediaPlayerMaxDuration =
        CVarDef.Create("wega.media_player_max_duration", 900, CVar.SERVERONLY);

    /*
        Night Light System CVars
    */
    /// <summary>
    /// Responsible for switching the night light system.
    /// </summary>
    public static readonly CVarDef<bool> NightLightEnabled =
        CVarDef.Create("wega.night_light_enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Switching adjusts all the lamps to the holiday mode according to the logic of updating the night lighting.
    /// </summary>
    public static readonly CVarDef<bool> PartyEnabled =
        CVarDef.Create("wega.party_enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /*
        Sound insulation CVars
    */
    /// <summary>
    /// If you enable this mode, it will process the sound with sound isolation.
    /// </summary>
    public static readonly CVarDef<bool> SoundInsulationEnabled =
        CVarDef.Create("wega.sound_insulation_enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /*
        Vote CVars
    */
    /// <summary>
    /// If enabled forcibly, it will trigger a vote for the mode at the end of the round.
    /// </summary>
    public static readonly CVarDef<bool> VoteRoundEndEnabled =
        CVarDef.Create("wega.roundend_vote_enabled", false, CVar.SERVERONLY);

    /*
        Ic Flavors
    */
    /// <summary>
    ///     Sets the maximum length for OOC flavor text.
    /// </summary>
    public static readonly CVarDef<int> OOCMaxFlavorTextLength =
        CVarDef.Create("ic.oocflavor_text_length", 2048, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for character description text.
    /// </summary>
    public static readonly CVarDef<int> CharacterDescriptionLength =
        CVarDef.Create("ic.character_description_length", 2048, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for green preferences text.
    /// </summary>
    public static readonly CVarDef<int> GreenPreferencesLength =
        CVarDef.Create("ic.green_preferences_length", 256, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for yellow preferences text.
    /// </summary>
    public static readonly CVarDef<int> YellowPreferencesLength =
        CVarDef.Create("ic.yellow_preferences_length", 256, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for red preferences text.
    /// </summary>
    public static readonly CVarDef<int> RedPreferencesLength =
        CVarDef.Create("ic.red_preferences_length", 256, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for tags text.
    /// </summary>
    public static readonly CVarDef<int> TagsLength =
        CVarDef.Create("ic.tags_length", 128, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for links text.
    /// </summary>
    public static readonly CVarDef<int> LinksLength =
        CVarDef.Create("ic.links_length", 512, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Sets the maximum length for NSFW preferences text.
    /// </summary>
    public static readonly CVarDef<int> NSFWPreferencesLength =
        CVarDef.Create("ic.nsfw_preferences_length", 1024, CVar.SERVER | CVar.REPLICATED);

    /*
        Lavaland CVars
    */
    public static readonly CVarDef<bool> LavalandEnabled =
        CVarDef.Create("lavaland.enabled", true, CVar.SERVERONLY);

    public static readonly CVarDef<int> LavalandMaxBuildings =
        CVarDef.Create("lavaland.max_buildings", 128, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> LavalandBuildingsDistance =
        CVarDef.Create("lavaland.buildings_distance", 50f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> LavalandSpawnIntervalMin =
        CVarDef.Create("lavaland.spawn_interval_min", 100f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float> LavalandSpawnIntervalMax =
        CVarDef.Create("lavaland.spawn_interval_max", 600f, CVar.SERVER | CVar.REPLICATED);
}
