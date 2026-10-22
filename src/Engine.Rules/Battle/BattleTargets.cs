using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Возможные цели действия — список, который <c>updateBattleActions</c> (GOG <c>0x623d93</c>)
/// строит для атаки ходящего; действие <c>Attack</c> доступно, если список не пуст. Им пользуется
/// политика ИИ (<see cref="BattleAi"/>). Ранг 1: разбор <c>docs/synthetic-maps/analysis/G-ai-battle.md</c>
/// §2 (сверено с корпусом — 2533 цели из 2533).</summary>
public static class BattleTargets
{
    /// <summary>Дистанция досягаемости «соседней» атаки (<c>0x65aad3</c>, для врага —
    /// <c>0x65ab4b</c>). Колонка: 1 — свой первый ряд, 0 — свой второй, 2 и 3 — первый и второй ряд
    /// врага; строка — <c>slot / 2</c>. <c>d = (|dy| + 2|dx|) &gt;&gt; 1</c>, для врага ещё <c>· |dx|</c>.</summary>
    public static int Distance(int from, int to, bool ally)
    {
        var (ax, ay) = Coord(from, own: true);
        var (bx, by) = Coord(to, own: ally);
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
        int d = (dy + 2 * dx) >> 1;
        return ally ? d : d * dx;
    }

    private static (int X, int Y) Coord(int slot, bool own) =>
        (own ? (Formation.IsFront(slot) ? 1 : 0) : (Formation.IsFront(slot) ? 2 : 3), Formation.Column(slot));

    /// <summary>Цели атаки <paramref name="attack"/> юнита <paramref name="unit"/> по возрастанию слота.
    /// Группа — своя для классов «по своим» (<see cref="Attacks.TargetsAllies"/>), иначе чужая; годность —
    /// <see cref="Eligible"/>. У «соседней» атаки по врагу (REACH 3, <c>0x65a8a8</c>): ходящий во втором
    /// ряду при живом, не ушедшем первом ряде своей группы целей не имеет (<c>0x65ac1c</c>); иначе
    /// остаются цели с минимальной <see cref="Distance"/>. ANY и ALL — все годные.
    /// <para>ponytail: статусы 0, 30 (скрыт) и 32 (<c>unitStatuses2</c>) модель не ведёт — в корпусе их нет.</para>
    /// <para><paramref name="human"/> — ходит человек без автобоя: список без фильтра <c>Always</c> (<see cref="Reachable"/>), у
    /// превращения себя — и сам ходящий (<c>0x660e11</c>).</para></summary>
    public static List<BattleUnit> Possible(BattleState state, BattleUnit unit, AttackType attack, bool human = false)
    {
        // TransformSelf: сам юнит в списке движка стоит отдельно (AiRareTargets.TransformSelfPossible), а
        // неотрицательные позиции — враги под запасную атаку (0x6613e1 отдаёт удар ей, когда цель — враг).
        // Ранг 2 по Lua (sub_5D36BF: findDamageAttackTargetsForAllReach берёт первую неотрицательную) и
        // журналу map-001 [1281528]: Волчий лорд бьёт врага S143UN000a.
        // Человеку (fillTargetsList 0x660e11, ранг 1) — цели запасной атаки и сам ходящий отрицательной позицией -(своя + 1): щелчок по
        // себе превращает, по врагу — удар запасной атакой (BattleSim.Striking). Без запасной атаки — только сам. Прежде себя в списке не
        // было — Повелитель волков превратиться не мог, а щелчок по врагу шёл превращением врага (партия 15.10, пометка 9).
        if (attack.Class == AttackClass.TransformSelf)
        {
            var alts = state.Rules?.AltAttackOf(attack) is { } alt ? Possible(state, unit, alt, human) : [];
            if (human && unit.OnField) alts.Add(unit);
            return alts;
        }
        if (attack.Class == AttackClass.Doppelganger)
        {
            // fillTargetsList 0x6619fa: пока не превращён (0x6618e9) и есть кого копировать — список копий,
            // иначе список запасной атаки.
            var copies = CopyTargets(state, unit, attack, human);
            if (copies.Count > 0) return copies;
            return state.Rules?.AltAttackOf(attack) is { } fallback ? Possible(state, unit, fallback, human) : [];
        }
        // Призыв: цели движка — позиции призыва (fillTargetsList 0x662ac6); нет ни одной (отряд полон, большие держат обе клетки) — список
        // пуст, Attack в наборе действий нет, ИИ защищается (партия 17.10: Лиф в полном отряде с двумя троллями).
        if (attack.Class == AttackClass.Summon && state.SummonCells(unit).Count == 0) return [];
        var reachable = Reachable(state, unit, attack);
        return human ? reachable : reachable.Where(u => !AiTargets.AlwaysImmune(u, attack.Source, attack.Class)).ToList();
    }

