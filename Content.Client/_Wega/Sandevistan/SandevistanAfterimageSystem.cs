using System.Linq;
using System.Numerics;
using Content.Shared._Wega.Clothing.Sandevistan;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Spawners;
using DrawDepthEnum = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Wega.Sandevistan;

/// <summary>
/// Client visuals for Sandevistan afterimages: copies the source user's sprite onto the
/// trailing entity, tints it a glowing blue and pins it below the mob layer so the
/// moving user draws on top — the David Martinez blur trail. The ghost fades its alpha out over
/// its lifetime instead of popping out instantly.
/// </summary>
public sealed partial class SandevistanAfterimageSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SandevistanAfterimageComponent, ComponentStartup>(OnStartup);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Over its life each ghost fades alpha toward 0 AND shifts from the bright "core" colour to a
        // darker deep-blue "echo" — so the trail reads as a bright cyan core near the user blurring back
        // into a dark-blue smear, instead of a row of flat sprite copies.
        var query = EntityQueryEnumerator<SandevistanAfterimageComponent, TimedDespawnComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var ghost, out var despawn, out var sprite))
        {
            var frac = ghost.FadeDuration > 0f ? Math.Clamp(despawn.Lifetime / ghost.FadeDuration, 0f, 1f) : 1f;
            var core = ghost.BaseColor;
            // Tail only cools/deepens slightly toward blue — kept bright so the trail doesn't go dark.
            var echo = new Color(core.R * 0.6f, core.G * 0.85f, core.B * 1f, core.A);
            var col = Color.InterpolateBetween(echo, core, frac);
            _sprite.SetColor((uid, sprite), col.WithAlpha(core.A * frac));
        }
    }

    // Server-spawned (networked) ghosts arrive with SourceEntity already set, so the copy can run on
    // startup. Client-spawned ghosts set SourceEntity after creation and call ApplyVisuals directly.
    private void OnStartup(Entity<SandevistanAfterimageComponent> ent, ref ComponentStartup args)
    {
        ApplyVisuals(ent);
    }

    /// <summary>
    /// Copies the source user's sprite onto the afterimage and paints it as a bright, unshaded blue
    /// ghost. Safe to call once SourceEntity is set; no-ops if the source has no sprite.
    /// </summary>
    public void ApplyVisuals(Entity<SandevistanAfterimageComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent.Comp.SourceEntity, out var userSprite))
            return;

        var sprite = EnsureComp<SpriteComponent>(ent);
        _sprite.CopySprite((ent.Comp.SourceEntity, userSprite), (ent.Owner, sprite));
        _sprite.SetDrawDepth((ent.Owner, sprite), (int) DrawDepthEnum.DeadMobs);
        // Bright glowing blue, like David Martinez's Sandevistan trail; alpha keeps it ghostly and
        // is faded down over the ghost's lifetime in FrameUpdate.
        _sprite.SetColor((ent.Owner, sprite), ent.Comp.BaseColor);

        // Unshaded so the trail glows at full brightness regardless of the (often dark) arena
        // lighting — otherwise the ghosts come out almost black. Must be set per layer; PostShader
        // alone doesn't take the sprite out of the lighting pass.
        for (var i = 0; i < sprite.AllLayers.Count(); i++)
            sprite.LayerSetShader(i, "unshaded");

        sprite.PostShader = null;
        sprite.RenderOrder = 0;
        sprite.EnableDirectionOverride = true;
        sprite.DirectionOverride = ent.Comp.DirectionOverride;

        // Subtle speed-smear: stretch the ghost along the user's current travel axis so each one is an
        // elongated streak in the direction of motion, not a crisp copy. Top-down sprites are screen-axis
        // aligned, so the larger velocity component picks which screen axis to stretch.
        if (TryComp<PhysicsComponent>(ent.Comp.SourceEntity, out var body) && body.LinearVelocity.LengthSquared() > 0.01f)
        {
            var v = body.LinearVelocity;
            var stretch = MathF.Abs(v.X) >= MathF.Abs(v.Y) ? new Vector2(1.25f, 0.95f) : new Vector2(0.95f, 1.25f);
            _sprite.SetScale((ent.Owner, sprite), sprite.Scale * stretch);
        }
    }
}
