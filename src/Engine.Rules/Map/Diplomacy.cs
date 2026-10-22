namespace D2.Rules.Map;

/// <summary>
/// Вид цели атаки глазами обёртки <c>attack</c> (<c>0x5d96fa</c>): она различает цель по типу id —
/// отряд (<c>IdType::Stack</c> = <c>0x23</c>) и укрепление (<c>IdType::Fortification</c> =
/// <c>0x1d</c>); всякая иная цель даёт величину 0. Ранг 1 — дизассемблирование обёртки
/// (<c>docs/corpus-schema.md</c>, «Дипломатические отношения»).
/// </summary>
public enum BattleTarget
{
    Stack,
    Fortification,
    Other,
}

/// <summary>
/// Величины дипломатии — столбцы <c>GVars.dbf</c> одной строки (ранг 1, поля таблицы; смещения
/// геттеров в <c>CGlobalVariables</c>: <c>D_WAR +0xbc</c>, <c>D_NEUTRAL +0xc0</c>,
/// <c>D_GOLD +0xc4</c>, <c>D_MK_ALLY +0xc8</c>, <c>D_ATTAK_SC +0xcc</c>, <c>D_ATTAK_FO +0xd0</c>,
/// <c>D_ATTAK_PC +0xd4</c>, <c>D_ROD +0xd8</c>, <c>D_BK_ALLY +0xdc</c>, <c>D_REF_ALLY +0xe0</c>,
/// <c>D_NOBLE +0xe4</c>).
/// </summary>
/// <param name="Peace"><c>D_PEACE</c> — верх шкалы (100). Обрезку в мутаторе делает литерал, а не
/// это поле (<see cref="Diplomacy.Max"/>).</param>
/// <param name="War"><c>D_WAR</c> — война при отношении не больше этого (33).</param>
/// <param name="Neutral"><c>D_NEUTRAL</c> — нейтралитет не больше этого, мир выше (66).</param>
/// <param name="GoldPerPoint"><c>D_GOLD</c> — сколько золота дани даёт одно очко отношений (50).</param>
/// <param name="MakeAlly"><c>D_MK_ALLY</c> — присваивается при заключении союза (100).</param>
/// <param name="BreakAlly"><c>D_BK_ALLY</c> — присваивается при разрыве (10).</param>
/// <param name="RefuseAlly"><c>D_REF_ALLY</c> — присваивается при отказе от союза (50).</param>
/// <param name="AttackAtPeace"><c>D_ATTAK_PC</c> — присваивается при атаке из мира (66).</param>
/// <param name="AttackStack"><c>D_ATTAK_SC</c> — прибавка за атаку отряда (−7).</param>
/// <param name="AttackFort"><c>D_ATTAK_FO</c> — прибавка за атаку укрепления (−10).</param>
/// <param name="RodDestroyed"><c>D_ROD</c> — прибавка за снос чужого жезла с виновником (−15).</param>
/// <param name="NobleAction"><c>D_NOBLE</c> — прибавка за успешное действие вора (−5).</param>
public sealed record DiplomacyRules(int Peace, int War, int Neutral, int GoldPerPoint, int MakeAlly,
                                    int BreakAlly, int RefuseAlly, int AttackAtPeace, int AttackStack,
                                    int AttackFort, int RodDestroyed, int NobleAction)
{
    /// <summary>Ванильные значения <c>GVars.dbf</c> — на случай отсутствия <c>Globals</c>
    /// (синтетика тестов): <c>D_PEACE</c> 100, <c>D_WAR</c> 33, <c>D_NEUTRAL</c> 66,
    /// <c>D_GOLD</c> 50, <c>D_MK_ALLY</c> 100, <c>D_BK_ALLY</c> 10, <c>D_REF_ALLY</c> 50,
    /// <c>D_ATTAK_PC</c> 66, <c>D_ATTAK_SC</c> −7, <c>D_ATTAK_FO</c> −10, <c>D_ROD</c> −15,
    /// <c>D_NOBLE</c> −5.</summary>
    public static readonly DiplomacyRules Vanilla = new(100, 33, 66, 50, 100, 10, 50, 66, -7, -10, -15, -5);
}

