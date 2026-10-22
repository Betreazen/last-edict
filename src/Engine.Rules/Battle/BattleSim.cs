using LastEdict.Rules.Magic;
using LastEdict.Rules.Rng;

namespace LastEdict.Rules.Battle;

/// <summary>Одна запись хода: один бросок точности (событие miss журнала) или одно лечение.
/// Attack — номер атаки юнита (1 или 2). У лечения Miss = false, Damage = 0. Crit — урон крита сверх Damage (<c>*critDamage</c>
/// <c>computeDamage</c>); экрану боя — надпись «Крит удар» (<c>X160TA0021</c>, <c>0x63b391</c>).</summary>
public sealed record TurnRecord(int Round, string Unit, string Target, bool Miss, int Damage, int TargetHpAfter, int Attack = 1, int Crit = 0);

/// <summary>Атака по своим без записи хода (<see cref="BattleSim.AllyLog"/>, только для показа): кто, по кому, класс атаки в момент удара
/// (у превращения себя атака после удара уже другая). Движок кладёт запись цели в <c>BattleAttackInfo</c> (<c>0x62c462</c>) у усиления
/// — только если у цели есть атака с уроном (<c>0x65cdca</c>), у воскрешения, снятия эффектов, дара атаки, оберегов, превращения себя и
/// двойника — всегда; надпись — строкой класса (<c>0x63b391</c>). Лечение и воскрешение с полученным здоровьем — в <see cref="TurnRecord"/>, как и было.</summary>
public sealed record AllyNote(int Round, string Unit, string Target, AttackClass Class, int Attack = 1);

/// <summary>Прогон боя на модели. В С2-харнессе IRng = EngineRng, подобранный по броскам боя
/// (EngineRng.Fit), и первый бросок модели — первый rng(BAT_INIT) очереди раунда 1.
///
/// Расход бросков очереди — замер шага 1 Task 5 по corpus/20260906-v2 (5 боёв, 2 юнита):
/// на раунд 1 приходится 4 броска rng(BAT_INIT) = 2 × живых юнитов, на раунд 2 — 1 = 1 × живых
/// (один юнит уже мёртв). Верно и то, и другое: движок держит две копии BattleMsgData на бой
/// (docs/corpus-schema.md, autobattle_forced — вторая строка приходит уже ПОСЛЕ battle_end:
/// battle-001 seq 53105 &gt; 53079, форс-ответ получает и вторая копия), И beforeBattleRound
/// раунда 1 зовётся дважды на ПЕРВОЙ из них (два события round=1 подряд без повторного
/// battle_start — дедуп зонда по указателю; README зонда, «Идентификация нового/законченного
/// боя»). Броски тратит только пара вызовов на первой копии — второй копии в окне боя не видно.
/// Все 11 журналов (5 v2, 5 _old, first-battle-raw) — один и тот же сейв (battle_start seq 52975,
/// первый бросок n 50689): одно наблюдение, повторённое 11 раз, а не 11 независимых.
/// Гипотезы (ранг 3, корпус слеп): (а) со 2-го раунда вызов всегда один — в корпусе 2-й раунд
/// только терминальный; (б) очередь берётся из второго вызова — при инициативах 65 против 50
/// оба вызова дают один порядок; (в) второй вызов не связан с автобоем — все бои сняты под
/// маркером probe/autobattle, между двумя вызовами стоит autobattle_forced.
/// <para>18.10 (ранг 1 по листингу): два построения — начало боя <c>0x6290bc</c> (строит и ставит раунд 0) и обработка хода
/// <c>0x627ffa</c> (очередь пуста — строит снова); между ними — предраунд двойников (<see cref="BeginPreRound"/>).</para></summary>
public sealed class BattleSim(BattleState state, IRng rng, RulesConstants constants)
{
    /// <summary>Сколько раз движок строит очередь в раунде 1 (см. описание класса).</summary>
    public const int FirstRoundQueueBuilds = 2;

    /// <summary>Группа-победитель, пока обе стороны живы — null.</summary>
    public string? Winner
    {
        get
        {
            // OnField, а не Alive: отступивший жив, но сторону не держит — ранг 1
            // (corpus/20260906-c, бой 1: жертва Fear ушла с поля с hp 118, двое её союзников затем
            // погибли, и движок объявил конец боя).
            bool attackersAlive = state.Units.Any(u => u.Attacker && u.OnField);
            bool defendersAlive = state.Units.Any(u => !u.Attacker && u.OnField);
            if (attackersAlive && defendersAlive) return null;
            return attackersAlive ? state.AttackerGroup : state.DefenderGroup;
        }
    }

    /// <summary>Бой действительно окончен: сторона мертва И очередь дошла до не-лекаря. Между этими
    /// двумя моментами движок даёт лекарям победившей стороны ещё ходы — ранг 2: A
    /// (battlemsgdata.h, BattleStateFlags.afterBattle «Battle is over but healers can make one more
    /// turn») плюс corpus/20260906-b, бой 1: после гибели всей стороны игрока идут round 7 с
    /// очередью, ход Oracle Elf (лечение себя 97 → 125) и round 8, и лишь ход не-лекаря даёт
    /// battle_end (seq 53584..53601). Выставляется в <see cref="BeginTurn"/>, поэтому работает и в
    /// прогоне по журнальной очереди.</summary>
    public bool Finished { get; private set; }

    /// <summary>Записи всех ударов и лечений по порядку; <see cref="Act"/> дописывает их по одной,
    /// поэтому после исключения (JournalRng в С2, NotSupportedException на Fear) здесь всё, что
    /// успело сойтись до него.</summary>
    public List<TurnRecord> Log { get; } = [];

    /// <summary>Решения ходов <see cref="Run"/> по порядку (ИИ и <see cref="TurnChoice"/>, по одному на действие) — для харнесса
    /// С2 и показа; модель их не читает.</summary>
    public List<ChoiceNote> Choices { get; } = [];

    /// <summary>Тики яда, мороза и ожога по порядку — только для показа (звук и надпись экрана боя, <c>docs/ai-screen.md</c> §12);
    /// модель их не читает.</summary>
    public List<DotTick> DotLog { get; } = [];

    /// <summary>Атаки по своим без записи хода (<see cref="AllyNote"/>) по порядку — только для показа (надписи экрана боя); модель и
    /// харнессы их не читают.</summary>
    public List<AllyNote> AllyLog { get; } = [];

    /// <summary>Какие тики сейчас пройдут (<see cref="Attacks.TickDots"/>: по классу, до гибели) — в <see cref="DotLog"/>.</summary>
    private void NoteDots(BattleUnit unit)
    {
        int hp = unit.Hp;
        foreach (var dot in unit.Dots.OrderBy(d => d.Class))
        {
            if (hp <= 0) break;
            hp = Math.Max(0, hp - dot.Damage);
            DotLog.Add(new DotTick(state.Round, unit.Id, dot.Class, dot.Damage, hp));
        }
    }

    /// <param name="choose">Решения не от ИИ (<see cref="TurnChoice"/>): зовётся перед каждым действием хода, null — решает ИИ.</param>
    public IReadOnlyList<TurnRecord> Run(int maxRounds = 30, Func<BattleUnit, TurnChoice?>? choose = null)
    {
        int first = state.Round + 1;   // battle_start несёт round=0, первый beforeBattleRound делает 1
        for (int round = first; round < first + maxRounds; round++)
        {
            // Именно round == 1, а не первый раунд прогона: удвоение вызова наблюдалось только на
            // первом раунде с начала боя (currentRound за все 6–8 ходов беса не растёт, так что
            // «раунд 1» в корпусе и есть начало боя); прогону с середины боя движок не мерился.
            BeginRound(round);
            if (round == 1)
            {
                PreRound(choose);
                if (Finished) break;
                BeginRound(round);   // второе построение очереди раунда 1 (FirstRoundQueueBuilds)
            }
            // Очередь нового раунда движок строит (и тратит броски) ДО решения, что бой окончен:
            // в корпусе после смертельного удара идут round=2, rng(10) и лишь потом battle_end (5/5).
            if (Finished) break;
            // Очередь раунда — список, а не готовый перечень: класс GiveAttack (19) вдвигает в неё
            // юнита прямо посреди раунда (BattleMsgData::giveAttack, GOG 0x62266e: turnsOrder[1] со
            // сдвигом хвоста), то есть цель ходит СЛЕДУЮЩЕЙ за нынешним ходящим. Пока GiveAttack не
            // применялся, это тот же перечень в том же порядке — Ф4/Ф5 не меняются. В конец её же
            // встаёт ждущий (Wait).
            var queue = _queue;
            for (int i = 0; i < queue.Count; i++)
            {
                var unit = queue[i];
                bool decided = Winner is not null;
                if (!unit.OnField || !BeginTurn(unit))
                {
                    // Сторона пала В НАЧАЛЕ хода (последний беглец ушёл, последний умер от тика DoT) — остаток
                    // очереди брошен, как после смертельного удара: обработка хода 0x627ffc после пропуска
                    // (флаг [ebp-0x18]) считает стороны 0x6256ff (живые, не ушедшие, не скрытые; 0x62821f,
                    // 0x628231) и на нуле зовёт 0x625ed6; движок строит новый раунд (rng(BAT_INIT) на каждого на
                    // поле) и лишь потом battle_end. Ранг 1 по корпусу: 20/20 таких концовок (7 тиков, 13 уходов),
                    // corpus/20260906-c [212934] seq 213282 turn_before → 213283 round → 213284 rng(10) → 213286 end.
                    if (Finished || (!decided && Winner is not null)) break;
                    continue;
                }
                // Ранг 3: у ATCK_TWICE решение ИИ принимается перед каждым ударом (второй удар после
                // гибели цели перенацеливается) — движок мог бы и терять удар; двойных атак в корпусе нет.
                for (int hit = 0; hit < Attacks.AttacksPerTurn(unit); hit++)
                {
                    var choice = choose?.Invoke(unit);
                    if (!(choice is null ? Perform(round, unit) : Perform(round, unit, choice)) || Winner is not null) break;
                }
                EndTurn(unit);
                InsertExtraTurns(queue, i);
                // Сторона пала этим ходом — остаток очереди раунда движок бросает; в раунде лекарей (сторона пала раньше) ходят все
                // лекари очереди: 20260909-arena2 [254442] раунд 6 — [000c, 000d], оба лечат S143UN000b (43 → 83 → 110).
                if (!decided && Winner is not null) break;
            }
            if (Finished) break;
        }
        return Log;
    }