    /// <summary>Цели по досягаемости, до фильтра иммунитета (<c>0x65a646</c>/<c>0x65a777</c>/<c>0x65a8a8</c>).
    /// <para>Фильтр — хвост общего <c>fillTargetsList</c> <c>0x65a1a5</c> (его зовут заполнители списка всех
    /// классов, у TransformOther — слот 3 <c>0x6605d1</c>): когда игрок ходящего не человек или идёт автобой
    /// (<c>0x622f58</c>; человек — <c>0x62a980</c> → <c>0x62a1a0</c>, <c>0x65a3ed</c>), из готового списка
    /// выкидываются цели с <c>Always</c> к атаке (<c>isUnitImmuneToAttack</c> <c>0x65a528</c>, alwaysOnly = 1,
    /// <c>0x65a4cb</c> → erase <c>0x667170</c>). Ранг 1. Идёт ПОСЛЕ выбора ближайших у «соседней»: иммунный
    /// ближний ряд дальний не открывает. Журнал corpus/20260920-legions passive-001 [10830678]: S073UN01f3
    /// (TransformOther, Mind) против единственного врага S073UN025d (G000UU0024, Mind Always) — защита 4 хода
    /// из 4, а не удар. Ход человека без автобоя список не фильтрует (<see cref="Possible"/> с <c>human</c>): Верховный вампир (смерть)
    /// бьёт упырей с <c>Always</c> к смерти — удар есть, урона нет (партия 15.10, пометка 3: прежде у вампира не было ни одной цели).</para></summary>
    private static List<BattleUnit> Reachable(BattleState state, BattleUnit unit, AttackType attack)
    {
        bool ally = attack.Effect.TargetsAllies;
        var group = ally ? state.Allies(unit) : state.Enemies(unit);
        var found = group.Where(u => Eligible(unit, attack, u)).OrderBy(u => u.Slot).ToList();
        if (attack.Reach != AttackReach.Adjacent || ally || found.Count == 0) return found;
        if (Formation.IsBack(unit.Slot) && state.Allies(unit).Any(u => Formation.IsFront(u.Slot) && u.OnField)) return [];
        int min = found.Min(u => Distance(unit.Slot, u.Slot, ally: false));
        return found.Where(u => Distance(unit.Slot, u.Slot, ally: false) == min).ToList();
    }

    /// <summary>Годность цели (<c>0x645b7d</c> + <c>canPerform</c> класса). <see cref="AttackClass.Revive"/>
    /// берёт только павших, не ушедших и ещё не воскрешённых (<c>0x65c3a7</c>); остальные — только тех, кто на поле
    /// и проходит <see cref="Attacks.CanPerform"/>; лечение — <see cref="HealEligible"/>.</summary>
    public static bool Eligible(BattleUnit attacker, AttackType attack, BattleUnit u)
    {
        if (attack.Class == AttackClass.Revive) return Revivable(u);
        if (attack.Class == AttackClass.Heal) return HealEligible(attacker, attack, u);
        return u.OnField && Attacks.CanPerform(attacker, attack, u);
    }

    private static bool Revivable(BattleUnit u) => !u.Alive && !u.Retreated && !u.Revived;

