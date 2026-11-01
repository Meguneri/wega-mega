// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from ArcaneSS14/arcane 81cd66873de20e597282565f27b686331e65f677.
using Robust.Shared.GameStates;

namespace Content.Shared._Wega.ArcaneSocial;

/// <summary>A handheld lead and its current physical connection.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ArcaneLeashComponent : Component
{
    [AutoNetworkedField] public EntityUid? Target;
    [AutoNetworkedField] public EntityUid? Anchor;
    [DataField, AutoNetworkedField] public float MaxDistance = 3f;
    public string? JointId;
}
