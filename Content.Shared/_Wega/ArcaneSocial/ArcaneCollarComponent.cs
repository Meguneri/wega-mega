// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from ArcaneSS14/arcane 81cd66873de20e597282565f27b686331e65f677.
using Robust.Shared.GameStates;

namespace Content.Shared._Wega.ArcaneSocial;

/// <summary>Allows a lead to attach when this item is worn in the neck slot.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ArcaneCollarComponent : Component;
