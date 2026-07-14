using Content.Shared._Wega.Duel;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Wega.Duel;

/// <summary>
/// Держит RSI-анимацию всех сущностей с <see cref="SyncedSpriteAnimationComponent"/> в ЕДИНОЙ фазе,
/// привязанной к глобальным часам. Обычно анимация спрайта отсчитывается от момента входа сущности в PVS,
/// поэтому одинаковые анимированные киберпанк-стены арены мерцают вразнобой (каждая появилась в свой миг).
/// Каждый кадр пишем всем слоям таймер анимации = <c>CurTime % длина_цикла</c> — одно и то же значение для
/// всех, поэтому одинаковые стены оказываются на одном кадре и мерцают синхронно, как далеко бы ни стояли.
/// Разные типы (разной длины цикла) синхронизируются каждый внутри своего типа.
/// </summary>
public sealed partial class SyncedSpriteAnimationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = (float)_timing.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<SyncedSpriteAnimationComponent, SpriteComponent>();
        while (query.MoveNext(out _, out _, out var sprite))
        {
            foreach (var layer in sprite.AllLayers)
            {
                if (layer.ActualRsi is not { } rsi
                    || !rsi.TryGetState(layer.RsiState, out var state)
                    || state.TotalDelay <= 0f)
                    continue;

                layer.AnimationTime = now % state.TotalDelay;
            }
        }
    }
}