    /// <summary>Предраунд двойников (<see cref="BeginPreRound"/>): каждый ходит один раз, как обычным ходом, в раунде 0.</summary>
    private void PreRound(Func<BattleUnit, TurnChoice?>? choose)
    {
        foreach (var unit in BeginPreRound())
        {
            if (!unit.OnField || !BeginTurn(unit))
            {
                if (Finished || Winner is not null) break;   // как в Run: сторона пала в начале хода — дальше не ходят
                continue;
            }
            for (int hit = 0; hit < Attacks.AttacksPerTurn(unit); hit++)
            {
                var choice = choose?.Invoke(unit);
                if (!(choice is null ? Perform(0, unit) : Perform(0, unit, choice)) || Winner is not null) break;
            }
            EndTurn(unit);
            if (Winner is not null) break;
        }
    }

    /// <summary>
    /// Предраунд начала боя — откуда второе построение очереди раунда 1 (<see cref="FirstRoundQueueBuilds"/>; ранг 1 по листингу, 18.10).
    /// Обработка боя <c>0x42840e</c> при раунде 0 зовёт начало боя <c>0x6290bc</c>: очередь раунда строится (<c>0x624474</c> →
    /// <c>beforeBattleRound</c> <c>0x621fd0</c>: раунд 0 → 1, бросок инициативы каждому), раунд снова ставится в 0 (<c>0x6220a8</c>), очередь
    /// <c>turnsOrder</c> очищается (<c>0x622622</c>), и в неё встают только юниты, чья атака — <c>L_DOPPELGANGER</c> (<c>0x6293cc</c>,
    /// <c>[0x8374e0]</c>; у превращённого класс уже запасной атаки) и у кого есть цель (<c>0x62941e</c>: список целей
    /// <c>0x644394</c> не пуст). Построение взвело флаг «перестроить» (<c>+0xf49</c> бит 1, <c>0x6245bb</c>); обработка хода
    /// <c>0x627ffa</c> его снимает и при пустой очереди (<c>0x6243a8</c>: первый в <c>turnsOrder</c> — пусто) строит раунд снова (раунд
    /// 0 → 1, новые броски). Без двойников это сразу — два построения подряд; с двойником — он ходит (превращается) в раунде 0, и лишь
    /// потом второе построение, где он стоит в очереди раунда 1 уже копией. Порядок нескольких двойников — список юнитов боя (ранг 2:
    /// в корпусе двойник в бою один). Журнал: <c>20260908-f6/map-001 [1281528]</c> — <c>S143UN0007</c> превращается в раунде 0
    /// (<c>turn_after round 0</c>), затем <c>round 1</c> и он второй в очереди; <c>20261016-port-synth [545297]</c> (так же <c>b [522314]</c>,
    /// <c>c [518729]</c>) — двойник человека <c>S143UN0019</c>: 7 бросков, превращение, снова 7 бросков и раунд 1. Возвращает двойников
    /// предраунда; раунд модели — 0 до второго построения.
    /// </summary>
    public IReadOnlyList<BattleUnit> BeginPreRound()
    {
        state.Round = 0;
        return [.. state.Units.Where(u => u.OnField && u.Attack.Class == AttackClass.Doppelganger
                                          && !(u.Transformed || u.TransformedLong || u.TransformedSelf || u.TransformedDoppelganger || u.LevelDrained)
                                          && BattleTargets.Possible(state, u, u.Attack).Count > 0)];
    }

    /// <summary>Одно действие по решению ИИ (<see cref="BattleAi.Choose"/>): защита, бегство, предмет или
    /// удар. Истина — был удар с записями хода (только тогда у ATCK_TWICE идёт второй).</summary>
    private bool Perform(int round, BattleUnit unit)
    {
        var d = Decide(unit);
        Choices.Add(new ChoiceNote(round, unit.Id, d.Action switch
        {
            AiAction.Defend => TurnChoiceKind.Defend, AiAction.Retreat => TurnChoiceKind.Retreat,
            AiAction.UseItem => TurnChoiceKind.UseItem, _ => TurnChoiceKind.Attack,
        }));
        switch (d.Action)
        {
            case AiAction.Defend: Defend(unit); return false;
            case AiAction.Retreat: Retreat(unit); return false;
            case AiAction.UseItem: UseItem(unit, d.ItemIndex, d.Target!); return false;
            default:
                if (d.SummonSlot >= 0) state.SummonSlot = d.SummonSlot;
                return Act(round, unit, unit.Attack.Reach == AttackType.ReachAll ? null : d.Target).Count > 0;
        }
    }

    /// <summary>Решение человека (<see cref="TurnChoice"/>) вместо решения ИИ — как <see cref="Perform(int, BattleUnit)"/>.</summary>
    private bool Perform(int round, BattleUnit unit, TurnChoice choice)
    {
        Choices.Add(new ChoiceNote(round, unit.Id, choice.Kind));
        if (choice.SummonSlot >= 0) state.SummonSlot = choice.SummonSlot;
        switch (choice.Kind)
        {
            case TurnChoiceKind.Defend: Defend(unit); return false;
            case TurnChoiceKind.Wait: Wait(unit); return false;
            case TurnChoiceKind.Retreat: Retreat(unit); return false;
            case TurnChoiceKind.UseItem: UseItem(unit, choice.ItemIndex, choice.Target ?? unit); return false;
            default: return Act(round, unit, unit.Attack.Reach == AttackType.ReachAll ? null : choice.Target).Count > 0;
        }
    }

    /// <summary>Решение ИИ для ходящего на текущем состоянии (<see cref="BattleAi.Choose"/>) — тем же
    /// генератором: ничьи тратят <c>rng(2)</c>. Журнальный С2 зовёт его, чтобы взять цель, которой нет в журнале.</summary>
    public AiDecision Decide(BattleUnit unit) => BattleAi.Choose(state, unit, rng, constants);

    /// <summary>Применение предмета по решению ИИ: зелье — <see cref="DrinkPotion"/>, иначе удар атакой
    /// предмета (<c>GItem.ATTACK_ID</c> через <see cref="BattleState.Rules"/>) — <see cref="UseBattleItem"/>.</summary>
    public void UseItem(BattleUnit bearer, int index, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        ArgumentNullException.ThrowIfNull(target);
        if (index < 0 || index >= bearer.BattleItems.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"у {bearer.Id} в боевых слотах {bearer.BattleItems.Count} предметов");
        var item = bearer.BattleItems[index];
        if (item.IsPotion) { DrinkPotion(bearer, index, target); return; }
        var rules = state.Rules ?? throw new InvalidOperationException($"предмет {item.Id}: справочника Gattacks в бою нет");
        var attack = rules.Attacks.TryGetValue(item.AttackId, out var a) ? a
            : throw new InvalidDataException($"предмет {item.Id}: ATTACK_ID \"{item.AttackId}\" в Gattacks не найден");
        UseBattleItem(bearer, index, attack, target);
    }

