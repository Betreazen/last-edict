using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Battle;

/// <summary>Очередь раунда: инициатива атаки + rng(BAT_INIT), по убыванию (battle-rules-survey.md
/// §1, globalvariables.h:37-38). Ранг доверия:
/// <list type="bullet">
/// <item>по броску на живого юнита, мёртвый не тратит — ранг 1 (раунд 2 corpus/20260906-v2: один
/// живой, один бросок, 5/5 боёв);</item>
/// <item><b>порядок раздачи бросков И правило ничьей — порядок массива
/// <c>BattleMsgData::unitsInfo</c>, то есть порядок <see cref="BattleState.Units"/>: сортировка
/// устойчивая, отдельного правила ничьей у движка нет. Ранг 1 по корпусу.</b>
/// <para>Раздача видна в журнале прямо: перед каждым <c>rng(10)</c> стоит <c>attack_get</c> того
/// юнита, чью инициативу движок сейчас считает, и идут они ровно в порядке списка <c>units</c>
/// снимка (corpus/20260909-arena2/map-001.jsonl, seq 98489-98496).</para>
/// <para>Ничью снимает харнесс очереди (TurnOrderHarnessTests, сверка построенной очереди с полем
/// <c>order</c> журнала на всём корпусе): 534 очереди из 127 боёв, это правило — 534 совпадения и
/// 0 расхождений, против 494/40 у прежнего «сторона нападающего, затем слот», 471/63 у «по
/// возрастанию id», 464/70 у «слот, затем сторона» и 427/107 у обратного порядка массива; ничьи
/// есть в 107 очередях из 534, так что гипотезы корпусом различимы, а не совпадают по случайности.</para>
/// <para>Порядок <c>unitsInfo</c> — это НЕ «сторона+слот»: в corpus/20260906-a массив идёт по
/// слотам 1,4,0,2 внутри каждой стороны. Стороны при этом всегда подряд — сперва нападающие,
/// потом обороняющиеся (127 боёв корпуса, исключений нет). Для порта это значит, что
/// <see cref="BattleState.Units"/> обязан заполняться в порядке отрядов, а не сортировкой по
/// слоту: сортировка сместит раздачу бросков и развалит очередь.</para></item>
/// <item>инициатива превращённого юнита берётся у текущей формы, а не у исходного типа — здесь не
/// смоделировано и корпусом не сверено (харнесс пропускает такие построения с причиной): 10
/// построений из 544. Снимает зонд, отдающий тип текущей формы в <c>battle_units</c>.</item>
/// </list>
/// <para><b>Last Edict (Р4, Р17):</b> бросок и порядок разделены. Случайная часть раунда (<see cref="Roll"/>) бросается заранее —
/// <c>BattleSim</c> бросает её для раунда N+1 в начале раунда N — и дальше не меняется; порядок (<see cref="Order"/>) считается по
/// характеристикам на миг построения, поэтому замедление, превращение, гибель, призыв и воскрешение сразу двигают и полосу следующего
/// раунда (<see cref="Order"/> без <c>commit</c>). <see cref="Build"/> — бросок и порядок разом, как у движка.</para></summary>
public static class TurnOrder
{
    /// <summary>Встанет ли юнит в очередь построения: на поле и не нажал «Бежать» в отложенный ход.</summary>
    public static bool Queued(BattleUnit unit) => !unit.RetreatingAfterWait && unit.OnField;

    /// <summary>Случайная часть инициативы — <c>rng(BAT_INIT)</c> — тем, кто встанет в очередь (<see cref="Queued"/>) и ещё без броска, в
    /// порядке <see cref="BattleState.Units"/> (порядок раздачи бросков движка).</summary>
    public static void Roll(BattleState state, IRng rng, RulesConstants c, IDictionary<string, int> rolls)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rolls);
        foreach (var u in state.Units)
            if (Queued(u) && !rolls.ContainsKey(u.Id)) rolls[u.Id] = rng.Next(c.BatInit);
    }

    /// <summary>Инициатива юнита при случайной части <paramref name="roll"/>: база атаки с цепочкой модификаторов плюс бросок, и лишь с
    /// суммы снимается процент LowerInitiative (см. <see cref="Build"/>).</summary>
    public static int Initiative(BattleUnit u, int roll, RulesConstants c)
    {
        ArgumentNullException.ThrowIfNull(u);
        int total = Units.EffectiveStats.OfAttack(u.Attack, u.Chain).Attack.Initiative + roll;
        if (u.LowerInitiativeLevel > 0) total -= total * c.LowerInitiativePercent(u.LowerInitiativeLevel) / 100;
        return total;
    }

    /// <summary>Очередь по броскам <paramref name="rolls"/> (нет броска — 0), по убыванию инициативы, ничья — порядок
    /// <see cref="BattleState.Units"/>. <paramref name="commit"/> — это построение раунда: «Бежать» в отложенный ход теряет бит, конечное
    /// LowerInitiative гаснет; без него — только взгляд вперёд, состояние не меняется.</summary>
    public static IReadOnlyList<BattleUnit> Order(BattleState state, IReadOnlyDictionary<string, int> rolls, RulesConstants c, bool commit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rolls);
        var scored = new List<(BattleUnit Unit, int Initiative)>();
        foreach (var u in state.Units)
        {
            if (u.RetreatingAfterWait)
            {
                if (commit) u.RetreatingAfterWait = false;
                continue;
            }
            if (!u.OnField) continue;
            scored.Add((u, Initiative(u, rolls.GetValueOrDefault(u.Id), c)));
            if (commit && u.LowerInitiativeLevel > 0 && !u.LowerInitiativeLong) u.LowerInitiativeLevel = 0;
        }
        // OrderByDescending в LINQ устойчив: равные значения остаются в порядке unitsInfo — правило ничьей движка.
        return scored.OrderByDescending(x => x.Initiative).Select(x => x.Unit).ToList();
    }

    /// <summary>Бросок и построение разом — очередь движка (<c>0x62457c</c>).
    /// <para>Нажавший «Бежать» в отложенный ход (бит 7) пропускает это построение и бит теряет — первая проверка цикла 0x62457c
    /// (0x624637 → 0x62464e), раньше павших и ушедших; отступивший броска не тратит и в очередь не попадает (corpus/20260906-c, seq
    /// 53208-53210), собравшийся бежать (бит 29) ещё ходит. Бросок 0x6246e7 прибавляется к инициативе атаки, затем при бите 13 (0x6246fa)
    /// из суммы вычитается сумма·BAT_LOWER_INI/100 (0x5838c0) и, если нет бита 14 (Long, 0x624739), бит 13 гасится тут же (0x624752):
    /// конечное снижение живёт ровно до ближайшего построения очереди (passive-001 [9719866]).</para></summary>
    public static IReadOnlyList<BattleUnit> Build(BattleState state, IRng rng, RulesConstants c)
    {
        var rolls = new Dictionary<string, int>(StringComparer.Ordinal);
        Roll(state, rng, c, rolls);
        return Order(state, rolls, c, commit: true);
    }
}
