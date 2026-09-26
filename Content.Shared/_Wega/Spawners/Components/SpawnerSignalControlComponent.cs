using Content.Shared.DeviceLinking;
using Robust.Shared.Prototypes;
using Robust.Shared.Localization;

namespace Content.Shared._Wega.Spawners.Components;

/// <summary>
/// Marks an entity whose <see cref="Content.Server.Spawners.Components.TimedSpawnerComponent"/>
/// can be toggled on/off via device-link signals.
///
/// The actual logic runs in <c>SpawnerSignalControlSystem</c> (Content.Server).
/// This component lives in Content.Shared so the client can load entity prototypes
/// that reference it without crashing.
/// </summary>
[RegisterComponent]
public sealed partial class SpawnerSignalControlComponent : Component
{
    /// <summary>
    /// Sink port name that toggles the spawner on/off when a signal is received.
    /// </summary>
    [DataField]
    public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

    /// <summary>
    /// Sender name shown in the chat announcement (e.g. "Арена").
    /// </summary>
    [DataField]
    public string AnnounceSender = "Арена";

    /// <summary>
    /// Locale id broadcast when the spawner is turned ON. Receives an <c>$seconds</c> argument
    /// (the spawner's actual interval), so the announced period never drifts from the real one.
    /// </summary>
    [DataField]
    public LocId EnabledMessage = "spawner-signal-control-enabled";

    /// <summary>
    /// Locale id broadcast when the spawner is turned OFF.
    /// </summary>
    [DataField]
    public LocId DisabledMessage = "spawner-signal-control-disabled";
}