    /// <summary><c>canPerform</c> лечения <c>0x65c086</c> (ранг 1): раненый живой — да; иначе, если лечение —
    /// ПЕРВАЯ атака юнита со второй атакой, решает вторая: Revive — павший годен по <c>0x65c3a7</c>, Cure — целый
    /// живой годен по <c>0x65c310</c>. Слот <c>+0x3c</c> лечения (<c>0x65c4d9</c>, <c>mov al,1</c>) пропускает
    /// павших мимо общей проверки <c>0x645b7d</c>. Журнал map-001 [1281528] seq 1286019: Иерофант поднимает
    /// павшего S143UN0007 (доля HP 0), а не лечит раненого S143UN0001 (35/65).</summary>
    private static bool HealEligible(BattleUnit attacker, AttackType attack, BattleUnit u)
    {
        if (u.Retreated) return false;
        if (u.OnField && u.Hp < u.HpMax) return Attacks.CanPerform(attacker, attack, u);
        if (!ReferenceEquals(attack, attacker.Attack)) return false;
        return attacker.Attack2?.Class switch
        {
            AttackClass.Revive => Revivable(u),
            AttackClass.Cure => u.OnField && Attacks.CanBeCured(u),
            _ => false,
        };
    }

    /// <summary>Копии двойника (<see cref="Copies"/>); уже превращённому (<c>cannotUseDoppelgangerAttack</c>
    /// <c>0x6618e9</c> — любой из битов 22..26) — пусто.</summary>
    public static List<BattleUnit> CopyTargets(BattleState state, BattleUnit unit, AttackType attack, bool human = false) =>
        unit.Transformed || unit.TransformedLong || unit.TransformedSelf || unit.TransformedDoppelganger || unit.LevelDrained
            ? [] : Copies(state, unit, attack, human);

    /// <summary>Не копируются никогда — прошитый список <c>0x661bdc</c> (строки <c>0x6f34f4</c>–<c>0x6f3524</c>).</summary>
    private static readonly HashSet<string> NotCopyable = new(StringComparer.OrdinalIgnoreCase)
        { "g000uu8003", "g000uu8006", "g000uu8103", "g000uu8106", "g000uu8150" };

    /// <summary>Кого может скопировать Doppelganger — <c>0x661a7a</c> (ранг 1). Свои и враги на поле, годные по
    /// <c>canPerform</c> <c>0x661e73</c>: малый, первая атака не Doppelganger (значит, и не сам ходящий), категория
    /// не Illusion/Guardian (<c>0x838170</c>/<c>0x8381a0</c>); минус прошитый <see cref="NotCopyable"/>; минус
    /// <c>Always</c> к атаке двойника (<c>0x65a1a5</c> → <c>0x65a528</c>, у ИИ и в автобое). Список движка —
    /// <c>std::set&lt;int&gt;</c>, враги в нём — <c>-(позиция+1)</c>, поэтому порядок обхода: враги по УБЫВАНИЮ
    /// слота, затем свои по возрастанию. Журнал: map-001 [1281528] копия — S143UN0002 (xp 15), а не S143UN000a
    /// (G000UU8150, xp 3360, в списке и к Mind Always).</summary>
    private static List<BattleUnit> Copies(BattleState state, BattleUnit unit, AttackType attack, bool human) =>
        state.Enemies(unit).OrderByDescending(u => u.Slot)
             .Concat(state.Allies(unit).OrderBy(u => u.Slot))
             .Where(u => u.OnField && u.Type.SizeSmall && u.Attack.Class != AttackClass.Doppelganger
                         && u.Type.Category is not (UnitCategory.Illusion or UnitCategory.Guardian)
                         && !NotCopyable.Contains(u.Type.Id)
                         && (human || !AiTargets.AlwaysImmune(u, attack.Source, attack.Class)))
             .ToList();
}

/// <summary>Gattacks.REACH — LattR.dbf.</summary>
public static class AttackReach
{
    public const int All = 1, Any = 2, Adjacent = 3;
}
