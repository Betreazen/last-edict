using LastEdict.Data.Map;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Play;

/// <summary>Эффекты событий, меняющие карту (<c>docs/scenario-events.md</c> §3а): создание отряда по шаблону, литьё, ландмарки,
/// туман, смена владельца, горы, перемещение, предметы, бой, земля, дипломатия. Ранги — там же.</summary>
public sealed partial class GameSession
{
    /// <summary><c>L_CREATE_STACK</c> (<c>CEffectCreateStack</c> <c>0x436c9a</c>): туман владельцу шаблона вокруг центра локации
    /// (<c>CEffectFog</c> первым эффектом, <c>0x43bd74</c>: <c>±scout</c> лидера без +1, квадрат без угловых клеток, как обычный обзор
    /// <c>0x5d4dfc</c>; нейтралу — нет), клетка <see cref="SummonCell"/>, отряд из шаблона. Новый отряд — триггер следующей
    /// проверки. Нет шаблона, локации или клетки — отряда нет (туман всё равно открыт).</summary>
    private bool CreateStack(CreateStackEffect c)
    {
        var book = RequireBook();
        if (!State.Events.Templates.TryGetValue(c.TemplateId, out var t) || State.Events.Zones.GetValueOrDefault(c.LocationId) is not { } zone)
            return true;
        if (!book.Rules.Units.TryGetValue(t.LeaderType, out var leaderType)) return false;
        if (!IsNeutral(t.Owner) && Map.Players.TryGetValue(t.Owner, out var owner) && Map.Fog.TryGetValue(owner.FogId, out var fog))
            LastEdict.Rules.Map.Fog.Reveal(fog, zone.X, zone.Y, book.ScoutByType.GetValueOrDefault(t.LeaderType), LastEdict.Rules.Map.Fog.DefaultShape);
        if (SummonCell(zone.X, zone.Y) is not { } at) return true;

        var map = Map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        string prefix = map.Stacks.Keys.FirstOrDefault() ?? map.Units.Keys.FirstOrDefault() ?? "S000";
        var idNext = map.IdNext;
        string stackId = NewId(ref idNext, map.Stacks.Keys, prefix, "KC");
        MapUnit NewUnit(string type, int level)
        {
            string id = NewId(ref idNext, units.Keys, prefix, "UN");
            var grown = book.Grown(type, level);
            var unit = new MapUnit { Id = id, TypeId = type, Level = grown?.Level ?? level, Hp = grown?.Type.HitPoints ?? 1 };
            units[id] = unit;
            return unit;
        }
        // Лидер — в клетке 2 (большой — 2 и 3), затем в первую клетку шаблона, где стоит лидер (тип и уровень лидера).
        var leader = NewUnit(t.LeaderType, t.LeaderLevel);
        List<string> ids = [leader.Id];
        int[] slots = Formation.EmptySlots();
        bool IsLeader(int slot) => t.Slots.Count > slot && t.Slots[slot] >= 0
                                   && t.Units[t.Slots[slot]].TypeId.Equals(t.LeaderType, StringComparison.OrdinalIgnoreCase)
                                   && t.Units[t.Slots[slot]].Level == t.LeaderLevel;
        int leaderCell = Enumerable.Range(0, 6).FirstOrDefault(IsLeader, -1);
        if (leaderCell < 0) leaderCell = 2;
        void Put(int cell, int index, bool big)
        {
            slots[cell] = index;
            if (big && Formation.IsFront(cell)) slots[Formation.Pair(cell)] = index;
        }
        Put(leaderCell, 0, !leaderType.SizeSmall);
        for (int p = 0; p < 6 && p < t.Slots.Count; p++)
        {
            if (t.Slots[p] < 0 || slots[p] >= 0 || IsLeader(p)) continue;
            var u = t.Units[t.Slots[p]];
            if (!book.Rules.Units.TryGetValue(u.TypeId, out var type)) continue;
            bool big = !type.SizeSmall;
            if (big && (p % 2 != 0 || slots[p + 1] >= 0)) continue;
            ids.Add(NewUnit(u.TypeId, u.Level).Id);
            Put(p, ids.Count - 1, big);
        }
        // Модификаторы шаблона по клеткам — юниту в клетке, здоровье до полного с ними.
        foreach (var m in t.Modifiers)
            if (m.Cell is >= 0 and < 6 && slots[m.Cell] >= 0 && units.TryGetValue(ids[slots[m.Cell]], out var u))
                units[u.Id] = u with { Modifiers = [.. u.Modifiers, m.ModifierId] };
        foreach (string id in ids) units[id] = units[id] with { Hp = HpMax(units[id]) };

        var stack = new MapStack
        {
            Id = stackId, Owner = t.Owner, Subrace = t.Subrace, LeaderId = leader.Id, LeaderAlive = true, X = at.X, Y = at.Y, Inside = "",
            UnitIds = ids, Slots = slots, AiOrder = (int)StackOrder.Normal, TemplateId = t.Id.ToUpperInvariant(),
            Order = (int)(IsNeutral(t.Owner) ? StackOrder.Stand : StackOrder.Normal),
            // AIPRIORITY шаблона (+0x58): 0x436e64 → 0x5e80cc, визитор vft 0x6f08f4 пускает 0…6 (0x60ecbb: [0x6e8468, 0x6e846c) = [0, 7)).
            // Карта 17 (PRI, 19.10): шаблон S143TM0001 с приоритетом 6 — у созданного S143KC0015 в сохранении оригинала 6, порт ставил 3.
            AiPriority = t.AiPriority is >= 0 and < 7 ? t.AiPriority : AiTacticScore.DefaultPriority,
        };
        stack = stack with { Move = Allowance(With(map, units: units), stack) };
        string orderTarget = t.OrderTarget.Equals("G000000000", StringComparison.OrdinalIgnoreCase) ? "" : t.OrderTarget;
        stack = OrderAllowed(stack, (StackOrder)t.Order, orderTarget, map)
            ? stack with { Order = t.Order, OrderTarget = orderTarget }
            : stack with
            {
                Order = (int)((StackOrder)t.Order == StackOrder.AttackStack ? StackOrder.Bezerk
                              : IsNeutral(t.Owner) ? StackOrder.Roam : StackOrder.Normal),
                OrderTarget = "",
            };
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack;
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[at] = [.. plan.GetValueOrDefault(at) ?? [], stackId];
        var look = new StackLook(stackId, at.X, at.Y, t.Owner, t.Subrace, leaderType.Id, LeaderAlive: true, AnyAlive: true,
                                 Facing: t.UseFacing ? t.Facing : 0, Inside: "", Invisible: false, LeaderName: t.Name.Trim());
        State = State with { Looks = Retold(Looks, With(map, units: units, stacks: stacks, plan: plan, idNext: idNext), [.. Looks.Stacks, look]) };
        Observer?.Happened(new TurnSight(TurnSightKind.Created, t.Owner, at.X, at.Y, stackId));   // показ (сессия Г)
        _triggered.Add(stackId);
        return true;
    }

