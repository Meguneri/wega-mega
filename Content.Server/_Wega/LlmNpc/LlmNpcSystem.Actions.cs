using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;

namespace Content.Server._Wega.LlmNpc;

/// <summary>
/// Слой действий NPC: инструменты (tool-calling), которые модель может вызвать в ходе диалога —
/// веб-поиск и «смешать напиток». Веб-поиск асинхронный и потокобезопасный; смешивание напитка трогает
/// игровой мир (спавн стакана, растворы, руки), поэтому маршалится обратно на главный поток.
/// </summary>
public sealed partial class LlmNpcSystem
{
    private const string DrinkGlassProto = "DrinkGlass";
    private const string DrinkSolution = "drink";
    private static readonly FixedPoint2 DrinkAmount = FixedPoint2.New(30);

    private static readonly object WebSearchDecl = new
    {
        type = "function",
        function = new
        {
            name = "web_search",
            description = "Ищет актуальную информацию в интернете. Вызывай, когда для ответа нужен " +
                          "реальный факт, определение или событие, которого ты не знаешь наверняка. " +
                          "Для обычной болтовни не вызывай.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "поисковый запрос на языке собеседника" },
                },
                required = new[] { "query" },
            },
        },
    };

    private static readonly object MakeDrinkDecl = new
    {
        type = "function",
        function = new
        {
            name = "make_drink",
            description = "Реально готовишь напиток: идёшь к бару, наливаешь ингредиенты и смешиваешь — " +
                          "займёт несколько секунд. ВСЕГДА указывай person (имя заказавшего из диалога) — " +
                          "тогда сама отнесёшь и вручишь готовый стакан; без person он останется у тебя. " +
                          "Название бери из своего списка напитков; выдумывать нельзя.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    drink = new { type = "string", description = "название напитка из твоего списка" },
                    person = new { type = "string", description = "кому подать готовый напиток (необязательно)" },
                },
                required = new[] { "drink" },
            },
        },
    };

    private static readonly object GoToDecl = new
    {
        type = "function",
        function = new
        {
            name = "go_to",
            description = "Реально идёшь к названному человеку (он должен быть неподалёку). Вызывай, " +
                          "когда тебя просят подойти или самой нужно приблизиться к кому-то.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    person = new { type = "string", description = "имя человека, к которому идти" },
                },
                required = new[] { "person" },
            },
        },
    };

    private static readonly object GiveItemDecl = new
    {
        type = "function",
        function = new
        {
            name = "give_item",
            description = "Относишь то, что у тебя в руке, названному человеку и вручаешь ему. " +
                          "Вызывай ПОСЛЕ make_drink, чтобы подать готовый напиток заказавшему, или " +
                          "когда просят что-то передать. Если руки пустые — инструмент скажет об этом.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    person = new { type = "string", description = "имя человека, которому вручить" },
                },
                required = new[] { "person" },
            },
        },
    };

    private static readonly object PickUpDecl = new
    {
        type = "function",
        function = new
        {
            name = "pick_up",
            description = "Подходишь к названному предмету неподалёку (на полу, столе, стойке) и " +
                          "поднимаешь его в руку. Вызывай, когда просят взять или принести вещь.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    item = new { type = "string", description = "название предмета, который взять" },
                },
                required = new[] { "item" },
            },
        },
    };

    private static readonly object FollowDecl = new
    {
        type = "function",
        function = new
        {
            name = "follow",
            description = "Идёшь следом за названным человеком, куда бы он ни шёл, пока не попросят " +
                          "остановиться. Вызывай, когда просят пойти с кем-то или сопровождать.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    person = new { type = "string", description = "имя человека, за которым идти" },
                },
                required = new[] { "person" },
            },
        },
    };

    private static readonly object SlapDecl = new
    {
        type = "function",
        function = new
        {
            name = "slap",
            description = "Отвешиваешь звонкую пощёчину. Для лёгкого проступка: пошлость, наглость, " +
                          "распускание рук. Предупреждение, а не драка.",
            parameters = new
            {
                type = "object",
                properties = new { person = new { type = "string", description = "кому" } },
                required = new[] { "person" },
            },
        },
    };

    private static readonly object AttackDecl = new
    {
        type = "function",
        function = new
        {
            name = "attack",
            description = "Реально бьёшь (рукой или тем, что в руке). ТОЛЬКО для самозащиты: тебя " +
                          "ударили или систематически изводят после предупреждений. Один вызов — один " +
                          "удар. НИКОГДА не добивай лежачих.",
            parameters = new
            {
                type = "object",
                properties = new { person = new { type = "string", description = "кого" } },
                required = new[] { "person" },
            },
        },
    };

    private static readonly object WalkDecl = new
    {
        type = "function",
        function = new
        {
            name = "walk",
            description = "Идёшь в указанную сторону на несколько шагов и остаёшься там (новое место). " +
                          "Вызывай на «иди туда», «отойди», «встань вон там» — направление прикинь по " +
                          "контексту; если совсем неясно куда, лучше переспроси словами.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    direction = new { type = "string", description = "север, юг, запад или восток" },
                    tiles = new { type = "integer", description = "сколько шагов (1-15, по умолчанию 3)" },
                },
                required = new[] { "direction" },
            },
        },
    };

    private static readonly object BeQuietDecl = new
    {
        type = "function",
        function = new
        {
            name = "be_quiet",
            description = "Замолкаешь: не отвечаешь ни на что, пока не пройдёт время или к тебе не " +
                          "обратятся по имени. Вызывай СРАЗУ, когда просят помолчать, заткнуться, " +
                          "не мешать или дать поговорить.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    minutes = new { type = "number", description = "на сколько минут замолчать (по умолчанию 5)" },
                },
            },
        },
    };

    private static readonly object SetMoodDecl = new
    {
        type = "function",
        function = new
        {
            name = "set_mood",
            description = "Меняешь своё текущее настроение (оно окрашивает тон следующих реплик). " +
                          "Вызывай при РЕАЛЬНОЙ перемене: тебя обидели → «обижена на ...»; обидчик " +
                          "искренне извинился или загладил вину → смени на мягче или передай пустую " +
                          "строку (= ровное настроение, обида прощена). Не злоупотребляй.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    mood = new { type = "string", description = "настроение короткой фразой; пустая строка = ровное" },
                },
                required = new[] { "mood" },
            },
        },
    };

    private static readonly object SetAttitudeDecl = new
    {
        type = "function",
        function = new
        {
            name = "set_attitude",
            description = "Запоминаешь своё отношение к человеку на эту смену (видишь его рядом с его " +
                          "именем). Вызывай при заметной перемене: нагрубил → «холодно, он хамил»; " +
                          "извинился и загладил → верни «тепло». Пустая строка = обычное ровное отношение.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    person = new { type = "string", description = "имя человека" },
                    attitude = new { type = "string", description = "отношение парой слов; пустая строка = обычное" },
                },
                required = new[] { "person", "attitude" },
            },
        },
    };

    private static readonly object StopDecl = new
    {
        type = "function",
        function = new
        {
            name = "stop",
            description = "Прекращаешь текущее занятие (следование, дорогу куда-то) и возвращаешься " +
                          "на своё место. Вызывай, когда просят остановиться, отстать или вернуться за стойку.",
            parameters = new { type = "object", properties = new { } },
        },
    };

    /// <summary>Оборачивает действие с игровым миром в инструмент: маршалит вызов на главный поток.</summary>
    private LlmTool BodyTool(string name, object decl, Func<string?, string> onMainThread)
    {
        return new LlmTool
        {
            Name = name,
            Declaration = decl,
            Invoke = argsJson =>
            {
                var tcs = new TaskCompletionSource<string?>();
                _taskManager.RunOnMainThread(() =>
                {
                    try { tcs.SetResult(onMainThread(argsJson)); }
                    catch (Exception e) { tcs.SetResult($"Не получилось: {e.Message}"); }
                });
                return tcs.Task;
            },
        };
    }

    /// <summary>Собирает набор инструментов для текущего NPC по включённым cvar'ам (или null, если пусто).</summary>
    private IReadOnlyList<LlmTool>? BuildTools(EntityUid uid, bool canMakeDrinks)
    {
        var tools = new List<LlmTool>();

        if (_cfg.GetCVar(WegaCVars.LlmNpcWebSearch))
        {
            tools.Add(new LlmTool
            {
                Name = "web_search",
                Declaration = WebSearchDecl,
                Invoke = argsJson =>
                {
                    var query = LlmBackend.Arg(argsJson, "query");
                    return string.IsNullOrWhiteSpace(query)
                        ? Task.FromResult<string?>(null)
                        : _search.SearchAsync(query!, _sawmill);
                },
            });
        }

        if (canMakeDrinks)
        {
            tools.Add(BodyTool("make_drink", MakeDrinkDecl, argsJson =>
            {
                var drink = LlmBackend.Arg(argsJson, "drink");
                var person = LlmBackend.Arg(argsJson, "person");
                return string.IsNullOrWhiteSpace(drink)
                    ? "Не указано название напитка."
                    : StartMakeDrink(uid, drink!, person);
            }));
        }

        // Тело: подойти и вручить. Пока без отдельного cvar'а — простые безопасные действия.
        tools.Add(BodyTool("go_to", GoToDecl, argsJson =>
        {
            var person = LlmBackend.Arg(argsJson, "person");
            return string.IsNullOrWhiteSpace(person) ? "Не указано, к кому идти." : StartGoTo(uid, person!);
        }));

        tools.Add(BodyTool("give_item", GiveItemDecl, argsJson =>
        {
            var person = LlmBackend.Arg(argsJson, "person");
            return string.IsNullOrWhiteSpace(person) ? "Не указано, кому вручить." : StartGive(uid, person!);
        }));

        tools.Add(BodyTool("pick_up", PickUpDecl, argsJson =>
        {
            var item = LlmBackend.Arg(argsJson, "item");
            return string.IsNullOrWhiteSpace(item) ? "Не указано, что взять." : StartPickUp(uid, item!);
        }));

        tools.Add(BodyTool("follow", FollowDecl, argsJson =>
        {
            var person = LlmBackend.Arg(argsJson, "person");
            return string.IsNullOrWhiteSpace(person) ? "Не указано, за кем идти." : StartFollow(uid, person!);
        }));

        tools.Add(BodyTool("slap", SlapDecl, argsJson =>
        {
            var person = LlmBackend.Arg(argsJson, "person");
            return string.IsNullOrWhiteSpace(person) ? "Не указано, кому." : DoSlap(uid, person!);
        }));

        tools.Add(BodyTool("attack", AttackDecl, argsJson =>
        {
            var person = LlmBackend.Arg(argsJson, "person");
            return string.IsNullOrWhiteSpace(person) ? "Не указано, кого." : DoAttack(uid, person!);
        }));

        tools.Add(BodyTool("walk", WalkDecl, argsJson =>
        {
            var direction = LlmBackend.Arg(argsJson, "direction");
            var tiles = int.TryParse(LlmBackend.Arg(argsJson, "tiles"), out var t) ? t : 3;
            return string.IsNullOrWhiteSpace(direction)
                ? "Не указано направление."
                : StartWalk(uid, direction!, tiles);
        }));

        tools.Add(BodyTool("stop", StopDecl, _ => StartStop(uid)));

        tools.Add(BodyTool("set_mood", SetMoodDecl, argsJson =>
        {
            if (!TryComp<LlmNpcComponent>(uid, out var npc))
                return "Недоступно.";
            var mood = LlmBackend.Arg(argsJson, "mood")?.Trim();
            if (string.IsNullOrWhiteSpace(mood))
            {
                npc.Mood = null;
                return "Настроение снова ровное — старое отпущено.";
            }
            npc.Mood = mood;
            npc.MoodUntil = _timing.RealTime + TimeSpan.FromMinutes(20);
            return $"Настроение теперь: «{mood}». Со временем само выровняется.";
        }));

        tools.Add(BodyTool("set_attitude", SetAttitudeDecl, argsJson =>
        {
            if (!TryComp<LlmNpcComponent>(uid, out var npc))
                return "Недоступно.";
            var person = LlmBackend.Arg(argsJson, "person")?.Trim();
            if (string.IsNullOrWhiteSpace(person))
                return "Не указано, к кому.";
            var attitude = LlmBackend.Arg(argsJson, "attitude")?.Trim();
            // Отношение вешаем на полное имя из мира (модель может назвать только «Лия»).
            var name = FindPerson(uid, person!) is { } t ? MetaData(t).EntityName : person!;
            if (string.IsNullOrWhiteSpace(attitude))
            {
                npc.Attitude.Remove(name);
                return $"Отношение к {name} снова обычное.";
            }
            npc.Attitude[name] = attitude!;
            return $"Теперь ты относишься к {name}: {attitude}.";
        }));

        tools.Add(BodyTool("be_quiet", BeQuietDecl, argsJson =>
        {
            if (!TryComp<LlmNpcComponent>(uid, out var npc))
                return "Недоступно.";
            var minutes = 5f;
            if (float.TryParse(LlmBackend.Arg(argsJson, "minutes"), out var m) && m > 0)
                minutes = Math.Min(m, 30f);
            npc.MuteUntil = _timing.RealTime + TimeSpan.FromMinutes(minutes);
            npc.ReplyAt = null;
            return $"Ты замолкаешь на {minutes:0} мин (или пока не позовут по имени). В say — короткое " +
                   "подтверждение вроде «как скажешь» и всё.";
        }));

        return tools.Count > 0 ? tools : null;
    }

    /// <summary>
    /// Запуск конвейера приготовления: рецепт ищется сразу (чтобы модель могла переспросить при
    /// промахе), затем тело идёт к бару, смешивает и — если указан person — само относит и вручает.
    /// </summary>
    private string StartMakeDrink(EntityUid uid, string drinkName, string? personName)
    {
        if (!Exists(uid) || TerminatingOrDeleted(uid) || !TryComp<LlmNpcComponent>(uid, out var npc))
            return "Бармен сейчас недоступен.";

        // Защита от дублей: мелкие модели любят вызвать make_drink дважды за одно раздумье.
        if (npc.PendingDrink is { } busy)
            return $"Ты УЖЕ готовишь «{busy.Name}» — не вызывай make_drink повторно, дождись готовности.";

        if (_drinks.Find(drinkName, out var suggestions) is not { } recipe)
        {
            var hint = suggestions.Count > 0 ? $" Похожее в баре есть: {string.Join(", ", suggestions)}." : "";
            return $"Напитка «{drinkName}» в баре нет.{hint}";
        }

        EntityUid? serve = null;
        if (!string.IsNullOrWhiteSpace(personName))
        {
            serve = FindPerson(uid, personName!);
            if (serve == null)
                return $"Не вижу рядом никого по имени «{personName}» — уточни, кому подать.";
        }

        // Хардкор: ингредиенты только из настоящих раздатчиков — нет запаса, нет и коктейля.
        // Отказ с конкретикой: модель объяснит гостю, чего именно не хватает.
        var missing = CheckIngredients(uid, recipe);
        if (missing.Count > 0)
        {
            NoteOwnAction(uid, npc,
                $"проверила запасы: «{recipe.Name}» сделать НЕЛЬЗЯ (нет: {string.Join("; ", missing)}). Вопрос закрыт");
            return $"Приготовить «{recipe.Name}» не выйдет — в раздатчиках нет: {string.Join("; ", missing)}. " +
                   "Так и объясни гостю и предложи что-то из того, что есть. Повторно make_drink для этого " +
                   "напитка НЕ вызывай, пока гость сам не попросит его снова.";
        }

        npc.PendingDrink = recipe;
        npc.PendingServe = serve;
        var dispenser = FindDispenser(uid, recipe);
        BeginMixErrand(uid, npc, dispenser);
        NoteOwnAction(uid, npc, serve is { } sv
            ? $"приняла заказ на «{recipe.Name}» для {MetaData(sv).EntityName} и пошла готовить"
            : $"пошла готовить «{recipe.Name}»");

        var placeText = dispenser != null ? "к раздатчику" : "к стойке";
        var serveText = serve is { } s ? $", затем сама отнесёшь и вручишь {MetaData(s).EntityName}" : "";
        return $"Ты ТОЛЬКО НАЧАЛА готовить «{recipe.Name}» (состав: {recipe.Recipe}): идёшь {placeText}, " +
               $"смешаешь за несколько секунд{serveText}. Напиток ЕЩЁ НЕ ГОТОВ — НЕ говори «вот твой напиток», " +
               $"скажи в духе «сейчас сделаю». Больше НИКАКИХ инструментов в этом ответе.";
    }

    /// <summary>Курс на раздатчик (если найден) или на своё место; нет ни того ни другого — мешаем тут.</summary>
    private void BeginMixErrand(EntityUid uid, LlmNpcComponent npc, EntityUid? dispenser)
    {
        npc.Errand = LlmErrand.GoMix;
        npc.ErrandTarget = dispenser; // у GoMix цель — раздатчик; null = идти к Home
        npc.ErrandItem = null;
        npc.ErrandTimeout = _timing.RealTime + ErrandLimit;

        var destination = dispenser is { } d ? Transform(d).Coordinates : npc.Home;
        if (destination is { } coords)
        {
            EnsureComp<ActiveNPCComponent>(uid);
            RegisterSteering(uid, coords).Range = ArriveRange - 0.2f;
        }
    }

    /// <summary>
    /// Смешивание закончилось: спавним стакан и наливаем НАСТОЯЩИЕ ингредиенты рецепта — игровая химия
    /// сама проводит реакцию (с пузырьками и звуком), как у живого бармена. Рецепты, требующие шейкер
    /// (Shake/Stir), в стакане не реагируют — тогда честно доливаем готовый продукт напрямую.
    /// </summary>
    private void FinishMixing(EntityUid uid, LlmNpcComponent npc)
    {
        if (npc.PendingDrink is not { } recipe)
        {
            CancelErrand(uid, npc);
            return;
        }

        // Перепроверка запасов перед разливом: с момента заказа их могли выпить.
        // Всё или ничего — начатый недококтейль хуже честного отказа.
        var nowMissing = CheckIngredients(uid, recipe);
        if (nowMissing.Count > 0)
        {
            _chat.TrySendInGameICMessage(uid,
                $"заглядывает в раздатчик и разводит руками — для «{recipe.Name}» уже не хватает: {string.Join("; ", nowMissing)}",
                InGameICChatType.Emote, ChatTransmitRange.Normal);
            _sawmill.Info($"{ToPrettyString(uid)} сорвала {recipe.Name}: закончилось {string.Join(";", nowMissing)}");
            CancelErrand(uid, npc, goHome: true);
            return;
        }

        var glass = Spawn(DrinkGlassProto, Transform(uid).Coordinates);
        if (!_solution.TryGetSolution(glass, DrinkSolution, out var soln, out var solution))
        {
            Del(glass);
            CancelErrand(uid, npc);
            return;
        }

        // Множитель: столько «порций» реакции, чтобы вышло ~30 единиц напитка.
        var mult = DrinkMultiplier(recipe);
        var allTaken = true;
        foreach (var (reagentId, amount) in recipe.Reactants)
        {
            var needed = FixedPoint2.New(amount * mult);
            // Строго из раздатчиков — их запасы реально тратятся. В стакан идёт ровно вычерпанное.
            var taken = TakeFromDispensers(uid, reagentId, needed);
            _sawmill.Info($"вычерпано из раздатчиков: {reagentId} {taken}/{needed}");
            if (taken > FixedPoint2.Zero)
                _solution.TryAddReagent(soln.Value, reagentId, taken);
            if (taken < needed)
                allTaken = false;
        }

        // Реакция не прошла, но ингредиенты честно вычерпаны целиком — значит, рецепту нужен шейкер
        // (Shake/Stir): «встряхиваем» — заменяем смесь готовым продуктом. Это не ресурсы из воздуха:
        // всё сырьё уже потрачено. А вот если вычерпать всё не удалось — недококтейль не отдаём.
        if (solution.GetTotalPrototypeQuantity(recipe.ReagentId) <= FixedPoint2.Zero)
        {
            if (!allTaken)
            {
                Del(glass);
                _chat.TrySendInGameICMessage(uid,
                    $"выливает неудавшуюся смесь — на «{recipe.Name}» не хватило ингредиентов",
                    InGameICChatType.Emote, ChatTransmitRange.Normal);
                CancelErrand(uid, npc, goHome: true);
                return;
            }

            _solution.RemoveAllSolution(soln.Value);
            _solution.TryAddReagent(soln.Value, recipe.ReagentId, DrinkAmount);
        }

        _hands.PickupOrDrop(uid, glass, dropNear: true);
        _chat.TrySendInGameICMessage(uid, $"смешивает ингредиенты и разливает «{recipe.Name}» по стаканам",
            InGameICChatType.Emote, ChatTransmitRange.Normal);
        NoteOwnAction(uid, npc, $"приготовила «{recipe.Name}»");
        _sawmill.Info($"{ToPrettyString(uid)} приготовила {recipe.Name} ({recipe.ReagentId})");

        var serve = npc.PendingServe;
        npc.PendingDrink = null;
        npc.PendingServe = null;
        npc.MixUntil = null;

        // Есть кому подать — сразу несём; иначе стоим с напитком в руке.
        if (serve is { } target && Exists(target))
            BeginErrand(uid, npc, LlmErrand.GiveItem, target, glass);
        else
            CancelErrand(uid, npc);
    }

    /// <summary>Пощёчина: близко — символический урон + громкий эмоут. Предупреждение, не драка.</summary>
    private string DoSlap(EntityUid uid, string personName)
    {
        if (FindPerson(uid, personName) is not { } target)
            return $"Не вижу рядом «{personName}».";

        var dist = (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(target)).Length();
        if (dist > 2f)
            return "Слишком далеко для пощёчины — сначала подойди (go_to).";
        if (_mobState.IsIncapacitated(target))
            return "Он и так лежит — оставь.";

        _rotateToFace.TryFaceCoordinates(uid, _transform.GetWorldPosition(target));
        var spec = new Content.Shared.Damage.DamageSpecifier();
        spec.DamageDict.Add("Blunt", 1);
        _damageable.TryChangeDamage(target, spec, origin: uid);
        _chat.TrySendInGameICMessage(uid, $"отвешивает звонкую пощёчину — {MetaData(target).EntityName}",
            InGameICChatType.Emote, ChatTransmitRange.Normal);
        NoteOwnAction(uid, Comp<LlmNpcComponent>(uid), $"дала пощёчину {MetaData(target).EntityName}");
        return "Пощёчина отвешена. В say — короткая колкость по делу.";
    }

    /// <summary>Настоящий удар: рукой или предметом в руке, один за вызов, лежачих не бьём.</summary>
    private string DoAttack(EntityUid uid, string personName)
    {
        if (FindPerson(uid, personName) is not { } target)
            return $"Не вижу рядом «{personName}».";

        if (_mobState.IsIncapacitated(target))
            return "Он уже лежит — хватит, ты не добиваешь.";

#pragma warning disable CS0618
        if (_damageable.GetTotalDamage(target) > 70)
#pragma warning restore CS0618
            return "Он уже еле стоит — достаточно, отойди (walk/stop).";

        var dist = (_transform.GetWorldPosition(uid) - _transform.GetWorldPosition(target)).Length();
        if (dist > 2f)
            return "Слишком далеко — сначала подойди (go_to).";

        if (!_melee.TryGetWeapon(uid, out var weaponUid, out var weapon))
            return "Нечем ударить.";

        _rotateToFace.TryFaceCoordinates(uid, _transform.GetWorldPosition(target));
        var hit = _melee.AttemptLightAttack(uid, weaponUid, weapon, target);
        NoteOwnAction(uid, Comp<LlmNpcComponent>(uid),
            hit ? $"ударила {MetaData(target).EntityName} в ответ" : "замахнулась, но промазала");
        return hit
            ? "Удар нанесён. Один — и хватит, если он не продолжает."
            : "Промахнулась.";
    }

    /// <summary>Порций реакции на один стакан (~30 единиц готового напитка).</summary>
    private static int DrinkMultiplier(LlmDrinks.DrinkRecipe recipe)
        => Math.Max(1, (int)Math.Ceiling((float)DrinkAmount.Int() / Math.Max(1, recipe.ProductAmount)));

    /// <summary>
    /// Проверяет запасы раздатчиков на весь рецепт. Возвращает список нехваток в человекочитаемом
    /// виде («лёд: нужно 10, есть 4») — пустой список значит, что всё есть.
    /// </summary>
    private List<string> CheckIngredients(EntityUid uid, LlmDrinks.DrinkRecipe recipe)
    {
        var missing = new List<string>();
        var mult = DrinkMultiplier(recipe);

        // Диагностика «почему нет»: сколько раздатчиков вообще видим и что в них по каждому реагенту.
        var dispensers = 0;
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);
        var dq = EntityQueryEnumerator<
            Content.Server.Chemistry.Components.ReagentDispenserComponent,
            Content.Shared.Storage.StorageComponent>();
        while (dq.MoveNext(out var disp, out _, out var st))
        {
            if (Transform(disp).MapID != map)
                continue;
            var dist = (_transform.GetWorldPosition(disp) - origin).Length();
            if (dist <= FindRange)
            {
                dispensers++;
                _sawmill.Debug($"раздатчик {ToPrettyString(disp)}: дистанция {dist:F1}, бутылей {st.StoredItems.Count}");
            }
        }
        _sawmill.Info($"проверка запасов «{recipe.Name}»: раздатчиков в радиусе {FindRange} — {dispensers}");

        foreach (var (reagentId, amount) in recipe.Reactants)
        {
            var needed = FixedPoint2.New(amount * mult);
            var available = CountAvailable(uid, reagentId);
            _sawmill.Info($"  реагент {reagentId}: нужно {needed}, есть {available}");
            if (available >= needed)
                continue;

            var name = _proto.TryIndex<Content.Shared.Chemistry.Reagent.ReagentPrototype>(reagentId, out var proto)
                ? proto.LocalizedName
                : reagentId;
            missing.Add($"{name} (нужно {needed}, есть {available})");
        }

        return missing;
    }

    /// <summary>
    /// Сколько реагента суммарно есть в баре: бутыли раздатчиков + бутылки в вендинг-автоматах
    /// (АлкоМат/СодоМат — на многих картах бар укомплектован именно ими, а не раздатчиками).
    /// </summary>
    private FixedPoint2 CountAvailable(EntityUid uid, string reagentId)
    {
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);
        var total = FixedPoint2.Zero;

        var query = EntityQueryEnumerator<
            Content.Server.Chemistry.Components.ReagentDispenserComponent,
            Content.Shared.Storage.StorageComponent>();
        while (query.MoveNext(out var disp, out _, out var storage))
        {
            if (Transform(disp).MapID != map || (_transform.GetWorldPosition(disp) - origin).Length() > FindRange)
                continue;

            foreach (var jug in storage.StoredItems.Keys)
            {
                if (_solution.TryGetDrainableSolution(jug, out _, out var sol))
                    total += sol.GetTotalPrototypeQuantity(reagentId);
            }
        }

        var vendors = EntityQueryEnumerator<Content.Shared.VendingMachines.VendingMachineComponent>();
        while (vendors.MoveNext(out var vend, out var machine))
        {
            if (Transform(vend).MapID != map || (_transform.GetWorldPosition(vend) - origin).Length() > FindRange)
                continue;

            foreach (var entry in machine.Inventory.Values)
            {
                if (entry.Amount == 0)
                    continue;
                var perBottle = BottleContains(entry.ID, reagentId);
                if (perBottle > FixedPoint2.Zero)
                    total += perBottle * (int)entry.Amount;
            }
        }

        // Принесённое гостями: яйца/лимоны/банки на стойке и в её руках.
        foreach (var (_, amount) in EnumerateLooseIngredients(uid, reagentId))
            total += amount;

        return total;
    }

    /// <summary>
    /// Реагент в свободных предметах рядом (на стойке/полу) и в руках NPC: яйца, лимоны, банки —
    /// всё, что гость принёс и положил перед барменшей. Перечисляет пары (предмет, количество).
    /// </summary>
    private IEnumerable<(EntityUid Item, FixedPoint2 Amount)> EnumerateLooseIngredients(EntityUid uid, string reagentId)
    {
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        // В руках.
        foreach (var held in _hands.EnumerateHeld((uid, null)))
        {
            if (TryComp<Content.Shared.Chemistry.Components.SolutionComponent>(held, out var sol))
            {
                var amount = sol.Solution.GetTotalPrototypeQuantity(reagentId);
                if (amount > FixedPoint2.Zero)
                    yield return (held, amount);
            }
        }

        // Свободные предметы поблизости (не в контейнерах/чужих руках).
        var query = EntityQueryEnumerator<
            Content.Shared.Item.ItemComponent,
            Content.Shared.Chemistry.Components.SolutionComponent>();
        while (query.MoveNext(out var item, out _, out var sol))
        {
            if (Transform(item).MapID != map || _container.IsEntityInContainer(item))
                continue;
            if ((_transform.GetWorldPosition(item) - origin).Length() > FindRange)
                continue;

            var amount = sol.Solution.GetTotalPrototypeQuantity(reagentId);
            if (amount > FixedPoint2.Zero)
                yield return (item, amount);
        }
    }

    /// <summary>Сколько нужного реагента в одной бутылке данного прототипа (по его начальному раствору).</summary>
    private FixedPoint2 BottleContains(string protoId, string reagentId)
    {
        if (!_proto.TryIndex<Robust.Shared.Prototypes.EntityPrototype>(protoId, out var proto)
            || !proto.TryGetComponent<Content.Shared.Chemistry.Components.SolutionComponent>(out var sol))
            return FixedPoint2.Zero;

        return sol.Solution.GetTotalPrototypeQuantity(reagentId);
    }

    /// <summary>
    /// Выбирает раздатчик под конкретный рецепт: тот, в чьих бутылях больше всего нужных ингредиентов
    /// (алкомат для крепкого, содомат для газировки), при равенстве — ближайший. Просто «ближайший»
    /// не годится: за Отвёрткой она уходила к содомату, стоящему на шаг ближе алкомата.
    /// </summary>
    private EntityUid? FindDispenser(EntityUid uid, LlmDrinks.DrinkRecipe recipe)
    {
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);

        EntityUid? best = null;
        var bestScore = -1;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<
            Content.Server.Chemistry.Components.ReagentDispenserComponent,
            Content.Shared.Storage.StorageComponent>();
        while (query.MoveNext(out var disp, out _, out var storage))
        {
            if (Transform(disp).MapID != map)
                continue;
            var dist = (_transform.GetWorldPosition(disp) - origin).Length();
            if (dist > FindRange)
                continue;

            // Сколько разных ингредиентов рецепта есть в этом автомате.
            var score = 0;
            foreach (var (reagentId, _) in recipe.Reactants)
            {
                foreach (var jug in storage.StoredItems.Keys)
                {
                    if (_solution.TryGetDrainableSolution(jug, out _, out var sol)
                        && sol.GetTotalPrototypeQuantity(reagentId) > FixedPoint2.Zero)
                    {
                        score++;
                        break;
                    }
                }
            }

            if (score > bestScore || score == bestScore && dist < bestDist)
            {
                best = disp;
                bestScore = score;
                bestDist = dist;
            }
        }

        // Вендинг-автоматы (АлкоМат/СодоМат) — равноправные кандидаты: у многих баров только они.
        var vendors = EntityQueryEnumerator<Content.Shared.VendingMachines.VendingMachineComponent>();
        while (vendors.MoveNext(out var vend, out var machine))
        {
            if (Transform(vend).MapID != map)
                continue;
            var dist = (_transform.GetWorldPosition(vend) - origin).Length();
            if (dist > FindRange)
                continue;

            var score = 0;
            foreach (var (reagentId, _) in recipe.Reactants)
            {
                foreach (var entry in machine.Inventory.Values)
                {
                    if (entry.Amount > 0 && BottleContains(entry.ID, reagentId) > FixedPoint2.Zero)
                    {
                        score++;
                        break;
                    }
                }
            }

            if (score > bestScore || score == bestScore && dist < bestDist)
            {
                best = vend;
                bestScore = score;
                bestDist = dist;
            }
        }

        return best;
    }

    /// <summary>
    /// Вычерпывает нужный реагент из бутылей раздатчиков в паре шагов от NPC (как это делает живой
    /// бармен через UI): запасы реально тратятся. Возвращает, сколько удалось достать.
    /// </summary>
    private FixedPoint2 TakeFromDispensers(EntityUid uid, string reagentId, FixedPoint2 needed)
    {
        var map = Transform(uid).MapID;
        var origin = _transform.GetWorldPosition(uid);
        var taken = FixedPoint2.Zero;

        var query = EntityQueryEnumerator<
            Content.Server.Chemistry.Components.ReagentDispenserComponent,
            Content.Shared.Storage.StorageComponent>();
        while (query.MoveNext(out var disp, out _, out var storage))
        {
            if (taken >= needed)
                break;
            // Радиус тот же, что у проверки запасов, иначе «есть» и «достала» разойдутся.
            if (Transform(disp).MapID != map || (_transform.GetWorldPosition(disp) - origin).Length() > FindRange)
                continue;

            foreach (var jug in storage.StoredItems.Keys)
            {
                if (taken >= needed)
                    break;
                if (!_solution.TryGetDrainableSolution(jug, out var src, out var srcSolution))
                    continue;

                var available = srcSolution.GetTotalPrototypeQuantity(reagentId);
                if (available <= FixedPoint2.Zero)
                    continue;

                var take = FixedPoint2.Min(available, needed - taken);
                taken += _solution.RemoveReagent(src.Value, reagentId, take);
            }
        }

        if (taken >= needed)
            return taken;

        // Раздатчиков не хватило — открываем бутылки из вендинг-автоматов (АлкоМат/СодоМат):
        // счётчик бутылок реально уменьшается, как будто бармен купил и разлил бутылку.
        var vendors = EntityQueryEnumerator<Content.Shared.VendingMachines.VendingMachineComponent>();
        while (vendors.MoveNext(out var vend, out var machine))
        {
            if (taken >= needed)
                break;
            if (Transform(vend).MapID != map || (_transform.GetWorldPosition(vend) - origin).Length() > FindRange)
                continue;

            foreach (var entry in machine.Inventory.Values)
            {
                while (taken < needed && entry.Amount > 0)
                {
                    var perBottle = BottleContains(entry.ID, reagentId);
                    if (perBottle <= FixedPoint2.Zero)
                        break;

                    entry.Amount--;
                    taken += FixedPoint2.Min(perBottle, needed - taken);
                    _sawmill.Debug($"открыта бутылка {entry.ID} из {ToPrettyString(vend)} ради {reagentId}");
                }
            }
            Dirty(vend, machine);
        }

        if (taken >= needed)
            return taken;

        // Принесённые ингредиенты: разбиваем яйцо / выжимаем лимон — предмет расходуется целиком.
        foreach (var (item, amount) in EnumerateLooseIngredients(uid, reagentId).ToList())
        {
            if (taken >= needed)
                break;
            taken += FixedPoint2.Min(amount, needed - taken);
            _sawmill.Debug($"использован предмет {ToPrettyString(item)} ради {reagentId}");
            Del(item);
        }

        return taken;
    }
}
