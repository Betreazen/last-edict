using D2.Rules.Rng;
using D2.Rules.Units;

namespace D2.Rules.Battle;

/// <summary>Второй круг классов атак (задача 6 Ф6): <c>Drain</c> (2), <c>DrainOverflow</c> (15),
/// <c>Revive</c> (14), <c>Summon</c> (17), <c>DrainLevel</c> (18). Живут отдельным файлом, чтобы
/// <see cref="Attacks"/> остался диспетчером и не перевалил за половину потолка в 800 строк.
///
/// <para><b>Откуда числа.</b> Все шесть тел прочитаны в дизассемблере GOG <c>Discipl2.exe</c> до
/// <c>ret</c> — <b>ранг 1</b>. Точки входа найдены так: <c>createBatAttack</c> (<c>0x644b3e</c>)
/// ветвится по категории класса из <c>AttackClassCategories</c>, каждая ветка зовёт конструктор,
/// конструктор кладёт свой vftable; слот 12 vftable — <c>onHit</c> (<c>batattack.h:106</c>).
/// Отсюда: <c>Drain::onHit</c> <c>0x65bd65</c>, <c>DrainOverflow::onHit</c> <c>0x65f81b</c>,
/// <c>Revive::onHit</c> <c>0x65f4de</c>, <c>Summon::onHit</c> <c>0x6631eb</c>,
/// <c>DrainLevel::onHit</c> <c>0x662880</c>, <c>Heal::onHit</c> <c>0x65c417</c>.</para>
///
/// <para><b>Тулсет тут был бы плохим источником.</b> Он эти три класса не читает, а
/// <i>переписывает</i>: <c>drainattackhooks.cpp</c> подменяет <c>onHit</c> целиком и берёт процент
/// вампиризма из своей настройки <c>userSettings().drainAttackHeal</c> (<c>L/settings.lua:63-64</c>,
/// по умолчанию 50). Совпадение с ванилью здесь есть, но это совпадение, а не источник.</para>
///
/// <para><b>Корпус к этому кругу слеп целиком</b> — сплошной проход по всем 74 файлам
/// <c>corpus/**/*.jsonl</c> (116 боёв, 20 типов юнитов) не нашёл ни одного из 24 вани́льных типов с
/// этими классами атак, ни одного воскрешения (переход hp 0 → hp &gt; 0 внутри боя), ни одного
/// призыва (новый id в <c>units[]</c>) и ни одного из битов 22/26/27. Поэтому «ранг 1» ниже везде
/// означает дизассемблер, а не журнал, и всё, что дизассемблер не показывает (порядок обхода
/// отряда, HP после превращения), честно помечено рангом 3.</para></summary>
internal static class LifeAttacks
{
    /// <summary>Вампиризм ванили — ровно половина снятых HP, и это не константа-процент, а
    /// <b>деление на два прямо в коде</b>: <c>Drain::onHit</c> <c>0x65be85-0x65be91</c>
    /// (<c>sub ecx,eax; mov eax,ecx; cdq; sub eax,edx; sar eax,1</c>) и то же самое у
    /// <c>DrainOverflow::onHit</c> <c>0x65f953-0x65f95d</c>. <c>cdq; sub; sar 1</c> — это
    /// целочисленное деление на 2 с усечением к нулю, то есть в точности <c>hpDiff / 2</c> в C#.
    /// В <c>GVars.dbf</c> такого поля нет вовсе (все 92 колонки просмотрены), в <c>.rdata</c>
    /// множителя тоже нет — половина зашита компилятором. <b>Ранг 1.</b>
    /// <para>Значение вынесено константой только ради читаемости формулы; менять его нечем.</para></summary>
    public const int DrainHealPercent = 50;

    /// <summary>Воскрешение возвращает половину потолка HP: <c>Revive::onHit</c>
    /// <c>0x65f535-0x65f558</c> — <c>mov [ebp-0x1c], 0x32</c>, затем
    /// <c>call [soldier+0x24]</c> (<c>IUsSoldier::getHitPoints</c>, индекс 9 при одном слоте
    /// базового <c>IUsUnitExtensionVftable</c>), <c>imul eax,eax,0x32; cdq; idiv 100</c>.
    /// <b>Ранг 1.</b> <c>QTY_HEAL</c> самой атаки (<c>g000aa1150</c> несёт 50) при этом
    /// <b>не читается</b> — совпадение чисел обманчиво.</summary>
    public const int RevivePercent = 50;

