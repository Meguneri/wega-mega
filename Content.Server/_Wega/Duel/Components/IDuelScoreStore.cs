using Robust.Shared.Network;

namespace Content.Server._Wega.Duel.Components;

/// <summary>
/// Хранилище счёта дуэлей. Реализуется и одиночной ареной (<see cref="DuelArenaComponent"/>), и
/// контроллером ротации (<see cref="DuelRotationComponent"/>), чтобы общая логика подсчёта в
/// DuelArenaSystem писала в одно и то же место независимо от режима: одиночная арена ведёт счёт
/// сама, ротация — общий по всем аренам.
/// </summary>
public interface IDuelScoreStore
{
    /// <summary>Победы по игрокам (NetUserId → число побед).</summary>
    Dictionary<NetUserId, int> Scores { get; }

    /// <summary>Последние известные имена игроков (NetUserId → имя) для табло.</summary>
    Dictionary<NetUserId, string> ScoreNames { get; }

    /// <summary>Поражения по игрокам (NetUserId → число поражений подряд).</summary>
    Dictionary<NetUserId, int> LosingStreaks { get; }

    /// <summary>Игрок текущей серии побед подряд.</summary>
    NetUserId? StreakUser { get; set; }

    /// <summary>Длина текущей серии побед подряд.</summary>
    int Streak { get; set; }

    /// <summary>Полностью обнуляет накопленный счёт: победы, имена, серии поражений и побед.</summary>
    void Reset();
}
