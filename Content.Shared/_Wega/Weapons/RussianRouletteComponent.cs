namespace Content.Shared._Wega.Weapons;

/// <summary>
/// Револьвер для русской рулетки. «Использовать в руке» (клавиша Z) прокручивает барабан на
/// СЛУЧАЙНУЮ камору вместо обычного шага на одну — иначе позиция патрона вычислялась бы счётом.
/// Состояние камор игроку не видно: у такой сущности намеренно нет <c>AmmoCounter</c>.
/// </summary>
[RegisterComponent]
public sealed partial class RussianRouletteComponent : Component
{
}