    /// <summary>Допустим ли приказ с целью (<c>ChangeStackOrder::canApply</c> <c>0x60ed92</c>): нейтралу «обычный» нельзя;
    /// атака — чужой отряд другой подрасы; защита отряда — свой подрасы; охрана города — укрепление; к локации — локация;
    /// помощь — пусто или свой отряд с «обычными» приказами; защита города — пусто или укрепление; прочим — цель пуста. У подрасы
    /// <c>L_NEUTRAL</c> (<c>[0x837b00]</c>) защита отряда и охрана города нельзя, а атаковать можно и свою подрасу (<c>0x60ef1f</c>).</summary>
    private bool OrderAllowed(MapStack stack, StackOrder order, string target, MapState map)
    {
        string kind = target.Length >= 6 ? target.Substring(4, 2).ToUpperInvariant() : "";
        var other = map.Stacks.GetValueOrDefault(target);
        bool neutral = NeutralSubrace(stack);
        return order switch
        {
            StackOrder.Normal => !IsNeutral(stack.Owner) && target.Length == 0,
            StackOrder.AttackStack => kind == "KC" && !Same(target, stack.Id) && other is not null && (neutral || !Same(other.Subrace, stack.Subrace)),
            StackOrder.DefendStack => !neutral && kind == "KC" && !Same(target, stack.Id) && other is not null && Same(other.Subrace, stack.Subrace),
            StackOrder.SecureCity => !neutral && kind == "FT",
            StackOrder.MoveToLocation or StackOrder.DefendLocation => kind == "LO",
            StackOrder.Assist => target.Length == 0 || other is not null && Same(other.Subrace, stack.Subrace)
                                 && other.Order == (int)StackOrder.Normal && other.AiOrder == (int)StackOrder.Normal,
            StackOrder.DefendCity => target.Length == 0 || kind == "FT",
            _ => target.Length == 0,
        };
    }

    /// <summary>Подраса отряда — категории <c>L_NEUTRAL</c> (<c>LSubRace</c> 5, <c>[0x837b00]</c>).</summary>
    private bool NeutralSubrace(MapStack stack) => Map.Subraces.TryGetValue(stack.Subrace, out int category) && category == NeutralSubraceCategory;

    private const int NeutralSubraceCategory = 5;

    /// <summary><c>L_CAST_SPELL</c>/<c>L_CAST_SPELL_MAP</c> (<c>0x4416b6</c> → <c>0x443600</c>): заклинатель — для призыва и
    /// площади 999 (<c>0x5dc173</c>; туман — тоже) <c>ID_PLAYER1</c> или владелец триггерера (нейтрал — ничего), для прочих — пусто;
    /// маны, известности и записи литья нет. Цель — хоть один годный отряд (<see cref="EventTargetOk"/>). Действие — общее с
    /// литьём: урон и лечение (<see cref="Strike"/>), призыв (<see cref="Summon"/>), усиление и ослабление (<see cref="ModifySpell"/>),
    /// ход (<see cref="RestoreMoveSpell"/>), невидимость (<see cref="HideSpell"/>), развеивание (<see cref="UnfogSpell"/>), туман
    /// (<see cref="FogSpell"/>, с 13.10). ponytail: земля, жезл, обереги — не перенесены (в ванильных событиях их нет).</summary>
    private bool CastByEvent(string spellId, string playerId, int x, int y, MapStack? triggerer, bool onTriggerer)
    {
        var book = RequireBook();
        if (!book.Spells.TryGetValue(spellId, out var spell)) return false;
        if (onTriggerer && (triggerer is null || spell.Area == WholeMap)) return true;
        string caster = "";
        if (spell.Category is SpellCategory.Summon or SpellCategory.Unfog or SpellCategory.Fog || spell.Area == WholeMap)
        {
            // 0x43bfe4 (ранг 1): заклинатель — ID_PLAYER1; пусто — владелец триггерера, и только в этой запасной ветке нейтрал — ничего.
            bool given = playerId.Length > 0 && !playerId.Equals("G000000000", StringComparison.OrdinalIgnoreCase);
            caster = given ? playerId : onTriggerer ? triggerer!.Owner : "";
            if (!given && onTriggerer && IsNeutral(caster)) return true;
        }
        switch (spell.Category)
        {
            case SpellCategory.Summon:
                if (caster.Length == 0 || SummonCell(x, y, spell.SummonUnitId) is not { } cell) return true;
                Summon(caster, spell.SummonUnitId, cell, null);
                return true;
            case SpellCategory.Attack or SpellCategory.Heal:
                if (spell.Area != WholeMap && TargetsOf(spell, x, y).All(s => !EventTargetOk(spell, s, caster))) return true;
                Strike(caster, spell, x, y);
                return true;
            case SpellCategory.Boost or SpellCategory.Lower or SpellCategory.RestoreMove or SpellCategory.Invisibility:
                if (spell.Area != WholeMap && TargetsOf(spell, x, y).All(s => !EventTargetOk(spell, s, caster))) return true;
                if (spell.Category == SpellCategory.RestoreMove) RestoreMoveSpell(caster, spell, x, y);
                else if (spell.Category == SpellCategory.Invisibility) HideSpell(caster, spell, x, y);
                else ModifySpell(caster, spell, x, y);
                return true;
            case SpellCategory.Unfog:
                if (caster.Length > 0) UnfogSpell(caster, spell, x, y);
                return true;
            case SpellCategory.Fog:
                if (caster.Length > 0) FogSpell(caster, spell, x, y);
                return true;
            default:
                return false;
        }
    }