    /// <summary>Урон плюс лечение атакующего. Ход <c>onHit</c> движка (<c>0x65bd65</c>):
    /// иммунитет → <c>computeDamage</c> (<c>0x65b747</c>, тот же, что у <c>Damage</c>) →
    /// <c>changeUnitHp(цель, −урон)</c> → <c>checkUnitDeath</c> → лечение атакующего на
    /// <c>hpDiff / 2 + attack-&gt;getDrain(hpDiff)</c> (<c>call [attack+0x28]</c>, слот 10
    /// <c>IAttackVftable</c> = <c>getDrain</c>) → <c>changeUnitHp(атакующий, +drain)</c>.
    ///
    /// <para><b>Бросков ровно столько же, сколько у <c>Damage</c>: два <c>rng(100)</c> на точность
    /// (свой <c>canMiss</c> <c>0x65bd13</c>, <c>mov al,1</c>) и один <c>rng(BAT_DAMAGE)</c> внутри
    /// <c>computeDamageWithBuffs</c>.</b> Само лечение не бросает ни одной кости — в теле
    /// <c>onHit</c> между вычислением урона и <c>changeUnitHp</c> вызовов генератора нет.
    /// Ранг 1 (дизассемблер).</para>
    ///
    /// <para><paramref name="drainPercent"/> — прибавка от цепочки модификаторов
    /// (<c>GmodifL TYPE 15 AttackDrain</c>, <c>EffectiveStats.OfAttack</c>). В движке это
    /// <c>attack-&gt;getDrain</c>, и <c>CUmAttack</c> считает его в HP, а не в процентах от
    /// половины: <c>template.lua getAttackDrain(unit, damage, prev) = prev + damage * 0.75</c>.
    /// Отсюда <c>hpDiff · percent / 100</c> слагаемым, а не множителем к половине — <b>ранг 2</b>,
    /// снимает бой задачи 13 с «Unholy Dagger» (<c>g000um2317</c>, PERCENT 25): 75 % от разницы HP
    /// против 62 %.</para>
    ///
    /// <para>Лечение атакующего усекается его собственным потолком HP (<c>changeUnitHp</c>), и
    /// именно неусвоенный остаток уходит союзникам у <c>DrainOverflow</c> — в движке это буквально
    /// возвращаемое значение <c>drainDamage − attackerHpDiff</c>.</para>
    ///
    /// <para><b>Крита у вампиризма нет — ранг 1 (задача 15 Ф7).</b> Это тот же <c>computeDamage</c>, но
    /// <c>computeCriticalHit = 0</c> и оба выхода NULL (<c>0x65bdf6</c>–<c>0x65bdfa</c>; у
    /// <c>DrainOverflow</c> <c>0x65f8b8</c>–<c>0x65f8bc</c>); в корпусе это 11 событий <c>dmg</c> с
    /// <c>crit = false</c> и <c>dmg = crit_dmg = −1</c>.</para></summary>
    public static AttackOutcome Drain(BattleUnit attacker, AttackType attack, BattleUnit target,
                                      int drainPercent, IRng rng, RulesConstants c,
                                      BattleState? state, bool overflow)
    {
        int before = target.Hp;
        int dwb = Formulas.DamageWithBuffs(attack, Formulas.DamageMax(c, attacker.Type.GlobalId),
                                           c.BoostDamagePercent(attacker.BoostDamageLevel),
                                           c.LowerDamagePercent(attacker.LowerDamageLevel), true, rng, c,
                                           state?.HumanOnEasy(attacker) == true);
        int damage = Formulas.ComputeDamage(dwb, Formulas.Armor(target, c));
        target.Hp = Math.Max(0, target.Hp - damage);

        int hpDiff = before - target.Hp;
        int drain = hpDiff * DrainHealPercent / 100 + ModifierDrain(hpDiff, drainPercent);
        int healed = Restore(attacker, drain);
        // Запись бьющего — всегда, и при нуле (0x65f9f5): по ней экран играет лечение на нём самом.
        state?.DrainHeals.Add(new DrainHeal(state.Round, attacker.Id, attacker.Id, healed));
        if (overflow && drain - healed > 0) Spill(attacker, drain - healed, state);
        return new AttackOutcome(false, damage, 0, true, DrainHealed: healed);
    }

