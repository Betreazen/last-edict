using LastEdict.Rules.Units;

namespace LastEdict.Rules.Battle;

/// <summary>Иммунитеты и варды.
///
/// <para><b>Откуда иммунитет берётся.</b> Три источника, и все три сходятся в одну пару
/// «категория + уровень» (<see cref="ImmunityGrant"/>, ранг 1 по данным):
/// <list type="bullet">
/// <item>врождённый по <b>источнику</b> атаки — <c>Gimmu.dbf</c>, 227 живых строк
/// (<c>UnitType.ImmunitiesBySource</c>);</item>
/// <item>врождённый по <b>классу</b> атаки — <c>GimmuC.dbf</c>, всего 13 живых строк: восемь юнитов
/// не берёт яд (класс 12) и пять не берёт дробление брони (класс 25)
/// (<c>UnitType.ImmunitiesByClass</c>);</item>
/// <item>выданный цепочкой модификаторов — <c>GmodifL</c> <c>TYPE 12</c>, то есть заклинание,
/// предмет или <b>вард</b> (<c>EffectiveStats.Immunities</c>). Вани́льных ward-модификаторов
/// четыре (шесть заполненных слотов <c>WARD1..4</c> у трёх атак ведут в них же), и все четыре дают
/// <see cref="ImmunityLevel.Once"/> к своему источнику.</item>
/// </list>
/// Класс из цепочки — <c>TYPE 14 Immunityclass</c> (колонки <c>IMMUNITYC</c>/<c>IMMUNECATC</c>, <c>EffectiveStats.ClassImmunities</c>):
/// в ванили 0 строк, только моды.</para>
///
/// <para><b>Ранги.</b> Перечень уровней и то, какая колонка что значит, — <b>ранг 1</b> (таблицы
/// самой игры <c>LImmune.dbf</c>, <c>LattS.dbf</c>, <c>LattC.dbf</c> плюс распределение значений по
/// всем 240 живым строкам <c>Gimmu</c>/<c>GimmuC</c>). Всё поведение ниже — <b>ранг 2</b> (survey
/// §6 по тулсету: маски <c>attackSourceImmunityStatuses</c>/<c>attackClassImmunityStatuses</c>,
/// расход <c>Once</c> через <c>statuses |= 1 &lt;&lt; pos</c> — <c>customattackhooks.cpp:496-535</c>)
/// либо <b>ранг 3</b> там, где так помечено. Живого журнала на иммунитеты ещё нет: его даёт событие
/// <c>immune</c> корпуса v4 (хук <c>isUnitImmuneToAttack</c>, GOG <c>0x65a528</c>) в прогоне
/// задачи 13.</para></summary>
public static class Immunity
{
    /// <summary>Потолок числа вардов на юните. <b>Ранг 1, задача 8:</b>
    /// <c>BattleMsgData::addUnitToWards</c> (GOG <c>0x62180a</c>) перебирает ровно восемь ячеек
    /// (<c>cmp dword ptr [ebp-0x24], 8</c> на <c>0x6218bb</c>, элементы по <c>+0x7c + i*4</c> в
    /// <c>UnitInfo</c> цели) и при отсутствии свободной выходит. Тулсет поднимает это до 48
    /// (<c>bestowwardshooks.cpp:75</c>) — здесь моделируется ваниль. В самой ванили потолок
    /// недостижим, см. <see cref="Bestow"/>.</summary>
    public const int WardLimit = 8;