    /// <summary>Вставка внеочередных ходов, выданных классом GiveAttack за только что закончившийся
    /// ход: <c>BattleMsgData::giveAttack</c> (GOG <c>0x62266e</c>) кладёт цель в
    /// <c>turnsOrder[1]</c>, сдвигая хвост, — то есть сразу за ходящим. Бросков это не тратит:
    /// очередь не перестраивается, а раздвигается.
    /// <para>Флаг снимается здесь же, чтобы один <c>GiveAttack</c> давал ровно один лишний ход.
    /// Юнит, уже ходивший в этом раунде, ходит ещё раз — это и есть смысл класса.</para></summary>
    /// <summary>Внеочередные ходы (класс GiveAttack) вставляются сразу за ходящим. Открыто для экрана боя:
    /// он ведёт бой по шагам (<c>LastEdict.Data.Battle.BattleSession</c>) и повторяет порядок <see cref="Run"/> сам.</summary>
    public void InsertExtraTurns(List<BattleUnit> queue, int after)
    {
        foreach (var u in state.Units)
        {
            if (!u.ExtraTurn) continue;
            u.ExtraTurn = false;
            queue.Insert(Math.Min(after + 1, queue.Count), u);
            // turnsOrder — 13 ячеек от ходящего (0x62266e: сдвиг с индекса 12, REA 13.10): переполненный хвост теряет последний ход.
            while (queue.Count - after > TurnsOrderCapacity) queue.RemoveAt(queue.Count - 1);
        }
    }

    /// <summary>Ячеек в очереди ходов движка (<c>BattleMsgData.turnsOrder</c>, <c>+0xe70</c>, 13 по 8 байт), считая ходящего.</summary>
    public const int TurnsOrderCapacity = 13;

    /// <summary>Конец боя (survey §7). Полный порядок движка — предметы → unsummon/untransform →
    /// groupUpgrade → battleCount; Ф6 умеет первые два пункта, остальные два заводят задачи 11 и 14.
    /// <para>Зовётся не из <see cref="Run"/>: конец боя в модели решает вызывающий (в С2 —
    /// событие <c>battle_end</c> журнала), а не сам прогон.</para></summary>
    public void AfterBattle() => AfterBattle(state);

    /// <summary>Конец боя по одному состоянию — для хозяина, у которого модели боя уже нет (бой с карты,
    /// <c>GameSession.FinishBattle</c>). Повторный вызов ничего не меняет.</summary>
    public static void AfterBattle(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        DestroyBattleItems(state);
        TransformAttacks.AfterBattle(state);
    }

    /// <summary>Первый пункт конца боя: применённые боевые предметы части видов исчезают.
    ///
    /// <para><b>Ранг 1.</b> <c>checkAndDestroyEquippedBattleItems</c> (GOG <c>0x629755</c>) обходит по
    /// два слота снаряжения у предводителя каждой стороны и на каждый зовёт <c>0x629824</c>, где
    /// категория сравнивается со сферой и четырьмя зельями: совпало — предмет снимается и
    /// уничтожается. Какой вид исчезает, решает <see cref="Item.IsDestroyedAfterBattle"/> (задача 10)
    /// — своего перечня здесь нет.</para>
    ///
    /// <para><b>Исчезает только применённое</b> (задача 10, ранг 1 по прогону <c>f7-m17-charges</c>):
    /// сфера, пролежавшая бой в слоте без применения, остаётся. Применение — удар атакой предмета по
    /// цели (<see cref="UseBattleItem"/>), которое и складывает номера в
    /// <see cref="BattleUnit.AppliedItems"/>; построение <c>attack_new</c> применением не является
    /// (П§16.4). Множество применений очищается здесь же — оно живёт один бой.</para>
    ///
    /// <para><b>Талисман остаётся.</b> В движке он идёт отдельной веткой и уничтожается только на
    /// нуле зарядов (<c>0x5eb9b3</c>, <c>jge</c> на <c>0x6299f4</c>), а заряды — состояние карты, не
    /// боя: их считает <see cref="Charges.TalismanAfterBattle"/> по тому же признаку «применяли ли»,
    /// и снимает талисман вызывающий, когда остаток дошёл до нуля.</para>
    ///
    /// <para>Обходятся все юниты, а не только предводители: признака предводителя у
    /// <see cref="BattleUnit"/> нет, а слоты остальных пусты — <see cref="BattleUnit.BattleItems"/>
    /// заполняет только вызывающий. Порядок относительно отката превращений безразличен (призванные
    /// предметов не носят), но выдержан по survey §7.</para></summary>
    private static void DestroyBattleItems(BattleState state)
    {
        foreach (var unit in state.Units)
        {
            // С конца: удаление сдвигает хвост списка, а номера применений указывают в него.
            for (int i = unit.BattleItems.Count - 1; i >= 0; i--)
                if (unit.BattleItems[i].IsDestroyedAfterBattle(unit.AppliedItems.Contains(i)))
                    unit.BattleItems.RemoveAt(i);
            unit.AppliedItems.Clear();
        }
    }

    /// <summary>Применение надетого боевого предмета: удар его атакой (<c>GItem.ATTACK_ID</c>,
    /// <c>getItemAttack</c> <c>0x6449b2</c>) по цели.
    ///
    /// <para><b>Что здесь правило (ранг 1, задача 10).</b> Применением считается именно этот вызов —
    /// проверка цели, — а не построение атаки: <c>attack_new</c> с <c>impl</c> вида <c>IM…</c> движок
    /// пишет на каждом ходу носителя, и в партии с 142 такими строками применений не было ни одного
    /// (П§16.4). В журнале применение видно событием <c>immune</c> с <c>always_only = false</c> по
    /// атаке предмета — то есть ровно тем вызовом иммунитета, который делает
    /// <see cref="Attacks.Apply"/> отсюда. Отсюда же и расход: номер предмета уходит в
    /// <see cref="BattleUnit.AppliedItems"/>, а его следствия считают
    /// <see cref="Item.IsDestroyedAfterBattle"/> и <see cref="Charges.TalismanAfterBattle"/>.</para>
    ///
    /// <para>Броска точности у атаки предмета нет (<c>itemAttack</c>, задача 32), иммунитет
    /// спрашивается как обычно. <b>Кто и когда применяет</b> — политика ИИ или решение игрока; как
    /// <see cref="Defend"/> и <see cref="Wait"/>, отсюда это не решается.</para></summary>
    /// <param name="index">Номер предмета в <see cref="BattleUnit.BattleItems"/> носителя.</param>
    /// <param name="attack">Атака предмета: <c>ATTACK_ID</c>, разрешённый вызывающим через
    /// справочник <c>Gattacks</c> (у <see cref="Item"/> лежит идентификатор, а не строка атаки).</param>
    public AttackOutcome UseBattleItem(BattleUnit bearer, int index, AttackType attack, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        if (index < 0 || index >= bearer.BattleItems.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"у {bearer.Id} в боевых слотах {bearer.BattleItems.Count} предметов");
        MarkUsed(bearer, index);
        return Attacks.Apply(bearer, attack, target, rng, constants, state.Round, state, itemAttack: true);
    }

