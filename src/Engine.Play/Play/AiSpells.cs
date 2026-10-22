using D2.Rules.Battle;
using D2.Rules.Magic;
using D2.Rules.Map;
using D2.Rules.Rng;
using D2.Rules.Units;

namespace D2.Data.Play;

/// <summary>Списки отрядов оперативного слоя, собранные в начале хода (<c>0x4719f8</c> → <c>0x4760b0</c>): свои сильнейшие
/// (<c>CAi +0x24</c> → <c>CAiOperational +0x1c</c>) и чужие (<c>CAi +0x34</c> → <c>+0x18</c>). Хранятся id: к моменту
/// литья отряд мог погибнуть.</summary>
public sealed record AiForces(IReadOnlyList<string> Own, IReadOnlyList<string> Others);

/// <summary>
/// Заклинания ИИ на карте — оперативный слой <c>CAiOperational</c> (<c>docs/ai-spells.md</c>). Драйвер <c>0x46d602</c>:
/// до тактики — лечение (<c>0x46d691</c>), смена приказов (<c>0x46dee2</c>, <see cref="GameSession"/>), при
/// <c>SUGG_LVL ≥ 10</c> — «натиск» (<c>0x46fe35</c>, <see cref="Harass"/>), затем защита (<c>0x46e322</c>); после тактики —
/// снова лечение. Нейтралы и
/// игроки со взятой столицей слой не ведут. Правила выбора — <see cref="AiSpellRules"/>.
/// </summary>
public static partial class AiSpells
{
    /// <summary>
    /// Сильнейшие отряды игрока (<c>0x476190</c> → <c>0x476252</c>): свои, действующий приказ <c>L_NORMAL</c> (при
    /// <c>L_NORMAL</c> в <c>+0xa8</c> — приказ ИИ <c>+0xb8</c>), лидер не призыв и не иллюзия (<c>0x476374</c> → <c>0x431d8c</c>:
    /// приведение к <c>IUsSummon</c>, иллюзия — его наследник; журнал 20261018b-port-synth, карта 18: у Нежити в списке только
    /// <c>S143KC0003</c> и <c>S143KC000c</c>, отряда иллюзии <c>S143KC000a</c> нет — ни раздачи приказов, ни помощника); по силе
    /// <c>0x5dcec7</c> по убыванию, при равенстве — по номеру id (<c>0x476604</c>); первые
    /// <see cref="AiSpellRules.ForcesPerPlayer"/>.
    /// </summary>
    public static IReadOnlyList<MapStack> MainForces(GameSession session, string playerId) =>
        [.. NormalStacks(session, playerId).Take(AiSpellRules.ForcesPerPlayer)];

    /// <summary>Тот же список без обрезки (<c>0x4760b0</c> с третьим аргументом 1 — раздача стратегических приказов
    /// <c>0x44e29e</c>).</summary>
    public static IReadOnlyList<MapStack> NormalStacks(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var book = session.Book!;
        return [.. session.Map.Stacks.Values
            .Where(s => Same(s.Owner, playerId) && AiTactics.OrderOf(s).Order == StackOrder.Normal
                        && !(session.Map.Units.TryGetValue(s.LeaderId, out var l) && UnitCategory.IsSummonImpl(book.CategoryOf(l.TypeId))))
            .OrderByDescending(s => AiTactics.Strength(session, s)).ThenBy(s => IdKey(s.Id))];
    }

    /// <summary>Списки начала хода (<c>0x4760b0</c>): свои — <see cref="MainForces"/>; чужие — то же у каждого другого
    /// игрока не нейтральной расы (союзники тоже), общий список снова по силе (<c>0x4763fc</c>).</summary>
    public static AiForces Forces(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var others = session.Map.Players.Keys
            .Where(p => !Same(p, playerId) && !AiTactics.NeutralOwner(session, p))
            .SelectMany(p => MainForces(session, p))
            .OrderByDescending(s => AiTactics.Strength(session, s)).ThenBy(s => IdKey(s.Id));
        return new AiForces([.. MainForces(session, playerId).Select(s => s.Id)], [.. others.Select(s => s.Id)]);
    }

