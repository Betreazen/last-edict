using D2.Rules.Battle;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>Входы силы для атаки отряда (<c>0x5dda25</c>): A — сила цели, B — своя сила против числа юнитов цели,
/// C — поддержка цели соседями из кэша, <see cref="CanDamage"/> — своя группа может ранить цель (<c>0x5dcd53</c>).</summary>
public sealed record AiAttackStrength(double A, double B, double C, bool CanDamage)
{
    /// <summary>Итог <see cref="AiTacticScore.Strength"/>.</summary>
    public double P => AiTacticScore.Strength(A, B, C, CanDamage);
}

/// <summary>
/// Атаки ИИ из карты (срез 6, <c>docs/ai-attack.md</c>): сила групп, кэш отрядов-целей и входы <c>0x5dda25</c>.
/// </summary>
public static partial class AiTactics
{
    /// <summary>Вид записи цели (<c>0x4542f2</c> <c>+4</c>): 0 — хозяин нейтрал (<c>0x5e5358</c>), 1 — лидер-призыв
    /// (<c>0x431d8c</c>, <c>IUsSummon</c>), 2 — прочие.</summary>
    public static int TargetKind(GameSession session, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        if (NeutralOwner(session, stack.Owner)) return 0;
        string type = session.Map.Units.TryGetValue(stack.LeaderId, out var leader) ? leader.TypeId : "";
        return UnitCategory.IsSummonImpl(session.Book!.CategoryOf(type)) ? 1 : 2;   // иллюзия — тоже IUsSummon
    }

    /// <summary>Хозяин нейтрален (<c>0x5e5358</c>): игрока нет или его раса не из играбельных (<c>0x5e53a6</c>).</summary>
    public static bool NeutralOwner(GameSession session, string playerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        return !session.Map.Players.TryGetValue(playerId, out var p)
               || !session.Book!.Economy.Races.TryGetValue(p.RaceId, out var race) || !race.Playable;
    }