    /// <summary>Выпить зелье из боевого слота носителя — <b>ранг 1</b> (отчёт B синтетических карт,
    /// ai-q33). Атаки у зелья нет (ни <c>attack_new</c>, ни <c>attack_hit</c>; в журнале —
    /// <c>item_unequip</c> → <c>potion_use</c> → <c>item_equip</c> того же слота).
    /// <list type="bullet">
    /// <item>лечебное — <c>HP_POTION</c> с зажимом по потолку: +50 дало 83 → 100 и 50 → 100
    /// (seq 122535, 1156401);</item>
    /// <item><c>MOD_POTION</c> ложится на цель сразу и держится до конца боя: <c>g000um2002</c> —
    /// броня Squire 0 → 15 в раундах 2 и 3 (seq 1155187…1156437), 8/0;</item>
    /// <item>предмет помечается применённым и после боя уничтожается, в том числе в проигранном
    /// (<see cref="Item.IsDestroyedAfterBattle"/>, 5/0); невыпитое остаётся (16/0).</item>
    /// </list>
    /// Отказы — движковые, <see cref="ItemUse.CanApplyPotion"/>; при отказе ничего не меняется.
    /// <para>Воскрешение — ранг 2: то же <c>applyPotion</c> (<c>0x5d70f9</c>), что на карте (HP 1 — журналы карты 54
    /// из 54; в бою корпус воскрешений зельем не видел), затем <c>0x664de0</c>, если цель была мертва и ожила, снимает
    /// её статусы павшего — мёртв, отступает, защита, яды (<c>0x6227a1</c> с 0, 1, 0x1d, 0x1f, 0x15, 0xf, 0x10…).</para>
    /// <para>ponytail: после боя <c>MOD_POTION</c> остаётся на юните до хода + 1
    /// (<c>CMidSpellEffects</c>, 3/0) — это состояние карты, его переносит вызывающий. Кто и когда пьёт,
    /// ИИ не решает: метод зовёт харнесс по <c>potion_use</c> или игрок, как <see cref="Defend"/>.</para></summary>
    /// <param name="index">Номер зелья в <see cref="BattleUnit.BattleItems"/> носителя.</param>
    public PotionResult DrinkPotion(BattleUnit bearer, int index, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        ArgumentNullException.ThrowIfNull(target);
        if (index < 0 || index >= bearer.BattleItems.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"у {bearer.Id} в боевых слотах {bearer.BattleItems.Count} предметов");
        var item = bearer.BattleItems[index];
        if (item.Category is ItemCategory.PotionRevive)
        {
            var revive = ItemUse.CanApplyPotion(item, inInventory: true, target.Hp, target.HpMax, false);
            if (revive != PotionResult.Ok) return revive;
            target.Hp = 1;
            target.Retreating = target.Defending = false;
            target.Dots.Clear();
            MarkUsed(bearer, index);
            return revive;
        }

        Units.Modifier? boost = null;
        if (item.PotionModifierId.Length > 0)
        {
            var catalogue = state.Rules?.Modifiers
                ?? throw new InvalidOperationException($"зелье {item.Id}: справочника Gmodif в бою нет");
            boost = catalogue.TryGetValue(item.PotionModifierId, out var m) ? m
                : throw new InvalidDataException($"зелье {item.Id}: MOD_POTION \"{item.PotionModifierId}\" в Gmodif не найден");
        }
        bool already = boost is not null && target.Chain.Any(m => m.Id.Equals(boost.Id, StringComparison.OrdinalIgnoreCase));
        var result = ItemUse.CanApplyPotion(item, inInventory: true, target.Hp, target.HpMax, already);
        if (result != PotionResult.Ok) return result;

        target.Hp += ItemUse.HealAmount(item, target.Hp, target.HpMax);
        if (boost is not null) target.Potions.Add(boost);
        MarkUsed(bearer, index);
        return result;
    }

    /// <summary>Предмет применён: для конца боя (<see cref="BattleUnit.AppliedItems"/>) и для хозяина
    /// (<see cref="BattleState.UsedItems"/>).</summary>
    private void MarkUsed(BattleUnit bearer, int index)
    {
        bearer.AppliedItems.Add(index);
        state.UsedItems.Add(new UsedItem(bearer.Id, index));
    }

    /// <summary>Очередь текущего раунда: её раздвигает GiveAttack и удлиняет <see cref="Wait"/>.</summary>
    private List<BattleUnit> _queue = [];

    /// <summary>Случайные части инициативы по раундам (Р4): раунд N+1 бросается в начале раунда N (<see cref="BeginRound"/>), дальше не
    /// меняется. Ключ — id юнита.</summary>
    private readonly Dictionary<int, Dictionary<string, int>> _rolls = [];

    private Dictionary<string, int> RollsOf(int round) =>
        _rolls.TryGetValue(round, out var rolls) ? rolls : _rolls[round] = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Очередь следующего раунда, какой она будет, если до его начала ничего не изменится: заранее брошенные случайные части и
    /// характеристики на этот миг (<see cref="TurnOrder.Order"/> без построения — состояние не меняется, бросков нет). Ходы «Ждать» и дар атаки
    /// следующего раунда ещё не известны. Сторона пала — пусто: следующий раунд — лекари победителя или конец боя.</summary>
    public IReadOnlyList<BattleUnit> NextRound() =>
        Winner is not null || Finished ? []
        : TurnOrder.Order(state, _rolls.GetValueOrDefault(state.Round + 1) ?? NoRolls, constants, commit: false);   // взгляд — без записи

    private static readonly IReadOnlyDictionary<string, int> NoRolls = new Dictionary<string, int>();

    /// <summary>Один вызов beforeBattleRound: номер раунда, сброс <see cref="BattleUnit.Waited"/>
    /// (<c>0x622078</c>) и одно построение очереди.
    /// <para><b>Инициатива на раунд вперёд (Р4, Р17).</b> Очередь раунда строится по случайным частям, брошенным в начале прошлого раунда
    /// (у первого раунда и у юнита без броска — бросок сейчас), и характеристикам на этот миг; следом бросается случайная часть следующего
    /// раунда всем, кто встанет в его очередь (сторона пала — не бросается: следующий — раунд лекарей или конец). Повторное построение того же раунда (раунд 1
    /// строится дважды) бросков не тратит.</para> Удвоение раунда 1 — в <see cref="Run"/>; журнальный
    /// прогон С2 зовёт это на каждое событие round.
    /// <para><b>Предел раундов</b> (<see cref="BattleState.LimitRounds"/>, ранг 1 по коду, корпус слеп — боёв ИИ с ИИ дольше
    /// 10 раундов в нём нет): переход раунда <c>0x62689c</c> до нового раунда спрашивает <c>0x626b2c</c> — в бою нет человека
    /// (<c>0x626ca5</c> по обеим группам) или быстрый бой (<c>0xf48</c> бит 7), и <c>currentRound</c> (<c>+0xf30</c>) равен
    /// <c>BAT_ROUND</c> (<c>GVars +0x24</c>, 10); затем строит новый раунд (<c>0x624474</c>), и если его очередь не пуста — всем
    /// юнитам нападающего (бит <c>attacker</c> <c>+0x30</c>), кроме павших, ушедших и скрытых, ставит «отступает» (бит 29) и
    /// снимает паралич, окаменение и их длинный срок (биты 2–4). Уходят они в свой ход раунда <c>BAT_ROUND + 1</c>
    /// (<see cref="BeginTurn"/>).</para></summary>
    public IReadOnlyList<BattleUnit> BeginRound(int round)
    {
        state.Round = round;
        // Только павшие призванные: изгнание при потере призывателя — в начале хода (0x628a84), очередь строится до него, и
        // изгоняемый бросает инициативу (20261014-replay [245844] seq 245966: раунд 3 — два броска, S143UN025e в их числе).
        ClearFallenSummons(orphans: false);
        foreach (var u in state.Units) u.Waited = false;
        var rolls = RollsOf(round);
        TurnOrder.Roll(state, rng, constants, rolls);
        _queue = [.. TurnOrder.Order(state, rolls, constants, commit: true)];
        if (Winner is not null) AfterBattleQueue(round);
        if (state.LimitRounds && round == constants.BatRound + 1 && _queue.Count > 0)
            foreach (var u in state.Units.Where(u => u.Attacker && u.Alive && !u.Retreated && !u.Hidden))
            {
                u.Retreating = true;
                u.Paralyzed = u.Petrified = u.DisableLong = false;
            }
        // Случайная часть следующего раунда — сейчас (Р4); сторона пала (дальше — лекари победителя или конец боя) — бросать нечего.
        if (Winner is null && !Finished) TurnOrder.Roll(state, rng, constants, RollsOf(round + 1));
        return _queue;
    }

    /// <summary>Раунд, построенный первым после гибели стороны (флаг <c>afterBattle</c>), 0 — такого ещё не было.</summary>
    private int _afterBattleRound;

    /// <summary>Очередь раунда, построенного после гибели стороны (переход раунда <c>0x62689c</c>, флаг <c>afterBattle</c>
    /// <c>0x6216af</c>/<c>0x6216db</c>): броски инициативы тратят все на поле (<see cref="TurnOrder.Build"/>), а в очереди остаются только
    /// лекари победителя (класс <c>Heal</c> у первой или второй атаки), у которых есть годная цель (<c>0x626d49</c> → <c>0x626ff5</c>);
    /// очередь не пуста — со всех снимаются яд, мороз и ожог (<c>0x627142</c>). Пуста — бой окончен. Следующее построение очереди
    /// бой кончает, кто бы в ней ни стоял первым (<c>0x628208</c>). То же правило у экрана боя — <c>BattleSession.StartHealers</c>.
    /// Ранг 1 по корпусу (С2, 16.10): 20260906-b [52975] — раунд 6 [0015 0026 0027 0028], 0026 добивает последнего, в раунде 7 три
    /// броска и очередь [0028] (лекарь, 97 → 125), 0027 из остатка раунда 6 не ходит; раунд 8 — три броска и battle_end;
    /// 20260909-arena2 [313563] — лекари на полном HP: шесть бросков раунда 5 и battle_end без хода. Узор «ходят лекари — следующий
    /// раунд кончает бой» — у 89 боёв корпуса из 89 с лекарем, которому было кого лечить.</summary>
    private void AfterBattleQueue(int round)
    {
        if (_afterBattleRound != 0) { Finished = true; return; }
        _afterBattleRound = round;
        string? winner = Winner;
        _queue = [.. _queue.Where(u => string.Equals(u.Attacker ? state.AttackerGroup : state.DefenderGroup, winner, StringComparison.OrdinalIgnoreCase)
                                       && HealAttack(u) is { } heal && BattleTargets.Possible(state, u, heal).Count > 0)];
        if (_queue.Count == 0) { Finished = true; return; }
        foreach (var unit in state.Units)
            unit.Dots.RemoveAll(d => d.Class is AttackClass.Poison or AttackClass.Frostbite or AttackClass.Blister);
    }