    /// <summary>
    /// Лечение (<c>0x46d691</c>, первая часть): каждому своему отряду списка не в крепости, если он тяжело ранен
    /// (<see cref="AiSpellRules.BadlyHurt"/>), — известное заклинание <c>L_HEAL</c> на одну клетку уровнем не выше яруса
    /// отряда (<see cref="AiSpellRules.Tier"/>), которое можно бросить (<see cref="GameSession.SpellRefusal"/>, без
    /// второго кошелька); из годных — последнее после сортировки по уровню (<c>0x472618</c>): старшее.
    /// Затем вторая часть — заклинания на всю карту (<see cref="WholeMap"/>).
    /// </summary>
    public static void Heal(GameSession session, string playerId, AiForces forces, IRng rng, List<string> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(forces);
        foreach (string id in forces.Own)
        {
            var map = session.Map;
            if (!map.Stacks.TryGetValue(id, out var stack) || stack.Inside.Length > 0) continue;
            var units = Group(session, stack).ToList();
            if (!AiSpellRules.BadlyHurt([.. units.Select(u => (u.Unit.Hp, u.HpMax))])) continue;
            int tier = AiSpellRules.Tier([.. units.Select(u => u.XpKilled)]);
            var fit = Known(session, playerId)
                .Where(s => s.Level <= tier && s.Category == SpellCategory.Heal && s.Area == 1
                            && session.SpellRefusal(playerId, s.Id, stack.X, stack.Y, useSpellBank: false) is null)
                .ToList();
            if (fit.Count == 0) continue;
            var spell = AiSpellRules.ListSort(fit, (a, b) => a.Level < b.Level)[^1];
            Cast(session, playerId, spell, (stack.X, stack.Y), useSpellBank: false, summonTarget: null, report);
        }
        WholeMap(session, playerId, rng, report);
    }

    /// <summary>
    /// Лечение, вторая часть (<c>0x46d97a</c>, ранг 1 по листингу): при <c>SUGG_LVL ≥ 10</c> и мане больше 1000 в четырёх
    /// валютах (<see cref="AiSpellRules.RichInMana"/>) — известные заклинания на всю карту, которые стоит бросить
    /// (<see cref="AiSpellRules.WholeMapWorth"/>: свои отряды и чужие не союзные вне крепостей) и можно бросить в клетку (0, 0) без
    /// второго кошелька; годные перемешиваются (<c>0x46a9f5</c>, броски R250), бросается первое. ponytail: в корпусе сценарии
    /// уровня 1 — сверять журналом нечем (тесты <c>AiSpellsHighLevelTests</c>).
    /// </summary>
    private static void WholeMap(GameSession session, string playerId, IRng rng, List<string> report)
    {
        var map = session.Map;
        if (session.State.Info.SuggestedLevel < AiSpellRules.HighScenarioLevel || !AiSpellRules.RichInMana(map.Players[playerId].Bank)) return;
        var outside = map.Stacks.Values.Where(s => s.Inside.Length == 0).ToList();
        var own = outside.Where(s => Same(s.Owner, playerId)).ToList();
        bool wounded = own.Any(s => s.UnitIds.Any(u => map.Units.TryGetValue(u, out var mu) && mu.Hp < session.HpMax(mu)));
        int foes = outside.Count(s => !Same(s.Owner, playerId) && !AiTactics.Allied(session, playerId, s.Owner));
        var fit = Known(session, playerId)
            .Where(s => s.Area == Spell.WholeMapArea && AiSpellRules.WholeMapWorth(s.Category, wounded, own.Count, foes)
                        && session.SpellRefusal(playerId, s.Id, 0, 0, useSpellBank: false) is null)
            .ToList();
        if (fit.Count == 0) return;
        AiSpellRules.Shuffle(fit, rng);
        Cast(session, playerId, fit[0], (0, 0), useSpellBank: false, summonTarget: null, report);
    }

