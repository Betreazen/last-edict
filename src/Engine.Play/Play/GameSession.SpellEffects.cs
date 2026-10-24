using LastEdict.Rules.Magic;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

// Эффекты заклинаний карты кроме удара, лечения и призыва (docs/ai-spells.md §3): модификатор (усиление, ослабление), запас
// хода, невидимость, туман и его развеивание, смена земли, снос жезла — и срок модификаторов заклинаний и зелий карты
// (CMidSpellEffects).
public sealed partial class GameSession
{
    /// <summary>Объекты, в чьей клетке земля заклинанием не меняется (<c>0x5f5631</c> с типами id 0x1d, 0x22, 0x25, 0x28, 0x29,
    /// 0x2b, 0x2c — <c>midgardid.h</c>): крепость, дорога, ориентир, лавка, руина, жезл, жила.</summary>
    private static readonly string[] TerrainBlockers = ["FT", "RA", "MM", "SI", "RU", "RD", "CR"];

    /// <summary>
    /// Клетки, где заклинание смены земли что-то меняет (цель <c>0x5db4fc</c>, эффект <c>0x4359a7</c>, ранг 1 по листингу):
    /// квадрат со стороной <c>AREA</c> вокруг центра (<c>c − AREA/2 … c − AREA/2 + AREA − 1</c>), обрезанный краем карты;
    /// в клетке нет <see cref="TerrainBlockers"/>, грунт — равнина или лес (<c>0x837318</c>/<c>0x837338</c>), в клетке и
    /// восьми соседях нет жилы, грунт ещё не тот, что делает заклинание (<c>GROUND_CAT</c>).
    /// </summary>
    private List<(int X, int Y)> TerrainCells(Spell spell, int cx, int cy)
    {
        var map = Map;
        int half = spell.Area / 2;
        var cells = new List<(int X, int Y)>();
        bool Has(int x, int y, IReadOnlyCollection<string> kinds) =>
            map.Plan.TryGetValue((x, y), out var ids) && ids.Any(id => id.Length >= 6 && kinds.Contains(id.Substring(4, 2).ToUpperInvariant()));
        for (int y = Math.Max(0, cy - half); y < Math.Min(map.Size, cy - half + spell.Area); y++)
            for (int x = Math.Max(0, cx - half); x < Math.Min(map.Size, cx - half + spell.Area); x++)
            {
                if (Has(x, y, TerrainBlockers) || map.Tiles[x, y].Ground is not (Ground.Plain or Ground.Forest)) continue;
                bool crystalNear = false;
                for (int nx = x - 1; nx <= x + 1 && !crystalNear; nx++)
                    for (int ny = y - 1; ny <= y + 1 && !crystalNear; ny++)
                        crystalNear = map.Contains(nx, ny) && Has(nx, ny, ["CR"]);
                if (!crystalNear && (int)map.Tiles[x, y].Ground != spell.Ground) cells.Add((x, y));
            }
        return cells;
    }

    /// <summary>
    /// Смена земли (<c>0x41cdd5</c> → эффект <c>0x43591d</c>/<c>0x4359a7</c>): клеткам <see cref="TerrainCells"/> — грунт
    /// <c>GROUND_CAT</c> заклинания и земля нейтральная (<c>L_NE</c>, категория <c>0x837930</c> в поле эффекта; ранг 2 — что визитёр
    /// <c>0x5e685e</c> пишет обе категории, по его аргументам).
    /// </summary>
    private void TerrainSpell(Spell spell, int x, int y)
    {
        var cells = TerrainCells(spell, x, y);
        if (cells.Count == 0) return;
        var tiles = (Tile[,])Map.Tiles.Clone();
        foreach (var (cx, cy) in cells)
            tiles[cx, cy] = new Tile((byte)((tiles[cx, cy].Raw & ~0b111111) | ((spell.Ground & 0b111) << 3) | NeutralTerrain));
        var owners = Book is { } book ? LandOwnership.Retag(Map, tiles, cells, null, book.Economy) : null;   // нейтральная — без хозяина
        State = State with { Looks = Retold(Looks, WithTiles(Map, tiles, owners), Looks.Stacks) };
    }

    /// <summary><c>Lterrain</c> <c>L_NE</c> — нейтральная земля.</summary>
    private const int NeutralTerrain = 5;