    /// <summary><c>attack-&gt;getDrain(hpLost)</c> — слот 10 <c>IAttackVftable</c>. У атаки без
    /// модификаторов (<c>CAttackImpl</c>, GOG vftable <c>0x6eafa4</c>) это <c>0x57360d</c>:
    /// <c>xor eax, eax; ret 4</c> — ноль, ранг 1. С цепочкой — <c>hpLost · percent / 100</c>, где
    /// <c>percent</c> — сумма <c>GmodifL TYPE 15</c> (<see cref="Units.EffectiveStats.OfAttack"/>);
    /// форма тела <c>CUmAttack::getDrain</c> в дизассемблере не найдена, но формула
    /// ⌊фактическая потеря HP × % / 100⌋ — <b>ранг 1 по журналу</b> (отчёт B синтетических карт,
    /// ai-q15: кинжал <c>g000um2317</c>, 25 %, три удара 3/0 — потеря 54 → +13, 11 → +2, 50 → +12;
    /// вариант «от dmg» 1/2). Зовут оба: <c>Drain::onHit</c> поверх половины и
    /// <c>Damage::onHit</c> (<c>0x65b67b</c>) вместо неё.</summary>
    internal static int ModifierDrain(int hpLost, int drainPercent) => hpLost * drainPercent / 100;

    /// <summary>Лечение с усечением по потолку HP — то, что делает <c>changeUnitHp</c>
    /// (GOG <c>0x5e75f3</c>). Возвращает, сколько легло на самом деле: у <c>DrainOverflow</c> это
    /// именно та величина, разность с которой уходит союзникам.</summary>
    internal static int Restore(BattleUnit unit, int amount)
    {
        int healed = Math.Max(0, Math.Min(amount, unit.HpMax - unit.Hp));
        unit.Hp += healed;
        return healed;
    }

    /// <summary>Раздача излишка вампиризма союзникам — порт
    /// <c>computeDrainOverflowGroupHeal</c> (GOG <c>0x65fbb8</c>, рекурсивная: самовызов виден на
    /// <c>0x65fcca</c>) вместе с двумя её помощниками, <c>isUnitNotAtFullHp</c> (<c>0x65fdfa</c>) и
    /// <c>updateUnitHealFromDrainOverflow</c> (<c>0x65fcd5</c>). Все три прочитаны до <c>ret</c> —
    /// <b>ранг 1</b> для формы:
    /// <list type="number">
    /// <item>считаются те, кому лечение нужно: не сам атакующий, не отступивший
    /// (<c>BattleStatus::Retreated</c>), живой (<c>hp != 0</c> — мёртвых вампиризм не поднимает) и
    /// у кого <c>hpMax − hp −</c> уже запланированное <c>&gt; 0</c>;</item>
    /// <item><c>single = излишек / число таких</c> (<c>idiv</c>, <c>0x65fbfb</c>); если вышло 0, а
    /// излишек положителен — <c>single = 1</c>;</item>
    /// <item>обход отряда по порядку, пока остаток положителен; каждому даётся <c>min(single,
    /// сколько ему не хватает)</c>, остаток уменьшается ровно на выданное;</item>
    /// <item>если остаток всё ещё положителен — та же процедура заново.</item>
    /// </list>
    /// Лечение сперва <b>планируется</b> и лишь потом применяется — оттого «уже запланированное»
    /// в пункте 1; из-за этого один и тот же юнит на втором круге не получит сверх своего потолка.
    /// <para><b>Порядок обхода — порядок группы</b>: <c>0x65fbb8</c> идёт по индексу <c>CMidUnitGroup</c> (<c>0x5f4441</c> — число,
    /// <c>0x5f44b9(i)</c> — юнит; ранг 1 по листингу, REA 13.10) — здесь по <see cref="BattleUnit.GroupIndex"/> (бой с карты: индекс в
    /// <c>UnitIds</c> группы), призванные и юниты без индекса (бой из журнала) — следом в порядке состояния боя. Сторона в бою — одна
    /// группа, так что «союзники» и есть группа атакующего. Различается только неделимый остаток. <i>Было иначе:</i> порядок слотов
    /// (клеток построения).</para></summary>
    private static void Spill(BattleUnit attacker, int overflow, BattleState? state)
    {
        // Без состояния боя раздать излишек некому, и тихо «просто не раздать» нельзя: получился бы
        // обычный Drain под именем DrainOverflow, то есть молча другой класс атаки.
        if (state is null)
            throw new InvalidOperationException(
                "DrainOverflow раздаёт излишек лечения союзникам и потому требует BattleState; " +
                "бой собран без состояния");
        var group = state.Allies(attacker).OrderBy(u => u.GroupIndex < 0 ? int.MaxValue : u.GroupIndex).ToList();
        var plan = new Dictionary<BattleUnit, int>();

        while (overflow > 0)
        {
            int needing = group.Count(u => Missing(u, plan) > 0);
            if (needing == 0) break;
            int single = Math.Max(1, overflow / needing);
            foreach (var u in group)
            {
                if (overflow <= 0) break;
                int give = Math.Min(single, Missing(u, plan));
                if (give <= 0) continue;
                plan[u] = plan.GetValueOrDefault(u) + give;
                overflow -= give;
            }
        }

        foreach (var (unit, amount) in plan)
        {
            unit.Hp += amount;
            state.DrainHeals.Add(new DrainHeal(state.Round, attacker.Id, unit.Id, amount));   // 0x65fb4a: запись на каждого полеченного
        }

        int Missing(BattleUnit u, Dictionary<BattleUnit, int> planned) =>
            u == attacker || u.Retreated || !u.Alive
                ? 0
                : u.HpMax - u.Hp - planned.GetValueOrDefault(u);
    }

