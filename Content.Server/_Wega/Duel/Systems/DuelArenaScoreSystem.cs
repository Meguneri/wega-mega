using System.Linq;
using Content.Server._Wega.Duel.Components;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;

namespace Content.Server._Wega.Duel.Systems;

/// <summary>
/// Счёт и таблица лидеров дуэльных арен. Выделен в отдельную систему, чтобы не перегружать
/// <see cref="DuelArenaSystem"/> логикой начисления побед/поражений и серий.
/// Работает с <see cref="IDuelScoreStore"/> — одиночными аренами и контроллерами ротации.
/// </summary>
public sealed partial class DuelArenaScoreSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;

    /// <summary>
    /// Возвращает идентификатор игрока, управляющего телом, или null для тел без разума (NPC).
    /// </summary>
    public NetUserId? GetUser(EntityUid body)
    {
        return _mind.TryGetMind(body, out _, out var mind) ? mind.UserId : null;
    }

    /// <summary>
    /// Записывает результат матча в хранилище счёта: победителя, серии побед, серии поражений.
    /// Возвращает строку табло или null, если счёт пуст.
    /// </summary>
    public string? RecordMatchResult(IDuelScoreStore store, IEnumerable<EntityUid> duelists, EntityUid? winner)
    {
        // Запоминаем актуальные имена всех бойцов этого боя по их NetUserId — чтобы общий счёт
        // отображался с именами, даже если кто-то из них не участвует в следующих раундах.
        foreach (var duelist in duelists)
        {
            var user = GetUser(duelist);
            if (user != null)
                store.ScoreNames[user.Value] = SafeName(duelist);
        }

        if (winner != null)
        {
            // Счёт ведём по игроку (NetUserId), а не по телу: иначе после клона/респавна
            // боец получает новый EntityUid и счёт каждый раунд начинается заново.
            var winnerUser = GetUser(winner.Value);

            if (winnerUser != null)
                store.Scores[winnerUser.Value] = store.Scores.GetValueOrDefault(winnerUser.Value) + 1;

            // Серия побед подряд: растёт, если победил тот же игрок, иначе начинается заново.
            if (winnerUser != null && store.StreakUser == winnerUser)
                store.Streak++;
            else
            {
                store.StreakUser = winnerUser;
                store.Streak = 1;
            }

            // Проигравшие получают +1 к серии поражений; победитель сбрасывает свою.
            foreach (var loser in duelists.Where(d => d != winner.Value))
            {
                var loserUser = GetUser(loser);
                if (loserUser != null)
                    store.LosingStreaks[loserUser.Value] = store.LosingStreaks.GetValueOrDefault(loserUser.Value) + 1;
            }

            if (winnerUser != null)
                store.LosingStreaks[winnerUser.Value] = 0;
        }
        else
        {
            // Никого живого — ничья: серия побед прерывается, все участники получают +1 поражение.
            store.StreakUser = null;
            store.Streak = 0;

            foreach (var duelist in duelists)
            {
                var user = GetUser(duelist);
                if (user != null)
                    store.LosingStreaks[user.Value] = store.LosingStreaks.GetValueOrDefault(user.Value) + 1;
            }
        }

        return BuildScoreboard(store);
    }

    /// <summary>
    /// Собирает строку общего счёта: «Имя — N», сортировка по убыванию побед, затем по имени.
    /// Источник — одиночная арена или контроллер ротации (см. <see cref="IDuelScoreStore"/>).
    /// Возвращает null, если счёта ещё нет.
    /// </summary>
    public string? BuildScoreboard(IDuelScoreStore store)
    {
        if (store.Scores.Count == 0)
            return null;

        var entries = store.Scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => store.ScoreNames.GetValueOrDefault(kv.Key, "?"))
            .Select(kv => $"{store.ScoreNames.GetValueOrDefault(kv.Key, "?")} — {kv.Value}");

        return string.Join(", ", entries);
    }

    /// <summary>
    /// Обнуляет накопленный счёт на всех дуэльных аренах, босс-аренах и контроллерах ротации.
    /// Возвращает число хранилищ, у которых счёт был непустым.
    /// </summary>
    public int ResetAllScores()
    {
        return ResetStores<DuelArenaComponent>()
            + ResetStores<BossArenaComponent>()
            + ResetStores<DuelRotationComponent>();
    }

    /// <summary>Обнуляет счёт во всех хранилищах типа <typeparamref name="T"/>, где он был непустым.</summary>
    private int ResetStores<T>() where T : Component, IDuelScoreStore
    {
        var cleared = 0;
        var query = EntityQueryEnumerator<T>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Scores.Count == 0)
                continue;

            comp.Reset();
            cleared++;
        }

        return cleared;
    }

    private string SafeName(EntityUid uid)
        => Exists(uid) ? MetaData(uid).EntityName : "?";
}