/// <summary>Изменённая пара: слово <c>RELATION</c> до и после. Ровно то, что зонд пишет в поле
/// <c>changed</c> события <c>relation</c> (пара в порядке хранения <c>CMidDiplomacy</c>).
/// <see cref="Before"/> = <see cref="uint.MaxValue"/> — пары до вызова не было; в журнале это
/// <c>before = -1</c>, то же число без знака.</summary>
public readonly record struct RelationPairChange(int Race1, int Race2, uint Before, uint After);

/// <summary>Итог одного вызова мутатора: новый список пар и те из них, чьё слово изменилось.
/// Список новый — записи <see cref="Relation"/> неизменяемы, как и всё состояние карты.</summary>
public sealed record RelationsChange(IReadOnlyList<Relation> Relations,
                                     IReadOnlyList<RelationPairChange> Changed);

/// <summary>Итог разрыва союза: отношения плюс новое <c>QTY_BREAKS</c> разорвавшего игрока
/// (<c>CMidPlayer.QTY_BREAKS</c> растёт на 1 ровно на разрыве — ранг 1, П§13.1).</summary>
public sealed record AllianceBreak(RelationsChange Change, int QtyBreaks);

/// <summary>
/// Дипломатия — план Ф7 §1.8, задача 13. Всё ранг 1, кроме оговорённого.
///
/// <para><b>Одно значение 0…100 на неупорядоченную пару рас</b> (<c>CMidDiplomacy</c>, слово
/// <c>RELATION</c>, разбор битов — <see cref="Relation"/>): война ≤ <c>D_WAR</c>, нейтралитет ≤
/// <c>D_NEUTRAL</c>, мир выше. Игроку отношения с нейтралами не показываются, но запись пары с
/// ними есть, и правило третьей стороны их считает.</para>
///
/// <para><b>Мутатор — <c>changeRelations</c> <c>0x5d9366</c></b>, прочитан дизассемблированием
/// целиком (<c>tools/d2probe/mss32/src/probediplo.cpp</c>, <c>docs/corpus-schema.md</c>):</para>
/// <code>
/// если A == B: вернуть true                                  ; 0x5d937b
/// для каждого игрока X карты, X != B:                        ; 0x5d9428
///     если у (X, B) вечная война (бит 30): пропустить          ; 0x5f7977
///     если X == A:  setRelation(A, B, absolute ? value : rel(A,B) + value)
///     иначе, если rel(X, B) &lt;= D_WAR:                         ; 0x5f7d94
///                   setRelation(A, X, rel(A,X) − value/2)       ; деление к нулю, 0x5d953f
/// </code>
/// <para><c>setRelation</c> (<c>0x5d959e</c>) обрезает величину в <c>[0, 100]</c> и применяет
/// <c>CVisitorDiploCurrent</c> режимом 1, а тот — <c>CMidDiplomacy::setCurrentRelation</c>
/// (<c>0x5f799c</c>): меняются <b>только биты 1…7</b>, при вечной войне запись отменяется. Значит
/// союз (бит 0), «прошлое отношение» (8…14), ход союза (15…29) и флаги 30/31 мутатор не трогает
/// вовсе. Правило третьей стороны живёт ВНУТРИ того же вызова — отдельного вызова для него нет,
/// и потому цикл здесь один.</para>
///
/// <para><b>Девять причин</b> — девять точек вызова в <c>.text</c>, у каждой своя обёртка с одним
/// чтением <c>GVars</c>; «=» — присваивание (<c>absolute</c>), «+» — прибавление:</para>
/// <list type="table">
/// <item><term><c>tribute</c> <c>0x5d935e</c></term><description><c>золото / D_GOLD</c>, +;
/// <c>a</c> — плательщик, <c>b</c> — получатель (<see cref="Tribute"/>)</description></item>
/// <item><term><c>alliance_make</c> <c>0x5d96e4</c></term><description><c>D_MK_ALLY</c>, =;
/// если обе стороны люди — <c>max(rel, D_NEUTRAL + 1)</c>, = (<see cref="MakeAlliance"/>)</description></item>
/// <item><term><c>attack</c> <c>0x5d9828</c></term><description>при мире <c>D_ATTAK_PC</c>, =;
/// иначе по отряду <c>D_ATTAK_SC</c>, по укреплению <c>D_ATTAK_FO</c>, по прочей цели 0, +
/// (<see cref="Attack"/>)</description></item>
/// <item><term><c>rod_destroyed</c> <c>0x5d98a9</c></term><description><c>D_ROD</c>, +, только при
/// непустом виновнике (<see cref="RodDestroyed"/>)</description></item>
/// <item><term><c>alliance_break</c> <c>0x5d98da</c></term><description><c>D_BK_ALLY</c>, =
/// (<see cref="BreakAlliance"/>)</description></item>
/// <item><term><c>alliance_refuse</c> <c>0x5d9961</c></term><description><c>D_REF_ALLY</c>, =;
/// обе стороны люди — вызова нет (<see cref="RefuseAlliance"/>)</description></item>
/// <item><term><c>noble</c> <c>0x5d99ba</c></term><description><c>D_NOBLE</c>, +, только при успехе
/// и цели-отряде/укреплении играбельной расы (<see cref="NobleAction"/>)</description></item>
/// <item><term><c>trade_item</c> <c>0x5d9a2a</c>, <c>trade_spell</c> <c>0x5d9abe</c></term>
/// <description><c>max(1, цена − доплата) / D_GOLD</c>, +; <c>a</c> — получатель золота
/// (<see cref="TradeValue"/>). Сама торговля (<c>L_TRADE_*</c>) в Ф7 намеренно не переносится —
/// здесь только величина. Ранг 1 живьём (<c>20260917-synth</c> q46b): +8 (55 → 63) =
/// <c>max(1, 500 − 100) / 50</c>; при нулевой величине строка <c>relation</c> всё равно пишется — со значением 0 и
/// <c>changed=[]</c> (seq 8735746). Окно торговли предлагает цену от 50 до 2 × цены предмета шагом 50 — это
/// ограничение только интерфейса, правилу величины оно не нужно.</description></item>
/// </list>
///
/// <para><b>Кто это зовёт в порте.</b> Снос жезла — <see cref="Rods.Break"/> (признак
/// <see cref="RodCommand.Blamed"/> и есть «непустой виновник»); успешное действие вора —
/// <see cref="Thieves.Perform"/>; дань — вместе с <see cref="Treasury.Tribute"/>; постановка жезла
/// спрашивает <see cref="AllyTerrain"/>. <b>Боя на карте в порте пока нет:</b> <see cref="MapSim"/>
/// боёв не начинает (бой Ф4 живёт отдельно, а <c>CEffectBattle</c> — это карта), поэтому
/// <see cref="Attack"/> — чистая функция, которую пока зовут только харнесс и тесты; когда бой
/// придёт на карту, вызывать её нужно в момент начала боя, до первого раунда (в журнале событие
/// <c>relation</c> стоит непосредственно перед <c>battle_start</c> с <c>round = 0</c>).</para>
///
/// <para><b>Чего мутатор не видит</b> (ранг 1, три вызывающих <c>CVisitorDiploCurrent</c>): «прошлое
/// отношение» пишет только сценарное событие (<c>CEffectDiploRelation</c> <c>0x4373c6</c> плюс
/// <c>CVisitorDiploPrevious</c> <c>0x5e8819</c>), и мимо мутатора же идут три чит-кода
/// (<c>givepeaceachance</c>, <c>badtothebone</c>, <c>cometogether</c>).</para>
/// </summary>
public static class Diplomacy
{
    /// <summary>Обрезка величины отношений — литерал <c>setRelation</c> <c>0x5d959e</c>, а не поле
    /// <c>D_PEACE</c> (ранг 1: обрезка стоит в мутаторе, чтение <c>D_PEACE</c> в нём отсутствует).</summary>
    public const int Max = 100;