    private IEnumerable<MapStack> TargetsOf(Spell spell, int x, int y) => Targets("", spell, x, y, check: true).Select(t => t.Stack);

    /// <summary>Годен ли отряд целью литья события (<c>0x5dac42</c>): невидимый — нет; при заданном заклинателе удар и
    /// ослабление — не свой и не союзник, остальное — свой; отряд в крепости — нет; удар — есть неиммунный; лечение — есть кого.</summary>
    private bool EventTargetOk(Spell spell, MapStack stack, string caster)
    {
        if (Invisible(stack.Id) || stack.Inside.Length > 0) return false;
        if (caster.Length > 0)
        {
            bool own = Same(stack.Owner, caster);
            if (spell.Category is SpellCategory.Attack or SpellCategory.Lower ? own || AiTactics.Allied(this, caster, stack.Owner) : !own)
                return false;
        }
        var living = stack.UnitIds.Select(id => Map.Units.GetValueOrDefault(id)).OfType<MapUnit>().Where(u => u.Hp > 0).ToList();
        return spell.Category switch
        {
            SpellCategory.Attack => living.Any(u => !Immune(u, spell.DamageSource)),
            SpellCategory.Heal => living.Sum(u => HpMax(u) - u.Hp) > 0,
            _ => true,
        };
    }

    /// <summary><c>L_REMOVE_LMARK</c> (<c>CVisitorEraseLandmark</c>): ландмарк уходит из плана и с карты; земля та же.</summary>
    private bool RemoveLandmark(RemoveLandmarkEffect r)
    {
        if (!Map.Objects.ContainsKey(r.LandmarkId)) return true;
        var objects = Map.Objects.Where(o => !Same(o.Key, r.LandmarkId)).ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
        var map = WithObjects(With(Map, plan: PlanWithout(Map, r.LandmarkId)), objects);
        State = State with
        {
            Looks = Retold(Looks, map, Looks.Stacks) with { Landmarks = [.. Looks.Landmarks.Where(l => !Same(l.Id, r.LandmarkId))] },
        };
        return true;
    }

    /// <summary><c>L_CHANGE_LANDMARK</c> (<c>CVisitorChangeLandmark</c>, <c>canApply</c> <c>0x60f6b6</c>, ранг 1): ландмарк есть, новый тип
    /// есть в таблице и того же размера — иначе ничего.</summary>
    private bool ChangeLandmark(ChangeLandmarkEffect c)
    {
        if (!Map.Objects.TryGetValue(c.LandmarkId, out var obj) || !Art.HasLandmark(c.LandmarkType)) return true;
        if (Art.LandmarkFootprint(obj.TypeId) != Art.LandmarkFootprint(c.LandmarkType)) return true;
        var objects = Map.Objects.ToDictionary(o => o.Key, o => o.Value, StringComparer.OrdinalIgnoreCase);
        objects[obj.Id] = new MapObject { Id = obj.Id, Kind = obj.Kind, X = obj.X, Y = obj.Y, Resource = obj.Resource, TypeId = c.LandmarkType };
        var map = WithObjects(Map, objects);
        State = State with
        {
            Looks = Retold(Looks, map, Looks.Stacks) with
            {
                Landmarks = [.. Looks.Landmarks.Select(l => Same(l.Id, obj.Id) ? l with { Type = c.LandmarkType } : l)],
            },
        };
        return true;
    }

    /// <summary><c>L_UNFOG</c> (<c>CEffectFog</c> <c>0x438314</c> → <c>0x5d5230</c>, ранг 1): каждому игроку списка, кроме нейтрала, квадрат
    /// <c>±r</c> без четырёх угловых клеток (<c>0x5d4dfc</c>, форма обычного обзора) вокруг центра локации,
    /// <c>r = (NUMVALUE == −1 ? радиус локации : NUMVALUE) + 1</c>; открыть — или закрыть, и тогда снова открыть обзор его крепостей
    /// и отрядов (<c>0x5d4c6e</c>, <c>0x5d5030</c>).</summary>
    private bool Unfog(UnfogEffect u)
    {
        if (State.Events.Zones.GetValueOrDefault(u.LocationId) is not { } zone) return true;
        int r = (u.Value == -1 ? zone.Radius : u.Value) + 1;
        foreach (string playerId in u.Entries.Select(e => e.PlayerId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsNeutral(playerId) || !Map.Players.TryGetValue(playerId, out var p) || !Map.Fog.TryGetValue(p.FogId, out var fog)) continue;
            for (int x = zone.X - r; x <= zone.X + r; x++)
                for (int y = zone.Y - r; y <= zone.Y + r; y++)
                    if (Map.Contains(x, y) && Fog.InSight(x - zone.X, y - zone.Y, r, Fog.DefaultShape)) fog[x, y] = !u.Enable;   // true — скрыто
            if (!u.Enable) Reveal(Map, playerId);
        }
        return true;
    }