    /// <summary>Атака лечения юнита: первая или вторая с классом <c>Heal</c>; нет — null (воскрешение не в счёт, <c>0x626f3b</c>).</summary>
    private static AttackType? HealAttack(BattleUnit unit) =>
        unit.Attack.Class == AttackClass.Heal ? unit.Attack : unit.Attack2?.Class == AttackClass.Heal ? unit.Attack2 : null;

    /// <summary>Действие «Ждать» (задача 24 Ф7, ранг 1): <c>CBatAttackWait::onHit</c> (GOG
    /// <c>0x659e3e</c>) ставит юнита в первое свободное место <c>turnsOrder</c> (<c>0x6224dd</c>: поиск
    /// <c>emptyId</c> по 13 местам) — то есть в конец очереди раунда — и взводит <c>waited</c>
    /// (<c>0x622e6e</c>). Бросков нет. Предлагается действие только до первого ожидания в раунде
    /// (<c>updateBattleActions</c> <c>0x623ef6</c>), и с раунда 1. Ложь — ждать нельзя.
    /// <para>Когда ждать — политика ИИ или решение игрока, <see cref="Run"/> её не зовёт (как
    /// <see cref="Defend"/>). Тик яда на ходу после ожидания не повторяется —
    /// <see cref="BattleUnit.TurnBegunRound"/>.</para>
    /// <para>Двойной удар после ожидания — оба удара, как у движка (ранг 1 по листингу, 16.10). Ячейка очереди
    /// несёт счётчик действий (<c>attackCount</c>, <c>+0xe74</c>; в новом раунде 2 у ATCK_TWICE, <c>0x626e30</c>).
    /// <c>onHit</c> дописывает ячейку со счётчиком 1 (<c>push 1</c> на <c>0x659e45</c>), конец <c>battleTurn</c> снимает с
    /// ходящего одно действие (<c>0x626519</c>), а для «Ждать» (вид 9, <c>0x62731a</c>) снимает все оставшиеся у юнита
    /// действия по одному, считая (<c>0x62552f</c>–<c>0x625559</c>), и дописывает одну ячейку с этим числом (<c>0x62556f</c>):
    /// у двойного 1 + 1 = 2. Ячейки внеочередных ходов (GiveAttack) того же юнита сливаются туда же —
    /// <see cref="RequeueWaiter"/>.</para>
    /// <para>Корпус: бит <c>waited</c> (0x10 в <c>flags</c>) не взведён ни в одном из 118 347 снимков
    /// юнитов по 111 журналам — ИИ под автобоем не ждёт; строгий С2 этим не объясняется.</para></summary>
    public bool Wait(BattleUnit unit)
    {
        if (state.Round < 1 || unit.Waited) return false;
        unit.Waited = true;
        _queue.Add(unit);
        return true;
    }

    /// <summary>Ждущий — в конец очереди <paramref name="queue"/>, где он ходит под номером <paramref name="current"/>. Движок сливает все
    /// оставшиеся ячейки юнита в одну в конце очереди со счётчиком «оставшиеся действия + 1» (<c>0x625525</c>–<c>0x625574</c>); у модели
    /// ячейка — это <see cref="Attacks.AttacksPerTurn"/> действий (внеочередная тоже: <c>giveAttack</c> зовётся со счётчиком 2 у
    /// ATCK_TWICE, <c>0x660438</c>), поэтому слитая ячейка — столько же его ходов подряд в конце: снятые поздние ячейки плюс одна.</summary>
    public static void RequeueWaiter(List<BattleUnit> queue, int current, BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(queue);
        int moved = 1;
        for (int i = queue.Count - 1; i > current; i--)
        {
            if (!ReferenceEquals(queue[i], unit)) continue;
            queue.RemoveAt(i);
            moved++;
        }
        for (; moved > 0; moved--) queue.Add(unit);
    }

    /// <summary>Начало хода юнита (beforeBattleTurn/battleTurn движка до удара): снимает Defend
    /// (ранг 1, corpus/20260906-a: в turn_before самого юнита бит 31 уже 0 — seq 53227, 53322, 53381,
    /// 53452; тулсет hooks.cpp:1884), один раз за раунд тикает DoT (<see cref="Attacks.TickDots"/>) и
    /// снимает паралич. Возвращает, ходит ли юнит: false — умер от тика или пропускает ход по параличу
    /// (ранг 1, corpus/20260906-c: turn_before st 4 → turn_after st 0 без battle_turn и бросков).
    /// Повторный вызов в том же раунде (второй beforeBattleTurn хода человека/автобоя стороны игрока —
    /// docs/corpus-schema.md) ничего не тикает: 0013 в 20260906-e тикнул 20 один раз при двух
    /// turn_before подряд (seq 53143/53147). Журнальный С2 зовёт это на каждое turn_before.</summary>
    public bool BeginTurn(BattleUnit unit)
    {
        bool decided = Winner is not null;
        ClearFallenSummons();
        // Сторона пала изгнанием призванного в начале хода — бой кончен сразу, без нового раунда (в отличие от гибели от яда и
        // ухода беглеца): 20260922-ui [379898] seq 380083 → 380100, 20261014-replay [245844] seq 245972 → 245988 — turn_before
        // изгоняемого и battle_end без строки round и бросков.
        if (!decided && Winner is not null) { Finished = true; return false; }
        if (Winner is not null && !Attacks.IsHealer(unit)) { Finished = true; return false; }
        if (unit.Unsummoned) return false;   // изгнан в начале своего же хода (0x628a84) — хода нет
        // Отложенное «Исцеление»: паралич и превращение снимаются у всех, на ком бит Cured, в начале
        // обработки хода (0x628874) — Attacks.ResolveCured, ранг 1. Без Cured это no-op.
        Attacks.ResolveCured(state);
        unit.Defending = false;
        // Варды, которые раздал сам ходящий, снимаются в начале ЕГО хода (beforeBattleTurn 0x62673f,
        // ранг 1, задача 21 Ф7) — Immunity.ClearWards. Не раздавал — no-op, Ф4/Ф5 ничего не замечают.
        Immunity.ClearWards(unit);
        if (unit.TurnBegunRound == state.Round) return unit.OnField;
        unit.TurnBegunRound = state.Round;
        NoteDots(unit);
        bool aliveBeforeTick = unit.Alive;
        Attacks.TickDots(unit, rng, constants, state.Round);
        if (aliveBeforeTick && !unit.Alive) state.DotDeaths.Add(new DotDeath(unit.Id, Log.Count));
        // Срок длинного превращения чужим (TransformLong, бит 23) — после ядов, до паралича: обработка хода
        // зовёт 0x6287d6 следом за тремя обработчиками DoT. При бите 23 — тот же checkLongEffectDuration, что у
        // длинного яда, с roundsPassed = раунд − раунд наложения (0x628821: 0x621fb8 − 0x6234bc → 0x626413);
        // сработал — откат 0x625d9e, и юнит ходит уже своей атакой. Ранг 1; корпус 20260919-human [4934456]:
        // жертва Ведьмы в раундах 2 и 3 бросает rng(100) в своём turn_before (55, 63 — держит), [7958576]:
        // S073UN0198 снова без битов 22/23 до второго превращения.
        // Бросок — и у умершего от тика: вызов 0x628119 → 0x6287d6 идёт за обработчиками DoT (0x6280c8, 0x6280e3,
        // 0x6280fe) без проверки гибели, а смерть снимает биты DoT (15–20, поэтому своего броска яд уже не делает:
        // 0x62832b смотрит бит 15), но не 22/23. Ранг 1: 20261019-cov [1531355] — отравленный и превращённый
        // S092UN010b (10 HP) гибнет от яда в своём turn_before 1531756, st 0xC18000 → 0xC00002, rng(100) = 15 (держит);
        // покрытие зонда отметило первый вход в 0x628821 на seq 1531757 — между ними. У 43 смертей от DoT корпуса без
        // бита 23 бросков нет ни одного. Умерший до хода (aliveBeforeTick = false) хода у движка не получает.
        if (aliveBeforeTick && unit.TransformedLong
            && Attacks.LongEffectBreaks(rng, constants, state.Round - unit.TransformAppliedRound))
        {
            TransformAttacks.Untransform(unit, state);
            state.Notes.Add(new BattleNote(state.Round, unit.Id, BattleNoteKind.Untransformed));
        }
        if (!unit.Alive) return false;
        // Паралич и окаменение проверяются РАНЬШЕ бегства — ранг 1 (задача 31 Ф7). Обработка хода зовёт
        // подряд обработчики паралича (GOG 0x628134 → 0x628d97, бит 2), окаменения (0x62815c → 0x628e48,
        // бит 3) и бегства (0x628188 → 0x628ef9); последний при взведённом бите 2 или 3 отвечает «ход
        // занят» (0x628f38–0x628f77), не трогая бит 29. Парализованный беглец пропускает ход и остаётся на
        // поле с намерением бежать: corpus/20260911-passive/passive-001 seq 778797 → 778800, st 0x20000014
        // до и после хода, бита 28 нет. Окаменение снимается тем же порядком, что паралич, — ранг 3.
        // Срок — 0x626200: в ход жертвы короткий снимается сразу, длинный (бит 4) — по checkLongEffectDuration
        // (0x626379, roundsPassed = раунд − раунд наложения [+0x32]); в ход наложившего длинный держится без
        // броска. Ход занят и когда снят (0x628d97 → al = 1 безусловно). Ранг 1: 20260920-gaps [393470] —
        // лидер под длинным параличом Призрака: раунд 2 rng 21 держит, раунд 3 rng 80 снимает, удара нет.
        if (unit.Paralyzed || unit.Petrified)
        {
            if (!unit.DisableLong || Attacks.LongEffectBreaks(rng, constants, state.Round - unit.DisableAppliedRound))
            {
                if (unit.Paralyzed) state.Notes.Add(new BattleNote(state.Round, unit.Id, BattleNoteKind.Unparalyzed));
                if (unit.Petrified) state.Notes.Add(new BattleNote(state.Round, unit.Id, BattleNoteKind.Unpetrified));
                unit.Paralyzed = unit.Petrified = unit.DisableLong = false;
            }
            return false;
        }
        // Собравшийся бежать (бит 29) в свой ход уходит с поля: бит 28 вместо 29 (0x628ff8, 0x629008), ход
        // пропущен, ни одного броска — ранг 1 (corpus/20260906-c, seq 53197 → 53198). Призванный беглец
        // вместо ухода изгоняется (бит 27 → 0x628f96 → 0x628fb4 → 0x628c00, ранг 1 по листингу; в корпусе бегства
        // призванных нет). Ранг 3 — порядок относительно тика DoT: жертва Fear в корпусе не была отравлена.
        if (unit.Retreating)
        {
            unit.Retreating = false;
            if (unit.Summoned) Unsummon(unit);
            else MarkRetreated(unit);
            return false;
        }
        return true;
    }

