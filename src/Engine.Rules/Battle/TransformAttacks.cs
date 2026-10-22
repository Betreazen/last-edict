namespace D2.Rules.Battle;

/// <summary>Третий круг классов атак (задача 7 Ф6): <c>GiveAttack</c> (19), <c>Doppelganger</c> (20),
/// <c>TransformSelf</c> (21), <c>TransformOther</c> (22) — плюс откат превращений и призыва после
/// боя (<see cref="AfterBattle"/>, survey §7).
///
/// <para><b>Откуда числа.</b> Все четыре <c>onHit</c> и все четыре <c>canMiss</c>/<c>isImmune</c>
/// прочитаны в <c>game-pristine/Discipl2.exe</c> (GOG 3.01) до <c>ret</c> — <b>ранг 1</b>; ранг 2
/// не понадобился нигде. Vftable добыты тем же способом, что у задачи 6: <c>createBatAttack</c>
/// (<c>0x644b3e</c>) ветвится по категории класса, ветка зовёт конструктор, конструктор кладёт свой
/// vftable. Сопоставление «категория ↔ класс» проверено на девяти классах, чьи vftable уже назвала
/// задача 6, и совпало с таблицей тулсета (<c>attackclasscat.cpp</c>, блок Gog) во всех девяти.
/// <list type="bullet">
/// <item><c>GiveAttack</c>: vft <c>0x6f33bc</c>, <c>onHit</c> <c>0x6603dc</c>, <c>canMiss</c> и
/// <c>isImmune</c> — общие заглушки <c>0x6598f5</c>/<c>0x659913</c>;</item>
/// <item><c>Doppelganger</c>: vft <c>0x6f34ac</c>, <c>onHit</c> <c>0x6621cd</c>, <c>canMiss</c>
/// <c>0x661fd5</c>, <c>isImmune</c> <c>0x66208b</c> (обе — развилки, см. ниже);</item>
/// <item><c>TransformSelf</c>: vft <c>0x6f345c</c>, <c>onHit</c> <c>0x6613e1</c>, <c>canMiss</c>
/// <c>0x6611a7</c>, <c>isImmune</c> <c>0x661279</c> (тоже развилки);</item>
/// <item><c>TransformOther</c>: vft <c>0x6f340c</c>, <c>onHit</c> <c>0x660845</c>, <c>canMiss</c>
/// <c>0x6607f3</c> (<c>mov al,1</c>), <c>isImmune</c> <c>0x660802</c> (прямой вызов
/// <c>isUnitImmuneToAttack</c> <c>0x65a528</c>).</item>
/// </list></para>
///
/// <para><b>Бросков боевого потока — ноль у всего круга, кроме точности TransformOther.</b>
/// Механическая сверка (обход графа вызовов из каждого <c>onHit</c> на три уровня вглубь): вызовов
/// <c>generateRandomNumber</c> (<c>0x54851f</c>) — ноль во всех четырёх телах и во всём, что они
/// зовут. Единственный источник случайности — <c>generateRandomNumberStd</c> (<c>0x548576</c>,
/// <c>rand()</c> процесса) внутри <c>getSummonUnitImplIdByAttack</c> (<c>0x660a60</c>), и он
/// срабатывает <b>только когда кандидатов больше одного</b>: при одном функция выходит раньше
/// (<c>0x660af0</c>). В ванили после фильтра по размеру кандидат ровно один у всех семи атак
/// классов 21 и 22, так что <b>ни одна вани́льная атака этого круга случайности не тратит вовсе</b>
/// — в отличие от <c>Summon</c>, где двух атак это касается.</para>
///
/// <para><b>Развилка обоих превращающих классов — не здесь.</b> У <c>Doppelganger</c> и
/// <c>TransformSelf</c> все ключевые слоты vftable ветвятся: пока превращение возможно, класс делает
/// своё дело, иначе <b>отдаёт удар запасной атаке целиком</b> (<c>Gattacks.ALT_ATTACK</c>, поле
/// <c>altAttack</c> объекта). Задача 8 дочитала <c>ALT_ATTACK</c> в
/// <see cref="AttackType.AltAttackId"/> и закрыла вторую ветвь: условие и вызов запасной атаки живут
/// в <see cref="Attacks.Apply"/> (рекурсией — запасная атака приносит свою точность и свой
/// иммунитет), а сюда управление доходит только по ветви превращения. До этого вторая ветвь честно
/// бросала исключение — это был долг задачи 7.</para></summary>
public static class TransformAttacks
{
    /// <summary>Передача хода союзнику (<c>onHit</c> <c>0x6603dc</c>). Ни точности, ни иммунитета —
    /// обе заглушки vftable, значит <b>ноль бросков</b>. Ранг 1.
    ///
    /// <para><b>Что это на самом деле — не «ещё одна атака», а вставка в очередь.</b> Тело
    /// <c>onHit</c> считает <c>attackCount = soldier-&gt;getAttackTwice() ? 2 : 1</c>
    /// (<c>0x66043b</c>: вызов слота 20 <c>IUsSoldier</c>, затем идиома
    /// <c>neg/sbb/neg; add eax,1</c>) и зовёт <c>BattleMsgData::giveAttack</c> (<c>0x62266e</c>) с
    /// <c>lastIndex = 1</c>. Сама <c>giveAttack</c> прочитана до <c>ret</c>: она сдвигает
    /// <c>turnsOrder[i] = turnsOrder[i-1]</c> для i от 12 вниз до <c>lastIndex+1</c> и кладёт
    /// <c>turnsOrder[1] = {unitId, attackCount}</c>. Смещение массива — <c>0xe70</c> от начала
    /// <c>BattleMsgData</c>, что ровно <c>assert_offset(BattleMsgData, turnsOrder, 3696)</c>
    /// тулсета. То есть <b>цель ходит следующей, сразу за нынешним ходящим</b>. Ранг 1.</para>
    ///
    /// <para><c>attackCount</c> модель не переносит: у неё это <see cref="Attacks.AttacksPerTurn"/>
    /// от того же <c>ATCK_TWICE</c>, то есть та же величина с другой стороны.</para>
    ///
    /// <para><b>Ранг 3 — попадает ли внеочередной ход в тот же раунд при переполнении очереди.</b>
    /// В движке <c>turnsOrder</c> — тринадцать ячеек, и вставка выталкивает последнюю. Модель
    /// держит очередь списком без потолка, потому что двенадцать бойцов плюс одна вставка в
    /// тринадцать ячеек влезают; два <c>GiveAttack</c> за раунд при полном поле — уже нет. Снимает
    /// бой задачи 13 с двумя Алхимиками (<c>g000uu0161</c>) и полными отрядами.</para></summary>
    /// <para><b>Внеочередной ход — полный ход</b>, ранг 1 по журналу (С2, 16.10): начало хода с тиком ядов и снятием паралича идёт
    /// в нём заново, хотя юнит в этом раунде уже ходил (<see cref="BattleUnit.TurnBegunRound"/> сбрасывается). Корпус
    /// <c>20261016-port-synth [502965]</c>: <c>S143UN0008</c> под длинным ядом — первый ход раунда 1 (seq 505354: <c>rng(100)</c>
    /// срока, 41 → 21), Алхимик <c>S143UN000a</c> отдаёт ему ход (505997), внеочередной ход (506000: снова <c>rng(100)</c>, 21 → 1).</para>
    public static AttackOutcome GiveAttack(BattleUnit target)
    {
        target.ExtraTurn = true;
        target.TurnBegunRound = -1;   // ещё не ходил в раунде — и так -1 или прошлый раунд; ходил — начало хода пойдёт заново
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Копия цели (<c>onHit</c> <c>0x6621cd</c>). Превращается <b>сам атакующий</b>, а не
    /// цель: <c>transformUnit</c> получает <c>&amp;this-&gt;unitId</c> (<c>0x662288</c>).
    ///
    /// <para><b>Во что именно — ранг 1, и это не «тип цели как есть».</b> Движок берёт
    /// <c>targetUnit-&gt;unitImpl-&gt;id</c> и прогоняет его через <c>CUnitGenerator</c>, слот 4
    /// vftable — <c>getGlobalUnitImplId</c> (<c>call [edx+0x10]</c> на <c>0x66226e</c>; глобальные
    /// данные достаются на <c>0x662257</c>/<c>0x66225e</c>). Тулсет сам подписывает этот слот
    /// «Used in doppelganger attack logic»: он возвращает <b>глобальную</b> реализацию, то есть
    /// строку <c>Gunits</c>, отбрасывая уровневый рост цели. В модели все <see cref="UnitType"/> и
    /// есть строки <c>Gunits</c>, поэтому это просто <c>target.Type</c>.
    /// <br/>Тулсет здесь свой мод, а не игру, описывает: <c>doppelgangerhooks.cpp</c> подменяет
    /// <c>onHit</c> целиком и зовёт <c>generateUnitImplId</c> с уровнем из скрипта
    /// <c>doppelganger.lua</c>. В ванили никакого уровня не передаётся вовсе.</para>
    ///
    /// <para><b>Бросков ноль</b> — <c>canMiss</c> (<c>0x661fd5</c>) в этой ветви возвращает
    /// <c>xor al,al</c>, иммунитет (<c>0x66208b</c>) там же ложь. Ранг 1; обе функции устроены
    /// одинаково: <c>cannotUseDoppelgangerAttack(unitId, bmd)</c> ложно и
    /// <c>hasTargetsToTransformInto</c> истинно → своя ветвь, иначе запасная атака.</para>
    ///
    /// <para><b><c>cannotUseDoppelgangerAttack</c> (<c>0x6618e9</c>) прочитана целиком</b> и
    /// оказалась ровно «юнит уже превращён»: пять подряд <c>getUnitStatus</c> по битам
    /// <c>0x16..0x1a</c> (Transform, TransformLong, TransformSelf, TransformDoppelganger,
    /// TransformDrainLevel), любой взведённый → истина. Значит превратиться можно один раз за бой
    /// любым способом. Ранг 1. Это и есть «раунд 0» из survey §4: никакого счётчика раундов нет,
    /// есть однократность.</para>
    ///
    /// <para>HP пересчитывается пропорционально (<c>keepHp = 0</c>, <c>push 0</c> на
    /// <c>0x66227b</c>) — <see cref="LifeAttacks.Retype"/>.</para>
    ///
    /// <para>Развилку («уже превращён» → запасная атака) держит <see cref="Attacks.Apply"/>: она же
    /// и применяет запасную атаку рекурсией, потому что та приносит с собой свою точность и свой
    /// иммунитет. Сюда управление доходит только по ветви превращения.</para></summary>
    public static AttackOutcome Doppelganger(BattleUnit attacker, BattleUnit target, BattleState? state)
    {
        var rules = Rules(state, AttackClass.Doppelganger);
        // Копия — тип цели, а не её эффективные статы: движок меняет реализацию юнита целиком.
        LifeAttacks.Retype(attacker, target.Type, rules, keepHp: false);
        attacker.ClearTransformStatuses();
        attacker.TransformedDoppelganger = true;
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Превращение себя (<c>onHit</c> <c>0x6613e1</c>). Тип берётся из <c>Gtransf</c> тем
    /// же <c>getSummonUnitImplIdByAttack</c> (<c>0x660a60</c>), что и у призыва, с фильтром по
    /// размеру превращаемого (<c>CMidUnit::getSizeSmall</c> <c>0x5e9830</c> → слот 6
    /// <c>IUsSoldier</c>). Ранг 1; фильтр по размеру подтверждён и в теле
    /// <c>fillAttackTransformIdList</c> (<c>0x597b00</c>): на <c>0x597bc9</c> результат
    /// <c>getSizeSmall</c> сравнивается с аргументом, несовпадение — кандидат пропущен (это
    /// поднимает до ранга 1 то, что задача 6 держала рангом 2).
    ///
    /// <para><b>Бросков ноль</b>: <c>canMiss</c> (<c>0x6611a7</c>) при цели, равной самому юниту,
    /// отдаёт <c>xor al,al</c>; иммунитет (<c>0x661279</c>) там же ложь. Ранг 1 — и потому
    /// <c>TransformSelf</c> убран из <see cref="Formulas.ClassHasPower"/>.</para>
    ///
    /// <para><b>Вани́льных атак класса две, и обе однозначны:</b> <c>g000aa0164</c> (Волчий лорд
    /// <c>g000uu0164</c>) → <c>g000uu0190</c>, <c>g000aa9022</c> (предмет) → <c>g000uu0092</c>. Оба
    /// кандидата малые, список после фильтра длиной 1, <c>rand()</c> не зовётся.</para>
    ///
    /// <para>HP пересчитывается пропорционально (<c>keepHp = 0</c>, <c>push 0</c> на
    /// <c>0x6615ad</c>); ставится бит TransformSelf (24, <c>push 0x18</c> на <c>0x6615e0</c>).
    /// Длинного бита у этого класса нет — превращение держится до конца боя.</para>
    ///
    /// <para><b>Превращается ЦЕЛЬ, а не обязательно сам атакующий</b> — задача 8 дочитала
    /// <c>onHit</c> до конца: на <c>0x6614e9</c> берётся <c>getUnit(objectMap, targetUnitId)</c>, и
    /// дальше весь путь идёт по нему. Развилка стоит выше: <c>0x62ac90(targetId, this+8)</c> (id
    /// различаются) и сравнение <c>getAllyOrEnemyGroupId(..., 1)</c> цели и себя — то есть по себе и
    /// по СОЮЗНИКУ класс превращает как обычно, и только по врагу отдаёт удар запасной атаке. Прежде
    /// модель падала на любой цели, кроме самого атакующего; развилку держит
    /// <see cref="Attacks.Apply"/>. Ранг 1.</para></summary>
    public static AttackOutcome TransformSelf(AttackType attack, BattleUnit target, BattleState? state)
    {
        return Morph(attack, target, state, AttackClass.TransformSelf, keepHp: false, apply: u =>
        {
            u.ClearTransformStatuses();
            u.TransformedSelf = true;
        });
    }

    /// <summary>Превращение цели (<c>onHit</c> <c>0x660845</c>). Тот же <c>Gtransf</c> и тот же
    /// фильтр по размеру, что у <see cref="TransformSelf"/>, но размер берётся у <b>цели</b>
    /// (<c>0x6608d8</c>). Ранг 1.
    ///
    /// <para><b>Броска два</b> — <c>canMiss</c> (<c>0x6607f3</c>) это голое <c>mov al,1</c>, а
    /// точность у вани́льных атак и правда меньше сотни (80/80/40/70/80). Иммунитет спрашивается
    /// прямым вызовом <c>isUnitImmuneToAttack</c> (<c>0x66081e</c>). Ранг 1.</para>
    ///
    /// <para><b>Все пять вани́льных атак ведут в одну пару кандидатов</b> — <c>g000uu5034</c>
    /// (малый) и <c>g000uu6104</c> (большой), — то есть после фильтра по размеру цели кандидат
    /// ровно один и <c>rand()</c> снова не зовётся.</para>
    ///
    /// <para><b>HP тут НЕ пересчитывается</b>, и это единственное место в движке, где
    /// <c>keepHp</c> истинен (<c>push 1</c> на <c>0x66093b</c>): цель сохраняет текущее HP, даже
    /// если оно выше <c>HIT_POINT</c> новой формы. Кламп в <c>onHit</c> тоже отсутствует —
    /// <c>setUnitHp</c> (<c>0x6609c8</c>) просто переписывает в <c>BattleMsgData</c> то, что
    /// осталось у юнита. Ранг 1. Потолок при этом остаётся исходным (<c>getHpMax</c> <c>0x5e987c</c> → <c>+0x38</c>,
    /// <see cref="LifeAttacks.Retype"/>): Бес из Ветерана — 175/250, как на панели оригинала (карта 17, 19.10). Сила бегства ИИ
    /// видит такого юнита исходным (<see cref="AiStrength.Effective"/>, <c>0x5dd0a7</c>).</para>
    ///
    /// <para>Биты: Transform (22) всегда, TransformLong (23) — под <c>attack-&gt;getInfinite()</c>
    /// (слот 13 <c>IAttackVftable</c>, <c>call [vft+0x34]</c> на <c>0x66098a</c>). Все пять
    /// вани́льных атак несут INFINITE = T.</para></summary>
    public static AttackOutcome TransformOther(AttackType attack, BattleUnit target, BattleState? state) =>
        Morph(attack, target, state, AttackClass.TransformOther, keepHp: true, apply: u =>
        {
            u.ClearTransformStatuses();
            u.Transformed = true;
            u.TransformedLong = attack.Infinite;
            u.TransformAppliedRound = state?.Round ?? 0;
        });

    /// <summary>Общее тело обоих превращений по <c>Gtransf</c>: выбрать кандидата, подменить тип,
    /// расставить биты. Пустой список — движок выходит из <c>onHit</c> сразу (сравнение с
    /// <c>emptyId</c> на <c>0x660907</c> и <c>0x66157c</c>), то есть удар попал и не сделал ничего.
    /// Кандидат — <see cref="LifeAttacks.Pick"/>, как у призыва: <c>rand()</c> процесса из боя с карты (ранг 2 — что превращение
    /// тратит бросок и при одном кандидате, по аналогии с призывом), без него несколько кандидатов — исключение.</summary>
    private static AttackOutcome Morph(AttackType attack, BattleUnit target, BattleState? state,
                                       AttackClass cls, bool keepHp, Action<BattleUnit> apply)
    {
        var rules = Rules(state, cls);
        var candidates = rules.SummonCandidates(attack.Id, smallSlot: target.Type.SizeSmall);
        if (candidates.Count == 0) return new AttackOutcome(false, 0, 0, false);
        // В ванили у всех семи атак классов 21 и 22 после фильтра по размеру кандидат ровно один; моды — через Pick.
        LifeAttacks.Retype(target, LifeAttacks.Pick(candidates, cls, attack, state), rules, keepHp);
        apply(target);
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Конец боя: сперва снимается призыв, иначе — превращение. Порядок и условие — из
    /// <c>CBatLogic::updateGroupsIfBattleIsOver</c> (A batlogichooks.cpp:99-115 повторяет его
    /// структуру): для каждого юнита <c>if (Summon) unsummon; else if (unit-&gt;transformed)
    /// untransform;</c>. То есть призванного не откатывают как превращённого, а превращение
    /// снимают у всех четырёх видов одинаково, по полю <c>CMidUnit::transformed</c>, а не по битам
    /// статуса. Ранг 2 (структура — из тулсета, сама <c>undoTransform</c> — ранг 1, ниже).
    ///
    /// <para><b>Что именно возвращает откат — ранг 1.</b> <c>visitors.undoTransformUnit</c>
    /// (<c>0x5e83de</c>) → <c>CVisitorUndoTransformUnit</c> (vftable <c>0x6f0b24</c>, слот 2
    /// <c>0x612a5d</c>) → <c>CMidUnit::undoTransform</c> (<c>0x5ea3bd</c>): снимается флаг
    /// превращения, возвращается исходная реализация, восстанавливается опыт
    /// (<c>currentXp = origXp</c>, <c>0x5ea43d</c>) и — если при превращении <c>keepHp</c> был
    /// ложью — HP пересчитывается обратно тем же пропорциональным правилом
    /// (<c>0x5ea4a6-0x5ea4b9</c>), но нулевое HP нулевым и остаётся.
    /// <br/><b>Сохранённые при превращении <c>origHp</c> и <c>origHpMax</c> в откате не читаются
    /// вовсе</b> — это бухгалтерия для сохранённой игры, а не источник HP.</para>
    ///
    /// <para><b>Опровергнут чужой реверс.</b> B (Disciples2-RL) даёт «откат превращения 30 %»
    /// (B:856) и «откат понижения уровня 1 %» (B:857). Ни того, ни другого числа в
    /// <c>undoTransform</c> нет: правило одно на все четыре класса и оно пропорциональное.</para>
    ///
    /// <para><b>Ранг 3 — три мелочи.</b> (1) Порядок обхода: движок сортирует
    /// <c>compareByUnsummonOrder</c>, модель идёт по слотам — различить нечем, потому что откат
    /// одного юнита не зависит от другого. (2) Опыт: движок восстанавливает довоенный
    /// <c>currentXp</c>, то есть <b>превращённый теряет добытый в бою опыт</b>; Ф6 опыт не считает
    /// вовсе (<see cref="BattleUnit.Xp"/> только хранится), поэтому восстанавливать нечего и
    /// строки для этого нет — заводить её должна задача, которая заведёт опыт. (3) Мёртвых и
    /// отступивших движок из списка не исключает; здесь так же.</para></summary>
    public static void AfterBattle(BattleState state)
    {
        foreach (var unit in state.Units.OrderBy(u => u.Slot).ToList())
        {
            if (unit.Summoned)
            {
                state.Units.Remove(unit);
                state.Notes.Add(new BattleNote(state.Round, unit.Id, BattleNoteKind.Unsummoned));
                continue;
            }
            if (unit.OrigType is not null) state.Notes.Add(new BattleNote(state.Round, unit.Id, BattleNoteKind.Untransformed));
            Untransform(unit, state);
        }
    }

    /// <summary>Откат превращения одного юнита — <c>CMidUnit::undoTransform</c> (см.
    /// <see cref="AfterBattle"/>). Не превращён — ничего. Зовут его конец боя и снятие превращения
    /// «Исцелением» (<see cref="Attacks.ResolveCured"/>).</summary>
    public static void Untransform(BattleUnit unit, BattleState? state)
    {
        if (unit.OrigType is not { } orig) return;
        int curHpMax = unit.HpMax;
        unit.Type = orig;
        // Исходная реализация — вместе с атаками и потолком, какими они были до превращения (прокачка,
        // порождённые атаки), а не заново из Gattacks по строке Gunits: см. BattleUnit.OrigAttack.
        unit.Attack = unit.OrigAttack ?? throw new InvalidOperationException($"у превращённого {unit.Id} не сохранена исходная атака");
        unit.Attack2 = unit.OrigAttack2;
        unit.HpMax = unit.OrigHpMax;
        if (!unit.TransformKeepHp)
            unit.Hp = LifeAttacks.ScaleHp(unit.Hp, curHpMax, unit.HpMax, zeroStaysZero: true);
        unit.OrigType = null;
        unit.TransformKeepHp = false;
        unit.ClearTransformStatuses();
    }

    private static GameRules Rules(BattleState? state, AttackClass cls) =>
        state?.Rules ?? throw new InvalidOperationException(
            $"класс {cls} подменяет тип юнита и потому требует BattleState.Rules " +
            "(Gunits/Gattacks/Gtransf); бой собран без справочников");
}