    /// <summary>Жезл в клетке (цель сноса заклинанием: <c>0x5f54e2</c> с типом id 0x2b).</summary>
    private MapRod? RodAt(int x, int y) => Map.Rods.Values.FirstOrDefault(r => r.X == x && r.Y == y);

    /// <summary>
    /// Снос жезла заклинанием (<see cref="SpellCast.RemoveRod"/>: <c>CEffectBreakRod</c> с пустым виновником, <c>0x41CD7E</c>):
    /// жезл клетки уходит, штрафа отношений нет, земля остаётся как есть (замерзает, <see cref="Rods.Break"/>). Свой жезл
    /// отказывает ещё целью (<c>0x5db847</c>).
    /// </summary>
    private void RodSpell(int x, int y)
    {
        if (RodAt(x, y) is not { } rod) return;
        var map = Map;
        var rods = map.Rods.Where(p => !Same(p.Key, rod.Id)).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        if (plan.TryGetValue((rod.X, rod.Y), out var ids)) plan[(rod.X, rod.Y)] = [.. ids.Where(id => !Same(id, rod.Id))];
        var next = WithRods(With(map, plan: plan), rods);
        State = State with { Looks = Retold(Looks, next, Looks.Stacks) with { Rods = [.. Looks.Rods.Where(r => !Same(r.Id, rod.Id))] } };
    }

    /// <summary>
    /// Модификатор заклинания (<c>CSpellResultModifier</c> <c>0x41ca78</c>, усиление и ослабление): каждому юниту отряда-цели
    /// (<see cref="Targets"/>; площадь — все отряды квадрата, вся карта — свои) — <c>MODIF_ID</c> в цепочку и запись
    /// <see cref="SpellEffectRecord"/> со сроком <c>ход + 1</c> (<c>CEffectModifier</c> <c>0x4396e2</c>). Ранг 1 по листингу.
    /// </summary>
    private void ModifySpell(string playerId, Spell spell, int x, int y)
    {
        if (spell.ModifierId.Length == 0) return;
        var map = Map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var records = map.SpellEffects.ToList();
        int stop = SpellCast.TurnStop(map.Turn);
        var targets = Targets(playerId, spell, x, y, check: false).Select(t => t.Stack.Id).ToList();
        foreach (string stackId in targets)
            foreach (string id in map.Stacks[stackId].UnitIds)
                AddEffect(units, records, id, spell.Id, spell.ModifierId, playerId, stop);
        State = State with { Looks = Retold(Looks, With(map, units: units, spellEffects: records), Looks.Stacks) };
        ClampMove(targets);
    }