    /// <summary>
    /// «Натиск» на людей (<c>0x46fe35</c>, состояние 3 драйвера, ранг 1 по листингу): при <c>SUGG_LVL ≥ 10</c> два прохода
    /// <c>0x46fe5e</c> — сначала в далёкие цели, потом в близкие. Проход идёт при <see cref="AiSpellRules.HarassGo"/>; число
    /// заклинаний — <see cref="AiSpellRules.HarassCount"/>. Цели — чужие сильнейшие (<see cref="AiForces.Others"/>), чей хозяин
    /// человек (<c>CMidPlayer +0x88</c>) и не союзник; «далёкая» — ни одного своего отряда ближе
    /// <see cref="AiSpellRules.HarassNear"/>. Цели перемешиваются (<c>0x46a9f5</c>); у первой, против которой есть заклинания
    /// категорий прохода (<see cref="AiSpellRules.HarassCategories"/>) на одну клетку, прошедшие проверку в её клетке со вторым
    /// кошельком, — они перемешиваются и бросаются по порядку, пока удачных литий меньше числа; дальше цели не смотрятся.
    /// Призыв идёт с приказом <c>L_NORMAL</c> без цели — и проверку в клетке цели (там отряд) не проходит.
    /// ponytail: в корпусе сценарии уровня 1 — сверять журналом нечем (тесты <c>AiSpellsHighLevelTests</c>).
    /// </summary>
    public static void Harass(GameSession session, string playerId, AiForces forces, IRng rng, List<string> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(forces);
        if (session.State.Info.SuggestedLevel < AiSpellRules.HighScenarioLevel) return;
        HarassPass(session, playerId, forces, far: true, rng, report);
        HarassPass(session, playerId, forces, far: false, rng, report);
    }

    private static void HarassPass(GameSession session, string playerId, AiForces forces, bool far, IRng rng, List<string> report)
    {
        if (!AiSpellRules.HarassGo(rng)) return;
        int count = AiSpellRules.HarassCount(far, rng);
        var map = session.Map;
        var targets = new List<MapStack>();
        foreach (string id in forces.Others)
        {
            if (!map.Stacks.TryGetValue(id, out var target) || !(map.Players.TryGetValue(target.Owner, out var owner) && owner.IsHuman)
                || AiTactics.Allied(session, playerId, target.Owner)) continue;
            var at = AiTactics.Position(map, target);
            bool near = map.Stacks.Values.Any(s => Same(s.Owner, playerId) && Cheb(AiTactics.Position(map, s), at) <= AiSpellRules.HarassNear);
            if (near != far) targets.Add(target);
        }
        AiSpellRules.Shuffle(targets, rng);
        var categories = AiSpellRules.HarassCategories(far);
        foreach (var target in targets)
        {
            var at = AiTactics.Position(session.Map, target);
            var fit = Known(session, playerId)
                .Where(s => categories.Contains(s.Category) && s.Area == 1
                            && session.SpellRefusal(playerId, s.Id, at.X, at.Y, useSpellBank: true) is null)
                .ToList();
            if (fit.Count == 0) continue;
            AiSpellRules.Shuffle(fit, rng);
            int cast = 0;
            foreach (var spell in fit)
            {
                if (cast >= count) break;
                if (Cast(session, playerId, spell, at, useSpellBank: true, summonTarget: null, report)) cast++;
            }
            return;
        }
    }