    /// <summary>Спрашивает ли этот класс атаки про иммунитет вообще.
    /// <para><b>Ранг 1, задача 6 Ф6.</b> Слот 8 vftable каждого <c>CBatAttack*</c> — это
    /// <c>isImmune</c> (<c>batattack.h:84-88</c>). У семи классов там стоит общая заглушка
    /// <c>0x659913</c> (<c>xor al,al</c> — «иммунитета не бывает»): <c>Heal</c>, <c>BoostDamage</c>,
    /// <c>Revive</c>, <c>Cure</c>, <c>Summon</c>, <c>GiveAttack</c>, <c>BestowWards</c>. У остальных
    /// шестнадцати — своя функция, и она зовёт <c>isUnitImmuneToAttack</c> (GOG <c>0x65a528</c>):
    /// проверено поимённо на <c>Drain</c> (<c>0x65bd22</c>, вызов виден и прямо в <c>onHit</c>
    /// <c>0x65bdc8</c>), <c>DrainOverflow</c> (<c>0x65f7d8</c>, в <c>onHit</c> <c>0x65f88a</c>),
    /// <c>DrainLevel</c> (<c>0x66283d</c>, в <c>onHit</c> <c>0x6628db</c>) и <c>Shatter</c>
    /// (<c>0x65d899</c> — тело видно целиком: пять push и call <c>0x65a528</c>). Vftable каждого
    /// класса добыты из <c>createBatAttack</c> (GOG <c>0x644b3e</c>).</para>
    /// <para>Прежде модель делала исключение только для <c>Revive</c> (survey §6 по
    /// <c>batattack.h:120-122</c>). Это было и уже, и шире, чем надо: лечение, снятие статусов и
    /// буст иммунитета не спрашивают тоже, а <c>Revive</c> попал в список не как особый случай, а
    /// как один из семи. Цена ошибки не косметическая: заряд <c>Once</c> из <c>Gimmu</c> тратился бы
    /// на лечении союзника и не достался бы удару, ради которого он есть.</para>
    /// <para><b>Задача 7 добавила два класса — <c>Doppelganger</c> и <c>TransformSelf</c>.</b> У них
    /// слот 8 не заглушка, а развилка, и обе прочитаны до <c>ret</c>: <c>0x66208b</c> и
    /// <c>0x661279</c> устроены одинаково с их же <c>canMiss</c> — пока превращение возможно
    /// (у доппельгангера <c>cannotUseDoppelgangerAttack</c> ложно и есть во что превращаться, у
    /// TransformSelf цель совпадает с самим юнитом), возвращается <c>xor al,al</c>, то есть
    /// <b>иммунитет не спрашивается вовсе</b> (<c>0x6620b9</c>, <c>0x66129b</c>); иначе ответ
    /// отдаёт запасная атака. Здесь они значатся неспрашивающими именно про первую ветвь; вторую
    /// задача 8 увела в <c>Attacks.Apply</c>, и там иммунитет спрашивает уже сама запасная атака по
    /// своему классу — то есть развилка сохранена, а не потеряна. Цена ошибки та
    /// же, что была у <c>Revive</c>: заряд <c>Once</c> тратился бы на превращении и не достался бы
    /// удару. <c>TransformOther</c> в списке нет и быть не должно — его <c>isImmune</c>
    /// (<c>0x660802</c>) это прямой вызов <c>isUnitImmuneToAttack</c> без всяких развилок.</para></summary>
    /// <para>С этапа Э2 — признак эффекта (<see cref="Effects.AttackEffect.ChecksImmunity"/>); не спрашивают Heal, BoostDamage, Revive,
    /// Cure, Summon, GiveAttack, BestowWards, Doppelganger, TransformSelf.</para>
    public static bool Checked(AttackClass cls) => Effects.AttackEffects.Of(cls).ChecksImmunity;

    /// <summary>Уровень иммунитета цели к этой атаке, без расхода <see cref="ImmunityLevel.Once"/>.
    /// <para>Для ответа <c>alwaysOnly</c> берётся старший уровень. Расход заряда от него не зависит: при
    /// <c>Always</c> одной категории <c>Once</c> другой всё равно тратится (<see cref="Blocks"/>, ранг 1).</para></summary>
    public static ImmunityLevel LevelOf(BattleUnit target, AttackType attack)
    {
        var (bySource, byClass) = Levels(target, attack);
        return bySource > byClass ? bySource : byClass;
    }