    /// <summary>Павший призванный уходит из боя до следующего хода — <b>ранг 1 по корпусу</b>: в <c>turn_after</c> хода, где его убили, он
    /// ещё в списке юнитов боя (HP 0, статус <c>0x8000003</c> — призван, мёртв), в следующем <c>turn_after</c> его нет вовсе, и на его место
    /// можно призвать снова (<c>corpus/20260918-probe65/passive-001 [326640]</c>: <c>S143UN0274</c> seq 326887 → 326922, <c>S143UN0275</c>
    /// 327036 → 327071, <c>S143UN0277</c> 327607 → 327642; так же <c>20260928-shots</c>, <c>20260930-probe72</c>, <c>20261014-replay</c>). Значит,
    /// трупа на поле нет, в панели его нет, в перемешивании площадной атаки он не участвует. Модель снимает его в начале следующего хода
    /// (и раунда) в <see cref="BattleState.Gone"/> — опыт за убийство и показ смерти его там находят.
    /// <para><b>Снятие призыва очередью эффектов</b> (<c>CBatAttackUnsummonEffectQueued</c>, 16.10, ранг 1 по корпусу). Обработка хода
    /// <c>0x627ffa</c> сразу после отложенного «Исцеления» (<c>0x628874</c>) зовёт <c>0x628a84</c>: по всем юнитам с битом 27 —
    /// призыватель (<c>0x62351d</c>) и его юнит в бою (<c>0x623cc0</c>); призванный остаётся, только если сам жив и призыватель есть, жив
    /// (<c>0x5e98ad</c>: HP &gt; 0) и не ушёл с поля (бит 28, <c>0x628b98</c>). Иначе — <c>0x628c00</c>: эффект изгнания
    /// (<c>CBatAttackUnsummonEffect</c>, фабрика <c>0x6458ae</c> вид 4) и следом очередной (<c>0x628cea</c>, вид 5, <c>onHit</c>
    /// <c>0x663c1f</c>: юнит уходит из отряда и из боя, на его место возвращаются тела, которые он закрывал). Тот же
    /// <c>0x628c00</c> зовут бегство призванного (<see cref="BeginTurn"/>) и конец боя (<c>0x62953c</c>,
    /// <see cref="TransformAttacks.AfterBattle"/>). Корпус: <c>20260922-ui [379898]</c> — призыватель <c>S143UN000b</c> пал в раунде 2,
    /// призванный <c>S143UN009a</c> (HP 39) в начале своего хода раунда 3 изгнан без удара, и бой кончен победой другой стороны (seq
    /// 380083 → 380100); так же <c>[5980207]</c> и <c>20261014-replay [245844]</c>.</para></summary>
    /// <param name="orphans">Изгонять и живых призванных, потерявших призывателя (начало хода); false — только павших (раунд).</param>
    public void ClearFallenSummons(bool orphans = true)
    {
        foreach (var gone in state.Units.Where(u => u.Summoned && (!u.Alive || orphans && SummonerLost(u))).ToList())
        {
            if (gone.Alive) Unsummon(gone);
            else
            {
                state.Units.Remove(gone);
                state.Gone.Add(gone);
            }
        }
    }

