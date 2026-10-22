namespace LastEdict.Rules.Map;

/// <summary>Переменные <c>GVars.dbf</c> для роста юнитов на карте. Ранг 1 — значения таблицы.</summary>
/// <param name="WarlordRegen"><c>REGEN_LWAR</c> = 15: прибавка регенерации отрядам лорда-Воителя.</param>
/// <param name="WeaponMaster"><c>WEAPN_MSTR</c> = 25: прибавка опыта «Инструктора», %.</param>
/// <param name="TalismanCharges"><c>GVars.TALIS_CHRG</c> — сколько зарядов у нового талисмана
/// (в ванили 5). Ранг 1: тот же литерал движок берёт из <c>[0x6ea178]</c> при создании талисмана
/// (<see cref="LastEdict.Rules.Magic.Charges.TalismanAfterBattle"/>, задача 10).</param>
/// <param name="LeaderMaxAbilities"><c>GVars.LDRMAXABIL</c> = 15 — сколько уровневых навыков может
/// носить лидер (<see cref="LastEdict.Rules.Units.SkillChoice.DefaultLimit"/>, задача 11). Ранг 1 на
/// число, ранг 3 на смысл: потребителя в экзешнике не искали, и в корпусе в потолок никто не
/// упёрся.</param>
/// <param name="GainSpell"><c>GVars.GAIN_SPELL</c> = 50 — шанс в процентах, что захватчик столицы узнаёт заклинания погибшего игрока
/// (<c>CEffectCaptureCapital</c> <c>0x434b3b</c>: один бросок R250 на все, поле <c>+0xac</c>, загрузчик <c>0x586ebd</c>). Ранг 1.</param>
/// <param name="RuinRegen"><c>GVars.REGEN_RUIN</c> = 20 — дневное лечение охраны руин, % от максимума (<c>0x5d463e</c>; probe72, строки
/// <c>regen term = ruin</c>, <c>pct</c> 20).</param>
public sealed record MapVariables(int WarlordRegen, int WeaponMaster, int TalismanCharges,
                                  int LeaderMaxAbilities, int GainSpell = 50, int RuinRegen = 20);

/// <summary>
/// Дневная регенерация HP — план Ф7 §1.4, задача 6. Всё ранг 1, кроме оговорённого.
/// <para><b>Формула — два слагаемых, у каждого своё округление вниз:</b>
/// <c>HP += ⌊HPmax·(REGEN_юнита + REGEN_LWAR·[лорд Воитель]) / 100⌋ + ⌊HPmax·REGEN_укрепления / 100⌋</c>.
/// Лордова прибавка складывается с юнитом ДО округления (прогон 2: Holy Avenger 250 при 5 + 15 получил +50, а не
/// 12 + 37); укрепление округляется отдельно (<c>f7-m16-regen</c>: лидер 150 в столице +59, а не +60; в городе 2
/// уровня +29, а не +30). Примитив — <c>healUnitByPercent</c> <c>0x5D4099</c>, процент от максимума HP.</para>
/// <para><b>Когда.</b> На каждом дневном тике игрока, включая загрузку карты (<c>CEffectRegenUnits::apply</c>
/// <c>0x43B873</c>). Вход в укрепление сам не лечит (прогон 2 §4.3). Павший не лечится.</para>
/// <para><b>Кто.</b> Проход укрепления <c>0x5D40D9</c> лечит гарнизон и визитёра (<c>fort+0x8C</c>), полевой
/// <c>0x5D41D8</c> — отряды игрока. <b>Оба слагаемых получают и визитёр, и гарнизон</b> (с прибавкой Воителя). Ранг 1
/// по корпусу: 4 из 4 раненых гарнизонов, из них различающий один — гарнизон столицы под Воителем 76 → 150 при HPmax 150
/// (только укрепление дало бы 128). <i>Было иначе:</i> «гарнизону только укрепление» (ранг 2, чтение разбора адресов). Порт гарнизоны
/// не лечил вовсе до 30.09 (<c>GameSession.RegenTerms</c>; probe72, строки <c>regen</c>: гарнизон — укрепление, затем юнит с Воителем,
/// без своей земли).</para>
/// <para><b>Порядок.</b> Проход укреплений — до восстановления хода, проход групп — после, и оба до дневного роста земли
/// (<c>regen</c> → <c>ap_restore</c> → <c>regen</c> → <c>land_grow</c> → <c>income</c> → <c>xp</c>): «своя земля» — клетка до роста.</para>
/// <para>Охрана руин лечится <c>REGEN_RUIN</c> в начале дня (<c>0x5d463e</c>, <see cref="MapVariables.RuinRegen"/>; 30.09, probe72).</para>
/// </summary>
public static class Regeneration
{
    /// <summary><c>Llord</c>: 1 — <c>L_WARRIOR</c>. Бонус проверяет <c>0x5D41D8</c>: категория лорда равна
    /// <c>[0x837750]</c>, игрок жив и не пленён (<c>0x5E5358</c>), раса играбельна (<c>0x5E4E73</c>). Ранг 1 — значение
    /// таблицы и прибавка по прогону 2 (Holy Avenger +50); состав условий — ранг 2 (чтение адресов, cities §5).</summary>
    public const int WarlordCategory = 1;

