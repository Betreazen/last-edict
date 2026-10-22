using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>Упреждающая проверка пути событиями сценария (<c>docs/map-path.md</c> §7, <c>docs/scenario-events.md</c> §1.2).</summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Упреждающая проверка пути (<c>0x43fc3c</c> → <c>0x440284</c>, ранг 1 по листингу): по клеткам сегодняшней части пути по
    /// порядку — кроме клеток с мешком (объект типа <c>0x27</c> в плане, <c>0x5f54e2</c>) — отряд временно ставится в клетку
    /// (<c>0x5e709b</c>; из крепости — <c>0x5e78a9</c>) и проверяются события без игрока хода с ним как триггерером (<c>0x44296c</c>;
    /// шанс — бросок R250 у каждого кандидата, чьи условия выполнены, на каждой такой клетке). Хозяин — человек и все эффекты безвредны
    /// (<c>0x4405ce</c> — <c>CMidPlayer +0x88</c>, <c>0x4405fb</c>: ни у одного эффекта слот <c>+0xc</c> не истинен — только смена приказа
    /// и включение событий, ранг 2) — эффекты переносятся в ход (<c>0x43f8f5</c>, список проверки пустеет) и применяются раньше проверки
    /// после хода (<see cref="MoveStack"/>), путь идёт дальше. Иначе (ИИ или вредный эффект) — путь кончается на этой клетке, а список
    /// проверки остаётся непустым (<c>0x44049b</c> → <c>0x4404ef</c>) и уничтожается: эффектов в ходе нет, событие срабатывает (или нет)
    /// в проверке после настоящего хода — со вторым броском шанса (<see cref="EventFiring.CutOnly"/>; ранг 1 по листингу <c>0x440464</c>…
    /// <c>0x4404a5</c>; журнал <c>20261018b-port-synth</c>, карта 17: шаг человека в зону «[PS-E01g]» — броски шанса 912674 и 912675, Гоблин
    /// создан один раз, после второго). После проверки отряд возвращается на место.
    /// Без событий или правил — путь целиком.
    /// Журнал human, день 8: <c>S073KC0002</c> встаёт с 3 очками в первой клетке зоны <c>S073LO0007</c> (событие <c>S073EV0015</c>
    /// «вход в локацию» для Легионов).
    /// </summary>
    /// <returns>Сколько клеток <paramref name="today"/> пройти, события, сработавшие на пройденной части (их — в
    /// <see cref="MoveStack"/>), и оборвала ли проверка путь (выход <c>0x440284</c> = 1, и на последней клетке: тогда у движка нет ни
    /// нападения, ни входа, а отряд раскрывается, <see cref="StopsPath"/>).</returns>
    public (int Walk, IReadOnlyList<EventFiring> Fired, bool Cut) PathEvents(string stackId, IReadOnlyList<PathPoint> today)
    {
        ArgumentNullException.ThrowIfNull(today);
        if (Book is null || State.Events.Events.Count == 0 || Over || !Map.Stacks.TryGetValue(stackId, out var stack))
            return (today.Count, [], false);
        var rules = new EventRules(Book.Economy.Races, Book.Diplomacy);
        var fired = new List<EventFiring>();
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < today.Count; i++)
        {
            var (x, y) = (today[i].X, today[i].Y);
            if (Map.Plan.TryGetValue((x, y), out var ids) && ids.Any(IsBag)) continue;
            var placed = stack with { X = x, Y = y, Inside = "" };
            stacks[stack.Id] = placed;
            var there = With(Map, stacks: stacks);
            var executed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var here = new List<EventFiring>();
            while (ScenarioEvents.Next(State.Events, State.EventProgress, there, rules, "", stack.Id, Rng, executed) is { Count: > 0 } f)
                here.AddRange(f.Where(e => Produces(e, placed)));
            if (here.Count == 0) continue;
            // Обрыв: эффекты проверки в ход не уходят (список уничтожается) — срабатывания только для обрыва и показа невидимого.
            if (StopsPath(stack.Owner, here)) return (i + 1, [.. fired, .. here.Select(h => h with { CutOnly = true })], true);
            // У человека безвредное идёт дальше, а проба состояние событий не меняет — то же срабатывание на следующей клетке не
            // повторяем (эффекты применяются один раз, MoveStack).
            fired.AddRange(here.Where(h => !fired.Any(f => f.Event.Id.Equals(h.Event.Id, StringComparison.OrdinalIgnoreCase)
                                                             && f.PlayerId.Equals(h.PlayerId, StringComparison.OrdinalIgnoreCase))));
        }
        return (today.Count, fired, false);
    }

    /// <summary>Срабатывания на клетке пути обрывают его (<c>0x440284</c>): у ИИ — всегда, у человека — если не все безвредны
    /// (<see cref="Harmless"/>). По срабатываниям всей проверки (<see cref="PathEvents"/>) — то же: у человека безвредные с прежних клеток
    /// путь не рвут, а рвущее на последней клетке в список попадает (то же событие безвредно везде или нигде; помечено
    /// <see cref="EventFiring.CutOnly"/>).</summary>
    private bool StopsPath(string owner, IReadOnlyList<EventFiring> firings) =>
        firings.Count > 0 && (firings.Any(f => f.CutOnly) || !State.Info.Humans.Contains(owner, StringComparer.OrdinalIgnoreCase)
                              || !firings.All(Harmless));

    /// <summary>
    /// Даст ли срабатывание эффект (<c>IEffectResult::Create</c> вернул не пусто): у <c>OCCUR_ONCE</c> — всегда (первый эффект —
    /// выключение самого события, <c>0x4378c2</c>); литьё на триггерер (<c>0x43bfe4</c> → <c>0x443600</c>) — только если заклинание
    /// применимо к отряду (слот <c>+0xc</c> заклинания: та же проверка цели, что у литья на карте, <see cref="StackRefusal"/>; заклинатель
    /// — <c>ID_PLAYER1</c>, пусто — хозяин отряда, ранг 3); прочие эффекты — всегда. Журнал legions: отряды Кланов проходят клетку
    /// <c>S073LO0005</c> (событие <c>S073EV0009</c>, лечение отряда — лечить некого) не останавливаясь.
    /// </summary>
    private bool Produces(EventFiring f, MapStack triggerer) =>
        f.DisablesItself || f.Event.Effects.Any(e => e switch
        {
            CastSpellOnTriggererEffect c => Book!.Spells.TryGetValue(c.SpellId, out var spell)
                                            && StackRefusal(c.PlayerId.Length > 0 && !c.PlayerId.Equals(NoPlayer, StringComparison.OrdinalIgnoreCase)
                                                                ? c.PlayerId : triggerer.Owner, spell, triggerer) is null,
            _ => true,
        });

    private const string NoPlayer = "G000000000";

    /// <summary>Эффекты события, с которыми человек идёт дальше (<c>0x4405fb</c>, слот <c>+0xC</c> ложен): только смена приказа
    /// (<c>CEffectChangeOrder</c>) и самовыключение одноразового события (у порта не в списке эффектов). Явное <c>L_ENABLE_EVENT</c>
    /// движок создаёт с флагом 1 — оно путь человека обрывает (разбор сессии В, <c>docs/day-cycle.md</c> §6).</summary>
    private static bool Harmless(EventFiring f) => f.Event.Effects.All(e => e is ChangeOrderEffect);

    private static bool IsBag(string id) => id.Length >= 6 && id.AsSpan(4, 2).Equals("BG", StringComparison.OrdinalIgnoreCase);
}
