using Content.Shared._Wega.Chess;
using Robust.Shared.Audio;

namespace Content.Server._Wega.Chess;

/// <summary>
/// Шахматная доска с настоящими правилами: партия живёт на сервере, клиент только рисует.
/// Логика — <see cref="ChessGameSystem"/>, правила — <see cref="ChessPosition"/>.
/// </summary>
[RegisterComponent]
public sealed partial class ChessGameComponent : Component
{
    /// <summary>Контроль времени по умолчанию, сек на игрока. 0 = без часов.</summary>
    [DataField]
    public int DefaultClockSeconds = 300;

    // Звуки настольных игр из набора Estação Pirata (он же обслуживает карты): мягкий стук
    // фигуры о доску и более резкий — на взятии. Регистр в именах файлов важен.
    [DataField]
    public SoundSpecifier MoveSound =
        new SoundPathSpecifier("/Audio/EstacaoPirata/Effects/Cards/cardPlace1.ogg");

    [DataField]
    public SoundSpecifier CaptureSound =
        new SoundPathSpecifier("/Audio/EstacaoPirata/Effects/Cards/cardShove1.ogg");

    // ── Runtime ───────────────────────────────────────────────────────────────

    /// <summary>Текущая позиция. Создаётся при первом открытии доски.</summary>
    [ViewVariables]
    public ChessPosition Position = new();

    /// <summary>Кто сидит за доску (сущности игроков). null = место свободно.</summary>
    [ViewVariables]
    public EntityUid? White;

    [ViewVariables]
    public EntityUid? Black;

    [ViewVariables]
    public float WhiteTime;

    [ViewVariables]
    public float BlackTime;

    [ViewVariables]
    public bool ClockEnabled;

    /// <summary>Часы идут только после первого хода чёрных — как на Lichess.</summary>
    [ViewVariables]
    public bool ClockRunning;

    /// <summary>
    /// Когда в следующий раз досылать состояние клиентам ради тикающих часов. Без этого таймер
    /// в окне стоял бы намертво между ходами: состояние UI уходит только по событиям.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextClockSync;

    [ViewVariables]
    public bool Finished;

    /// <summary>Победитель; null при ничьей или пока партия идёт.</summary>
    [ViewVariables]
    public ChessColor? Winner;

    [ViewVariables]
    public int LastFrom = -1;

    [ViewVariables]
    public int LastTo = -1;
}