    /// <summary>
    /// Защита (<c>0x46e322</c>): при <c>SUGG_LVL &lt; 10</c> — свои крепости (<c>0x46e37c</c>), затем кристаллы
    /// (<c>0x46f93e</c>), затем жезлы у своих крепостей (<c>0x46f1d1</c>, <see cref="GuardRods"/>).
    /// </summary>
    public static void Defend(GameSession session, string playerId, AiForces forces, IRng rng, List<string> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State.Info.SuggestedLevel < 10) GuardForts(session, playerId, forces, rng, report);
        GuardCrystals(session, playerId, rng, report);
        GuardRods(session, playerId, report);
    }

    /// <summary>
    /// Жезлы у своих крепостей (<c>0x46f1d1</c>, ранг 1 по листингу; бросков R250 нет). Список клеток: по своим крепостям (порядок
    /// id) и жезлам карты — жезл не дальше <see cref="AiSpellRules.FortGuardRange"/> от входа крепости; свой — только если ни один
    /// отряд чужого игрока играбельной расы (невидимые, союзники и стоящие в крепости — тоже) не ближе того же к этому входу;
    /// клетка — один раз; сортировка по Чебышёву от входа своей столицы (устойчивая). Затем по известным заклинаниям игрока:
    /// снос жезла площади 1 — в первый чужой жезл списка, прошедший проверку (со вторым кошельком); смена земли в лес — вокруг
    /// первого своего жезла: первая клетка-равнина по рамкам радиусов 1…<see cref="AiSpellRules.RodTerrainRings"/> (строки по y,
    /// внутри по x), прошедшая проверку, иначе клетка самого жезла без проверки — командой сервера (<see cref="GameSession.CastCommand"/>): отказ проверки
    /// снимает только оплату и запись, земля в квадрате жезла всё равно меняется. Литьё — без второго кошелька. Не больше одного
    /// литья на заклинание. ponytail: в корпусе ИИ таких заклинаний не бросал — сверять нечем; порядок крепостей и жезлов — по id (ранг 2).
    /// </summary>
    private static void GuardRods(GameSession session, string playerId, List<string> report)
    {
        var map = session.Map;
        var book = session.Book!;
        if (map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && Same(s.Owner, playerId)) is not { } capital) return;
        var cap = AiTactics.Entrance(capital);
        bool Playable(string owner) => map.Players.TryGetValue(owner, out var p) && book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Playable;
        var list = new List<(int X, int Y, int Dist, bool Own)>();
        foreach (var fort in AiTactics.Forts(map).Where(f => Same(f.Owner, playerId)).OrderBy(f => IdKey(f.Id)).ToList())
        {
            var fp = AiTactics.Entrance(fort);
            foreach (var rod in map.Rods.Values.OrderBy(r => IdKey(r.Id)))
            {
                bool own = Same(rod.Owner, playerId);
                if (own && map.Stacks.Values.Any(s => !Same(s.Owner, playerId) && Playable(s.Owner)
                                                      && Cheb(AiTactics.Position(map, s), fp) <= AiSpellRules.FortGuardRange))
                    continue;
                if (Cheb((rod.X, rod.Y), fp) > AiSpellRules.FortGuardRange || list.Any(e => (e.X, e.Y) == (rod.X, rod.Y))) continue;
                list.Add((rod.X, rod.Y, Cheb(cap, (rod.X, rod.Y)), own));
            }
        }
        if (list.Count == 0) return;
        list = [.. list.OrderBy(e => e.Dist)];
        foreach (var spell in Known(session, playerId).ToList())
        {
            bool terrain = spell.Category == SpellCategory.ChangeTerrain && spell.Ground == (int)Ground.Forest;
            if (!terrain && !(spell.Category == SpellCategory.RemoveRod && spell.Area == 1)) continue;
            foreach (var rec in list)
            {
                if (terrain)
                {
                    if (!rec.Own) continue;
                    var at = TerrainCell(session, playerId, spell, (rec.X, rec.Y));
                    // Команда без проверки (0x443600): отказ проверки CEffectSpell снимает только оплату и запись — земля меняется.
                    var (refusal, unpaid) = session.CastCommand(playerId, spell.Id, at.X, at.Y, useSpellBank: false);
                    report.Add(refusal is not null ? $"заклинание {playerId} {spell.Id} ({at.X},{at.Y}) отказ: {refusal}"
                               : unpaid is not null ? $"заклинание {playerId} {spell.Id} ({at.X},{at.Y}) без оплаты: {unpaid}"
                               : $"заклинание {playerId} {spell.Id} ({at.X},{at.Y})");
                    break;
                }
                if (rec.Own || session.SpellRefusal(playerId, spell.Id, rec.X, rec.Y, useSpellBank: true) is not null) continue;
                Cast(session, playerId, spell, (rec.X, rec.Y), useSpellBank: false, null, report);
                break;
            }
        }
    }

    /// <summary>Клетка смены земли у своего жезла (<c>0x46f745</c>): по рамкам квадратов радиусов 1…4 вокруг жезла (строки по y,
    /// внутри по x) первая клетка карты с равниной, прошедшая проверку литья; нет — клетка самого жезла.</summary>
    private static (int X, int Y) TerrainCell(GameSession session, string playerId, Spell spell, (int X, int Y) rod)
    {
        var map = session.Map;
        for (int r = 1; r <= AiSpellRules.RodTerrainRings; r++)
            for (int y = rod.Y - r; y <= rod.Y + r; y++)
                for (int x = rod.X - r; x <= rod.X + r; x++)
                {
                    if (Math.Max(Math.Abs(x - rod.X), Math.Abs(y - rod.Y)) != r || !map.Contains(x, y)) continue;
                    if (map.At(x, y).Ground == Ground.Plain && session.SpellRefusal(playerId, spell.Id, x, y, useSpellBank: true) is null)
                        return (x, y);
                }
        return rod;
    }

    /// <summary>
    /// Защита своих крепостей (<c>0x46e37c</c>). Воры (<see cref="Intruders"/>, лидер <c>LEADER_CAT</c> 5) — список на
    /// начало; нет ни воров, ни чужих отрядов — выход. По своим крепостям без визитёра в порядке id: каждый чужой отряд
    /// списка ближе <see cref="AiSpellRules.FortGuardRange"/> клеток ко входу — удар (<see cref="Strike"/>), каждый
    /// вор ближе <see cref="AiSpellRules.IntruderRange"/> — призыв уровней 1…2, одно заклинание (<c>0x46e881</c>).
    /// </summary>
    private static void GuardForts(GameSession session, string playerId, AiForces forces, IRng rng, List<string> report)
    {
        var thieves = Intruders(session, playerId, leaderCategory: 5);
        if (thieves.Count == 0 && forces.Others.Count == 0) return;
        foreach (var fort in AiTactics.Forts(session.Map).Where(f => Same(f.Owner, playerId)).OrderBy(f => IdKey(f.Id)).ToList())
        {
            if (session.Map.Sites.FirstOrDefault(f => Same(f.Id, fort.Id)) is not { StackId.Length: 0 } live) continue;
            var at = AiTactics.Entrance(live);
            foreach (string id in forces.Others)
                if (session.Map.Stacks.TryGetValue(id, out var enemy) && Cheb(AiTactics.Position(session.Map, enemy), at) <= AiSpellRules.FortGuardRange)
                    Strike(session, playerId, enemy, rng, report, fort.Id);
            foreach (string id in thieves)
                if (session.Map.Stacks.TryGetValue(id, out var thief) && Cheb(AiTactics.Position(session.Map, thief), at) <= AiSpellRules.IntruderRange)
                    Choose(session, playerId, thief, AiTactics.Position(session.Map, thief), 1,
                           [new AiSpellWish(SpellCategory.Summon, 1, 2)], fallback: false, rng, report, fort.Id);
        }
    }

    /// <summary>
    /// Защита кристаллов (<c>0x46f93e</c>): чужие жезлоносцы (<see cref="Intruders"/>, <c>LEADER_CAT</c> 4); по
    /// кристаллам на равнине или в лесу, чья земля — земля расы игрока (<c>0x5e515e</c>), каждый жезлоносец ближе
    /// <see cref="AiSpellRules.IntruderRange"/>: ярус больше 1 — удар (<see cref="Strike"/>), иначе призыв уровней 2…3,
    /// одно заклинание (<c>0x46fd8d</c>).
    /// </summary>
    private static void GuardCrystals(GameSession session, string playerId, IRng rng, List<string> report)
    {
        var rodders = Intruders(session, playerId, leaderCategory: 4);
        if (rodders.Count == 0) return;
        var book = session.Book!;
        var player = session.Map.Players[playerId];
        if (!book.Economy.Races.TryGetValue(player.RaceId, out var race)) return;
        int own = race.Terrain;
        foreach (var crystal in session.Map.Objects.Values.Where(o => o.Kind == "CR").OrderBy(o => IdKey(o.Id)).ToList())
        {
            var tile = session.Map.At(crystal.X, crystal.Y);
            if (tile.Ground is not (Ground.Plain or Ground.Forest) || tile.Terrain != own) continue;
            foreach (string id in rodders)
            {
                if (!session.Map.Stacks.TryGetValue(id, out var rodder)) continue;
                var at = AiTactics.Position(session.Map, rodder);
                if (Cheb(at, (crystal.X, crystal.Y)) > AiSpellRules.IntruderRange) continue;
                if (AiSpellRules.Tier([.. Group(session, rodder).Select(u => u.XpKilled)]) > 1) Strike(session, playerId, rodder, rng, report, crystal.Id);
                else Choose(session, playerId, rodder, at, 1, [new AiSpellWish(SpellCategory.Summon, 2, 3)], fallback: false, rng, report, crystal.Id);
            }
        }
    }

    /// <summary>Чужие отряды (<c>0x46e6af</c>, <c>0x46fc33</c>) в порядке id: хозяин не игрок, приказ <c>+0xa8</c> —
    /// <c>L_NORMAL</c> (сам, не действующий), видимый (<c>+0x9b</c>), категория лидера такая-то, хозяин не союзник
    /// (<c>0x5f7ebd</c>). ponytail: жезлоносец движка — способность лидера <c>L_ROD</c> (<c>0x837618</c>), здесь —
    /// <c>LEADER_CAT</c> 4 (у всех таких в ванили <c>GLabi</c> 13).</summary>
    private static IReadOnlyList<string> Intruders(GameSession session, string playerId, int leaderCategory)
    {
        var map = session.Map;
        var book = session.Book!;
        var invisible = session.Looks.Stacks.Where(s => s.Invisible).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. map.Stacks.Values
            .Where(s => !Same(s.Owner, playerId) && s.Order == (int)StackOrder.Normal && !invisible.Contains(s.Id)
                        && map.Units.TryGetValue(s.LeaderId, out var l) && book.LeaderCategoryOf(l.TypeId) == leaderCategory
                        && !AiTactics.Allied(session, playerId, s.Owner))
            .OrderBy(s => IdKey(s.Id)).Select(s => s.Id)];
    }

    /// <summary>Удар по отряду (<c>0x46f08c</c>): число и уровни — <see cref="AiSpellRules.StrikePlan"/> по ярусу цели и
    /// дню; ищутся <c>L_ATTACK</c> и <c>L_SUMMON</c> этих уровней.</summary>
    private static void Strike(GameSession session, string playerId, MapStack enemy, IRng rng, List<string> report, string why)
    {
        int tier = AiSpellRules.Tier([.. Group(session, enemy).Select(u => u.XpKilled)]);
        var (count, min, max) = AiSpellRules.StrikePlan(tier, session.Map.Turn);
        Choose(session, playerId, enemy, AiTactics.Position(session.Map, enemy), count,
               [new AiSpellWish(SpellCategory.Attack, min, max), new AiSpellWish(SpellCategory.Summon, min, max)],
               fallback: false, rng, report, why);
    }

    /// <summary>
    /// Выбор и литьё (<c>0x46e92c</c>). Категории обходятся по кругу со случайной (<c>rng(число)</c>); годные
    /// заклинания каждой (<see cref="Fit"/>) — в общий список. Пусто: если это не запасной заход — запасной с одной
    /// <c>L_ATTACK</c> уровней 2…2 (или 2…(наименьший − 1) у записи атаки с наименьшим больше 2) и числом 1. Иначе —
    /// отбор старших (<see cref="AiSpellRules.KeepHighest"/>, оставить <paramref name="count"/>), число
    /// <c>min(rng(count) + 1, всего)</c>, перемешивание, обрезка, порядок <see cref="AiSpellRules.SummonsLast"/>; каждое
    /// бросается в клетку цели со вторым кошельком, призыву — приказ <c>L_ATTACK_STACK</c> на цель.
    /// </summary>
    private static void Choose(GameSession session, string playerId, MapStack target, (int X, int Y) at, int count,
                               IReadOnlyList<AiSpellWish> wishes, bool fallback, IRng rng, List<string> report, string why)
    {
        int start = rng.Next(wishes.Count);
        var spells = new List<Spell>();
        for (int k = 0; k < wishes.Count; k++)
            spells.AddRange(Fit(session, playerId, target, at, wishes[(start + k) % wishes.Count]));
        report.Add($"выбор {playerId} {target.Id} ({at.X},{at.Y}) у {why} {string.Join('+', wishes.Select(w => $"{w.Category}{w.MinLevel}-{w.MaxLevel}"))}"
                   + $" ×{count}: [{string.Join(' ', spells.Select(s => s.Id))}]");
        if (spells.Count == 0)
        {
            if (fallback) return;
            var wish = new AiSpellWish(SpellCategory.Attack, 2, 2);
            foreach (var w in wishes.Where(w => w.Category == SpellCategory.Attack).Take(1))
                if (w.MinLevel > 2) wish = wish with { MaxLevel = w.MinLevel - 1 };
            Choose(session, playerId, target, at, 1, [wish], fallback: true, rng, report, why);
            return;
        }
        AiSpellRules.KeepHighest(spells, s => s.Level, count);
        int n = Math.Min(rng.Next(count) + 1, spells.Count);
        AiSpellRules.Shuffle(spells, rng);
        spells.RemoveRange(n, spells.Count - n);
        foreach (var spell in AiSpellRules.ListSort(spells, (x, y) => AiSpellRules.SummonsLast(x.Category, y.Category)))
            Cast(session, playerId, spell, at, useSpellBank: true,
                 summonTarget: spell.Category == SpellCategory.Summon ? target.Id : null, report);
    }

    /// <summary>
    /// Годные заклинания одной записи (<c>0x46eda3</c>), в порядке списка известных: категория записи, на одну клетку
    /// (<c>AREA</c> 1: не вся карта <c>0x582b5a</c> и не площадь <c>0x582b6b</c>), призыв — не иллюзии (<c>0x438ff3</c>),
    /// уровень в диапазоне, против цели годится по иммунитетам (<see cref="WorksOn"/>), бросить можно — в клетку цели,
    /// призыв — в клетку, которую ему найдёт литьё.
    /// </summary>
    private static IEnumerable<Spell> Fit(GameSession session, string playerId, MapStack target, (int X, int Y) at, AiSpellWish wish)
    {
        var book = session.Book!;
        foreach (var spell in Known(session, playerId))
        {
            if (spell.Category != wish.Category || spell.Area != 1) continue;
            if (spell.Category == SpellCategory.Summon && book.CategoryOf(spell.SummonUnitId) == UnitCategory.Illusion) continue;
            if (spell.Level < wish.MinLevel || spell.Level > wish.MaxLevel) continue;
            if (!WorksOn(session, target.UnitIds, spell)) continue;
            // Призыв проверяется в клетке постановки (0x5edded); литьё ищет её снова — по карте на момент литья.
            var cell = spell.Category == SpellCategory.Summon ? session.SummonCell(at.X, at.Y, spell.SummonUnitId) ?? (-1, -1) : at;
            if (session.SpellRefusal(playerId, spell.Id, cell.X, cell.Y, useSpellBank: true) is not null) continue;
            yield return spell;
        }
    }

    /// <summary>
    /// Годится ли заклинание против отряда по иммунитетам (<c>0x476c55</c>): атака — по источнику <c>DAMAGE_SRC</c>,
    /// призыв — по источнику и классу первой атаки призываемого; юнит с иммунитетом не <c>L_NOTIMMUNE</c> к чему-то из
    /// этого считается; доля — <see cref="AiSpellRules.FewImmune"/>. Прочие категории годятся всегда.
    /// </summary>
    private static bool WorksOn(GameSession session, IReadOnlyList<string> units, Spell spell)
    {
        var rules = session.Book!.Rules;
        int source;
        AttackClass? cls = null;
        if (spell.Category == SpellCategory.Attack) source = spell.DamageSource;
        else if (spell.Category == SpellCategory.Summon && rules.ImplOf(spell.SummonUnitId)?.Attack is { } attack)
            (source, cls) = (attack.Source, attack.Class);
        else return true;
        int immune = 0, total = 0;
        foreach (string id in units)
        {
            if (!session.Map.Units.TryGetValue(id, out var mu)) continue;
            total++;
            var u = MapBattle.Unit(mu, 0, rules, isAttacker: false);
            var (bySource, byClass) = Immunity.LevelsFor(u, source, cls ?? AttackClass.Damage);
            if (bySource != ImmunityLevel.NotImmune || (cls is not null && byClass != ImmunityLevel.NotImmune)) immune++;
        }
        return AiSpellRules.FewImmune(immune, total);
    }

    private static bool Cast(GameSession session, string playerId, Spell spell, (int X, int Y) at, bool useSpellBank,
                             string? summonTarget, List<string> report)
    {
        string? why = session.CastSpell(playerId, spell.Id, at.X, at.Y, useSpellBank, summonTarget);
        report.Add(why is null ? $"заклинание {playerId} {spell.Id} ({at.X},{at.Y})" : $"заклинание {playerId} {spell.Id} ({at.X},{at.Y}) отказ: {why}");
        return why is null;
    }

    /// <summary>Известные игроку заклинания в порядке <c>CPlayerKnownSpells</c>.</summary>
    private static IEnumerable<Spell> Known(GameSession session, string playerId) =>
        session.Map.Players.TryGetValue(playerId, out var p)
            ? p.KnownSpells.Select(id => session.Book!.Spells.GetValueOrDefault(id)).OfType<Spell>()
            : [];

    /// <summary>Юниты группы отряда (павшие тоже): текущее и наибольшее здоровье, опыт за убийство выросшего типа.</summary>
    private static IEnumerable<(MapUnit Unit, int HpMax, int XpKilled)> Group(GameSession session, MapStack stack) =>
        Group(session, stack.UnitIds);

    private static IEnumerable<(MapUnit Unit, int HpMax, int XpKilled)> Group(GameSession session, IReadOnlyList<string> units)
    {
        var rules = session.Book!.Rules;
        foreach (string id in units)
            if (session.Map.Units.TryGetValue(id, out var mu))
                yield return (mu, session.HpMax(mu), MapBattle.Unit(mu, 0, rules, isAttacker: false).Type.XpKilled);
    }

    private static uint IdKey(string id) =>
        uint.TryParse(id[^4..], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint k) ? k : 0;

    private static int Cheb((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
