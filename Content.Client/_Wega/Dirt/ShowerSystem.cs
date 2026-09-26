using Content.Shared.DirtVisuals;
using Robust.Client.GameObjects;

namespace Content.Client.Shower
{
    public sealed partial class ShowerSystem : EntitySystem
    {
        [Dependency] private AppearanceSystem _appearance = default!;
        [Dependency] private SpriteSystem _sprite = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ShowerComponent, AppearanceChangeEvent>(OnAppearanceChanged);
        }

        private void OnAppearanceChanged(EntityUid uid, ShowerComponent component, ref AppearanceChangeEvent args)
        {
            if (args.Sprite == null)
                return;

            if (!_appearance.TryGetData(uid, ShowerVisuals.Spraying, out bool spraying))
                spraying = false;

            _sprite.LayerSetVisible(uid, ShowerVisuals.Spraying, spraying);

            // При включении заново запускаем анимацию струи: невидимый слой не тикает
            // (FrameUpdate пропускает !Visible), а если предыдущий показ доиграл до конца без
            // зацикливания — слой сам выставил AutoAnimated=false и «застыл» на последнем кадре.
            // Включаем авто-анимацию и сбрасываем на нулевой кадр, чтобы струя проигрывалась заново.
            if (spraying)
            {
                _sprite.LayerSetAutoAnimated(uid, ShowerVisuals.Spraying, true);
                _sprite.LayerSetAnimationTime(uid, ShowerVisuals.Spraying, 0f);
            }
        }
    }
}