    /// <summary>Воскрешение (<c>0x65f4de</c>). Иммунитет <b>не спрашивает</b> — в теле <c>onHit</c>
    /// вызова <c>isUnitImmuneToAttack</c> (<c>0x65a528</c>) нет вовсе, а слот 8 vftable
    /// (<c>0x6f32cc</c>) занят общей заглушкой <c>0x659913</c>: <see cref="Immunity.Checked"/>.
    /// Промахнуться тоже не может — <c>canMiss</c> там же заглушка, значит <b>ноль бросков</b>
    /// (<see cref="Formulas.ClassHasPower"/>). Ранг 1 на всё перечисленное.
    /// <para>Возвращает <c>hpMax · 50 / 100</c> и снимает биты <c>Dead</c> (1) и <c>XpCounted</c> (0)
    /// — <c>setUnitStatus</c> на <c>0x65f598</c> и <c>0x65f5a8</c>. Модель «мёртв» держит через
    /// <c>Hp == 0</c>, поэтому оба бита снимаются самим присваиванием HP; <c>XpCounted</c> Ф6 не
    /// моделирует вовсе.</para>
    /// <para><b>Ранг 2 — что <c>HpMax</c> модели и есть <c>getHitPoints</c> движка.</b> Движок
    /// спрашивает потолок у реализации юнита (сгенерированной, с учётом уровневого роста), а модель
    /// хранит эффективный потолок из <c>battle_units</c>. Для юнита без роста это одно и то же
    /// число; разойдутся они только на прокачанном воскрешаемом. Снимает бой задачи 13, где
    /// Иерофант (<c>g000uu0150</c>, атака <c>g000aa1150</c>) поднимает союзника с уровнем &gt; 1.</para>
    /// <para>Живого воскресить нельзя — <c>method14</c> vftable (<c>0x65f606</c>, единственный
    /// <c>mov al,1</c> среди 23 классов) разрешает <c>Revive</c> целиться <i>в мёртвых</i>, а не в
    /// кого угодно. По живому эффекта нет: <c>Applied = false</c>.</para></summary>
    public static AttackOutcome Revive(BattleUnit target)
    {
        if (target.Alive) return new AttackOutcome(false, 0, 0, false);
        int hp = target.HpMax * RevivePercent / 100;
        target.Hp = hp;
        target.Revived = true;   // ранг 2: журнал map-001 [1281528] seq 1286030, flags 8 → 72; место записи в onHit не прочитано
        return new AttackOutcome(false, 0, hp, hp > 0);
    }