    /// <summary>Останавливает ли иммунитет этот удар. <see cref="ImmunityLevel.Once"/> тратится:
    /// первый удар нужного вида не проходит и взводит бит в маске юнита, второй проходит.
    /// <para>Ранг 1 (<c>CBatLogic::battleTurn</c> <c>0x62481a</c>): <c>isImmune</c> (<c>0x624e03</c>) спрашивается
    /// после броска на промах и <b>при любом его исходе</b>; если ответ «да», движок взводит бит у каждой
    /// категории уровня <c>Once</c> — источника (<c>0x622c79</c>, <c>0x625020</c>) и класса (<c>0x622cc0</c>,
    /// <c>0x625073</c>) — не глядя на <c>Always</c> другой категории. Значит, промах тоже тратит заряд
    /// (ai-q52b, вопрос 2), а <c>Always</c> класса не бережёт оберег источника.</para></summary>
    public static bool Blocks(BattleUnit target, AttackType attack)
    {
        var (bySource, byClass) = Levels(target, attack);
        int sourceBit = attack.Source >= 0 ? 1 << attack.Source : 0, classBit = 1 << (int)attack.Class;
        bool Holds(ImmunityLevel level, int used, int bit) =>
            level == ImmunityLevel.Always || (level == ImmunityLevel.Once && bit != 0 && (used & bit) == 0);
        if (!Holds(bySource, target.SourceImmunityUsed, sourceBit) && !Holds(byClass, target.ClassImmunityUsed, classBit))
            return false;
        if (bySource == ImmunityLevel.Once) target.SourceImmunityUsed |= sourceBit;
        if (byClass == ImmunityLevel.Once) target.ClassImmunityUsed |= classBit;
        return true;
    }

    /// <summary>Наложить варды на юнита. Возвращает, сколько легло. Тело <c>onHit</c> класса
    /// <c>BestowWards</c> (GOG <c>0x65d528</c>) прочитано до <c>ret</c> задачей 8 — <b>ранг 1</b>, и
    /// оно короткое: <c>attack-&gt;getWards()</c> (слот 14, <c>call [vft+0x38]</c>), затем по каждому
    /// id варда три шага.
    /// <list type="number">
    /// <item><b>Повтор отсеивается</b> (<c>call 0x65d448</c> на <c>0x65d5bd</c>, <c>jne</c> → к
    /// следующему варду). Внутри: <c>BattleMsgData::getUnitWards(targetId, &amp;list)</c>
    /// (<c>0x621caf</c>) и поиск id варда в этом списке — нашёлся, вард пропущен. Это и есть находка
    /// ревью задачи 3, поднятая здесь со «стоило бы» до ранга 1.</item>
    /// <item><b>Потолок восемь</b> — <c>BattleMsgData::addUnitToWards</c> (<c>0x62180a</c>) ищет
    /// свободную ячейку в массиве из восьми (<c>cmp [ebp-0x24], 8</c> на <c>0x6218bb</c>, элементы
    /// по <c>+0x7c + i*4</c> в <c>UnitInfo</c> цели); свободной нет — вард не кладётся.</item>
    /// <item><b>Заряд <c>Once</c> обнуляется</b> (<c>0x62190f</c>–<c>0x62196c</c>): движок берёт из варда
    /// его источник иммунитета, переводит в номер 0..7 (<c>0x62198e</c> — сравнение с восемью
    /// глобальными категориями <c>LattS</c>) и делает <c>unitInfo-&gt;[0x24] &amp;= ~(1 &lt;&lt; n)</c>.
    /// Байт <c>+0x24</c> — та самая маска потраченных <c>Once</c> по источнику: её же читает
    /// <c>0x622906</c>, которую зовёт <c>isUnitImmuneToAttack</c> (<c>0x65a5ed</c>). То есть
    /// <b>лёгший вард возвращает заряд</b>, даже если по этому источнику он уже был потрачен. Шаг
    /// работает только вместе с первым: пока вард стоит, повтор до него не доходит вовсе (заряд не
    /// возвращается), а вот наложенный заново после границы хода — доходит. Без шага 3 вард во
    /// втором раунде не давал бы ничего; без шага 1 давал бы бесконечно. Ранг 1.</item>
    /// </list>
    /// <para><b>Учёт у раздавшего (ранг 1, задача 21 Ф7).</b> У <b>атакующего</b> свой массив из
    /// восьми пар «кому — что» (<c>+0x3c + i*8</c>, первый цикл <c>0x62184a</c>): он заполнен —
    /// вард не регистрируется и у цели тоже. По этому массиву варды и снимаются — в начале хода
    /// раздавшего (<see cref="ClearWards"/>), поэтому <paramref name="caster"/> модель теперь ведёт
    /// (<see cref="BattleUnit.BestowedWards"/>). <c>null</c> — вард положен вне боя-учёта (тест):
    /// снимать его некому. Мелочь, оставленная намеренно: <c>CMidUnit::addModifier</c>
    /// (<c>0x65d5d2</c>) движок зовёт ДО обоих потолков, поэтому вард сверх потолка всё равно
    /// попадает в цепочку самого юнита и не снимается <b>до конца боя</b>. В ванили недостижимо (ниже).</para>
    /// <para><b>После боя варды не живут (ранг 1, <c>20260917-synth</c> q59):</b> во втором бою того же отряда
    /// оберега нет (урон 10, seq 250645), в сохранении <c>t01-009.sg</c> ward-модификаторов на юнитах нет. В порте
    /// варды лежат только в <see cref="BattleUnit.Wards"/> и в юнит карты не переносятся. После гибели последнего
    /// врага раунд лекарей ещё идёт, и раздающий успевает положить вард (seq 247257).</para>
    /// <para><b>Заряд <see cref="ImmunityLevel.Once"/> — на бой (ранг 1, q52b):</b> оберег Храмовника сработал раз за
    /// бой и вернулся во втором бою того же дня (seq 323425, попадание без урона). В порте маски
    /// <see cref="BattleUnit.SourceImmunityUsed"/>/<see cref="BattleUnit.ClassImmunityUsed"/> живут в
    /// <see cref="BattleUnit"/> и с новым боем начинаются с нуля. Тратит ли заряд промах — открыто.</para>
    /// <para><b>Потолок восемь в ванили недостижим.</b> Ward-модификаторов на всю игру ровно четыре
    /// (<c>g000um0001</c> Air, <c>g000um0005</c> Water, <c>g000um0010</c> Earth, <c>g000um0016</c>
    /// Fire; в <c>Gspells.WARD1..4</c> нет ни одного), а повторы теперь отсеиваются — значит больше
    /// четырёх вардов на юните не окажется. Потолок оставлен: он ранга 1 и нужен модам.</para></summary>
    public static int Bestow(BattleUnit target, IEnumerable<Modifier> wards, BattleUnit? caster = null)
    {
        int placed = 0;
        foreach (var w in wards)
        {
            if (Has(target, w)) continue;
            if (caster is not null && caster.BestowedWards.Count >= WardLimit) continue;
            if (target.Wards.Count >= WardLimit) continue;
            target.Wards.Add(w);
            caster?.BestowedWards.Add((target, w));
            RefreshOnceCharge(target, w);
            placed++;
        }
        return placed;
    }

