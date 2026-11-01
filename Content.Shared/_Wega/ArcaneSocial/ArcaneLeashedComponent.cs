// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from ArcaneSS14/arcane 81cd66873de20e597282565f27b686331e65f677.
namespace Content.Shared._Wega.ArcaneSocial;

/// <summary>Server-side backlink used to release a lead when its target is removed.</summary>
[RegisterComponent]
public sealed partial class ArcaneLeashedComponent : Component
{
    public EntityUid Leash;
}