    /// <summary><c>L_CHANGE_OWNER</c> (<c>CEffectChangeOwner</c> <c>0x4366e2</c>): <c>ID_PLAYER1</c> — подраса, владелец — её игрок;
    /// пусто — владелец и подраса триггерера. Отряду: туман новому владельцу <c>±scout</c>, выход из крепости (<see cref="ExitCell"/>;
    /// некуда — остаётся внутри), facing 7, приказ «обычный» (нейтралу «стоять»), приказ ИИ «обычный».</summary>
    private bool ChangeOwner(ChangeOwnerEffect c, MapStack? triggerer)
    {
        string owner, subrace;
        if (c.PlayerId.Length == 0 || c.PlayerId.Equals("G000000000", StringComparison.OrdinalIgnoreCase))
        {
            if (triggerer is null) return true;
            (owner, subrace) = (triggerer.Owner, triggerer.Subrace);
        }
        else if (State.Events.SubraceOwners.TryGetValue(c.PlayerId, out var player)) (owner, subrace) = (player, c.PlayerId);
        else return true;
        foreach (var target in Targets(c.StackId, c.FirstOnly))
        {
            var stack = Map.Stacks[target.Id];
            if (stack.Inside.Length > 0 && ExitCell(stack) is { } exit) stack = LeaveFort(stack, exit);
            var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            stacks[stack.Id] = stack with
            {
                Owner = owner, Subrace = subrace, OrderTarget = "", AiOrder = (int)StackOrder.Normal, AiOrderTarget = "",
                Order = (int)(IsNeutral(owner) ? StackOrder.Stand : StackOrder.Normal),
            };
            // Чужие «защищать отряд» на этот отряд сброшены (CVisitorChangeStackOwner 0x60d6a5 → 0x60d3ea, GameSession.Capture.cs).
            var map = StackOwnerChanged(With(Map, stacks: stacks), stack.Id);
            if (!IsNeutral(owner) && map.Players.TryGetValue(owner, out var p) && map.Fog.TryGetValue(p.FogId, out var fog))
                LastEdict.Rules.Map.Fog.Reveal(fog, stack.X, stack.Y, ScoutRange(map, stacks[stack.Id]), LastEdict.Rules.Map.Fog.DefaultShape);
            State = State with
            {
                Looks = Retold(Looks, map, [.. Looks.Stacks.Select(s => Same(s.Id, stack.Id)
                    ? s with { Owner = owner, Subrace = subrace, Facing = 7, X = stack.X, Y = stack.Y, Inside = stack.Inside } : s)]),
            };
            Observer?.Happened(new TurnSight(TurnSightKind.OwnerChanged, owner, stack.X, stack.Y, stack.Id));   // показ (сессия Г)
        }
        return true;
    }

    /// <summary>Клетка выхода из крепости (<c>0x436853</c> → <c>0x5e9527</c>): вход E плюс сдвиги <see cref="ExitCells"/> по порядку —
    /// первая, где отряд может стоять; нет — null. ponytail: проверка пути от E (<c>0x5cc6da</c>, режим 2) не делается — соседняя
    /// проходимая клетка достижима (как у <see cref="LeaveFort(string)"/>).</summary>
    private (int X, int Y)? ExitCell(MapStack stack)
    {
        if (PathFinder.EntranceOf(Map, stack.Inside) is not { } gate) return null;
        var opt = MoveOptionsOf(stack);
        foreach (var (dx, dy) in ExitCells)
        {
            int x = gate.X + dx, y = gate.Y + dy;
            if (Map.Contains(x, y) && PathFinder.CellCost(Map, stack, x, y, opt, MovementRules.Vanilla) != Movement.Forbidden) return (x, y);
        }
        return null;
    }