    private static bool Has(BattleUnit target, Modifier ward)
    {
        foreach (var w in target.Wards)
            if (string.Equals(w.Id, ward.Id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Возврат заряда <see cref="ImmunityLevel.Once"/> по источнику варда — шаг 3 выше
    /// (<c>0x62196c</c>: <c>and byte [unitInfo+0x24], ~(1 &lt;&lt; n)</c>).</summary>
    private static void RefreshOnceCharge(BattleUnit target, Modifier ward)
    {
        foreach (var e in ward.Elements)
            if (e.Type == ModifierElementType.Immunity && e.Immunity is { } source and >= 0 and < 32)
                target.SourceImmunityUsed &= ~(1 << source);
    }

    /// <summary>Снятие вардов, которые раздал <paramref name="caster"/>, — в начале ЕГО хода.
    /// <para><b>Ранг 1, задача 21 Ф7.</b> <c>beforeBattleTurn</c> (GOG <c>0x62673f</c>) после снятия
    /// Defend (<c>push 0x1f</c>) зовёт <c>0x621bee</c> — список целей из массива <c>+0x3c</c> самого
    /// ходящего, — и по каждой цели снимает её модификаторы, наложенные ходящим
    /// (<c>0x621adc</c> → <c>CMidUnit::removeModifier</c> <c>0x5e9984</c> → <c>0x621d64</c>), затем
    /// чистит массив (<c>0x621dfe</c>). Носитель вард до хода раздавшего не теряет: прежняя модель
    /// (снятие в ход носителя, ранг 3) была неверна, права русская вики. Доп. ход раздавшего
    /// (<c>GiveAttack</c>) снимает их тоже — это тот же <c>beforeBattleTurn</c>.</para>
    /// <para>Раздавший погиб — его хода больше нет, варды живут до конца боя: в
    /// <c>beforeBattleRound</c> (<c>0x621fd0</c>) снятия нет, только сброс <c>waited</c>
    /// (<c>0x622078</c>). Удаление модификаторов мёртвых в начале раунда — добавка тулсета
    /// (<c>battlemsgdatahooks.cpp:468-478</c>), не ваниль.</para></summary>
    public static void ClearWards(BattleUnit caster)
    {
        foreach (var (target, ward) in caster.BestowedWards) target.Wards.Remove(ward);
        caster.BestowedWards.Clear();
    }

    private static (ImmunityLevel BySource, ImmunityLevel ByClass) Levels(BattleUnit target, AttackType attack) =>
        LevelsFor(target, attack.Source, attack.Class);

    /// <summary>Уровни иммунитета цели к источнику и к классу по отдельности, без расхода
    /// <see cref="ImmunityLevel.Once"/>. Политика ИИ (<see cref="AiTargets"/>) спрашивает их порознь —
    /// и не всегда по той атаке, которой бьёт (DoT берёт источник первой атаки, <c>0x5d2159</c>).</summary>
    public static (ImmunityLevel BySource, ImmunityLevel ByClass) LevelsFor(BattleUnit target, int source, AttackClass cls)
    {
        var chain = ChainStats(target);
        var innate = Level(target.Type.ImmunitiesBySource, source);
        var granted = Level(chain?.Immunities, source);
        // Класс — так же: врождённый (GimmuC) и выданный цепочкой (TYPE 14 Immunityclass, моды), сильнейший.
        var innateClass = Level(target.Type.ImmunitiesByClass, (int)cls);
        var grantedClass = Level(chain?.ClassImmunities, (int)cls);
        return (innate > granted ? innate : granted, innateClass > grantedClass ? innateClass : grantedClass);
    }

    /// <summary>Заряд <see cref="ImmunityLevel.Once"/> ещё не потрачен — бит в маске юнита не взведён
    /// (<c>0x622906</c> по источнику, <c>0x622957</c> по классу).</summary>
    public static bool OnceLive(BattleUnit target, int source) =>
        source is >= 0 and < 32 && (target.SourceImmunityUsed & (1 << source)) == 0;

    /// <inheritdoc cref="OnceLive(BattleUnit, int)"/>
    public static bool OnceLive(BattleUnit target, AttackClass cls) =>
        (target.ClassImmunityUsed & (1 << (int)cls)) == 0;

    /// <summary>Иммунитеты, выданные цепочкой (постоянные модификаторы плюс варды). Пустая цепочка
    /// — пустой список без единой аллокации: Ф4/Ф5 не должны подорожать.
    ///
    /// <para><b>Кеш сознательно не заведён — по измерению, а не на глаз.</b> Замер 08.09.2026
    /// (задача 8 Ф6, временный бенчмарк): свёртка цепочки на одну проверку иммунитета —
    /// <c>1,011</c> мкс против <c>0,114</c> мкс на пустой, то есть <b>8,9×</b> и до 62 % стоимости
    /// <see cref="Attacks.Apply"/>. Но на весь корпус С2 это <b>49 мс из 13 минут, 0,006 %</b>:
    /// кешировать нечего, сложность обошлась бы дороже выигрыша. Бенчмарк был временным и удалён —
    /// числа сохранены здесь, чтобы решение пересматривали по факту, а не собирали замер заново
    /// (находка LOW ревью задачи 8).</para>
    ///
    /// <para><b>Когда вернуться к вопросу:</b> если длина <see cref="BattleUnit.Chain"/> в бою
    /// начнёт систематически превышать десяток. Сейчас она почти всегда нулевая: ward-модификаторов
    /// в ванили четыре на всю игру, а потолок в восемь слотов недостижим (задача 8, ранг 1).
    /// Тогда написать бенчмарк заново и померить, а не угадывать.</para></summary>
    private static EffectiveStats? ChainStats(BattleUnit unit)
    {
        var chain = unit.Chain;
        return chain.Count == 0 ? null : EffectiveStats.Of(unit.Type, chain);
    }

    /// <summary>Сильнейший уровень среди строк нужной категории. Категория меньше нуля —
    /// <c>AttackType.SourceUnset</c> (атака собрана в тесте, колонка <c>SOURCE</c> не читалась):
    /// иммунитета нет, и сдвига на отрицательное число тоже.</summary>
    private static ImmunityLevel Level(IReadOnlyList<ImmunityGrant>? grants, int category)
    {
        var level = ImmunityLevel.NotImmune;
        if (grants is null || category < 0) return level;
        foreach (var g in grants)
            if (g.Category == category && g.Level > level) level = g.Level;
        return level;
    }
}
