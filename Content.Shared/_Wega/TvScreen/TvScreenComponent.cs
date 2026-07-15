using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared.TvScreen;

/// <summary>
/// Marks an entity as a TV/cinema screen: the client-side media player system renders the
/// currently broadcast video clip onto a dynamic sprite layer of this entity.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TvScreenComponent : Component;