    /// <summary>
    /// После модификатора запас хода отряда не выше нового дневного (<c>CEffectModifier</c> <c>0x4396e2</c>: <c>0x439886</c>–<c>0x4398af</c> —
    /// запас <c>+0x98</c> больше <c>0x5ed035</c> → <c>changeStackMove</c> на разницу). Журнал <c>20261016e-av</c>, карта 7: Посох парализации по
    /// Гному — <c>ap_restore</c> cause <c>modifier_clamp</c> 20 → 0 (seq 14001016), и 0 на дне 2. Прежде порт запас не трогал (20).
    /// </summary>
    private void ClampMove(IEnumerable<string> stackIds)
    {
        foreach (string id in stackIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            if (Map.Stacks.TryGetValue(id, out var stack) && Math.Max(DayAllowance(stack), 0) is var allowance && stack.Move > allowance)
                SetMove(id, allowance);
    }

    /// <summary>
    /// Обереги (<c>CSpellResultGiveWards</c>, vtable <c>0x6cdb64</c>, применение <c>0x41ce93</c>; ранг 1 по листингу, 16.10): только
    /// отряд в клетке цели (<c>0x5f54e2</c> с типом 0x23 — площадь <c>AREA</c> не смотрится, в отличие от <see cref="ModifySpell"/>),
    /// каждому его юниту — каждый оберег <c>WARD1..4</c> (<c>0x582b37</c>) тем же эффектом-модификатором со сроком <c>ход + 1</c>
    /// (<c>CEffectModifier</c> <c>0x4396e2</c> → <c>0x43f8e8</c>), что у усиления. <c>MODIF_ID</c> результат оберегов не читает.
    /// Оберег — строка <c>Gmodif</c> (защита от источника или класса атаки), в бою он работает как любой модификатор цепочки юнита.
    /// </summary>
    private void WardSpell(string playerId, Spell spell, int x, int y)
    {
        if (spell.Wards.Count == 0 || StackAt(x, y) is not { } stack) return;
        var map = Map;
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var records = map.SpellEffects.ToList();
        int stop = SpellCast.TurnStop(map.Turn);
        foreach (string id in stack.UnitIds)
            foreach (string ward in spell.Wards)
                AddEffect(units, records, id, spell.Id, ward, playerId, stop);
        State = State with { Looks = Retold(Looks, With(map, units: units, spellEffects: records), Looks.Stacks) };
        ClampMove([stack.Id]);
    }

    /// <summary>Модификатор со сроком на юнита: в цепочку и в <see cref="MapState.SpellEffects"/>.</summary>
    private static void AddEffect(Dictionary<string, MapUnit> units, List<SpellEffectRecord> records, string unitId, string origin,
                                  string modifierId, string casterId, int turnStop)
    {
        if (!units.TryGetValue(unitId, out var unit)) return;
        string modifier = modifierId;
        units[unitId] = unit with { Modifiers = [.. unit.Modifiers, modifier] };
        records.Add(new SpellEffectRecord(unitId, origin, modifier, casterId, turnStop));
    }

    /// <summary>
    /// Запас хода (<c>CSpellResultMove</c> <c>0x41c617</c>): отряду-цели — <c>дневной запас · RESTORE_M / 100</c>
    /// (<c>0x5ed035</c>, <c>0x5838c0</c>) через <c>CEffectChangeAP</c> (<c>0x43588c</c> → <c>changeStackMove</c> <c>0x5e780d</c>),
    /// не выше дневного запаса (ранг 2: 5 из 5 строк <c>ap_restore</c> корпуса). Модификатор заклинания этой категории
    /// (у «Swift» — <c>g000um7538</c>) результат хода не кладёт: категория выбирает один результат (<c>0x443828</c>).
    /// </summary>
    private void RestoreMoveSpell(string playerId, Spell spell, int x, int y)
    {
        if (spell.RestoreMovePercent == 0) return;
        var map = Map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var (stack, _) in Targets(playerId, spell, x, y, check: false).ToList())
        {
            int allowance = DayAllowance(stack);
            int add = allowance * spell.RestoreMovePercent / 100;
            stacks[stack.Id] = stack with { Move = Math.Clamp(stack.Move + add, 0, Math.Max(allowance, stack.Move)) };
        }
        State = State with { Looks = Retold(Looks, With(map, stacks: stacks), Looks.Stacks) };
    }

