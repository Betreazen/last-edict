using LastEdict.Data.Play;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Diagnostics;

/// <summary>Нарушение инварианта карты: код (вид нарушения), объект (отряд, юнит, укрепление, игрок…) и подробности словами.</summary>
public sealed record Violation(string Code, string Object, string Detail);

/// <summary>
/// Инварианты состояния карты (<see cref="MapState"/>) — то, что движок держит всегда и что порт обязан держать так же: тихая ошибка
/// модели (юнит в двух группах, отрицательная казна, отряд вне плана) видна здесь раньше, чем ломает показ или правило. Проверка
/// чистая — только читает. Зонд порта зовёт её в начале каждого хода и после каждого боя (строка <c>invariant</c>); на сохранениях
/// оригинала из корпуса нарушений нет (<c>MapInvariantsTests</c>) — так проверяется сама проверка.
/// <para>Коды: <c>unit_missing</c> (группа ссылается на юнит, которого нет), <c>unit_shared</c> (юнит в двух местах),
/// <c>unit_orphan</c> (юнит ни в одной группе), <c>slots</c> (клетки построения не сходятся с юнитами), <c>hp</c> (здоровье вне
/// [0, максимум]), <c>leader</c> (лидер не в своём отряде), <c>leader_alive</c> (признак «лидер жив» не совпадает со здоровьем лидера), <c>stack_empty</c>, <c>owner</c> (хозяин не игрок карты), <c>bank</c>
/// (валюта вне [0, <see cref="Economy.CurrencyMax"/>]), <c>off_map</c>, <c>plan</c> (отряд не в плане своей клетки или план
/// ссылается на отряд, которого нет), <c>inside</c> (<see cref="MapStack.Inside"/> и <see cref="MapSite.StackId"/> не взаимны),
/// <c>capacity</c> (группа выросла сверх вместимости — только при сравнении с прошлым состоянием), <c>item</c> (экземпляр предмета без типа или тип не в правилах), <c>talisman</c>
/// (заряды не у талисмана или вне [0, ∞)), <c>relation</c> (отношение вне 0…100).</para>
/// </summary>
public static class MapInvariants
{
    /// <summary>Клеток в группе (<c>POS_0..POS_5</c>).</summary>
    private const int Cells = UnitCells.CellCount;

    /// <summary>Отношение рас — 0…100 (<see cref="Relation.Current"/>, война ≤ 33, мир &gt; 66).</summary>
    private const int RelationMax = 100;

    /// <summary>Нарушения инвариантов <paramref name="map"/>. <paramref name="book"/> — правила: без них не проверяются потолок
    /// здоровья, лидерство и типы предметов. <paramref name="before"/> — прошлое проверенное состояние той же партии: с ним
    /// проверяется вместимость групп (она — правило добавления, а не состояния, см. <see cref="Capacity"/>).</summary>
    public static IReadOnlyList<Violation> Check(MapState map, GameBook? book, MapState? before = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var found = new List<Violation>();
        Groups(map, book, found);
        if (before is not null) Capacity(map, before, book, found);
        Units(map, book, found);
        Stacks(map, found);
        Sites(map, found);
        Players(map, found);
        Items(map, book, found);
        Relations(map, found);
        return found;
    }

    // ------------------------------------------------------------------ группы и юниты

    /// <summary>Группа юнитов: отряд, гарнизон укрепления или охрана руины; <see cref="Capacity"/> — предел занятых клеток.</summary>
    private sealed record Group(string Id, IReadOnlyList<string> UnitIds, IReadOnlyList<int> Slots, int Capacity)
    {
        public int Occupied => Slots.Count(c => c >= 0);
    }