    /// <summary>
    /// Сила группы <c>0x5dcec7(карта, id, безГорода, юниты, врагов, 0, безКрепости = 0, полноеЗдоровье = 0)</c>:
    /// сумма <see cref="AiStrength.Unit"/> по живым юнитам (здоровье текущее, реализация выросла уровнем, цепочка
    /// модификаторов — <see cref="MapBattle.Unit"/>); броня укрепления (<c>0x5dcf60</c>) — если отряд в деревне или
    /// столице и не «без города». <paramref name="fullHp"/> — последний флаг (<c>полноеЗдоровье = 1</c>): здоровье юнитов
    /// считается полным (так зовут <c>0x4692bf</c> у тренера).
    /// </summary>
    public static double GroupStrength(GameSession session, MapStack stack, bool ignoreCity, int enemies, bool fullHp = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stack);
        var book = session.Book!;
        int fortArmor = ignoreCity ? 0 : AiFortArmor(session, stack.Inside);
        return Fighters(session, stack.UnitIds, fullHp).Sum(u => AiStrength.Unit(u, ignoreCity, enemies, book.Rules.Constants, fortArmor));
    }

    /// <summary>Броня укрепления для силы ИИ (<c>0x5dcf60</c>): столица — <c>PROT_CAP</c>, деревня — <c>PROT_уровень</c>,
    /// прочее — 0.</summary>
    public static int AiFortArmor(GameSession session, string siteId)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (siteId.Length == 0 || session.Map.Sites.FirstOrDefault(f => Same(f.Id, siteId)) is not { } fort) return 0;
        var book = session.Book!;
        return fort.Kind switch
        {
            SiteKind.Capital => book.AiCapitalArmor,
            SiteKind.Village when fort.Size is { } level && level >= 1 && level <= book.AiCityArmor.Count => book.AiCityArmor[level - 1],
            _ => 0,
        };
    }

    /// <summary>Сила набора юнитов (<c>0x5dcec7</c> по вектору id): сумма <see cref="AiStrength.Unit"/> по живым.</summary>
    public static double UnitsStrength(GameSession session, IEnumerable<string> unitIds, bool ignoreCity, int enemies, int fortArmor)
    {
        ArgumentNullException.ThrowIfNull(session);
        var constants = session.Book!.Rules.Constants;
        return Fighters(session, unitIds).Sum(u => AiStrength.Unit(u, ignoreCity, enemies, constants, fortArmor));
    }

    private static IEnumerable<BattleUnit> Fighters(GameSession session, IEnumerable<string> unitIds, bool fullHp = false)
    {
        var rules = session.Book!.Rules;
        foreach (string id in unitIds)
            if (session.Map.Units.TryGetValue(id, out var mu) && mu.Hp > 0) yield return Fighter(rules, mu, fullHp);
    }

    /// <summary>Юнит для силы <c>0x5dd06c</c>: реализация выросшая уровнем, здоровье текущее (<paramref name="fullHp"/> — полное),
    /// броня, атака и иммунитеты — через цепочку модификаторов.</summary>
    internal static BattleUnit Fighter(D2.Rules.Battle.GameRules rules, MapUnit mu, bool fullHp = false)
    {
        var u = MapBattle.Unit(mu, 0, rules, isAttacker: false);
        if (fullHp) u.Hp = u.HpMax;
        if (u.Modifiers.Count == 0) return u;
        // Сила 0x5dd06c читает юнита через обёртки модификаторов (IUsSoldier реализации): броня, атака и иммунитеты
        // (вес 0x5dd21b) действующие. Корпус: A атаки отряда human 816 из 816 (без цепочки 785), счёт атаки крепости у
        // стража столицы с модификаторами сложности (−20 % здоровья, −10 брони, −10 % урона) сходится только так; иммунитеты
        // от модификаторов (варды, навыки лидера) — с 25.09: human, своя сила B 766 → 818, руины B 97 → 136.
        var eff = D2.Rules.Units.EffectiveStats.Of(u.Type, u.Modifiers);
        var type = u.Type with
        {
            Armor = eff.Armor,
            ImmunitiesBySource = eff.Immunities.Count > 0 ? [.. u.Type.ImmunitiesBySource ?? [], .. eff.Immunities] : u.Type.ImmunitiesBySource,
            ImmunitiesByClass = eff.ClassImmunities.Count > 0 ? [.. u.Type.ImmunitiesByClass ?? [], .. eff.ClassImmunities] : u.Type.ImmunitiesByClass,
        };
        return new BattleUnit
        {
            Id = u.Id, Type = type,
            Attack = D2.Rules.Units.EffectiveStats.OfAttack(u.Attack, u.Modifiers).Attack, Attack2 = u.Attack2,
            Attacker = false, Slot = 0, HpMax = u.HpMax, Hp = u.Hp, Xp = u.Xp, Modifiers = u.Modifiers,
        };
    }

    /// <summary>
    /// Кэш отрядов-целей (<c>0x45e7cb</c> с фильтром <c>0x45e98a</c> → <c>0x45ea2e</c>, вектор <c>+0x0c</c>
    /// <c>CMidServerLogicData2</c>): не сам отряд (<c>+0x18</c>); не своя подраса (<c>+0x90</c>), если только подраса своего
    /// отряда не «нейтральной» категории (<c>0x5f7189(карта, L_NEUTRAL)</c> — у главного нейтрала); хозяина фильтр не сравнивает,
    /// так что нейтрал подрасы, скажем, эльфов берёт в цели и нейтралов других подрас (разбор 24.09: прежде порт отбрасывал всех
    /// своего хозяина); не союзник (<c>0x5f7ebd</c>), не невидимый (<c>+0x9b</c>, <c>0x45eac1</c>); отряд в крепости или руине — по позиции входа; цель приказа — первой
    /// (расстояние 0), прочие — по евклиду от позиции отряда; до трёх, исключённые кэшем пропускаются; у «главного»
    /// отряда (радиус <see cref="AiTacticTurn.Radius"/>, ставит <see cref="Candidates"/>) цели у чужих столиц — нет
    /// (<c>0x45f5f1</c>).
    /// Подраса типа лидера — <see cref="SameLeaderSubrace"/>. У лидера «только по воде» (<c>0x45e78c</c>, слот <c>+0x70</c>)
    /// цель должна стоять на воде (<c>0x45e9db</c>: грунт клетки <c>+4/+8</c> — вода): legions, водный берсерк
    /// <c>S073KC0029</c> — в кэше только отряды на воде, по всей карте.
    /// </summary>
    public static IReadOnlyList<MapStack> StackTargets(GameSession session, AiTacticTurn turn, MapStack stack)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(stack);
        var map = session.Map;
        var self = Position(map, stack);
        bool waterOnly = session.WaterOnly(stack);
        var (_, orderTarget) = OrderOf(stack);
        return [.. map.Stacks.Values
            .Where(s => !Same(s.Id, stack.Id))
            .Where(s => !waterOnly || session.IsWater(s.X, s.Y))
            .Where(s => StackTargetOk(session, stack, s))
            .Where(s => !turn.Excluded.Contains(s.Id))
            .Select(s => (Stack: s, Pos: Position(map, s)))
            .Where(p => !RadiusDrops(turn, p.Stack.Id, p.Pos))
            .OrderBy(p => Same(p.Stack.Id, orderTarget) ? 0.0 : AiTacticScore.SetDistance(p.Pos.X - self.X, p.Pos.Y - self.Y))
            .ThenBy(p => p.Stack.Id, StringComparer.OrdinalIgnoreCase)
            .PickThree(p => turn.WinTargets.Contains(p.Stack.Id))
            .Select(p => p.Stack)];
    }

    /// <summary>Отряд годен в цели атакующему (<c>0x45ea2e</c>, ранг 1): не своя подраса (<c>+0x90</c>), если только подраса
    /// атакующего не «нейтральной» категории; подраса типа лидера — <see cref="SameLeaderSubrace"/>; не союзник (<c>0x5f7ebd</c>),
    /// не невидимый (<c>+0x9b</c>). Его же зовёт охрана крепости (<c>0x45ed86</c>).</summary>
    private static bool StackTargetOk(GameSession session, MapStack attacker, MapStack s)
    {
        bool neutralSubrace = session.Map.Subraces.TryGetValue(attacker.Subrace, out int kind) && kind == NeutralSubrace;
        return (neutralSubrace || !Same(s.Subrace, attacker.Subrace)) && !SameLeaderSubrace(session, attacker, s)
               && !Allied(session, attacker.Owner, s.Owner) && !session.Invisible(s.Id);
    }

    /// <summary>Подраса типа лидера (<c>0x5edb9c</c> → <c>IUsSoldier</c> слот <c>+0x10</c>) совпала, и это не
    /// «нейтральная» подраса (<c>[0x837b00]</c>, <c>LSubRace</c> 5): такой отряд в кэш не идёт (<c>0x45ea2e</c>).</summary>
    private static bool SameLeaderSubrace(GameSession session, MapStack a, MapStack b)
    {
        var book = session.Book!;
        int sa = session.Map.Units.TryGetValue(a.LeaderId, out var la) ? book.SubraceOf(la.TypeId) : 0;
        int sb = session.Map.Units.TryGetValue(b.LeaderId, out var lb) ? book.SubraceOf(lb.TypeId) : 0;
        return sa != 0 && sa == sb && sa != NeutralSubrace;
    }

    /// <summary><c>LSubRace</c> «нейтральный» — категория <c>0x837af8</c> (по ней же <c>0x5f7189</c> ищет игрока-нейтрала).</summary>
    private const int NeutralSubrace = 5;

    /// <summary>
    /// Входы <c>0x5dda25</c> у атаки отряда (<c>0x453755</c>):
    /// <list type="bullet">
    /// <item>B — своя сила «без города» против числа юнитов цели (<c>0x4534e3</c>: массив на 1..6 врагов);</item>
    /// <item>A — сила цели с бронёй её укрепления против числа своих юнитов (<c>0x4542f2</c>, <c>+0x10</c>);</item>
    /// <item>C — <c>0x4543bc</c>: по отрядам кэша, кроме цели, вида 2 и ближе 10 клеток к цели по обеим осям —
    /// сумма их A, если хоть один сильнее цели, иначе 0.</item>
    /// </list>
    /// Число юнитов — длина вектора отряда (<c>0x5f4441</c>), павшие в нём тоже.
    /// </summary>
    public static AiAttackStrength AttackStrength(GameSession session, MapStack own, MapStack target,
                                                  IReadOnlyList<MapStack> cache)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(cache);
        var map = session.Map;
        int ownCount = own.UnitIds.Count;
        double b = GroupStrength(session, own, ignoreCity: true, target.UnitIds.Count);
        double a = GroupStrength(session, target, ignoreCity: false, ownCount);
        var at = Position(map, target);
        double sum = 0, max = 0;
        foreach (var x in cache)
        {
            if (Same(x.Id, target.Id) || TargetKind(session, x) != 2) continue;
            var p = Position(map, x);
            if (Math.Abs(at.X - p.X) >= SupportRange || Math.Abs(at.Y - p.Y) >= SupportRange) continue;
            double ax = GroupStrength(session, x, ignoreCity: false, ownCount);
            sum += ax;
            if (ax > max) max = ax;
        }
        double c = a < max ? sum : 0;
        var mine = Fighters(session, own.UnitIds).ToList();
        var theirs = Fighters(session, target.UnitIds).ToList();
        return new AiAttackStrength(a, b, c, AiStrength.CanDamage(mine, theirs));
    }

    /// <summary>Поддержка цели считается у отрядов ближе этого по обеим осям (<c>0x454446</c>: −10 &lt; d &lt; 10).</summary>
    private const int SupportRange = 10;
    /// <summary>
    /// Атака отряда (<c>AttackStack</c>, <c>AttackParty</c>, <c>AttackStackRoam</c>): построитель
    /// <c>0x4533fb</c> — по кэшу отрядов-целей (<see cref="StackTargets"/>), невидимые мимо; точка — позиция цели
    /// (<c>0x5ed9b2</c>); счёт <c>0x453d15</c> → <see cref="AiTacticScore.AttackStack"/> с силой
    /// <see cref="AttackStrength"/>. ponytail: план операции (<c>CAiTactic +0x5158</c>) и главный враг
    /// (<c>+0x51c0</c>) не разобраны — пусты, поэтому цель-призыв (вид записи 1) всегда выбрасывается; «отрядов с
    /// особым лидером» (<c>0x466f57</c>) — 0; мешок в локации нейтрала не ищется. Заклинания и подмога перед атакой —
    /// <see cref="WithPreparations"/> (второй кошелёк — цель из чужих сильнейших и хозяин её человек, <c>0x45422e</c>).
    /// </summary>
    private static IEnumerable<(AiTacticOption, double)> AttackStacks(GameSession session, AiTacticTurn turn, MapStack stack,
                                                                      AiActionType type)
    {
        var map = session.Map;
        var self = Position(map, stack);
        // CAiAttackParty, слот 4 (0x450a7d): действующий приказ L_ATTACK_STACK, а отряда-цели приказа нет — смена приказа.
        if (type == AiActionType.AttackParty && OrderOf(stack) is (StackOrder.AttackStack, var goal) && !map.Stacks.ContainsKey(goal)
            && ChangeOrderOption(session, stack, goal) is { } change)
            yield return change;
        if (!GroupCanFight(session, stack)) yield break;   // 0x45341e
        var cache = StackTargets(session, turn, stack);
        bool attackerNeutral = NeutralOwner(session, stack.Owner);
        var kind = type switch
        {
            AiActionType.AttackStackBezerk => AttackEvalKind.Bezerk,
            AiActionType.AttackStackRoam => AttackEvalKind.Roam,
            AiActionType.AttackParty => AttackEvalKind.Party,
            _ => AttackEvalKind.Normal,
        };
        int fieldLeaders = FieldLeaders(session, stack.Owner);
        foreach (var target in cache)
        {
            var at = Position(map, target);
            var strength = AttackStrength(session, stack, target, cache);
            bool neutral = NeutralOwner(session, target.Owner);
            // «Нейтрал сторожит мешок» (0x4538a1) зовётся с записью ЦЕЛИ (та же, чей хозяин проверен 0x5e5358 у 0x4537dc):
            // приказ и коробка поиска — её. До 25.09 порт брал атакующего.
            bool guardsBag = neutral && AiTacticScore.GuardsBag(OrderOf(target).Order, bagInLocation: false, BagInBox(map, at));
            var owner = map.Players.GetValueOrDefault(target.Owner);
            int today = turn.Memo.Status(stack.Id, target.Id, at.X, at.Y, turn.Day);
            int yesterday = turn.Memo.Status(stack.Id, target.Id, at.X, at.Y, turn.Day - 1);
            bool captured = turn.CapturedByOther(stack.Id, target.Id);
            bool enemy = turn.MainEnemy.Length > 0 && Same(target.Owner, turn.MainEnemy);
            var mods = new AttackModifierFacts(TargetOwnerIsTacticEnemy: enemy, InOperationTargets: false, IsMoveToTarget: false,
                                               RecordKind1: TargetKind(session, target) == 1, TargetInside: target.Inside.Length > 0,
                                               TargetNeutral: neutral, CapturedByOther: captured, StatusToday: today,
                                               StatusYesterday: yesterday);
            bool human = owner?.IsHuman ?? false;
            var facts = new AttackStackFacts(today, neutral, human, neutral,
                                             AiTacticScore.Influence(turn.Grid, at.X, at.Y), Cheb(self, at),
                                             AiTacticScore.AttackModifiers(1.0, mods), strength.P, guardsBag,
                                             NeutralCount: fieldLeaders, attackerNeutral, kind);
            var side = new AttackSide(strength.P, AiTacticScore.ThresholdPassed(strength.P, attackerNeutral, human, neutral),
                                      human, neutral, target.UnitIds, target.Inside.Length == 0,
                                      SpellBank: human && turn.Forces.Others.Contains(target.Id, StringComparer.OrdinalIgnoreCase));
            var attack = new AiTacticOption(type, stack.Id, target.Id, at.X, at.Y);
            if (kind == AttackEvalKind.Normal)
            {
                if (Made(session, turn, stack, attack, side.Passed) is not { } made) continue;
                attack = made;
            }
            foreach (var c in WithPreparations(session, turn, stack, attack, AiTacticScore.AttackStack(turn.Weights, facts), side))
                yield return c;
        }
    }

    /// <summary>
    /// Фабрика действия обычного оценщика атаки (слот <c>0x14</c>: отряд <c>0x45129b</c>, крепость <c>0x450444</c>) после
    /// счёта. Порог силы взят — действие без «напрямую» и без усечения (N = 0): подход по пути к цели и бой по прибытии. Не
    /// взят — только если отряду разрешены слабые атаки (<see cref="AiTacticTurn.WeakAttacks"/>, слот <c>0x24</c> обёртки),
    /// иначе null и кандидата нет вовсе — планировщик подготовки (<c>0x471ec7</c>) зовётся уже после фабрики; разрешены —
    /// порог N = <c>0x450558</c> (<see cref="GameSession.DirectReach"/>: цель под туманом — обзор лидера, видна — 10): Чебышёв ≤ N — «напрямую» (N = 0), дальше —
    /// подход с усечением N. У бродяги, берсерка и «на отряд приказа» фабрики свои, без проверки и без «напрямую». Журнал
    /// human: слабых (p &lt; 0,5) кандидатов атаки в первой оценке отряда за день — 5 (все у других оценщиков), в повторных — 251.
    /// </summary>
    private static AiTacticOption? Made(GameSession session, AiTacticTurn turn, MapStack stack, AiTacticOption attack, bool passed)
    {
        if (passed) return attack;
        if (!turn.WeakAttacks) return null;
        int reach = session.DirectReach(stack, (attack.X, attack.Y));
        bool direct = Cheb(Position(session.Map, stack), (attack.X, attack.Y)) <= reach;
        return attack with { Direct = direct, Reach = direct ? 0 : reach };
    }

    /// <summary>Мешок в коробке поиска нейтрала (<see cref="AiTacticScore.BagBox"/>).</summary>
    private static bool BagInBox(MapState map, (int X, int Y) at)
    {
        var (x0, x1, y0, y1) = AiTacticScore.BagBox(at.X, at.Y, map.Size);
        return map.Sites.Any(b => b.Kind == SiteKind.Bag && !b.Gone && b.X >= x0 && b.X < x1 && b.Y >= y0 && b.Y < y1);
    }
    /// <summary>
    /// Слот <c>0x30</c> <c>IAiTacticInternal</c> (<c>0x466f57</c>, зовёт счёт атаки у <c>0x453840</c> с хозяином атакующего — слот
    /// <c>0x38</c> его обёртки): отряды карты этого игрока, у лидера (<c>+0x28</c>, <c>IUsStackLeader</c>) категория
    /// <c>[0x837a20]</c>/<c>[0x837a30]</c>/<c>[0x837a40]</c> — первые три из пяти глобалов <c>LleadC</c> (<c>0x837a18</c>…), по порядку
    /// объявления боец, разведчик, маг (1–3; жезл 4 и знатный 5 — нет; ранг 2), и отряд не помечен <c>AI_IGNORE</c> (<c>+0x9c</c>).
    /// Больше одного при силе 1 — счёт нейтральной цели ×0,75 (<see cref="AiTacticScore.AttackStack"/>).
    /// </summary>
    private const int FieldLeaderCategoryMax = 3;   // LleadC L_MAGE; жезл 4 и знатный 5 — нет

    public static int FieldLeaders(GameSession session, string ownerId)
    {
        ArgumentNullException.ThrowIfNull(session);
        var map = session.Map;
        var book = session.Book!;
        return map.Stacks.Values.Count(s => Same(s.Owner, ownerId) && !s.AiIgnore && map.Units.TryGetValue(s.LeaderId, out var leader)
                                            && book.LeaderCategoryOf(leader.TypeId) is >= 1 and <= FieldLeaderCategoryMax);
    }
}