    // null — призыватель не известен (бой собран без призыва): движок такого не знает, модель не изгоняет.
    private bool SummonerLost(BattleUnit summoned) =>
        summoned.SummonerId is { } id
        && state.Units.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase)) is not { Alive: true, Retreated: false };

    /// <summary>Изгнание живого призванного посреди боя (<c>0x628c00</c>): с поля и из очереди — в <see cref="BattleState.Gone"/>, с
    /// сообщением для показа.</summary>
    private void Unsummon(BattleUnit unit)
    {
        unit.Unsummoned = true;
        state.Units.Remove(unit);
        state.Gone.Add(unit);
        state.Notes.Add(new BattleNote(state.Round, unit.Id, BattleNoteKind.UnsummonedInBattle));
    }

    /// <summary>Конец хода юнита (afterBattleTurn движка при unitId != nextUnitId): снимает
    /// КОНЕЧНЫЕ, то есть не помеченные Long, BoostDamage и LowerDamage — survey §7,
    /// A hooks.cpp:1786-1791 (removeFiniteBoostLowerDamage), ранг 2. Бросков не тратит.
    /// <para>Именно конец хода, а не начало: буст применяется, когда носитель бьёт, и снятие в
    /// beforeBattleTurn обнулило бы его смысл целиком.</para>
    /// <para><b>LowerInitiative здесь не снимается</b> — ранг 1: конечное снижение гасит само построение
    /// очереди (<c>0x624752</c>, см. <see cref="TurnOrder.Build"/>), применив его один раз.</para>
    /// <para><b>Ранг 3 — пропущенный ход.</b> <see cref="Run"/> зовёт это только после состоявшегося
    /// действия; ход, пропущенный по параличу, окаменению или отступлению, статусы не снимает.
    /// В движке afterBattleTurn зовётся, похоже, и там — но это не проверено, а сегодня разницы нет
    /// вовсе: буста и снижений в корпусе не встречается. Журнальный режим С2 зовёт EndTurn на каждом
    /// turn_after с unit != next, то есть в нём пропущенный ход учитывается.</para></summary>
    public void EndTurn(BattleUnit unit)
    {
        if (!unit.BoostDamageLong) unit.BoostDamageLevel = 0;
        if (!unit.LowerDamageLong) unit.LowerDamageLevel = 0;
        // Воскрешённый и призванный этим ходом встанут в следующий раунд — их случайная часть бросается сейчас, чтобы полоса
        // следующего раунда была точной (Р4). Раунд, начатый не этой моделью (ход собран в проверке), заранее не бросает.
        if (Winner is null && _rolls.TryGetValue(state.Round + 1, out var next)) TurnOrder.Roll(state, rng, constants, next);
    }

    /// <summary>Действие «защита» вместо удара: статус Defend до следующего <see cref="BeginTurn"/>
    /// юнита (CBatAttackDefend::onHit — hooks.cpp:3300 setUnitStatus Defend=true), бросков не тратит
    /// (корпус: между turn_before 53111 и turn_after 53113 нет rng). Когда защищаться — политика ИИ
    /// (<see cref="BattleAi.Choose"/>, зовёт <see cref="Run"/>); журнальный С2 зовёт по биту 31 в
    /// turn_after ходящего.</summary>
    public void Defend(BattleUnit unit) => unit.Defending = true;

    /// <summary>Одно действие юнита (один вызов battleTurn): первая атака по каждой цели, затем
    /// вторая атака (ATTACK2_ID) по тем целям, по которым первая попала и которые живы, в том же
    /// порядке — ранг 1 (corpus/20260906-c Shamaness: 3 удара, затем ровно столько Fear-бросков,
    /// сколько живых попаданий; 20260906-e Master Thug/Spider: яд после попадания, нет после промаха
    /// и после убившего удара). Цель: площадная (REACH 1) — вся группа врага (для Heal — своя)
    /// через <see cref="Attacks.Shuffle"/>, живые; иначе <paramref name="target"/> — обязательна: её
    /// выбирает политика ИИ (<see cref="BattleAi.Choose"/>) или журнал (С2). Записи лечения — только с healed &gt; 0, по
    /// id цели (журнал показывает лечение одним снимком HP, порядка у него нет). Пустой список — бить
    /// (лечить) некого. Ранг 3 для boost = lower = 0 и easy = false: в корпусе у dmg_buffs всегда
    /// boost == lower == 0 и easy == false (C1HarnessTests). Каждая запись сразу попадает в
    /// <see cref="Log"/>.</summary>
    public IReadOnlyList<TurnRecord> Act(int round, BattleUnit unit, BattleUnit? target = null)
    {
        var attack = Striking(unit, unit.Attack, target);
        // Summon в пару «атакующий — цель» не укладывается вовсе: движок целится в ПОЗИЦИЮ в отряде
        // атакующего, а не в юнита (потому у него и method17 свой — 0x66386f, единственный на 23
        // класса, разрешающий слот мёртвого и отступившего). Цель передаётся формально и не
        // читается; записи хода нет — в журнале у призыва ни power, ни miss, ни dmg.
        if (attack.Class == AttackClass.Summon)
        {
            Attacks.Apply(unit, attack, unit, rng, constants, round, state);
            state.Actions.Add(new ActionNote(round, unit.Id, unit.Id, 1, attack.Class));
            return [];
        }

        var (targets, places) = TargetsOf(unit, attack, target);

        var records = new List<TurnRecord>();
        if (attack.Effect.TargetsAllies)
        {
            ActOnAllies(round, unit, attack, targets, records);
            Log.AddRange(records);
            return records;
        }

        var hits = new List<BattleUnit>();
        bool sure = SureFirstHit(attack, places.Count, round);
        foreach (var t in targets)
        {
            int place = Math.Max(0, places.FindIndex(p => ReferenceEquals(p, t)));   // одиночная цель — 0
            int reduction = Formulas.MageLeaderPowerReduction(unit.Type, place);
            var r = Attacks.Apply(unit, attack, t, rng, constants, round, state, reduction, sureHit: sure);
            state.Actions.Add(new ActionNote(round, unit.Id, t.Id, 1, attack.Class));
            bool landed = !r.Immune && (!r.Miss || sure);
            if (!r.Immune) sure = false;   // гарантия снимается первой неиммунной целью, промахнулась она или нет
            Add(new TurnRecord(round, unit.Id, t.Id, r.Miss, r.Damage, t.Hp, Crit: r.CritDamage));
            // Иммунная к первой атаке цель второй не получает — ранг 1, задача 19 Ф7: battleTurn
            // держит флаг «не попали» ([ebp-0xa0] = 1 на 0x624c5d), снимает его только после onHit
            // (0x6251ae), а иммунная ветка уходит к следующей цели раньше. Цели с флагом собираются
            // (0x625227) и вычёркиваются из списка (0x6252b7–0x62534b) до следующего номера атаки
            // (0x62494d). Прав вики. Тот же счёт уже держали харнессы (C1 AttackIndexOf, C2 Recorded):
            // вторая атака — по целям с событием dmg, а у иммунной его нет.
            if (landed && t.Alive) hits.Add(t);
        }
        // Вторая атака проходит свой canPerform: по отступающему яд не кладётся, слабый яд на
        // сильный тоже (Attacks.CanPerform, ранг 1). Штраф мага-лидера ко второй атаке не передаётся —
        // ранг 3: ни у одного из шести прошитых магов-лидеров второй атаки нет. Ход запасной атакой (превращение себя по врагу)
        // второй атаки юнита не несёт.
        if (unit.Attack2 is { } second && ReferenceEquals(attack, unit.Attack))
            foreach (var t in hits.Where(h => Targetable(unit, second, h)))
            {
                var r = Attacks.Apply(unit, second, t, rng, constants, round, state);
                state.Actions.Add(new ActionNote(round, unit.Id, t.Id, 2, second.Class));
                Add(new TurnRecord(round, unit.Id, t.Id, r.Miss, r.Damage, t.Hp, Attack: 2, Crit: r.CritDamage));
            }
        return records;

        void Add(TurnRecord rec) { records.Add(rec); Log.Add(rec); }
    }

    /// <summary>Атака по своим и вторая атака по тем же целям — <b>ранг 1</b>. <c>battleTurn</c> идёт по номерам атак (<c>0x62494d</c>) одинаково
    /// для всех классов: вторая — по целям первой, на которых флаг «не попали» снят (у атак по своим броска точности нет — снят у всех, кроме
    /// иммунных), если её <c>canPerform</c> соглашается. Так лечение Аббатисы снимает яд её второй атакой (<c>Cure</c>), Патриарх
    /// (<c>g000uu0150</c>: лечение, затем <c>Revive</c>) поднимает павшего, а Лесная Дева лечит того, на кого наложила обереги. Журнал оригинала
    /// <c>corpus/20260908-f6/map-001 [1281528]</c> seq 1286022–1286030: <c>attack_new number 1</c> (G000AA0150, класс 6) и
    /// <c>number 2</c> (G000AA1150, класс 14) по павшему <c>S143UN0007</c> — HP 0 → 60 (половина 120), флаги 8 → 72; лечение павшего не
    /// трогает (<see cref="Attacks"/>, лечение). Прежде вторую атаку получали только удары по врагу — снятие яда и воскрешение второй атакой
    /// не срабатывали вовсе (партия 15.10, пометки 2 и 5).
    /// <para>Записи хода — только с полученным здоровьем (лечение, воскрешение — с номером атаки); прочее — в <see cref="AllyLog"/>: у
    /// <c>Cure</c> и <c>BoostDamage</c> в журнале нет ни power, ни miss, ни dmg.</para></summary>
    private void ActOnAllies(int round, BattleUnit unit, AttackType attack, List<BattleUnit> targets, List<TurnRecord> records)
    {
        var landed = new List<BattleUnit>();
        foreach (var t in targets.OrderBy(u => u.Id, StringComparer.OrdinalIgnoreCase))
        {
            var h = Attacks.Apply(unit, attack, t, rng, constants, round, state);
            state.Actions.Add(new ActionNote(round, unit.Id, t.Id, 1, attack.Class));
            if (h.Healed > 0) records.Add(new TurnRecord(round, unit.Id, t.Id, false, 0, t.Hp));
            if (h.Healed == 0 && attack.Class != AttackClass.Heal && (attack.Class != AttackClass.BoostDamage || h.Applied))
                AllyLog.Add(new AllyNote(round, unit.Id, t.Id, attack.Class));
            if (!h.Immune) landed.Add(t);
        }
        if (unit.Attack2 is not { } second || !ReferenceEquals(attack, unit.Attack) || !second.Effect.TargetsAllies) return;
        foreach (var t in landed.Where(t => Targetable(unit, second, t)))
        {
            var h = Attacks.Apply(unit, second, t, rng, constants, round, state);
            state.Actions.Add(new ActionNote(round, unit.Id, t.Id, 2, second.Class));
            if (h.Healed > 0) records.Add(new TurnRecord(round, unit.Id, t.Id, false, 0, t.Hp, Attack: 2));
            else if (second.Class != AttackClass.Heal) AllyLog.Add(new AllyNote(round, unit.Id, t.Id, second.Class, Attack: 2));
        }
    }

    /// <summary>Атака, которой юнит бьёт эту цель: у превращения себя по врагу — запасная (<c>onHit</c> <c>0x6613e1</c> отдаёт удар ей, и цели
    /// она берёт сама: у <c>g000aa1164</c> REACH «все» — журнал <c>corpus/20260908-f6/map-001</c> seq 1285970–1285987: Волчий лорд
    /// <c>S143UN0008</c> бьёт обоих врагов, броски и урон по каждому); у двойника — запасная, если цель не из его копий (<c>fillTargetsList</c>
    /// <c>0x6619fa</c>: копий нет — список запасной атаки; <c>0x662336</c>). Копии — без фильтра <c>Always</c>: цель выбрал либо человек из
    /// своего списка, либо ИИ из своего — у ИИ без копий, прошедших фильтр, цель приходит из списка запасной атаки. Иначе — сама атака.</summary>
    private AttackType Striking(BattleUnit unit, AttackType attack, BattleUnit? target)
    {
        if (target is null || state.Rules?.AltAttackOf(attack) is not { } alt) return attack;
        return attack.Class switch
        {
            AttackClass.TransformSelf when target.Attacker != unit.Attacker => alt,
            AttackClass.Doppelganger when !BattleTargets.CopyTargets(state, unit, attack, human: true).Contains(target) => alt,
            _ => attack,
        };
    }

    /// <summary>Порог списка целей и последний раунд гарантии для Paralyze/Petrify (<c>0x627c99</c>).</summary>
    private const int SureHitMinTargets = 4, SureHitLastRoundForStun = 40;

    /// <summary>Гарантированное попадание по первой цели площадной атаки — <b>ранг 1</b>.
    /// <c>0x627c99</c> (зовётся из <c>battleTurn</c> на <c>0x624ad3</c> только для первой атаки): REACH = L_ALL
    /// (<c>0x8380c0</c>) и в списке целей больше трёх (<c>0x6426c0</c>, <c>cmp eax, 3; jbe</c>); у L_PARALYZE
    /// (<c>0x837480</c>) и L_PETRIFY (<c>0x837540</c>) ещё и раунд (<c>0x621fb8</c>) не больше 40. Флаг
    /// (<c>[ebp-0x2c]</c>) гасится на первой цели, прошедшей <c>canPerform</c>, живой и не иммунной
    /// (<c>0x624e42</c>–<c>0x624e83</c>), и её промах сбрасывается в «попал»; броски потрачены, <c>miss</c> в
    /// журнале остаётся true. Список — вся перемешанная группа с мёртвыми (<see cref="Attacks.Shuffle"/>).
    /// Корпус (<c>re-ai3/forcehit2.py</c>): список &gt; 3 и промах первой цели — удар прошёл 18/18 по dmg/attack_hit
    /// (+3 окаменения по биту 3 в turn_after); список ≤ 3 — ни одного (0/14); промахи не первой цели не
    /// спасаются никогда. <c>passive-003 [12529065]</c> seq 12529518: miss=true, dmg 15, HP 34 → 19.
    /// ponytail: второе правило той же пары — <c>0x627d31</c> (L_TRANSFORM_OTHER + L_ALL: не больше ⌈n/2⌉
    /// попаданий, остальные — принудительный промах, <c>0x624e87</c>) — не перенесено, в ванили такой атаки нет.</summary>
    internal static bool SureFirstHit(AttackType attack, int listed, int round) =>
        attack.Reach == AttackType.ReachAll && listed >= SureHitMinTargets
        && !(attack.Class is AttackClass.Paralyze or AttackClass.Petrify && round > SureHitLastRoundForStun);

    /// <summary>Может ли этот класс целиться в этого юнита. Все двадцать два класса, кроме одного,
    /// бьют только тех, кто на поле; <see cref="AttackClass.Revive"/> — ровно наоборот, он
    /// единственный, кому движок разрешает мёртвую цель (слот 14 vftable, 0x65f606 — единственный
    /// не-заглушка среди 23 классов). Отступивший недосягаем и для него: с поля он ушёл, а
    /// воскрешать живого нечего. Остальное — <c>canPerform</c> класса (<see cref="Attacks.CanPerform"/>).</summary>
    private static bool Targetable(BattleUnit attacker, AttackType attack, BattleUnit u) => BattleTargets.Eligible(attacker, attack, u);

    /// <summary>Цели действия и места в перемешанной группе. Площадная (REACH 1) — вся группа врага
    /// (для атак по своим — своя) через <see cref="Attacks.Shuffle"/>; <c>places</c> — весь
    /// перемешанный список с мёртвыми, по нему считается штраф мага-лидера. Одиночная — переданная цель
    /// (<see cref="BattleAi.Choose"/> или журнал), мест нет.</summary>
    private (List<BattleUnit> targets, List<BattleUnit> places) TargetsOf(BattleUnit unit, AttackType attack, BattleUnit? target)
    {
        if (attack.Reach == AttackType.ReachAll)
        {
            var group = attack.Effect.TargetsAllies ? state.Allies(unit) : state.Enemies(unit);
            var places = Attacks.Shuffle(group, rng);
            return (places.Where(u => Targetable(unit, attack, u)).ToList(), places);
        }
        if (target is null)
            throw new ArgumentNullException(nameof(target),
                $"одиночная атака {attack.Class} юнита {unit.Id}: цель выбирает BattleAi.Choose или журнал");
        // canPerform и для одиночной цели (battleTurn 0x624c88 — по каждой цели до бросков): «нельзя» —
        // ни бросков, ни эффекта, ход сделан (20260920-gaps [393470]: Paralyze по окаменевшему).
        return (Targetable(unit, attack, target) ? [target] : [], []);
    }

    /// <summary>Решение ИИ бежать с поля (действие <c>CBatAttackRetreat</c>): бит 29 у самого
    /// ходящего, в свой следующий ход он уходит (<see cref="BeginTurn"/>). Когда бежать — политика
    /// ИИ (<see cref="BattleAi.Choose"/>, зовёт <see cref="Run"/>); журнальный С2 зовёт по биту 29 в
    /// <c>turn_after</c> ходящего, как <see cref="Defend"/> по биту 31. Ранг 1: <c>20260911-passive/passive-001 [393173]</c> —
    /// <c>S073UN0214</c> в свой ход раунда 3 без удара получает бит 29, в раунде 4 его ещё бьют, в
    /// раунде 5 он уже бит 28 и очередь строится без него.
    /// <para><b>Быстрое бегство</b> (<c>LmodifE</c> 16 <c>FastRetreat</c>, в ванили — модификатор <c>g000um7546</c> артефакта
    /// <c>G000IG2007</c>, «Allows leader to instantly retreat»): <c>CBatAttackRetreat::onHit</c> (GOG <c>0x659fee</c>, слот 12
    /// vftable <c>0x6f2d2c</c>) берёт реализацию ходящего (<c>0x623cc0</c> → <c>0x5e97d8</c>), приводит её к <c>IUsStackLeader</c>
    /// (<c>0x40a41f</c>, тип <c>0x78d0c0</c>) и спрашивает <c>getFastRetreat</c> (<c>call [edx+0x20]</c>, <c>0x65a056</c>; у
    /// модификатора <c>CUmStack</c> — <c>0x5a3a2f</c>: свой флаг или предыдущий слой). Истина — сразу бит 28 (<c>0x65a06a</c>,
    /// <c>setUnitStatus 0x6227a1</c>) и эффект вида «ушёл» (<c>0x62c4f0(3)</c>): юнит покидает поле в этот же ход; ложь — бит 29
    /// (<c>0x65a089</c>). Не лидер (приведение дало null) — всегда бит 29. Ранг 1 по листингу; других чтений
    /// <c>getFastRetreat</c> через это приведение в коде нет (все 145 вызовов <c>0x40a41f</c> просмотрены). Корпус слеп: артефакта
    /// в журналах и сохранениях нет. Ждавший до бегства ещё получает <c>retreatingAfterWait</c> (<c>0x622eaf</c> → <c>0x6236d0</c>,
    /// бит 7 <c>+0x30</c>) — <see cref="BattleUnit.RetreatingAfterWait"/>: следующий раунд он пропускает.</para></summary>
    public void Retreat(BattleUnit unit)
    {
        // Ждавший в этом раунде — ещё и бит 7 (0x65a001 → 0x65a01b), до выбора «ушёл / уходит»: следующий раунд он пропустит.
        if (unit.Waited) unit.RetreatingAfterWait = true;
        if (HasFastRetreat(unit)) MarkRetreated(unit);
        else unit.Retreating = true;
    }

    /// <summary>Юнит ушёл с поля (бит 28) — с местом в журнале модели для дележа опыта (<see cref="BattleState.Retreats"/>).</summary>
    private void MarkRetreated(BattleUnit unit)
    {
        unit.Retreated = true;
        state.Retreats.Add(new RetreatMark(unit.Id, Log.Count));
    }

    /// <summary><c>IUsStackLeader::getFastRetreat</c> ходящего: реализация — лидер (<see cref="UnitType.IsLeader"/>) и в цепочке
    /// есть <c>FastRetreat</c> (<see cref="Units.EffectiveStats.FastRetreat"/>).</summary>
    private static bool HasFastRetreat(BattleUnit unit) =>
        unit.Type.IsLeader && unit.Chain.Count > 0 && Units.EffectiveStats.Of(unit.Type, unit.Chain).FastRetreat;
}
