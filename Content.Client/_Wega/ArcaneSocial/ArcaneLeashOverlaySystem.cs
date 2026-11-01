// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from ArcaneSS14/arcane 81cd66873de20e597282565f27b686331e65f677.
using Content.Shared._Wega.ArcaneSocial;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Wega.ArcaneSocial;

public sealed partial class ArcaneLeashOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    private ArcaneLeashOverlay? _overlay;

    public override void Initialize()
    {
        _overlay = new ArcaneLeashOverlay(EntityManager);
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        if (_overlay != null)
            _overlays.RemoveOverlay(_overlay);
        _overlay = null;
    }
}

public sealed class ArcaneLeashOverlay(IEntityManager entities) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var transform = entities.System<SharedTransformSystem>();
        var query = entities.EntityQueryEnumerator<ArcaneLeashComponent>();
        while (query.MoveNext(out var leash))
        {
            if (leash.Target is not { } target || leash.Anchor is not { } anchor ||
                !entities.TryGetComponent<TransformComponent>(target, out var targetXform) ||
                !entities.TryGetComponent<TransformComponent>(anchor, out var anchorXform) ||
                targetXform.MapID != args.MapId || targetXform.MapID != anchorXform.MapID)
                continue;
            args.WorldHandle.DrawLine(transform.GetWorldPosition(anchorXform),
                transform.GetWorldPosition(targetXform), Color.SaddleBrown);
        }
    }
}