    /// <summary>Отряд выходит из крепости в клетку <paramref name="at"/>: визитёра у крепости нет, отряд — в плане клетки.</summary>
    private MapStack LeaveFort(MapStack stack, (int X, int Y) at)
    {
        var moved = stack with { X = at.X, Y = at.Y, Inside = "" };
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = moved;
        var plan = Map.Plan.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value.Where(id => !Same(id, stack.Id))]);
        plan[at] = [.. plan.GetValueOrDefault(at) ?? [], stack.Id];
        var sites = Map.Sites.Select(s => Same(s.StackId, stack.Id) ? s with { StackId = "" } : s).ToList();
        var map = With(Map, stacks: stacks, plan: plan, sites: sites);
        State = State with
        {
            Looks = Retold(Looks, map, [.. Looks.Stacks.Select(s => Same(s.Id, stack.Id) ? s with { X = at.X, Y = at.Y, Inside = "" } : s)])
                    with { Forts = FortLooks(Looks.Forts, map) },
        };
        return moved;
    }

    /// <summary><c>L_REMOVE_MOUNTAIN</c> (<c>0x439a3d</c>): в квадрате <c>±R</c> локации горы → равнина с нейтральной местностью;
    /// объекты гор, задевающие зону, убираются, горы их охвата вне зоны перекладываются новыми объектами (<see cref="RelayMountains"/>).
    /// Гор в зоне нет — ничего. Ранг 1. Показ (сессия Г, <see cref="TurnSightKind.MountainsRemoved"/>) — если сняты или переложены объекты
    /// гор: сообщение сноса <c>0x478346</c> движок шлёт только тогда (<c>0x439b85…0x439b9b</c>).</summary>
    private bool RemoveMountains(RemoveMountainsEffect m, string playerId)
    {
        if (State.Events.Zones.GetValueOrDefault(m.LocationId) is not { } zone) return true;
        _nextMountainId ??= Looks.Mountains.Select(g => g.Id + 1).DefaultIfEmpty(0).Max();
        Tile[,]? tiles = null;
        List<(int X, int Y)> cells = [];
        int neutral = Book?.Economy.Races.Values.FirstOrDefault(r => r.Neutral)?.Terrain ?? LegacyRaces.Terrain(LegacyRaces.Neutral);
        for (int x = zone.X - zone.Radius; x <= zone.X + zone.Radius; x++)
            for (int y = zone.Y - zone.Radius; y <= zone.Y + zone.Radius; y++)
            {
                if (!Map.Contains(x, y) || Map.Tiles[x, y].Ground != Ground.Mountain) continue;
                tiles ??= (Tile[,])Map.Tiles.Clone();
                tiles[x, y] = new Tile((byte)((tiles[x, y].Raw & ~0b111111) | ((int)Ground.Plain << 3) | neutral));
                cells.Add((x, y));
            }
        if (tiles is null) return true;
        var owners = Book is { } book ? LandOwnership.Retag(Map, tiles, cells, null, book.Economy) : null;   // нейтральная — без хозяина
        bool Touches(MountainLook g) => g.X <= zone.X + zone.Radius && g.X + g.Width - 1 >= zone.X - zone.Radius
                                        && g.Y <= zone.Y + zone.Radius && g.Y + g.Height - 1 >= zone.Y - zone.Radius;
        var removed = Looks.Mountains.Where(Touches).OrderBy(g => g.Id).ToList();
        var kept = Looks.Mountains.Where(g => !Touches(g)).ToList();
        var relaid = RelayMountains(removed, kept, tiles);
        State = State with
        {
            Looks = Retold(Looks, WithTiles(Map, tiles, owners), Looks.Stacks) with { Mountains = [.. kept, .. relaid] },
        };
        if (removed.Count > 0 || relaid.Count > 0)
            Observer?.Happened(new TurnSight(TurnSightKind.MountainsRemoved, playerId, zone.X, zone.Y, zone.Id) { Cells = cells });
        return true;
    }

    /// <summary><c>L_MOVE_STACK</c>/<c>L_MOVE_BESIDE</c> (<c>CEffectMove</c> <c>0x439e84</c>): обычный путь без учёта очков хода,
    /// отряд выходит из крепости и встаёт в конец пути; очки хода не меняются, туман владельцу по точкам пути. Пути нет — ничего.
    /// Рядом (<paramref name="beside"/>): клетка триггерера непроходима, поиск снимает её с конца (<c>docs/map-path.md</c> §1) —
    /// отряд встаёт на предпоследнюю точку пути (сосед, через которого путь дешевле); уже рядом — не двигается (<c>0x43a03f</c>).
    /// Путь не обрезается у врага старта («идти» = 0, <c>0x5cd4f5</c>): разбор допускал обрезку (ранг 2), но живой случай корпуса
    /// её опровергает — <c>EV0018</c> (20260919-human, день 3): эльфы <c>KC000a</c> стояли рядом с отрядом Легионов, путь шёл вдоль
    /// него, а движок поставил их в центр области (90,23; сохранение t04-010).</summary>
    private bool MoveByEvent(string stackId, int x, int y, bool beside = false)
    {
        if (!Map.Stacks.TryGetValue(stackId, out var stack)) return true;
        var path = PathFinder.Find(Map, stack, x, y, MoveOptionsOf(stack), MovementRules.Vanilla, int.MaxValue / 4);
        var points = path.Points.Select(p => (p.X, p.Y)).Where(p => p != (stack.X, stack.Y)).ToList();
        if (beside && points.Count > 0 && points[^1] == (x, y)) points.RemoveAt(points.Count - 1);
        while (points.Count > 0 && Map.Plan.TryGetValue(points[^1], out var ids) && ids.Any(id => Map.Stacks.ContainsKey(id) && !Same(id, stackId)))
            points.RemoveAt(points.Count - 1);
        if (points.Count == 0) return true;
        if (stack.Inside.Length > 0) stack = LeaveFort(stack, points[0]);
        var before = points.Count > 1 ? points[^2] : (stack.X, stack.Y);
        var end = points[^1];
        Relocate(stack.Id, end, PathFinder.Facing(new PathPoint(before.Item1, before.Item2, 0, 0), new PathPoint(end.X, end.Y, 0, 0)));
        var map = Map;
        if (map.Players.TryGetValue(stack.Owner, out var p) && !IsNeutral(stack.Owner) && map.Fog.TryGetValue(p.FogId, out var fog))
            foreach (var point in points) LastEdict.Rules.Map.Fog.Reveal(fog, point.Item1, point.Item2, ScoutRange(map, map.Stacks[stackId]), LastEdict.Rules.Map.Fog.DefaultShape);
        return true;
    }

    /// <summary>Отряд переставлен в клетку (<c>MoveStack</c> эффекта: план убрать → позиция → план), очки хода те же, событий нет.</summary>
    private void Relocate(string stackId, (int X, int Y) at, int facing)
    {
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stacks[stackId] with { X = at.X, Y = at.Y, Inside = "" };
        var plan = Map.Plan.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)[.. p.Value.Where(id => !Same(id, stackId))]);
        plan[at] = [.. plan.GetValueOrDefault(at) ?? [], stackId];
        State = State with
        {
            Looks = Retold(Looks, With(Map, stacks: stacks, plan: plan),
                           [.. Looks.Stacks.Select(s => Same(s.Id, stackId) ? s with { X = at.X, Y = at.Y, Facing = facing, Inside = "" } : s)]),
        };
    }

    /// <summary><c>L_GIVE_ITEM</c> (<c>0x43ce32</c> → <c>0x438616</c>): получатель — <paramref name="recipient"/> (триггерер, иначе
    /// отряд в ходу); его нет или <c>GIVETO</c> 1 — столица кандидата, а если столицы нет — прежний получатель; никого — ничего. Новый
    /// экземпляр предмета в инвентарь; затем сообщение 44 хозяину получателя (<c>0x47ac42</c>) — окно полученного
    /// (<see cref="TurnSightKind.ItemsGiven"/>; клетка — получателя, объект — событие <paramref name="eventId"/>).</summary>
    private bool GiveItem(GiveItemEffect g, string candidate, MapStack? recipient, string eventId)
    {
        var capital = recipient is null || g.GiveTo == 1
            ? Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && Same(s.Owner, candidate))
            : null;
        if (capital is null && recipient is null) return true;
        var ids = Map.IdNext;
        string instance = NewId(ref ids, Map.ItemTypes.Keys, Map.Stacks.Keys.FirstOrDefault() ?? "S000", "IM");
        var types = Map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        types[instance] = g.ItemType;
        if (capital is null && recipient is { } triggerer)
        {
            var stacks = Map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            stacks[triggerer.Id] = stacks[triggerer.Id] with { Items = [.. stacks[triggerer.Id].Items, instance] };
            State = State with { Looks = Retold(Looks, With(Map, stacks: stacks, itemTypes: types, idNext: ids), Looks.Stacks) };
            ChargeNewTalismans([instance]);
            Given(TurnSightKind.ItemsGiven, triggerer.Owner, triggerer.X, triggerer.Y, eventId, [instance]);
            return true;
        }
        var sites = Map.Sites.Select(s => ReferenceEquals(s, capital) ? s with { Items = [.. s.Items, instance] } : s).ToList();
        State = State with { Looks = Retold(Looks, With(Map, sites: sites, itemTypes: types, idNext: ids), Looks.Stacks) };
        Given(TurnSightKind.ItemsGiven, capital!.Owner, capital.X, capital.Y, eventId, [instance]);
        return true;
    }

    /// <summary><c>L_DESTROY_ITEM</c> (<c>0x43d933</c>): при <c>TRIG_ONLY</c> — все экземпляры типа у триггерера (снаряжение и
    /// инвентарь), иначе — все на карте (отряды, укрепления, мешки).</summary>
    private bool DestroyItem(DestroyItemEffect d, MapStack? triggerer)
    {
        if (d.TriggererOnly && triggerer is null) return true;
        bool Gone(string instance) => instance.Length > 0 && Map.ItemTypes.TryGetValue(instance, out var type)
                                      && type.Equals(d.ItemType, StringComparison.OrdinalIgnoreCase);
        var stacks = Map.Stacks.ToDictionary(p => p.Key, p => !d.TriggererOnly || Same(p.Key, triggerer!.Id)
            ? p.Value with { Items = [.. p.Value.Items.Where(i => !Gone(i))], Equipment = [.. p.Value.Equipment.Select(i => Gone(i) ? "" : i)] }
            : p.Value, StringComparer.OrdinalIgnoreCase);
        var sites = d.TriggererOnly ? Map.Sites : [.. Map.Sites.Select(s => s with { Items = [.. s.Items.Where(i => !Gone(i))] })];
        // CVisitorDestroyItem (0x610a7a): у талисмана снимается и запись зарядов (0x5eba93).
        var gone = Map.Stacks.Values.SelectMany(s => s.Items.Concat(s.Equipment)).Concat(Map.Sites.SelectMany(s => s.Items))
                      .Where(i => Gone(i) && !Kept(i)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Kept(string instance) => stacks.Values.Any(s => s.Items.Contains(instance) || s.Equipment.Contains(instance))
                                      || sites.Any(s => s.Items.Contains(instance));
        var charges = Map.TalismanCharges.Where(c => !gone.Contains(c.Key)).ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);
        State = State with { Looks = Retold(Looks, With(Map, stacks: stacks, sites: sites, talismanCharges: charges), Looks.Stacks) };
        return true;
    }

    /// <summary><c>L_BATTLE</c> (<c>0x43c485</c>): отряд S подходит к триггереру (<see cref="MoveByEvent"/>), лидер триггерера —
    /// иллюзия → триггерер уничтожен; иначе бой — нападает S, защищается триггерер; дальнейшие проверки останавливаются.
    /// Бой играет наблюдатель (<see cref="ITurnObserver.Battle"/>: экран боя в ходе ИИ); в действие человека вне конца хода при
    /// <see cref="EventBattlesOnScreen"/> бой с его стороной ждёт экрана (<see cref="PendingEventBattle"/>); иначе — автобой.</summary>
    private bool BattleByEvent(BattleEffect b, MapStack? triggerer)
    {
        if (triggerer is null || TriggererCell(triggerer) is not { } cell) return true;
        var s = Targets(b.StackId, firstOnly: true).FirstOrDefault();
        if (s is null) return true;
        MoveByEvent(s.Id, cell.X, cell.Y, beside: true);
        if (!Map.Stacks.ContainsKey(s.Id) || !Map.Stacks.ContainsKey(triggerer.Id)) return true;
        if (IsIllusion(triggerer.LeaderId))
        {
            RemoveStack(triggerer.Id, killer: Map.Stacks[s.Id].Owner, killerGroup: s.Id);   // иллюзия: вещи — отряду S
            _triggered.Add(triggerer.Id);
            return true;
        }
        _stop = true;
        if (PendingEventBattle is not null)
        {
            // Второй бой того же действия (ранг 3): встаёт в очередь и начинается, когда экран закроет первый (NextEventBattle).
            _eventBattleQueue.Enqueue((s.Id, triggerer.Id));
            return true;
        }
        var state = StartBattle(s.Id, triggerer.Id, duel: true);
        if (Observer?.DecideBattle(state) is { Log: var played })
        {
            FinishBattle(played);
            return true;
        }
        if (OnScreen(s.Owner, triggerer.Owner))
        {
            _eventBattle = state;
            return true;
        }
        AutoBattle(state);
        return true;
    }

    /// <summary>Бой эффекта ждёт экрана: сцена его показывает, идёт действие человека (не <see cref="EndTurn"/>) и одна из сторон —
    /// человек.</summary>
    private bool OnScreen(string attackerOwner, string defenderOwner) =>
        EventBattlesOnScreen && !_endTurn && (IsHuman(defenderOwner) || IsHuman(attackerOwner));

    /// <summary>Бой без экрана: автобой обеих сторон на генераторе партии, итог — <see cref="FinishBattle"/>.</summary>
    private void AutoBattle(BattleState state)
    {
        var rules = Book!.Rules;
        var battle = new LastEdict.Data.Battle.BattleSession(state, new BattleSim(state, BattleRng, rules.Constants), rules.Constants) { Auto = true };
        battle.PlayOut();
        FinishBattle(battle.Log);
    }

    /// <summary>Сцена показывает бой эффекта с человеком экраном боя: в действие человека бой не доигрывается автобоем, а ждёт
    /// (<see cref="PendingEventBattle"/>). Без сцены (тесты, харнессы) — автобой.</summary>
    public bool EventBattlesOnScreen { get; set; }

    /// <summary>Бой эффекта <c>L_BATTLE</c>, собранный (<see cref="StartBattle"/>) и ждущий экрана: сцена играет его на
    /// <see cref="BattleRng"/> и отдаёт журнал в <see cref="FinishBattle"/> (проверка после боя — там). Нет — null.</summary>
    public BattleState? PendingEventBattle => _battle is { } b && ReferenceEquals(b.State, _eventBattle) ? b.State : null;

    private BattleState? _eventBattle;

    private readonly Queue<(string Attacker, string Defender)> _eventBattleQueue = new();

    /// <summary>Бой эффекта для экрана: ждущий (<see cref="PendingEventBattle"/>) или, когда прошлый закрыт, следующий из очереди того
    /// же действия — если нападающий жив и у цели есть защитники (<see cref="StartBattle"/>). Бой очереди без стороны человека
    /// экрану не отдаётся — играется автобоем, как вне очереди (<see cref="OnScreen"/>). Нет — null.</summary>
    public BattleState? NextEventBattle()
    {
        if (PendingEventBattle is { } pending) return pending;
        if (_battle is not null) return null;
        while (_eventBattleQueue.TryDequeue(out var next))
        {
            if (!Map.Stacks.TryGetValue(next.Attacker, out var attacker) || MapBattle.DefenderOf(Map, next.Defender) is null) continue;
            string defenderOwner = Map.Stacks.GetValueOrDefault(next.Defender)?.Owner ?? "";
            var state = StartBattle(next.Attacker, next.Defender, duel: true);
            if (OnScreen(attacker.Owner, defenderOwner)) return _eventBattle = state;
            AutoBattle(state);
            if (PendingEventBattle is { } nested) return nested;   // проверка после автобоя начала свой бой с человеком
        }
        return null;
    }

    /// <summary>Бой эффекта ждёт экрана — сейчас или в очереди.</summary>
    public bool EventBattleWaiting => PendingEventBattle is not null || _eventBattleQueue.Count > 0;

    /// <summary><c>L_CHANGE_TERRAIN</c> (<c>0x43f128</c>): местность <c>LOOKUP</c> в квадрате со стороной N вокруг центра локации
    /// (x ∈ [cx − ⌊N/2⌋, cx − ⌊N/2⌋ + N)), только на равнине и в лесу.</summary>
    private bool ChangeTerrain(ChangeTerrainEffect c)
    {
        if (State.Events.Zones.GetValueOrDefault(c.LocationId) is not { } zone || c.Lookup is < 1 or > 7) return true;
        Tile[,]? tiles = null;
        List<(int X, int Y)> cells = [];
        int x0 = zone.X - c.Value / 2, y0 = zone.Y - c.Value / 2;
        for (int x = x0; x < x0 + c.Value; x++)
            for (int y = y0; y < y0 + c.Value; y++)
            {
                if (!Map.Contains(x, y) || Map.Tiles[x, y].Ground is not (Ground.Plain or Ground.Forest)) continue;
                tiles ??= (Tile[,])Map.Tiles.Clone();
                tiles[x, y] = new Tile((byte)((tiles[x, y].Raw & ~0b111) | c.Lookup));
                cells.Add((x, y));
            }
        // Земля расы, у которой двое игроков, — тому из них, чей источник роста ближе (LandOwnership.Retag).
        var owners = tiles is not null && Book is { } book ? LandOwnership.Retag(Map, tiles, cells, null, book.Economy) : null;
        if (tiles is not null) State = State with { Looks = Retold(Looks, WithTiles(Map, tiles, owners), Looks.Stacks) };
        return true;
    }

    /// <summary><c>L_DIPLOMACY</c> (<c>0x43cbbd</c>): значение не «мирное» — союз разрывается; прежнее = текущее, текущее =
    /// <c>DIPLOMACY</c>, вечная война = <c>ENABLE</c>. <c>L_ALLY</c> (<c>0x43c2a0</c>): прежнее = текущее, текущее = <c>D_PEACE</c>,
    /// союз, ход союза — сегодня, <c>PERMALLI</c> — ИИ не разорвёт (бит 31, ранг 2).</summary>
    private bool ChangeRelation(string player1, string player2, Func<Relation, uint> change)
    {
        var rules = new EventRules(RequireBook().Economy.Races, RequireBook().Diplomacy);
        int? Race(string id) => Diplomacy.PartyOf(Map, rules.Races, id);
        if (Race(player1) is not { } a || Race(player2) is not { } b || a == b) return true;
        var relations = Map.Relations.ToList();
        int at = relations.FindIndex(r => r.Pairs(a, b));
        var old = at >= 0 ? relations[at] : new Relation(a, b, 0);
        var now = old with { Raw = change(old) };
        if (at >= 0) relations[at] = now;
        else relations.Add(now);
        State = State with { Looks = Retold(Looks, With(Map, relations: relations), Looks.Stacks) };
        return true;
    }

    /// <summary>Текущее отношение (биты 1…7) без обрезки по вечной войне: <c>CEffectDiploRelation</c> (<c>0x4372f4</c>) передаёт «прежнее»
    /// снимком самого прежнего (<c>0x5f78c5</c>, биты 8…14) — прежнее не меняется (ранг 1, седьмой круг).</summary>
    private static uint WithCurrent(uint raw, int current) => (raw & ~(0x7Fu << 1)) | ((uint)Math.Clamp(current, 0, 100) << 1);

    /// <summary>
    /// <c>L_DIPLOMACY</c> (<c>0x43cbbd</c>, ранг 1): значение не «мирное» (≤ <c>D_NEUTRAL</c>) — сначала <c>CEffectChangeAlliance</c> союз=0:
    /// был союз — разрыв (<c>0x5d91fd</c>, ранг 1): <c>CVisitorDiploAlliance</c> — бит союза 0, ход разрыва, бит 31 «ИИ не разорвёт» 0;
    /// мутатор <c>0x5d9366(P1, P2, D_BK_ALLY)</c> (<see cref="Diplomacy.BreakAlliance"/>: пара := <c>D_BK_ALLY</c>, враждующим с P2 — минус
    /// половина к P1); +1 <c>QTY_BREAKS</c> P1; разведка друг друга снимается (<see cref="SpyEachOther"/>); затем <c>CEffectDiploRelation</c>: текущее := <c>DIPLOMACY</c>, прежнее — как было,
    /// «вечная война» := <c>ENABLE</c>.
    /// </summary>
    private bool DiplomacyByEvent(DiplomacyEffect d)
    {
        var book = RequireBook();
        if (Races(d.Player1, d.Player2) is not { } pair) return true;
        if (d.Relation <= book.Diplomacy.Neutral && Diplomacy.AreAllies(Map.Relations, pair.A, pair.B) && Map.Players.TryGetValue(d.Player1, out var p1))
        {
            // Мутатор разрыва — 0x5d9366(карта, P1, P2, D_BK_ALLY) (у союза — наоборот, P2, P1).
            var broken = Diplomacy.BreakAlliance(Map.Relations, Diplomacy.PlayerParties(Map, book.Economy), book.Diplomacy, pair.A, pair.B, Map.Turn,
                                                 p1.QtyBreaks);
            // Бит 31 (ИИ не разорвёт) разрыв снимает (0x5e88aa → 0x5f7a9e); общий Diplomacy.WithAlliance его не трогает.
            var relations = broken.Change.Relations.Select(r => r.Pairs(pair.A, pair.B) ? r with { Raw = r.Raw & ~(1u << 31) } : r).ToList();
            var players = SpyEachOther(Map.Players, d.Player1, d.Player2, spied: false);
            players[p1.Id] = players[p1.Id] with { QtyBreaks = broken.QtyBreaks };
            State = State with { Looks = Retold(Looks, With(Map, players: players, relations: relations), Looks.Stacks) };
        }
        return ChangeRelation(d.Player1, d.Player2, r =>
        {
            uint raw = WithCurrent(r.Raw, d.Relation);
            return d.Enable ? raw | (1u << 30) : raw & ~(1u << 30);
        });
    }

    /// <summary>
    /// <c>L_ALLY</c> (<c>0x43c2a0</c>, ранг 1): <c>CEffectDiploRelation</c> — текущее := <c>D_PEACE</c> (прежнее и «вечная война» как были),
    /// затем <c>CEffectChangeAlliance</c> союз=1 (<c>0x5d91af</c>, тот же путь, что союз из дипломатии <c>0x442cb5</c>): допуск
    /// <see cref="Diplomacy.CanMakeAlliance"/> (выше <c>D_NEUTRAL</c> или оба — люди, байт <c>IS_HUMAN</c> +0x88); бит союза, ход «сегодня»,
    /// бит «ИИ не разорвёт» := <c>PERMALLI</c> (<c>0x5e88aa</c>); <see cref="Diplomacy.MakeAlliance"/> — паре <c>D_MK_ALLY</c> (у двух людей
    /// <c>max(текущее, D_NEUTRAL + 1)</c>), враждующим с первым игроком — минус половина ко второму; оба — разведаны друг другом
    /// (<c>0x5d7dbc</c>, <see cref="SpyEachOther"/>).
    /// </summary>
    private bool AllyByEvent(AllyEffect a)
    {
        var book = RequireBook();
        ChangeRelation(a.Player1, a.Player2, r => WithCurrent(r.Raw, book.Diplomacy.Peace));
        if (Races(a.Player1, a.Player2) is not { } pair || !Map.Relations.Any(r => r.Pairs(pair.A, pair.B))) return true;
        bool bothHuman = Map.Players.TryGetValue(a.Player1, out var p1) && p1.IsHuman && Map.Players.TryGetValue(a.Player2, out var p2) && p2.IsHuman;
        if (!Diplomacy.CanMakeAlliance(Map.Relations, book.Diplomacy, pair.A, pair.B, bothHuman)) return true;
        var made = Diplomacy.MakeAlliance(Map.Relations, Diplomacy.PlayerParties(Map, book.Economy), book.Diplomacy, pair.B, pair.A, Map.Turn, bothHuman);
        var relations = made.Relations.Select(r => r.Pairs(pair.A, pair.B)
                                                    ? r with { Raw = a.Permanent ? r.Raw | (1u << 31) : r.Raw & ~(1u << 31) } : r).ToList();
        State = State with { Looks = Retold(Looks, With(Map, players: SpyEachOther(Map.Players, a.Player1, a.Player2, spied: true), relations: relations),
                                          Looks.Stacks) };
        return true;
    }

    /// <summary>Стороны дипломатии двух разных игроков (для пары отношений; у движка — типы рас); нет игрока, расы или одна сторона — null.</summary>
    private (int A, int B)? Races(string player1, string player2)
    {
        var races = RequireBook().Economy.Races;
        int? Race(string id) => Diplomacy.PartyOf(Map, races, id);
        return Race(player1) is { } a && Race(player2) is { } b && a != b ? (a, b) : null;
    }

    private static MapState WithObjects(MapState map, IReadOnlyDictionary<string, MapObject> objects) => new()
    {
        Size = map.Size, Tiles = map.Tiles, Stacks = map.Stacks, Units = map.Units, Players = map.Players,
        Objects = objects, Plan = map.Plan, Fog = map.Fog, Turn = map.Turn, Rods = map.Rods, Sites = map.Sites,
        Relations = map.Relations, TalismanCharges = map.TalismanCharges, SpellCasts = map.SpellCasts,
        ItemCasts = map.ItemCasts, SpellEffects = map.SpellEffects, ItemTypes = map.ItemTypes, Limits = map.Limits, MapSeed = map.MapSeed, Subraces = map.Subraces,
        Destroyed = map.Destroyed, Tombs = map.Tombs,
        IdNext = map.IdNext, LandOwners = map.LandOwners, LandOwnerIds = map.LandOwnerIds,
    };
}