    /// <summary>Невидимость (<c>CSpellResultInvisibility</c> <c>0x41c9a5</c>): отряд-цель прячется (<c>CEffectShow</c>
    /// <c>0x43e2e4</c> с 0, флаг <c>+0x9b</c>). Показывается снова при действии (<see cref="Unhide"/>).</summary>
    private void HideSpell(string playerId, Spell spell, int x, int y)
    {
        var hidden = Targets(playerId, spell, x, y, check: false).Select(t => t.Stack.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        State = State with { Looks = Looks with { Stacks = [.. Looks.Stacks.Select(s => hidden.Contains(s.Id) ? s with { Invisible = true } : s)] } };
    }

    /// <summary>
    /// Отряд снова виден (<c>CEffectShow</c> с 1, применение <c>0x43e32c</c>: флаг <c>+0x9b</c> снимается, если стоит). У движка эффект
    /// с 1 ставят 11 мест, все — до самого действия (ранг 1 по листингу, <c>docs/map-path.md</c> §9):
    /// <list type="bullet">
    /// <item>генератор хода <c>0x43fc3c</c>: путь оборван событием (выход <c>0x440284</c> = 1, <c>0x43fe8c</c>) — идущий
    /// (<see cref="MoveStack"/>); объект действия пути (<c>+0</c> искателя, <c>0x5cd1c7</c>) — враждебный отряд, к которому отряд
    /// сегодня подойдёт: идущий (<c>0x43fef1</c>) и цель (<c>0x43ff69</c>) — <see cref="StartBattle"/> (вор против невидимой цели не
    /// выбирает, а бьётся: <see cref="ThiefActions"/>);</item>
    /// <item>нападение на отряд или крепость <c>0x440667</c> (<c>0x4406bf</c>), до убийства иллюзии, выбора вора и <c>CEffectBattle</c> —
    /// <see cref="StartBattle"/>, <see cref="Enter"/> (пустая чужая крепость), действия вора;</item>
    /// <item>объект за концом пути <c>0x4408cb</c>: своя крепость (<c>0x44099e</c>, до <c>CEffectEnterCity</c>), руина (<c>0x440a53</c>,
    /// до боя и вора), место (<c>0x440b59</c>, до <c>CEffectVisit</c>), мешок (<c>0x440c0b</c>, до <c>CEffectTakeBag</c>) —
    /// <see cref="Enter"/>; жезл (<c>0x440c9f</c>, до <c>CEffectBreakRod</c>) — <see cref="BreakRod"/>;</item>
    /// <item>постановка жезла <c>0x440cfc</c> (<c>0x440e3b</c>, до <c>CEffectDropRod</c>; человек — <c>CDropRodMsg</c>, ИИ —
    /// <c>CAiPlantRodAction</c>) — <see cref="PlaceRod"/>; выброс мешка <c>0x441575</c> (<c>0x4415c8</c>, до <c>CEffectDropBag</c>) —
    /// <see cref="DropBag"/>.</item>
    /// </list>
    /// Простой ход и ход к своему отряду отряд не показывают. Корпус <c>20261014-replay</c> passive-002 (два литья Illudere terra
    /// <c>S143PL0002</c>): видимыми в следующем сохранении стали ровно напавшие (<c>RevealHarnessTests</c>).
    /// </summary>
    internal void Unhide(string stackId)
    {
        if (!Invisible(stackId)) return;
        State = State with { Looks = Looks with { Stacks = [.. Looks.Stacks.Select(s => Same(s.Id, stackId) ? s with { Invisible = false } : s)] } };
    }

    /// <summary>
    /// Развеивание тумана (<c>CSpellResultUnfog</c> <c>0x41c723</c>): у заклинателя открываются клетки <see cref="SpellFogCells"/> — радиус
    /// <c>AREA / 2</c> (вся карта — сторона карты вокруг её центра); эффект <c>CEffectFog</c> (<c>0x4382a8</c> → <c>0x438314</c> →
    /// <c>0x5d5230</c>) тот же, что у события <c>L_UNFOG</c>, нейтралу (<c>0x5e5358</c>) — ничего. Ранг 1: партия 17.10
    /// (<c>20261017-port-synth</c>, карта 11) — «Взгляд Сивиллы», «Истинное зрение» и «Посох света» (<c>AREA</c> 5) открыли по 21 клетке:
    /// квадрат 5×5 без углов (<c>PortSynthMapsTests.Magic2_CastsMatchOriginalSaves</c>; прежний квадрат ±3 открывал 49).
    /// </summary>
    private void UnfogSpell(string playerId, Spell spell, int x, int y)
    {
        var map = Map;
        if (IsNeutral(playerId) || !map.Players.TryGetValue(playerId, out var p) || !map.Fog.TryGetValue(p.FogId, out var fog)) return;
        bool whole = spell.Area == WholeMap;
        if (whole) (x, y) = (map.Size / 2, map.Size / 2);
        foreach (var (fx, fy) in SpellFogCells(x, y, whole ? map.Size : spell.Area / 2, map.Size)) fog[fx, fy] = false;
    }

    /// <summary>
    /// Туман (<c>0x41c7f8</c>): каждому игроку, кроме заклинателя (у заклинания на всю карту — и кроме союзников, <c>0x5f7ebd</c>),
    /// закрываются клетки <see cref="SpellFogCells"/> — радиус <c>AREA / 2</c>, у всей карты — половина стороны вокруг центра карты;
    /// эффект <c>CEffectFog</c> тот же, что у развеивания: нейтрала (<c>0x5e5358</c> в <c>0x5d5230</c>) не трогает, после закрытия обзор
    /// крепостей и отрядов игрока открывается снова (<c>0x5d4c6e</c>, <c>0x5d5030</c>). Ранг 1: партия 17.10, карта 11 — у нейтрала туман
    /// после «Тумана Смерти» и «Посоха сумерек» не изменился (порт закрывал ему 22 и 27 клеток).
    /// </summary>
    private void FogSpell(string playerId, Spell spell, int x, int y)
    {
        var map = Map;
        bool whole = spell.Area == WholeMap;
        if (whole) (x, y) = (map.Size / 2, map.Size / 2);
        var cells = SpellFogCells(x, y, whole ? map.Size / 2 : spell.Area / 2, map.Size).ToList();
        foreach (var p in map.Players.Values)
        {
            if (Same(p.Id, playerId) || IsNeutral(p.Id) || !map.Fog.TryGetValue(p.FogId, out var fog)) continue;
            if (whole && playerId.Length > 0 && AiTactics.Allied(this, playerId, p.Id)) continue;
            foreach (var (fx, fy) in cells) fog[fx, fy] = true;
            Reveal(map, p.Id);
        }
    }

    /// <summary>
    /// Клетки эффекта тумана заклинания (<c>0x5d4dfc</c>, ранг 1 по листингу): квадрат <c>±r</c> вокруг клетки; при <c>r ≥ сторона / 2</c>
    /// он обрезается краями карты. Четыре угла квадрата снимаются, кроме совпавшего с тем же углом карты (у всей карты квадрат и есть
    /// карта — остаётся целиком). Клетки вне карты пропускаются.
    /// </summary>
    internal static IEnumerable<(int X, int Y)> SpellFogCells(int x, int y, int r, int size)
    {
        int x0 = x - r, y0 = y - r, x1 = x + r + 1, y1 = y + r + 1;
        if (r >= size / 2) (x0, y0, x1, y1) = (Math.Max(x0, 0), Math.Max(y0, 0), Math.Min(x1, size), Math.Min(y1, size));
        int last = size - 1;
        var corners = new HashSet<(int, int)>();
        if (!(x0 == 0 && y0 == 0)) corners.Add((x0, y0));
        if (!(x0 == 0 && y1 - 1 == last)) corners.Add((x0, y1 - 1));
        if (!(x1 - 1 == last && y0 == 0)) corners.Add((x1 - 1, y0));
        if (!(x1 - 1 == last && y1 - 1 == last)) corners.Add((x1 - 1, y1 - 1));
        for (int cx = Math.Max(x0, 0); cx < Math.Min(x1, size); cx++)
            for (int cy = Math.Max(y0, 0); cy < Math.Min(y1, size); cy++)
                if (!corners.Contains((cx, cy))) yield return (cx, cy);
    }

    /// <summary>
    /// Снятие модификаторов заклинаний и зелий карты в начале хода владельца (<c>CEffectRemoveSpell</c> <c>0x43b9ef</c>): записи,
    /// чей владелец (<c>CASTER_ID</c>, пустой — хозяин юнита) — этот игрок и чей срок вышел (ход ≥ <c>TURN_STOP</c>), уходят, а с
    /// ними по одному вхождению модификатора из цепочки юнита.
    /// </summary>
    internal MapState ExpireEffects(MapState map, string playerId)
    {
        if (map.SpellEffects.Count == 0) return map;
        var owners = UnitOwners(map);
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var keep = new List<SpellEffectRecord>();
        foreach (var r in map.SpellEffects)
        {
            string owner = r.CasterId.Length > 0 ? r.CasterId : owners.GetValueOrDefault(r.UnitId, "");
            if (!Same(owner, playerId) || SpellCast.Live(r.TurnStop, map.Turn)) { keep.Add(r); continue; }
            if (!units.TryGetValue(r.UnitId, out var unit)) continue;
            int at = unit.Modifiers.ToList().FindIndex(m => Same(m, r.ModifierId));
            if (at >= 0) units[r.UnitId] = unit with { Modifiers = [.. unit.Modifiers.Where((_, i) => i != at)] };
        }
        return keep.Count == map.SpellEffects.Count ? map : With(map, units: units, spellEffects: keep);
    }

    /// <summary>Хозяин юнита: отряда, где он стоит, или крепости, чей он гарнизон.</summary>
    private static Dictionary<string, string> UnitOwners(MapState map)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in map.Stacks.Values)
            foreach (string id in s.UnitIds) owners[id] = s.Owner;
        foreach (var site in map.Sites)
            foreach (string id in site.Guards) owners.TryAdd(id, site.Owner);
        return owners;
    }
}