    /// <summary>Понижение уровня (<c>0x662880</c>). Порядок движка: иммунитет
    /// (<c>0x6628db</c> — спрашивается, в отличие от <c>Revive</c>) → уровень цели →
    /// <c>drainLevel = уровень − 1</c> → цикл <c>for (i = уровень; i &gt; drainLevel; i--)</c>, то
    /// есть в ванили <b>ровно один шаг</b>, берущий <c>getPrevUnitImplId</c> →
    /// <c>visitors.transformUnit</c> (<c>0x662965</c>) → бит <c>TransformDrainLevel</c> (26,
    /// <c>push 0x1a</c> на <c>0x66298e</c>). Ранг 1.
    /// <para>Многошаговое понижение — <b>настройка тулсета</b> (<c>leveledDrainLevelAttack</c> и
    /// скрипт <c>drainLevel.lua</c>), в ванили её нет: чужой реверс здесь описывает свой мод, а не
    /// игру.</para>
    /// <para>Предыдущий тип — <c>Gunits.PREV_ID</c> (<see cref="UnitType.PrevId"/>). Пустой
    /// (<c>g000000000</c>) или неизвестный — движок выходит из <c>onHit</c> сразу
    /// (<c>if (transformImplId == emptyId) return</c>), то есть удар попал, но не сделал ничего;
    /// у нас это <c>Applied = false</c>.</para>
    /// <para><b>HP после превращения — ранг 1, поднят задачей 7 с третьего.</b> Задача 6 не смогла
    /// заглянуть внутрь <c>visitors.transformUnit</c> (там строится визитор и работа уходит в его
    /// vftable) и усекала HP новым потолком. Цепочка дочитана до конца: <c>transformUnit</c>
    /// <c>0x5e838d</c> → <c>CVisitorTransformUnit</c> (vftable <c>0x6f0b14</c>, слот 2
    /// <c>0x6129fc</c>) → <c>CMidUnit::transform</c> <c>0x5ea25c</c>. Пересчёт там
    /// <b>пропорциональный</b>, а не усечение: <c>0x5ea32a-0x5ea33d</c>
    /// (<c>imul; cdq; idiv; test eax,eax; jne; mov [esi+0x20],1</c>), то есть
    /// <c>hp · newHpMax / oldHpMax</c> с полом 1. Подробности и оговорка про <c>keepHp</c> —
    /// у <see cref="Retype"/>. DrainLevel передаёт <c>keepHp = false</c> (<c>push 0</c> на
    /// <c>0x66295b</c>), значит пересчёт идёт.</para>
    /// <para><b>Бросков ноль сверх точности:</b> два <c>rng(100)</c> (свой <c>canMiss</c>
    /// <c>0x66282e</c>), и ни одного в теле — генератор в <c>onHit</c> не зовётся. Ранг 1.</para></summary>
    public static AttackOutcome DrainLevel(BattleUnit target, BattleState? state)
    {
        var rules = Rules(state, AttackClass.DrainLevel);
        if (!rules.Units.TryGetValue(target.Type.PrevId, out var prev))
            return new AttackOutcome(false, 0, 0, false);   // предыдущего уровня нет — эффекта нет
        Retype(target, prev, rules, keepHp: false);
        target.ClearTransformStatuses();
        target.LevelDrained = true;
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Подмена реализации юнита прямо в бою — порт <c>CMidUnit::transform</c>
    /// (GOG <c>0x5ea25c</c>, прочитан до <c>ret</c>). Общий путь всех четырёх превращений:
    /// <c>DrainLevel</c> (задача 6), <c>TransformSelf</c>, <c>TransformOther</c> и
    /// <c>Doppelganger</c> (<see cref="TransformAttacks"/>). Во всём движке у
    /// <c>visitors.transformUnit</c> ровно четыре места вызова — эти
    /// (<c>0x660945</c>, <c>0x6615b7</c>, <c>0x662288</c>, <c>0x662965</c>).
    ///
    /// <para><b>Что делает движок, по порядку (ранг 1).</b>
    /// <list type="number">
    /// <item>Если юнит уже превращён (<c>transformed</c>, <c>+0x28</c>), <b>сперва откат</b> —
    /// <c>undoTransform</c> <c>0x5ea3bd</c> (<c>0x5ea264-0x5ea274</c>): исходная реализация, HP обратно по
    /// <c>keepHp</c> прошлого превращения, <c>origTypeId</c> пуст (<c>0x5ea47f-0x5ea48c</c>). Значит второе
    /// превращение поверх первого — то же, что первое у исходного юнита: тип, HP и потолок исходного,
    /// <c>keepHp</c> — этого вызова.</item>
    /// <item>Запоминает исходный тип, взводит <c>transformed</c>, сохраняет <paramref name="keepHp"/>
    /// (<c>+0x30</c>), HP (<c>+0x34</c>) и потолок исходной реализации (<c>+0x38</c>) — <c>0x5ea292-0x5ea2c5</c>.</item>
    /// <item>Меняет реализацию юнита.</item>
    /// <item>Если <paramref name="keepHp"/> ложь — <c>hp = hp · newHpMax / oldHpMax</c>, и ноль
    /// поднимается до единицы (<c>0x5ea2fd-0x5ea33d</c>). Истина — HP не трогают вовсе.</item>
    /// </list>
    /// Корпус <c>20261018b-port-synth [5062040]</c> (карта 17, Б36): Ветеран <c>S143UN000f</c> 175/250 понижен Гномом до
    /// 140/200 (<c>G000UU0037</c>), затем Ведьма превращает его в Беса — <c>attack_hit</c> 140 → <b>175</b> (откат 140 · 250 / 200),
    /// у порта было 140.</para>
    ///
    /// <para><b>Потолок HP — <c>CMidUnit::getHpMax</c> <c>0x5e987c</c>, ранг 1:</b> превращённый с <c>keepHp</c>
    /// (<c>0x5ea4cb</c> ложно) отдаёт сохранённый потолок исходного (<c>+0x38</c>), прочие — <c>getHitPoints</c> текущей
    /// реализации. Его спрашивают лечение и его цели (<c>0x65fdfa</c>, <c>0x5d023c</c>), воскрешение, «Исцеление», обереги,
    /// зелья ИИ (<c>0x5d242e</c>), панель боя (<c>0x64bf2d</c>). Поэтому у превращённого Ведьмой <see cref="BattleUnit.HpMax"/>
    /// остаётся исходным: Бес 175/250, а не 175/50 (<c>battle_units.hp_max</c> = 50 — это <c>getHitPoints</c> реализации, мимо
    /// <c>0x5e987c</c>; <c>ai_strength_units.max</c> = 250 — через него).</para>
    ///
    /// <para><b>Ранг 2 — что <c>HpMax</c> модели и есть потолок движка</b> (тот же
    /// зазор, что у <see cref="Revive"/>): движок спрашивает потолок у сгенерированной реализации
    /// с уровневым ростом, модель хранит эффективный потолок из <c>battle_units</c>. Совпадают,
    /// пока превращаемый не прокачан.</para></summary>
    public static void Retype(BattleUnit unit, UnitType type, GameRules rules, bool keepHp)
    {
        if (unit.OrigType is not null) TransformAttacks.Untransform(unit, null);   // 0x5ea264 → undoTransform 0x5ea3bd

        unit.OrigType = unit.Type;
        (unit.OrigAttack, unit.OrigAttack2, unit.OrigHpMax) = (unit.Attack, unit.Attack2, unit.HpMax);
        unit.TransformKeepHp = keepHp;

        int oldHpMax = unit.HpMax;
        unit.Type = type;
        unit.Attack = rules.AttackOf(type)
                      ?? throw new InvalidDataException($"у типа {type.Id} нет атаки {type.AttackId} в Gattacks");
        unit.Attack2 = rules.Attack2Of(type);
        if (keepHp) return;   // getHpMax 0x5e987c: потолок исходного (+0x38), HP не трогают
        unit.HpMax = type.HitPoints;
        unit.Hp = ScaleHp(unit.Hp, oldHpMax, unit.HpMax);
    }

    /// <summary><c>hp · newHpMax / oldHpMax</c> с полом 1 — <c>CMidUnit::transform</c>
    /// <c>0x5ea32a</c> и <c>CMidUnit::undoTransform</c> <c>0x5ea4a6</c>. Единица ставится, даже
    /// если результат обнулился; при откате движок добавляет проверку «а был ли HP ненулевым»
    /// (<c>0x5ea4b5</c>) — за неё отвечает <paramref name="zeroStaysZero"/>. Ранг 1.</summary>
    public static int ScaleHp(int hp, int oldHpMax, int newHpMax, bool zeroStaysZero = false)
    {
        if (oldHpMax <= 0) return Math.Min(hp, newHpMax);
        int scaled = hp * newHpMax / oldHpMax;
        if (scaled == 0 && !(zeroStaysZero && hp == 0)) scaled = 1;
        return scaled;
    }

    /// <summary>Призыв (<c>0x6631eb</c>). Ни точности, ни иммунитета (обе заглушки vftable
    /// <c>0x6f3584</c>), <c>method17</c> — свой (<c>0x66386f</c>, <c>mov al,1</c>): единственный из
    /// 23 классов, кому разрешено целиться в слот мёртвого <i>и отступившего</i>. Ставит бит
    /// <c>Summon</c> (27). Ранг 1.
    ///
    /// <para><b>Главное про броски, и оно неочевидное.</b> В движке две разные функции случайных
    /// чисел: <c>generateRandomNumber</c> (GOG <c>0x54851f</c>) — тот самый R250 под критической
    /// секцией, чей поток воспроизводит С2, и <c>generateRandomNumberStd</c> (<c>0x548576</c>) —
    /// три инструкции поверх импортированного <c>rand</c> (<c>call [0x6cc2bc]; xor edx,edx;
    /// div</c>). Выбор призываемого делает <c>getSummonUnitImplIdByAttack</c> (<c>0x660a60</c>), и
    /// он зовёт <b>std</b>-версию — единственный вызов, на <c>0x660b3e</c>. Значит <c>Summon</c>
    /// тратит <b>ноль</b> бросков боевого потока и один бросок постороннего, который модель не
    /// держит вовсе. Ранг 1.</para>
    ///
    /// <para><b>Следствие, которое надо принять, а не обойти:</b> когда кандидатов больше одного,
    /// <i>кого</i> призвали, модель предсказать не может — это <c>rand()</c> процесса. В ванили так
    /// у двух атак из двадцати одной (<c>g000aa6007</c> и <c>g000aa6014</c>,
    /// <see cref="GameRules.SummonCandidates"/>); у остальных девятнадцати кандидат после фильтра по
    /// размеру ровно один, и тогда <c>rand() % 1 = 0</c> — призыв детерминирован. Выбор — <see cref="Pick"/>:
    /// в бою с карты по потоку CRT партии, без него неоднозначный случай падает с текстом.</para>
    ///
    /// <para><b>Куда и кого (ранг 1, дизасм 17.10).</b> Позиция — выбранная (<see cref="BattleState.SummonSlot"/>: человек — щелчком,
    /// ИИ — <c>0x5d1079</c>) из <see cref="BattleState.SummonCells"/>, иначе первая из них. Кого — <c>0x6635dc</c>: если пара позиции (<c>p ^ 1</c>)
    /// тоже годна для призыва (<c>canPerform</c> <c>0x662f80</c> по паре из <c>onHit</c> <c>0x6632e3…0x663310</c>) или обе клетки пусты,
    /// сначала берутся большие кандидаты (<c>0x660a60</c> с «малый» = 0, <c>0x6637a9</c>), и лишь если их нет — малые (<c>0x6637f9</c>);
    /// большой встаёт на чётную клетку пары (<c>0x6633cd…0x6633e6</c>). У тёмного эльфа Лифа (<c>g000aa6007</c>) так призывается
    /// Гигантский чёрный паук, у Тёмного Лакхаана (<c>g000aa8007</c>) — Чёрный дракон, у Магистра-оккультиста — один из двух больших.
    /// В корпусе призывов этими тремя атаками нет (64 призыва — все одним кандидатом-малым).</para></summary>
    public static AttackOutcome Summon(BattleUnit attacker, AttackType attack, BattleState? state)
    {
        var rules = Rules(state, AttackClass.Summon);
        int? chosen = state!.SummonSlot;
        state.SummonSlot = null;                                        // выбор позиции — на один призыв
        var cells = state.SummonCells(attacker);
        int slot = chosen is { } c && cells.Contains(c) ? c : cells.Count > 0 ? cells[0] : -1;
        if (slot < 0) return new AttackOutcome(false, 0, 0, false);   // отряд полон — призывать некуда
        var big = cells.Contains(Formation.Pair(slot)) ? rules.SummonCandidates(attack.Id, smallSlot: false) : [];
        var candidates = big.Count > 0 ? big : rules.SummonCandidates(attack.Id, smallSlot: true);
        if (candidates.Count == 0) return new AttackOutcome(false, 0, 0, false);
        var type = Pick(candidates, AttackClass.Summon, attack, state);
        if (big.Count > 0) slot &= ~1;                                 // большой — на чётную клетку пары

        state!.Units.Add(new BattleUnit
        {
            // С картой — номер счётчика карты (BattleState.NewUnitId, 0x6631eb → 0x5ee8bd). Без неё — размер списка, а не слот: слот
            // освобождается, когда призванный гибнет, и второй призыв в тот же слот дал бы двух юнитов с одним id.
            Id = state.NewUnitId?.Invoke() ?? $"{attacker.Id}-summon{state.Units.Count + state.Gone.Count}", Type = type,
            Attack = rules.AttackOf(type)
                     ?? throw new InvalidDataException($"у типа {type.Id} нет атаки {type.AttackId} в Gattacks"),
            Attack2 = rules.Attack2Of(type), Attacker = attacker.Attacker, Slot = slot,
            HpMax = type.HitPoints, Hp = type.HitPoints, Summoned = true, SummonerId = attacker.Id,
        });
        return new AttackOutcome(false, 0, 0, true);
    }

    /// <summary>Кого призвать или в кого превратить: <c>candidates[rand() % n]</c> (<c>0x660b3e</c> → <c>generateRandomNumberStd</c>
    /// <c>0x548576</c>). Бросок — только при двух и больше кандидатах: один <c>getSummonUnitImplIdByAttack</c> отдаёт без <c>rand()</c>
    /// (<c>0x660aed…0x660b1b</c>: размер списка 1 — выход до <c>0x660b3e</c>); журнал — <c>20261016e-av</c> seq 15948372…15948377: призыв
    /// Элементаля Воздуха (один кандидат) без строки <c>crt_rand</c> (прежде порт тратил бросок CRT партии и при одном). С
    /// <see cref="BattleState.ProcessRand"/> несколько кандидатов — по потоку CRT партии, без него — <see cref="NotSupportedException"/>.
    /// Порядок кандидатов — как у <see cref="GameRules.SummonCandidates"/> (ранг 3: порядок списка движка не сверен; в ванили
    /// неоднозначны только <c>g000aa6007</c> и <c>g000aa6014</c>).</summary>
    internal static UnitType Pick(IReadOnlyList<UnitType> candidates, AttackClass cls, AttackType attack, BattleState? state)
    {
        if (candidates.Count == 1) return candidates[0];
        if (state?.ProcessRand is { } rand) return candidates[rand.Next() % candidates.Count];
        throw new NotSupportedException(
            $"{cls} атакой {attack.Id}: кандидатов {candidates.Count} ({string.Join(", ", candidates.Select(t => t.Id))}), а выбор " +
            "движок делает через generateRandomNumberStd (GOG 0x548576) — поток rand() процесса; у боя без карты его нет (BattleState.ProcessRand)");
    }

    private static GameRules Rules(BattleState? state, AttackClass cls) =>
        state?.Rules ?? throw new InvalidOperationException(
            $"класс {cls} меняет состав или тип юнита и потому требует BattleState.Rules " +
            "(Gunits/Gattacks/Gtransf); бой собран без справочников");
}
