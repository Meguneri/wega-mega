using Content.Client.Graphics;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Movement.Systems;

public sealed partial class FloorOcclusionSystem : SharedFloorOcclusionSystem
{
    private static readonly ProtoId<ShaderPrototype> HorizontalCut = "WegaWaterline";
    private static readonly ProtoId<ShaderPrototype> Submerged = "WegaSubmerged";

    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FloorOcclusionComponent, ComponentStartup>(OnOcclusionStartup);
        SubscribeLocalEvent<FloorOcclusionComponent, ComponentShutdown>(OnOcclusionShutdown);
        SubscribeLocalEvent<FloorOcclusionComponent, AfterAutoHandleStateEvent>(OnOcclusionAuto);
        SubscribeLocalEvent<FloorOcclusionComponent, DownedEvent>(OnDowned);
        SubscribeLocalEvent<FloorOcclusionComponent, StoodEvent>(OnStood);
        SubscribeLocalEvent<FloorOcclusionComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<FloorOcclusionComponent, BeforePostShaderRenderEvent>(OnShaderRender);
    }

    private void OnShaderRender(Entity<FloorOcclusionComponent> ent, ref BeforePostShaderRenderEvent args)
    {
        if (args.Id != ContentPostShaderIds.FloorOcclusion || _standing.IsDown(ent.Owner))
            return;

        // Уровень воды привязан к персонажу, а не к размеру временной текстуры постэффекта.
        var position = _transform.GetWorldPosition(ent.Owner);
        var center = args.Viewport.WorldToLocal(position);
        var above = args.Viewport.WorldToLocal(position + System.Numerics.Vector2.UnitY);
        var pixelsPerMeter = System.Numerics.Vector2.Distance(center, above);
        args.Shader.SetParameter("waterline", 1f - (center.Y + 0.2f * pixelsPerMeter) / args.Viewport.Size.Y);
    }

    private void OnDowned(Entity<FloorOcclusionComponent> ent, ref DownedEvent args)
    {
        SetShader(ent.Owner, ent.Comp.Enabled);
    }

    private void OnStood(Entity<FloorOcclusionComponent> ent, ref StoodEvent args)
    {
        SetShader(ent.Owner, ent.Comp.Enabled);
    }

    private void OnAppearanceChange(Entity<FloorOcclusionComponent> ent, ref AppearanceChangeEvent args)
    {
        SetShader(ent.Owner, ent.Comp.Enabled);
    }

    private void OnOcclusionAuto(Entity<FloorOcclusionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        SetShader(ent.Owner, ent.Comp.Enabled);
    }

    private void OnOcclusionStartup(Entity<FloorOcclusionComponent> ent, ref ComponentStartup args)
    {
        SetShader(ent.Owner, ent.Comp.Enabled);
    }

    private void OnOcclusionShutdown(Entity<FloorOcclusionComponent> ent, ref ComponentShutdown args)
    {
        SetShader(ent.Owner, false);
    }

    protected override void SetEnabled(Entity<FloorOcclusionComponent> entity)
    {
        SetShader(entity.Owner, entity.Comp.Enabled);
    }

    private void SetShader(Entity<SpriteComponent?> sprite, bool enabled)
    {
        if (!_spriteQuery.Resolve(sprite.Owner, ref sprite.Comp, false))
            return;

        if (enabled)
        {
            // Лёжа погружается весь силуэт, стоя — только нижняя часть тела.
            var shader = ProtoMan.Index(_standing.IsDown(sprite.Owner) ? Submerged : HorizontalCut).InstanceUnique();
            _sprite.SetPostShader(sprite, new SpriteComponent.PostShaderArgs(ContentPostShaderIds.FloorOcclusion, shader)
            {
                Before = ContentPostShaderIds.BeforeOutlines,
                RaiseShaderEvent = true,
            });
        }
        else
        {
            _sprite.RemovePostShader(sprite, ContentPostShaderIds.FloorOcclusion);
        }
    }
}