    /// <summary>+10 к регенерации отряда <b>в поле на земле своей расы</b> (террейн клетки под отрядом равен
    /// <see cref="RaceInfo.Terrain"/> владельца) — литерал кода, не таблицы (ранг 2 на место:
    /// <c>reference/D2ModdingToolset/mss32/src/unitutils.cpp:431</c>, cities §5). Ранг 1 по корпусу: первая большая
    /// партия — отряды ИИ под Воителем в поле получили 30 % (5 + 15 + 10: 1 → 46 при HPmax 150, 57 → 84 при 90,
    /// 1 → 34 при 110), нейтралы — 15 % (5 + 10: 52 → 61 при 65). Входит в слагаемое юнита (одно округление; на
    /// корпусе с отдельным округлением неотличимо — ранг 2 на округление). <i>Было открыто:</i> план Ф7 §3 п. 4.</summary>
    public const int OwnLandBonus = 10;

    /// <summary>Процент юнита: собственная регенерация (<c>Gunits.REGEN</c> с ростом и цепочкой — сюда же входит
    /// Natural Healing), <c>REGEN_LWAR</c> у лорда-Воителя и <see cref="OwnLandBonus"/> в поле на своей земле.</summary>
    public static int UnitPercent(int unitRegen, int lordCategory, bool playable, bool captured, int warlordRegen,
                                  bool onOwnLand = false) =>
        unitRegen + (lordCategory == WarlordCategory && playable && !captured ? warlordRegen : 0)
                  + (onOwnLand ? OwnLandBonus : 0);

    /// <summary>Процент столицы — <c>Grace.REGEN_H</c> расы (35 в ванили). <c>getRegen</c> столицы <c>0x606CDB</c>.</summary>
    public static int CapitalPercent(RaceInfo race) => race.RegenHeal;

    /// <summary>Процент деревни — <c>GcityInf.REGEN_F(SIZE)</c> + <c>REGEN_B</c> записи <c>.sg</c> (<c>0x5FE12A</c>,
    /// <c>0x59BFFB</c>). В ванили <c>REGEN_F</c> одинаков у всех рас: 10/15/20/25/30.
    /// <para>В бунтующей деревне регенерации нет — ни у гарнизона, ни у гостя, и не только слагаемого укрепления (сессия пропускает
    /// такие деревни целиком). Урона в дни бунта тоже нет: <c>RIOT_DMG</c> бьёт один раз, в день сброса (<see cref="RiotDamage"/>).
    /// В сам день сброса деревня уже лечит: <c>20261016b-port-synth</c>, карта 2, деревня R1 (<c>RIOT_T</c> 3), ход Кланов 3 —
    /// строки <c>regen</c> укрепления 15 % (80 → 102, 40 → 49) и юнита (→ 132, → 62), затем удар <c>hp_change</c> 16 и 8.
    /// <i>Было иначе:</i> «вместо регенерации урон» (план §1.4); до 16.10 — «в день сброса не лечит никого» (ai-q11: гость 90/90
    /// без строк <c>regen</c> — он был здоров, лечить было нечего).</para></summary>
    /// <exception cref="ArgumentException">Нет строки <c>GcityInf</c> расы и размера.</exception>
    public static int VillagePercent(EconomyRules rules, string raceId, int size, int regenBonus, bool rioting)
    {
        if (rioting) return 0;
        var city = rules.Cities.FirstOrDefault(c =>
                       c.RaceId.Equals(raceId, StringComparison.OrdinalIgnoreCase) && c.Size == size)
                   ?? throw new ArgumentException($"в GcityInf нет строки расы {raceId} размера {size}", nameof(raceId));
        return city.RegenFort + regenBonus;
    }

    /// <summary>Урон конца мятежа: <c>⌊HP × RIOT_DMG / 100⌋</c> от HP <b>до</b> дневного лечения, гарнизону и визитёру, один раз
    /// при <see cref="Fortifications.EndRiot"/>; вычитается после лечения и роста земли (<see cref="RiotHit"/> — когда лечить нечего).
    /// Ранг 1: ai-q11 <c>riot_dmg_20pct_at_end</c> 4/0 и прогон 13.10 7/7 (150 → 120, визитёр 90 → 72 — здоровые, до и после лечения
    /// одно и то же); <c>20261016b-port-synth</c>, карта 2 — раненый гарнизон R1: 80 → лечение 132 → удар 16 (20 % от 80, не от 132)
    /// → 116, 40 → 62 → удар 8 → 54. Следующая за ним <c>heal</c> — платное лечение ИИ, а не регенерация.</summary>
    public static int RiotDamage(int hpBeforeHeal, int riotDamagePercent) =>
        hpBeforeHeal <= 0 ? 0 : hpBeforeHeal * riotDamagePercent / 100;

    /// <summary>HP после урона конца мятежа у юнита, которого лечение не тронуло: <c>HP − <see cref="RiotDamage"/></c>.</summary>
    public static int RiotHit(int hp, int riotDamagePercent) => hp <= 0 ? 0 : hp - RiotDamage(hp, riotDamagePercent);

    /// <summary>Один дневной тик: два слагаемых со своим округлением, не выше максимума; павший остаётся павшим.</summary>
    /// <param name="unitPercent"><see cref="UnitPercent"/> (у гарнизона тоже, без прибавки своей земли).</param>
    /// <param name="fortPercent">Процент укрепления; вне укрепления — 0.</param>
    public static int Tick(int hp, int hpMax, int unitPercent, int fortPercent)
    {
        if (hp <= 0) return 0;
        int gained = hpMax * unitPercent / 100 + hpMax * fortPercent / 100;
        return Math.Min(hpMax, hp + Math.Max(gained, 0));
    }
}
