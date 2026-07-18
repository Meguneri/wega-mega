using Robust.Shared.Serialization;

namespace Content.Shared._Wega.Duel;

/// <summary>
/// UI портативного боевого анализатора: список дуэлей текущей сессии + сводка, просмотр
/// подробного отчёта (разметка бумаги: бар-чарты, цвета) и печать любого из них.
/// </summary>
[Serializable, NetSerializable]
public enum FightAnalyzerUiKey : byte
{
    Key,
}

/// <summary>Одна дуэль в списке анализатора.</summary>
[Serializable, NetSerializable]
public sealed class FightAnalyzerDuelEntry
{
    /// <summary>Сквозной номер дуэли за раунд.</summary>
    public int Number;

    /// <summary>Строка списка: «Дуэль №7 — Иван vs Пётр (5 мин назад)».</summary>
    public string Title = string.Empty;

    /// <summary>Подробный отчёт с разметкой (как на распечатке).</summary>
    public string Report = string.Empty;
}

[Serializable, NetSerializable]
public sealed class FightAnalyzerBuiState : BoundUserInterfaceState
{
    /// <summary>Дуэли сессии, от свежих к старым.</summary>
    public List<FightAnalyzerDuelEntry> Duels = new();

    /// <summary>Сводка сессии с разметкой (дуэли, таблица бойцов).</summary>
    public string SessionReport = string.Empty;
}

/// <summary>Печать: номер дуэли, 0 = сводка сессии. Бумага падает в руки открывшему.</summary>
[Serializable, NetSerializable]
public sealed class FightAnalyzerPrintMessage : BoundUserInterfaceMessage
{
    public int DuelNumber;

    public FightAnalyzerPrintMessage(int duelNumber)
    {
        DuelNumber = duelNumber;
    }
}