    /// <summary>Биты 1…7 слова <c>RELATION</c> — единственное, что меняет мутатор.</summary>
    private const int CurrentShift = 1;
    private const uint CurrentMask = 0x7F << CurrentShift;

    /// <summary>Мир — отношение выше <c>D_NEUTRAL</c> (<c>areRelationsPeaceful</c> <c>0x5f7e10</c>).</summary>
    public static bool IsPeaceful(DiplomacyRules rules, int current)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return current > rules.Neutral;
    }

    /// <summary>Война — отношение не больше <c>D_WAR</c> (<c>areRelationsAtWar</c> <c>0x5f7d94</c>);
    /// это же условие включает третью сторону.</summary>
    public static bool IsAtWar(DiplomacyRules rules, int current)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return current <= rules.War;
    }

    /// <summary>Текущее отношение пары; <b>записи нет — ноль</b> (ранг 1: геттер <c>0x5f788f</c> на
    /// null от <c>findPair 0x5f79f9</c> кладёт 0 — <c>0x5F78AA</c>; наблюдений нет, во всех картах корпуса
    /// список пар полон).</summary>
    public static int Current(IReadOnlyList<Relation> relations, int race1, int race2) =>
        Find(relations, race1, race2)?.Current ?? 0;

    /// <summary>Союзники ли расы (<c>areAllies</c> <c>0x5f78fc</c> — бит 0 пары).</summary>
    public static bool AreAllies(IReadOnlyList<Relation> relations, int race1, int race2) =>
        Find(relations, race1, race2)?.Ally ?? false;

    /// <summary>Вечная война пары (<c>0x5f7977</c> — бит 30).</summary>
    public static bool AtEternalWar(IReadOnlyList<Relation> relations, int race1, int race2) =>
        Find(relations, race1, race2)?.EternalWar ?? false;

    /// <summary>
    /// Мутатор <c>changeRelations</c> целиком, вместе с правилом третьей стороны.
    /// </summary>
    /// <param name="playerRaces">Расы ИГРОКОВ карты, по одной записи на игрока: цикл третьей
    /// стороны идёт по игрокам (<c>0x5d9428</c>), а не по записям пар. Расу игрока даёт
    /// <see cref="PlayerRaces"/>.</param>
    /// <param name="value">Четвёртый аргумент мутатора — до обрезки.</param>
    /// <param name="absolute">Пятый: присвоить (true) или прибавить (false).</param>
    public static RelationsChange Change(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                         DiplomacyRules rules, int raceA, int raceB, int value, bool absolute)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(playerRaces);
        ArgumentNullException.ThrowIfNull(rules);
        if (raceA == raceB) return new RelationsChange(relations, []);

        var next = relations.ToList();
        foreach (int x in playerRaces)
        {
            if (x == raceB) continue;
            if (Find(next, x, raceB)?.EternalWar == true) continue;
            if (x == raceA)
                Set(next, raceA, raceB, absolute ? value : Current(next, raceA, raceB) + value);
            else if (IsAtWar(rules, Current(next, x, raceB)))
                Set(next, raceA, x, Current(next, raceA, x) - value / 2);
        }
        return new RelationsChange(next, Diff(relations, next));
    }

    /// <summary>Величина за атаку: при мире — присваивание <c>D_ATTAK_PC</c>, иначе прибавление по
    /// виду цели (обёртка <c>0x5d96fa</c>).</summary>
    public static (int Value, bool Absolute) AttackValue(DiplomacyRules rules, int current, BattleTarget target)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (IsPeaceful(rules, current)) return (rules.AttackAtPeace, true);
        return (target switch
        {
            BattleTarget.Stack => rules.AttackStack,
            BattleTarget.Fortification => rules.AttackFort,
            _ => 0,
        }, false);
    }

    /// <summary>
    /// Атака: отношение атакующего и владельца цели. <b>Обёртка выходит без вызова</b>, если у цели
    /// нет владельца или его раса непроходная (<c>isPlayerRaceUnplayable</c> <c>0x5e5358</c>) — то
    /// есть бой с нейтралами отношений не двигает вовсе (ранг 1: обёртка плюс замер П§16.3 «бои
    /// человека с нейтралами в мутатор не идут»).
    /// </summary>
    /// <param name="targetPlayable"><c>Grace.PLAYABLE</c> расы владельца цели.</param>
    public static RelationsChange Attack(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                         DiplomacyRules rules, int attackerRace, int defenderRace,
                                         BattleTarget target, bool targetPlayable)
    {
        ArgumentNullException.ThrowIfNull(relations);
        if (!targetPlayable) return new RelationsChange(relations, []);
        var (value, absolute) = AttackValue(rules, Current(relations, attackerRace, defenderRace), target);
        return Change(relations, playerRaces, rules, attackerRace, defenderRace, value, absolute);
    }

    /// <summary>Меняет ли действие вора отношения: <b>только успешное</b> действие по отряду или
    /// укреплению играбельной расы (гейты <c>0x43a3b1…0x43a3cd</c> и <c>0x5e5358</c>). Категория
    /// действия не проверяется, поэтому <c>L_SPY_RUIN</c>, <c>L_STEAL_MERCHANT</c> и
    /// <c>L_STEAL_MAGE</c> бесплатны просто по виду цели (руина <c>0x29</c>, лавка <c>0x28</c>).</summary>
    public static bool NobleChangesRelations(ThiefTargetKind kind, bool targetPlayable, bool success) =>
        success && targetPlayable && kind is ThiefTargetKind.Stack or ThiefTargetKind.Fortification;

    /// <summary>Успешное действие вора: <c>D_NOBLE</c> игроку вора с владельцем цели
    /// (<see cref="NobleChangesRelations"/> решает, зовётся ли мутатор вовсе).</summary>
    public static RelationsChange NobleAction(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                              DiplomacyRules rules, int thiefRace, int targetRace,
                                              ThiefTargetKind kind, bool targetPlayable, bool success)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(rules);
        return NobleChangesRelations(kind, targetPlayable, success)
            ? Change(relations, playerRaces, rules, thiefRace, targetRace, rules.NobleAction, false)
            : new RelationsChange(relations, []);
    }

    /// <summary>Снос чужого жезла: <c>D_ROD</c> игроку сносившего отряда с владельцем жезла. При
    /// пустом виновнике (путь заклинания Sinkhole) вызова нет — <see cref="RodCommand.Blamed"/>
    /// и есть это условие.</summary>
    public static RelationsChange RodDestroyed(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                               DiplomacyRules rules, int breakerRace, int rodOwnerRace,
                                               bool blamed)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(rules);
        return blamed
            ? Change(relations, playerRaces, rules, breakerRace, rodOwnerRace, rules.RodDestroyed, false)
            : new RelationsChange(relations, []);
    }

    /// <summary>Очки отношений за дань: <c>золото / D_GOLD</c> (<c>idiv</c> <c>0x5d933c</c>).
    /// Потолка у конверсии нет — она упирается только в обрезку <see cref="Max"/> (5200 золота →
    /// 104 → 100, П§16.3).</summary>
    public static int TributeValue(DiplomacyRules rules, long gold)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return (int)(gold / rules.GoldPerPoint);
    }

    /// <summary>Дань: <c>a</c> — плательщик, <c>b</c> — получатель. Само золото переводит
    /// <see cref="Treasury.Tribute"/>.</summary>
    public static RelationsChange Tribute(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                          DiplomacyRules rules, int payerRace, int recipientRace, long gold) =>
        Change(relations, playerRaces, rules, payerRace, recipientRace, TributeValue(rules, gold), false);

    /// <summary>Очки за подарок в обмене (<c>trade_item</c>, <c>trade_spell</c>):
    /// <c>max(1, цена − доплата) / D_GOLD</c>. Сам обмен в Ф7 не переносится.</summary>
    public static int TradeValue(DiplomacyRules rules, long price, long surcharge)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return (int)(Math.Max(1, price - surcharge) / rules.GoldPerPoint);
    }

    /// <summary>Можно ли заключить союз — <c>canMakeAlliance</c> <c>0x5daa0e</c>: отношение выше
    /// <c>D_NEUTRAL</c>. Если <b>обе</b> стороны — люди, порог не спрашивается вовсе (ранг 1:
    /// обёртка союза у двух людей вместо <c>D_MK_ALLY</c> присваивает <c>max(rel, D_NEUTRAL + 1)</c>,
    /// то есть сама поднимает отношение до мира).</summary>
    public static bool CanMakeAlliance(IReadOnlyList<Relation> relations, DiplomacyRules rules,
                                       int race1, int race2, bool bothHuman)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return bothHuman || IsPeaceful(rules, Current(relations, race1, race2));
    }

    /// <summary>
    /// Заключение союза: взводится бит 0, «ход союза» (биты 15…29) становится ходом заключения, и
    /// затем мутатор присваивает <c>D_MK_ALLY</c> — а у двух людей <c>max(rel, D_NEUTRAL + 1)</c>.
    /// </summary>
    /// <param name="turn">Ход заключения (<c>CScenarioInfo.CUR_TURN</c>).</param>
    public static RelationsChange MakeAlliance(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                               DiplomacyRules rules, int race1, int race2, int turn,
                                               bool bothHuman = false)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(rules);
        int value = bothHuman
            ? Math.Max(Current(relations, race1, race2), rules.Neutral + 1)
            : rules.MakeAlly;
        var flagged = WithAlliance(relations, race1, race2, ally: true, turn);
        var change = Change(flagged, playerRaces, rules, race1, race2, value, absolute: true);
        return new RelationsChange(change.Relations, Diff(relations, change.Relations));
    }

    /// <summary>
    /// Разрыв союза: бит 0 гаснет, «ход союза» перезаписывается ходом РАЗРЫВА (то есть это ход
    /// последней смены состояния, ранг 1 — 3 → 5 в снимках <c>m15-diplo</c>), мутатор присваивает
    /// <c>D_BK_ALLY</c>, а у разорвавшего игрока <c>QTY_BREAKS</c> растёт на 1.
    /// </summary>
    /// <param name="qtyBreaks">Прежнее <c>QTY_BREAKS</c> разорвавшего (<see cref="MapPlayer.QtyBreaks"/>).</param>
    public static AllianceBreak BreakAlliance(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                              DiplomacyRules rules, int race1, int race2, int turn,
                                              int qtyBreaks = 0)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(rules);
        var flagged = WithAlliance(relations, race1, race2, ally: false, turn);
        var change = Change(flagged, playerRaces, rules, race1, race2, rules.BreakAlly, absolute: true);
        return new AllianceBreak(new RelationsChange(change.Relations, Diff(relations, change.Relations)),
                                 qtyBreaks + 1);
    }

    /// <summary>Отказ от союза: присваивается <c>D_REF_ALLY</c>. Если обе стороны — люди, обёртка
    /// выходит без вызова мутатора (<c>0x5d98e2</c>), и отношение остаётся прежним.</summary>
    public static RelationsChange RefuseAlliance(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                                 DiplomacyRules rules, int race1, int race2, bool bothHuman)
    {
        ArgumentNullException.ThrowIfNull(relations);
        ArgumentNullException.ThrowIfNull(rules);
        return bothHuman
            ? new RelationsChange(relations, [])
            : Change(relations, playerRaces, rules, race1, race2, rules.RefuseAlly, absolute: true);
    }

    /// <summary>Почему атака невозможна; <c>null</c> — можно. Союзника атаковать нельзя — текст
    /// <c>X005TA0303</c> «расторгните альянс перед атакой», гейты <c>0x5f7ebd</c>/<c>0x5f7f5a</c>
    /// над <c>areAllies</c>. Наблюдений отказа в корпусе нет (боёв между союзниками нет ни одного —
    /// это и проверяет харнесс <c>ally_no_attack</c>).</summary>
    public static string? AttackRefusal(IReadOnlyList<Relation> relations, int attackerRace, int defenderRace) =>
        AreAllies(relations, attackerRace, defenderRace)
            ? $"расы {attackerRace} и {defenderRace} в союзе: союзника атаковать нельзя (X005TA0303)"
            : null;

    /// <summary>
    /// Предикат «эта клетка — земля союзника» для <see cref="Rods.PlaceRefusal"/> (код отказа 8,
    /// <c>X005TA0647</c>, <c>0x5d9d1d</c> → <c>areAllies</c>): террейн клетки принадлежит расе, с
    /// которой у <paramref name="ownRace"/> взведён бит союза. Своя земля союзной не считается.
    /// Сторона → террейн — земля расы её игрока (<see cref="TerrainOfParty"/>); без карты — по роду из правил (<see cref="EconomyRules.TerrainOf"/>),
    /// без правил — по роду оригинала (<see cref="LegacyRaces.Terrain"/>, ранг 1).
    /// </summary>
    public static Func<int, bool> AllyTerrain(IReadOnlyList<Relation> relations, IReadOnlyList<int> playerRaces,
                                              int ownRace, EconomyRules? rules = null, MapState? map = null)
    {
        ArgumentNullException.ThrowIfNull(playerRaces);
        var allyTerrains = playerRaces.Where(r => r != ownRace && AreAllies(relations, ownRace, r))
                                      .Select(r => rules is null ? LegacyRaces.Terrain(r) : map is null ? rules.TerrainOf(r) : TerrainOfParty(map, rules, r))
                                      .ToHashSet();
        return terrain => allyTerrains.Contains(terrain);
    }

    /// <summary>Стороны игроков карты по одной записи на игрока — материал цикла третьей стороны. Сторона — <see cref="PartyOf(MapPlayer,
    /// IReadOnlyDictionary{string, RaceInfo})"/>: у движка раса игрока <c>CMidPlayer.RACE_ID</c> → <c>Grace.RACE_TYPE</c> (та же цепочка,
    /// что <c>0x5e4727</c>). Игрок с расой вне <c>Grace.dbf</c> в список не идёт.</summary>
    public static IReadOnlyList<int> PlayerParties(MapState map, EconomyRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        return [.. map.Players.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                  .Select(p => PartyOf(p, rules.Races)).OfType<int>()];
    }

    /// <summary>Сторона дипломатии игрока (<see cref="MapPlayer.Party"/>); своей нет — род его расы (<c>Grace.RACE_TYPE</c>), как у движка.
    /// Расы нет в справочнике — null.</summary>
    public static int? PartyOf(MapPlayer player, IReadOnlyDictionary<string, RaceInfo> races)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(races);
        return races.TryGetValue(player.RaceId, out var race) ? player.Party ?? race.RaceType : null;
    }

    /// <summary>Сторона дипломатии игрока <paramref name="playerId"/> (<see cref="PartyOf(MapPlayer, IReadOnlyDictionary{string, RaceInfo})"/>);
    /// игрока нет — null.</summary>
    public static int? PartyOf(MapState map, IReadOnlyDictionary<string, RaceInfo> races, string playerId)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Players.TryGetValue(playerId, out var player) ? PartyOf(player, races) : null;
    }

    /// <summary>Своя земля стороны <paramref name="party"/>: земля расы первого её игрока (<see cref="RaceInfo.Terrain"/>); игрока нет — по
    /// роду (<see cref="EconomyRules.TerrainOf"/>).</summary>
    public static int TerrainOfParty(MapState map, EconomyRules rules, int party)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var player in map.Players.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            if (PartyOf(player, rules.Races) == party && rules.Races.TryGetValue(player.RaceId, out var race)) return race.Terrain;
        return rules.TerrainOf(party);
    }

    /// <summary>Запись пары в любом порядке рас; <c>null</c> — записи нет.</summary>
    private static Relation? Find(IReadOnlyList<Relation> relations, int race1, int race2)
    {
        foreach (var r in relations)
            if (r.Pairs(race1, race2)) return r;
        return null;
    }

    /// <summary><c>setRelation</c> <c>0x5d959e</c>: обрезка в <c>[0, 100]</c>, затем визитор
    /// (<c>0x5e8788</c>, <c>apply</c> <c>0x613d3c</c>) → <c>setCurrentRelation</c> <c>0x5f799c</c>: запись только
    /// битов 1…7; при вечной войне записи нет (<c>0x5F79D0</c>). <b>Записи пары нет — ничего не пишется и не
    /// создаётся</b> (<c>0x5F79CE</c> <c>je</c> → <c>false</c>; ответ визитора мутатор не читает). Ранг 1 по
    /// листингу; корпус: 71 строка <c>relation</c>, <c>before = null</c> — 0.</summary>
    private static void Set(List<Relation> relations, int raceA, int raceX, int value)
    {
        int clamped = Math.Clamp(value, 0, Max);
        for (int i = 0; i < relations.Count; i++)
        {
            if (!relations[i].Pairs(raceA, raceX)) continue;
            if (relations[i].EternalWar) return;
            uint raw = (relations[i].Raw & ~CurrentMask) | ((uint)clamped << CurrentShift);
            relations[i] = relations[i] with { Raw = raw };
            return;
        }
    }

    /// <summary>Бит союза и «ход союза» (биты 15…29) — их ставит не мутатор, а
    /// <c>CVisitorDiploAlliance</c> (<c>0x5e88aa</c>) перед его вызовом. Сеттер <c>0x5f7a9e</c> пару тоже не
    /// создаёт (<c>0x5F7AB6</c> <c>je</c> → <c>false</c>). Слово он собирает заново: <c>raw &amp; 0x40007fff</c> — «вечная война» (бит 30)
    /// и текущее отношение остаются, бит 31 «ИИ не разорвёт» := аргумент «навсегда» (ранг 1, 28.09): у союза и разрыва из дипломатии
    /// (<c>0x430f1d</c> → <c>0x5e88aa</c>) — 0; взводит его только событие <c>L_ALLY</c> с <c>PERMALLI</c> — уже после
    /// (<c>GameSession.EventEffects</c>).</summary>
    private static IReadOnlyList<Relation> WithAlliance(IReadOnlyList<Relation> relations, int race1, int race2,
                                                        bool ally, int turn)
    {
        const int TurnShift = 15;
        const uint TurnMask = 0x7FFFu << TurnShift;
        const uint Permanent = 1u << 31;
        var next = relations.ToList();
        for (int i = 0; i < next.Count; i++)
        {
            if (!next[i].Pairs(race1, race2)) continue;
            uint raw = (next[i].Raw & ~(1u | TurnMask | Permanent)) | (ally ? 1u : 0u)
                       | ((uint)(turn & 0x7FFF) << TurnShift);
            next[i] = next[i] with { Raw = raw };
            return next;
        }
        return next;
    }

    /// <summary>Пары, чьё слово изменилось — в порядке хранения, как их печатает зонд.</summary>
    private static IReadOnlyList<RelationPairChange> Diff(IReadOnlyList<Relation> before,
                                                          IReadOnlyList<Relation> after)
    {
        var changed = new List<RelationPairChange>();
        foreach (var now in after)
        {
            var was = Find(before, now.Race1, now.Race2);
            if (was is { } had && had.Raw == now.Raw) continue;
            changed.Add(new RelationPairChange(now.Race1, now.Race2, was?.Raw ?? uint.MaxValue, now.Raw));
        }
        return changed;
    }
}