    /// <summary>Группы карты. Предел отряда — <c>min(лидерство, 6)</c> (<see cref="GameSession.Leadership"/>; без правил — 6),
    /// деревни — её уровень (<see cref="Fortifications.GarrisonCapacity"/>), столицы и руины — 6.</summary>
    private static IEnumerable<Group> AllGroups(MapState map, GameBook? book)
    {
        foreach (var s in map.Stacks.Values) yield return new Group(s.Id, s.UnitIds, s.Slots, StackCapacity(map, s, book));
        foreach (var site in map.Sites.Where(s => !s.Gone && s.Kind is SiteKind.Capital or SiteKind.Village or SiteKind.Ruin))
            yield return new Group(site.Id, site.Guards, site.GuardSlots,
                                   site.Kind == SiteKind.Village ? Fortifications.GarrisonCapacity(site.Size ?? 1) : Cells);
    }

    /// <summary>
    /// Вместимость — правило добавления юнита (найм, обмен, эффект), а не состояния: группу, данную сценарием, движок держит и сверх
    /// предела. Корпус: 20260930-probe72 — по два юнита в деревнях 1-го уровня с первого дня (синтетика S143, <c>disciples-modding
    /// docs/synthetic-maps/ai-q12…/plan-output.txt</c>), отряд S135KC002e из трёх клеток при лидерстве 2 (q57, день 1); одиночный крупный
    /// лидер при лидерстве 1 — две клетки (human t42, legions t50, probe72 q22). Поэтому нарушение — группа сверх предела, у которой
    /// занятых клеток стало больше, чем в прошлом проверенном состоянии <paramref name="before"/> (новая группа — без сравнения).
    /// </summary>
    private static void Capacity(MapState map, MapState before, GameBook? book, List<Violation> found)
    {
        var was = AllGroups(before, book).ToDictionary(g => g.Id, g => g.Occupied, StringComparer.OrdinalIgnoreCase);
        foreach (var g in AllGroups(map, book))
            if (g.Occupied > g.Capacity && was.TryGetValue(g.Id, out int earlier) && g.Occupied > earlier)
                found.Add(new("capacity", g.Id, $"занято клеток {earlier} → {g.Occupied}, вмещает {g.Capacity}; юниты {string.Join(",", g.UnitIds)}"));
    }

    /// <summary>Лидерство лидера отряда с цепочкой (как <see cref="GameSession.Leadership"/>), не больше шести.</summary>
    private static int StackCapacity(MapState map, MapStack stack, GameBook? book)
    {
        if (book is null || !map.Units.TryGetValue(stack.LeaderId, out var leader)) return Cells;
        int basic = book.TryGlobal(leader.TypeId, out var id, out _) && book.Extras.Units.TryGetValue(id, out var e) ? e.Leadership : 0;
        var chain = leader.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
        return Math.Min(EffectiveStats.OfStack(chain, leadership: basic).Leadership, Cells);
    }

