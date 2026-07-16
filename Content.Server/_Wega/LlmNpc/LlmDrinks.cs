using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Prototypes;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Игровое заземление по напиткам: строит из реальных химических реакций (те, чей продукт —
/// реагент группы «Drinks») каталог коктейлей, который скармливается в промпт, и умеет сопоставить
/// запрошенное моделью название с настоящим рецептом. Так NPC перестаёт выдумывать напитки и знает
/// ровно то, что действительно можно смешать в игре. Ничего не спавнит — только справочник и поиск.
/// </summary>
public sealed class LlmDrinks
{
    private const string DrinkGroup = "Drinks";

    private readonly IPrototypeManager _proto;

    // Ленивая инициализация: прототипы готовы только после загрузки, а сервис создаётся в Initialize.
    private bool _built;
    private string _catalog = string.Empty;
    private readonly Dictionary<string, DrinkRecipe> _byName = new();

    public LlmDrinks(IPrototypeManager proto)
    {
        _proto = proto;
    }

    public readonly record struct DrinkRecipe(
        string ReagentId,
        string Name,
        string Recipe,
        List<(string ReagentId, int Amount)> Reactants,
        int ProductAmount);

    /// <summary>Компактный список названий всех смешиваемых напитков — уходит в системный промпт.</summary>
    public string Catalog()
    {
        Build();
        return _catalog;
    }

    /// <summary>
    /// Ищет напиток по названию (или id реагента), которое назвала модель: точное совпадение, затем
    /// вхождение. Возвращает рецепт или null; в <paramref name="suggestions"/> кладёт близкие варианты.
    /// </summary>
    public DrinkRecipe? Find(string requested, out IReadOnlyList<string> suggestions)
    {
        Build();
        suggestions = Array.Empty<string>();

        var key = Normalize(requested);
        if (key.Length == 0)
            return null;

        if (_byName.TryGetValue(key, out var exact))
            return exact;

        // Вхождение в обе стороны: «маргарита» ↔ «клубничная маргарита».
        var partial = _byName
            .Where(kv => kv.Key.Contains(key) || key.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();

        if (partial.Count == 1)
            return partial[0];

        if (partial.Count > 1)
        {
            suggestions = partial.Take(5).Select(r => r.Name).ToList();
            return null;
        }

        // Совсем мимо — подсказываем первые несколько названий из каталога.
        suggestions = _byName.Values.Take(5).Select(r => r.Name).ToList();
        return null;
    }

    private void Build()
    {
        if (_built)
            return;
        _built = true;

        var names = new List<string>();

        foreach (var reaction in _proto.EnumeratePrototypes<ReactionPrototype>())
        {
            // Продукт-напиток: единственный реагент группы «Drinks» среди продуктов реакции.
            var productId = reaction.Products.Keys.FirstOrDefault(id =>
                _proto.TryIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(id, out var r)
                && r.Group == DrinkGroup);

            if (productId == null
                || !_proto.TryIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(productId, out var product))
                continue;

            var name = product.LocalizedName;
            var recipe = string.Join(", ", reaction.Reactants.Select(kv =>
            {
                var ing = _proto.TryIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(kv.Key, out var ir)
                    ? ir.LocalizedName
                    : kv.Key;
                return $"{ing} {kv.Value.Amount}";
            }));

            var reactants = reaction.Reactants
                .Select(kv => (kv.Key, kv.Value.Amount.Int()))
                .ToList();
            var productAmount = reaction.Products[productId].Int();

            var entry = new DrinkRecipe(productId, name, recipe, reactants, productAmount);
            // Первая реакция на имя побеждает; ключим по нормализованному имени и по id реагента.
            _byName.TryAdd(Normalize(name), entry);
            _byName.TryAdd(Normalize(productId), entry);
            names.Add(name);
        }

        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        var sb = new StringBuilder();
        sb.Append("Напитки, которые ты реально умеешь смешивать (только из этого списка): ");
        sb.Append(string.Join(", ", names.Distinct()));
        sb.Append('.');
        _catalog = sb.ToString();
    }

    private static string Normalize(string s)
        => s.Trim().ToLowerInvariant().Replace("ё", "е");
}