    /// <summary>Юнит — ровно в одной группе и один раз в ней; ссылки групп ведут на юниты карты; клетки построения сходятся с
    /// юнитами (каждый юнит стоит, крупный — в двух клетках одной колонки).</summary>
    private static void Groups(MapState map, GameBook? book, List<Violation> found)
    {
        var place = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in AllGroups(map, book))
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in g.UnitIds)
            {
                if (!map.Units.ContainsKey(id)) found.Add(new("unit_missing", g.Id, $"юнита {id} нет на карте"));
                if (!seen.Add(id)) found.Add(new("unit_shared", id, $"дважды в группе {g.Id}"));
                else if (place.TryGetValue(id, out string? other)) found.Add(new("unit_shared", id, $"в группах {other} и {g.Id}"));
                else place[id] = g.Id;
            }
            Slots(g, found);
        }
        foreach (string id in map.Units.Keys.Where(id => !place.ContainsKey(id)))
            found.Add(new("unit_orphan", id, "юнит ни в одной группе"));
    }

    private static void Slots(Group g, List<Violation> found)
    {
        if (g.Slots.Count == 0 && g.UnitIds.Count == 0) return;   // пустая охрана без клеток (разграбленная руина)
        if (g.Slots.Count != Cells)
        {
            found.Add(new("slots", g.Id, $"клеток {g.Slots.Count}, а не {Cells}"));
            return;
        }
        var cellsOf = new Dictionary<int, List<int>>();
        for (int c = 0; c < Cells; c++)
        {
            int index = g.Slots[c];
            if (index < 0) continue;
            if (index >= g.UnitIds.Count)
            {
                found.Add(new("slots", g.Id, $"клетка {c} → индекс {index}, юнитов {g.UnitIds.Count}"));
                continue;
            }
            (cellsOf.TryGetValue(index, out var list) ? list : cellsOf[index] = []).Add(c);
        }
        for (int i = 0; i < g.UnitIds.Count; i++)
        {
            if (!cellsOf.TryGetValue(i, out var cells)) found.Add(new("slots", g.Id, $"юнит {g.UnitIds[i]} не стоит ни в одной клетке"));
            else if (cells.Count > 2 || (cells.Count == 2 && cells[0] / 2 != cells[1] / 2))
                found.Add(new("slots", g.Id, $"юнит {g.UnitIds[i]} в клетках {string.Join(",", cells)}"));
        }
    }

    /// <summary>Здоровье юнита — в [0, максимум]: максимум — тип, выращенный до уровня, с цепочкой модификаторов (как в бою,
    /// <see cref="GameSession.HpMax"/>).</summary>
    private static void Units(MapState map, GameBook? book, List<Violation> found)
    {
        foreach (var u in map.Units.Values)
        {
            if (u.Hp < 0) found.Add(new("hp", u.Id, $"здоровье {u.Hp}"));
            if (book is null || HpMax(u, book) is not { } max) continue;
            if (u.Hp > max) found.Add(new("hp", u.Id, $"здоровье {u.Hp} больше максимума {max} ({u.TypeId} ур. {u.Level})"));
        }
    }

    /// <summary>Потолки здоровья по правилам: ключ — тип, уровень и цепочка модификаторов. Проверка идёт в начале каждого хода и после
    /// каждого боя на потоке хода ИИ — у большинства юнитов между проверками ключ тот же (замер 13.10: карта 96×96, 422 юнита —
    /// 1,4 мс на проверку без кэша).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameBook, System.Collections.Concurrent.ConcurrentDictionary<string, int?>>
        HpMaxCache = new();

    private static int? HpMax(MapUnit unit, GameBook book)
    {
        string key = $"{unit.TypeId}|{unit.Level}|{string.Join(",", unit.Modifiers)}";
        return HpMaxCache.GetOrCreateValue(book).GetOrAdd(key, _ =>
        {
            if (book.Grown(unit.TypeId, unit.Level) is not { } grown) return null;
            var chain = unit.Modifiers.Select(m => book.Rules.Modifiers?.GetValueOrDefault(m)).OfType<Modifier>().ToList();
            return EffectiveStats.Of(grown.Type, chain).HitPoints;
        });
    }

    // ------------------------------------------------------------------ отряды и места

    /// <summary>Отряд: хозяин — игрок карты, лидер в отряде, юниты есть; на карте — в её пределах и в плане своей клетки; внутри
    /// объекта — визитёр этого объекта. План не ссылается на отряды, которых нет.</summary>
    private static void Stacks(MapState map, List<Violation> found)
    {
        var sites = map.Sites.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var s in map.Stacks.Values)
        {
            if (!map.Players.ContainsKey(s.Owner)) found.Add(new("owner", s.Id, $"хозяин {s.Owner} не игрок карты"));
            if (s.UnitIds.Count == 0) found.Add(new("stack_empty", s.Id, "в отряде нет юнитов"));
            else if (!s.UnitIds.Contains(s.LeaderId, StringComparer.OrdinalIgnoreCase))
                found.Add(new("leader", s.Id, $"лидер {s.LeaderId} не в отряде"));
            // LEADR_ALIV — ровно «здоровье лидера > 0»: снятый признак при живом лидере урезает список тактики ИИ (без атак, руин,
            // торговцев) — так было после зелья воскрешения до 14.10.
            else if (map.Units.TryGetValue(s.LeaderId, out var leader) && (leader.Hp > 0) != s.LeaderAlive)
                found.Add(new("leader_alive", s.Id, $"признак «лидер жив» {s.LeaderAlive}, а у лидера {s.LeaderId} здоровье {leader.Hp}"));
            if (!map.Contains(s.X, s.Y))
            {
                found.Add(new("off_map", s.Id, $"клетка ({s.X},{s.Y}) вне карты {map.Size}x{map.Size}"));
                continue;
            }
            if (!(map.Plan.TryGetValue((s.X, s.Y), out var here) && here.Contains(s.Id, StringComparer.OrdinalIgnoreCase)))
                found.Add(new("plan", s.Id, $"нет в плане клетки ({s.X},{s.Y}){(s.Inside.Length > 0 ? " внутри " + s.Inside : "")}"));
            if (s.Inside.Length == 0) continue;
            if (!sites.TryGetValue(s.Inside, out var site))
                found.Add(new("inside", s.Id, $"внутри {s.Inside}, а такого объекта нет"));
            else if (site.Kind is SiteKind.Capital or SiteKind.Village && !site.StackId.Equals(s.Id, StringComparison.OrdinalIgnoreCase))
                found.Add(new("inside", s.Id, $"внутри {site.Id}, а визитёр там {(site.StackId.Length > 0 ? site.StackId : "-")}"));
        }
        foreach (var (cell, ids) in map.Plan)
            foreach (string id in ids.Where(IsStackId))
                if (!map.Stacks.TryGetValue(id, out var s)) found.Add(new("plan", id, $"в плане клетки ({cell.X},{cell.Y}), а отряда нет"));
                else if ((s.X, s.Y) != cell) found.Add(new("plan", id, $"в плане клетки ({cell.X},{cell.Y}), а стоит в ({s.X},{s.Y})"));
    }

    /// <summary>Id отряда — класс <c>KC</c> (<c>S001KC0003</c>).</summary>
    private static bool IsStackId(string id) => id.Length >= 6 && id.AsSpan(4, 2).Equals("KC", StringComparison.OrdinalIgnoreCase);

    /// <summary>Укрепление: хозяин — игрок карты; визитёр — существующий отряд, который считает себя внутри.</summary>
    private static void Sites(MapState map, List<Violation> found)
    {
        foreach (var site in map.Sites.Where(s => !s.Gone))
        {
            if (site.Owner.Length > 0 && !map.Players.ContainsKey(site.Owner))
                found.Add(new("owner", site.Id, $"хозяин {site.Owner} не игрок карты"));
            if (site.StackId.Length == 0) continue;
            if (!map.Stacks.TryGetValue(site.StackId, out var s)) found.Add(new("inside", site.Id, $"визитёр {site.StackId}, а отряда нет"));
            else if (!s.Inside.Equals(site.Id, StringComparison.OrdinalIgnoreCase))
                found.Add(new("inside", site.Id, $"визитёр {s.Id}, а он {(s.Inside.Length > 0 ? "внутри " + s.Inside : "на карте")}"));
        }
    }

    /// <summary>Казна и казна магии игрока — каждая валюта в [0, 9999] (<see cref="Economy.CurrencyMax"/>: <c>Bank</c> движка
    /// зажимает каждое сложение и вычитание).</summary>
    private static void Players(MapState map, List<Violation> found)
    {
        foreach (var p in map.Players.Values)
        {
            BankRange(p.Id, "казна", p.Bank, found);
            BankRange(p.Id, "казна магии", p.SpellBank, found);
        }
    }

    private static void BankRange(string id, string what, Bank bank, List<Violation> found)
    {
        long[] values = [bank.Gold, bank.Infernal, bank.Life, bank.Death, bank.Runic, bank.Grove];
        if (values.Any(v => v < 0 || v > Economy.CurrencyMax))
            found.Add(new("bank", id, $"{what} {string.Join(":", values)} вне [0, {Economy.CurrencyMax}]"));
    }

    // ------------------------------------------------------------------ предметы и отношения

    /// <summary>Предметы отрядов (инвентарь и снаряжение), мешков, складов и руин — экземпляры с типом (<see cref="MapState.ItemTypes"/>),
    /// тип есть в правилах; снаряжение — из инвентаря; заряды талисманов — только у экземпляров-талисманов и не меньше нуля.</summary>
    private static void Items(MapState map, GameBook? book, List<Violation> found)
    {
        void Known(string owner, string item)
        {
            if (!map.ItemTypes.TryGetValue(item, out string? type)) found.Add(new("item", item, $"у {owner}: экземпляра нет в списке предметов"));
            else if (book is not null && book.Items.Count > 0 && !book.Items.ContainsKey(type))
                found.Add(new("item", item, $"у {owner}: тип {type} не в правилах"));
        }
        foreach (var s in map.Stacks.Values)
        {
            foreach (string item in s.Items) Known(s.Id, item);
            foreach (string item in s.Equipment.Where(e => e.Length > 0))
                if (!s.Items.Contains(item, StringComparer.OrdinalIgnoreCase)) found.Add(new("item", item, $"снаряжение {s.Id} не в его инвентаре"));
        }
        foreach (var site in map.Sites.Where(s => !s.Gone))
        {
            foreach (string item in site.Items) Known(site.Id, item);
            if (site.Kind == SiteKind.Ruin && site.ItemId.Length > 0) RuinItem(site, book, found);
        }
        foreach (var (item, charges) in map.TalismanCharges)
        {
            if (charges < 0) found.Add(new("talisman", item, $"зарядов {charges}"));
            if (!map.ItemTypes.TryGetValue(item, out string? type)) continue;   // запись пережила предмет — движок так же держит
            if (book?.Items.GetValueOrDefault(type) is { } it && it.Category != ItemCategory.Talisman)
                found.Add(new("talisman", item, $"заряды у {type} ({it.Category}), а не у талисмана"));
        }
    }

    /// <summary>Предмет руины (<c>MidRuin.ITEM</c>) — тип из правил, как у всех 86 предметов руин штатных карт. Экземпляр (<c>S…IM…</c>)
    /// движок при грабеже не создаёт (<c>CEffectLootRuin</c> → <c>0x5e732f</c>, <c>item_create</c> ret false у <c>0x43915f</c>), а после
    /// грабежа человеком Discipl2.exe вылетел (port-synth-8-edge, руина RL, журнал 20261016e-av) — такая карта недопустима.</summary>
    private static void RuinItem(MapSite site, GameBook? book, List<Violation> found)
    {
        if (!IsTypeId(site.ItemId))
            found.Add(new("item", site.ItemId, $"у руины {site.Id}: экземпляр вместо типа предмета (G000IG…)"));
        else if (book is not null && book.Items.Count > 0 && !book.Items.ContainsKey(site.ItemId))
            found.Add(new("item", site.ItemId, $"у руины {site.Id}: тип не в правилах"));
    }

    /// <summary>Id типа (<c>G000IG…</c>), а не экземпляра (<c>S001IM…</c>): у руины предмет — тип.</summary>
    private static bool IsTypeId(string id) => id.StartsWith("G", StringComparison.OrdinalIgnoreCase);

    /// <summary>Отношение пары рас — текущее и прошлое в 0…100.</summary>
    private static void Relations(MapState map, List<Violation> found)
    {
        foreach (var r in map.Relations)
            if (r.Current > RelationMax || r.Previous > RelationMax)
                found.Add(new("relation", $"{r.Race1}-{r.Race2}", $"отношение {r.Current} (прошлое {r.Previous}) вне 0…{RelationMax}"));
    }
}
