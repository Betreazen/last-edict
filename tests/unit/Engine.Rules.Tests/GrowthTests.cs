using LastEdict.Data;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Задача 11 фазы Ф6: рост статов — <c>GDynUpgr</c> (уровневый рост юнита) и <c>GleaUpg</c>
/// (способности лидера). Все числа — из живой ванили через <c>LastEdict.Data</c>; синтетических таблиц
/// здесь нет. Игра не запускается.
/// <para>Формула роста прочитана в дизассемблере <c>game-pristine/Discipl2.exe</c> (GOG 3.01) —
/// адреса перечислены у <see cref="UnitGrowth"/>. Тесты утверждают её на конкретных вани́льных
/// юнитах, а заодно — что она согласуется сама с собой на всех 356.</para>
/// </summary>
public class GrowthTests(ITestOutputHelper output)
{
    private static readonly Lazy<GameRules> Rules = new(() => RulesLoader.Load(ImportedData.Content));
    private static readonly Lazy<RulesLoader.RulesExtras> Extras =
        new(() => RulesLoader.LoadExtras(ImportedData.Content));
    private static readonly Lazy<ModifierTables> Tables =
        new(() => ModifierLoader.Load(ImportedData.Content));

    /// <summary>Юнит, его строка «дочитанных колонок» и обе прибавки — одним куском, как их видит
    /// движок в <c>0x598a86</c>.</summary>
    private sealed record Subject(UnitType Type, RulesLoader.UnitExtra Extra,
                                  DynUpgrade? First, DynUpgrade? Second);

    private static Subject Of(string unitId)
    {
        var extra = Extras.Value.Units[unitId];
        return new Subject(Rules.Value.Units[unitId], extra, Upgrade(extra.DynUpg1Id), Upgrade(extra.DynUpg2Id));
    }

    private static DynUpgrade? Upgrade(string id) =>
        id.Length == 0 ? null : Extras.Value.DynUpgrades[id];

    private static GrownUnit Grow(Subject s, int level) =>
        UnitGrowth.Of(s.Type, s.Extra.DynUpgLevel, level, s.First, s.Second, s.Extra.Regen, s.Extra.Move);

    // ---- число шагов: DYN_UPG1 до DYN_UPG_LV, дальше DYN_UPG2 --------------------------------

    /// <summary>Squire (<c>g000uu0001</c>, LEVEL 1, DYN_UPG_LV 10): первая прибавка упирается в
    /// девять шагов, дальше растёт только вторая. Числа — <c>0x598b40</c> и <c>0x598b4c</c>.</summary>
    [SkippableFact]
    public void Steps_SwitchToSecondUpgradeAboveDynUpgLevel()
    {
        ImportedData.RequireRules();
        var s = Of("base:unit/squire-empire");
        Assert.Equal((1, 10), (s.Type.Level, s.Extra.DynUpgLevel));

        Assert.Equal(new GrowthSteps(0, 0), UnitGrowth.StepsFor(1, 10, 1));
        Assert.Equal(new GrowthSteps(9, 0), UnitGrowth.StepsFor(1, 10, 10));
        Assert.Equal(new GrowthSteps(9, 1), UnitGrowth.StepsFor(1, 10, 11));
        Assert.Equal(new GrowthSteps(9, 5), UnitGrowth.StepsFor(1, 10, 15));
        // потолок первой прибавки — DYN_UPG_LV минус собственный уровень, а не DYN_UPG_LV
        Assert.Equal(new GrowthSteps(7, 0), UnitGrowth.StepsFor(3, 10, 10));
        Assert.Equal(new GrowthSteps(7, 2), UnitGrowth.StepsFor(3, 10, 12));
        output.WriteLine("шагов у Squire на 15-м уровне: 9 + 5");
    }

    /// <summary>Уровень ниже собственного движок не порождает вовсе (вызывающий ограничивает его
    /// снизу — <c>getUnitLevelByImplId</c>), и модель это не прощает молча.</summary>
    [SkippableFact]
    public void Steps_BelowOwnLevel_Throws()
    {
        ImportedData.RequireRules();
        var e = Assert.Throws<ArgumentOutOfRangeException>(() => UnitGrowth.StepsFor(3, 10, 2));
        Assert.Contains("ниже собственного уровня", e.Message);
    }

    /// <summary>Отрицательное число шагов первой прибавки движок не отсекает — но в ванили оно
    /// недостижимо: у всех 356 юнитов <c>DYN_UPG_LV</c> = 10, а собственный уровень не выше 5.
    /// Проверяется по всей таблице, а не на выбранных примерах.</summary>
    [SkippableFact]
    public void VanillaSteps_AreNeverNegative()
    {
        ImportedData.RequireRules();
        const int levelMax = 25;
        int checkedUnits = 0, levels = 0;
        foreach (var (id, extra) in Extras.Value.Units)
        {
            int baseLevel = Rules.Value.Units[id].Level;
            Assert.Equal(10, extra.DynUpgLevel);
            for (int level = baseLevel; level <= levelMax; level++)
            {
                var steps = UnitGrowth.StepsFor(baseLevel, extra.DynUpgLevel, level);
                Assert.True(steps.First >= 0 && steps.Second >= 0,
                            $"{id}: уровень {level} дал шаги {steps.First}/{steps.Second}");
                Assert.Equal(level - baseLevel, steps.First + steps.Second);
                levels++;
            }
            checkedUnits++;
        }
        Assert.Equal(356, checkedUnits);
        output.WriteLine($"юнитов проверено {checkedUnits}, уровней {levels}, DYN_UPG_LV всюду 10");
    }

    // ---- сами статы -------------------------------------------------------------------------

    /// <summary>Squire на десятом и пятнадцатом уровне. Прибавки: <c>g000DU0001</c> (hp 10,
    /// xpKilled 2, move 1, damage 3, power 1) и <c>g002DU0001</c> (hp 5, xpKilled 1, damage 1).</summary>
    [SkippableFact]
    public void Squire_GrowsByTheTable()
    {
        ImportedData.RequireRules();
        var s = Of("base:unit/squire-empire");
        Assert.Equal((100, 20, 80, 5, 0), (s.Type.HitPoints, s.Type.XpKilled, s.Type.XpNext,
                                           s.Extra.Regen, s.Extra.Move));

        var one = Grow(s, 1);
        Assert.Equal(s.Type, one.Type);                       // ноль шагов — база бит в бит
        Assert.Equal((5, 0), (one.Regeneration, one.MoveAllowance));

        var ten = Grow(s, 10);                                // 9 шагов первой прибавки
        Assert.Equal((190, 38, 80), (ten.Type.HitPoints, ten.Type.XpKilled, ten.Type.XpNext));
        Assert.Equal((5, 9), (ten.Regeneration, ten.MoveAllowance));

        var fifteen = Grow(s, 15);                            // 9 + 5
        Assert.Equal((215, 43), (fifteen.Type.HitPoints, fifteen.Type.XpKilled));
        Assert.Equal(9, fifteen.MoveAllowance);               // MOVE второй прибавки нулевой

        var attack = Rules.Value.Attacks[s.Type.AttackId];
        var a10 = UnitGrowth.OfAttack(attack, UnitGrowth.StepsFor(1, 10, 10), s.First, s.Second);
        Assert.Equal((50, 89, 52, 0), (a10.Initiative, a10.Power, a10.QtyDamage, a10.QtyHeal));
        var a15 = UnitGrowth.OfAttack(attack, UnitGrowth.StepsFor(1, 10, 15), s.First, s.Second);
        Assert.Equal((89, 57), (a15.Power, a15.QtyDamage));   // POWER второй прибавки нулевой
        output.WriteLine($"Squire 1->10->15: hp {s.Type.HitPoints}->{ten.Type.HitPoints}->" +
                         $"{fifteen.Type.HitPoints}, урон {attack.QtyDamage}->{a10.QtyDamage}->{a15.QtyDamage}");
    }

    /// <summary>Собственный уровень типа — это ноль роста, а не единица: Matriarch (LEVEL 3) на
    /// третьем уровне равна своей строке <c>Gunits</c>, а на десятом получает семь шагов.</summary>
    [SkippableFact]
    public void OwnLevel_IsZeroGrowth()
    {
        ImportedData.RequireRules();
        var s = Of("base:unit/matriarch");
        Assert.Equal(3, s.Type.Level);
        Assert.Equal(s.Type, Grow(s, 3).Type);
        Assert.Equal(100 + 7 * 10, Grow(s, 10).Type.HitPoints);
    }

    /// <summary>Три поля атаки растут только с ненулевой базы — иначе ядовитая атака набирала бы
    /// урон, а бьющая — лечение. Оба случая в ванили настоящие: у Giant Spider вторая атака
    /// (<c>g000aa5536</c>, паралич) имеет <c>QTY_DAM</c> = 0 при <c>DAMAGE</c> 13 в прибавке, у
    /// Matriarch вторая (<c>g000aa1017</c>, Cure) — <c>QTY_HEAL</c> = 0 при <c>HEAL</c> 4.</summary>
    [SkippableFact]
    public void AttackGrowth_OnlyFromNonZeroBase()
    {
        ImportedData.RequireRules();
        var spider = Of("base:unit/giant-spider-neutral");
        var steps = UnitGrowth.StepsFor(spider.Type.Level, spider.Extra.DynUpgLevel, 10);
        Assert.Equal(new GrowthSteps(9, 0), steps);
        Assert.Equal(13, spider.First!.Damage);

        var main = UnitGrowth.OfAttack(Rules.Value.Attacks[spider.Type.AttackId], steps, spider.First, spider.Second);
        Assert.Equal(130 + 9 * 13, main.QtyDamage);
        var second = UnitGrowth.OfAttack(Rules.Value.Attacks[spider.Type.Attack2Id], steps, spider.First, spider.Second);
        Assert.Equal(0, second.QtyDamage);                    // без стража было бы 117

        var matriarch = Of("base:unit/matriarch");
        var mSteps = UnitGrowth.StepsFor(3, 10, 10);
        var heal = UnitGrowth.OfAttack(Rules.Value.Attacks[matriarch.Type.AttackId], mSteps,
                                       matriarch.First, matriarch.Second);
        Assert.Equal(40 + 7 * 4, heal.QtyHeal);
        var cure = UnitGrowth.OfAttack(Rules.Value.Attacks[matriarch.Type.Attack2Id], mSteps,
                                       matriarch.First, matriarch.Second);
        Assert.Equal(0, cure.QtyHeal);                        // без стража было бы 28
        output.WriteLine("стражи проверены на паралитике Giant Spider и на Cure Matriarch");
    }

    /// <summary>Сколько раз стражи вообще меняют ответ на вани́льных данных — счётчик без бюджета:
    /// если бы он оказался нулём, предыдущий тест держался бы на выдуманных примерах.
    /// <c>POWER</c> в ванили не растёт с нулевой базы ни разу — так и печатаем, а не умалчиваем.</summary>
    [SkippableFact]
    public void GuardsMatter_CountedOverWholeTable()
    {
        ImportedData.RequireRules();
        int damage = 0, heal = 0, power = 0, pairs = 0;
        foreach (var (id, extra) in Extras.Value.Units)
        {
            var type = Rules.Value.Units[id];
            foreach (var attackId in new[] { type.AttackId, type.Attack2Id })
            {
                if (!Rules.Value.Attacks.TryGetValue(attackId, out var attack)) continue;
                foreach (var upgrade in new[] { Upgrade(extra.DynUpg1Id), Upgrade(extra.DynUpg2Id) })
                {
                    if (upgrade is null) continue;
                    pairs++;
                    if (attack.QtyDamage == 0 && upgrade.Damage != 0) damage++;
                    if (attack.QtyHeal == 0 && upgrade.Heal != 0) heal++;
                    if (attack.Power <= 0 && upgrade.Power != 0) power++;
                }
            }
        }
        output.WriteLine($"пар «атака × прибавка» {pairs}; страж сработал бы: урон {damage}, " +
                         $"лечение {heal}, сила {power}");
        Assert.True(pairs > 0, "ни одной пары — тест ничего не проверил");
        Assert.Equal((52, 12, 0), (damage, heal, power));
    }

    /// <summary>Рост — база, модификаторы поверх неё, и порядок не переставим. «+20 % Hit Points»
    /// (<c>g000um9001</c>) на Squire десятого уровня даёт 228, а не 210: 190 + 20 %, а не 120 + 90.</summary>
    [SkippableFact]
    public void GrowthGoesUnderModifiers_NotOverThem()
    {
        ImportedData.RequireRules();
        var s = Of("base:unit/squire-empire");
        var chain = new[] { Tables.Value.Modifiers["base:modifier/20-hit-points"] };

        var grown = Grow(s, 10);
        Assert.Equal(228, EffectiveStats.Of(grown.Type, chain).HitPoints);

        int modifiedFirst = EffectiveStats.Of(s.Type, chain).HitPoints;   // 100 + 20 % = 120
        int wrongOrder = UnitGrowth.Of(s.Type with { HitPoints = modifiedFirst },
                                       s.Extra.DynUpgLevel, 10, s.First, s.Second).Type.HitPoints;
        Assert.Equal(210, wrongOrder);
        output.WriteLine("Squire L10 + 20 % HP: 228 (рост под модификатором) против 210 (наоборот)");
    }

    // ---- GleaUpg: способности лидера ---------------------------------------------------------

    /// <summary>391 строка, пятнадцать лордов из восемнадцати. Три лорда
    /// (<c>g000LR0013…0015</c>) не имеют ни одной строки — это данные, а не потеря загрузчика.</summary>
    [SkippableFact]
    public void LeaderUpgrades_CoverFifteenLordsOfEighteen()
    {
        ImportedData.RequireRules();
        var all = Tables.Value.LeaderUpgrades;
        Assert.Equal(391, all.Count);
        Assert.Equal(18, Tables.Value.Lords.Count);

        var withRows = all.Select(u => u.LordId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(15, withRows.Count);
        var without = Tables.Value.Lords.Keys.Where(id => !withRows.Contains(id)).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "base:lord/neutral-race-guildmaster", "base:lord/neutral-race-mage-lord", "base:lord/neutral-race-warrior-lord" }, without);
        output.WriteLine($"строк {all.Count}, лордов со строками {withRows.Count}, без строк: " +
                         string.Join(", ", without));
    }

    /// <summary>Доступность по уровню: до <c>MIN_LEVEL</c> строки нет, с него — есть и больше не
    /// пропадает. Проверяется на всех пятнадцати лордах, а не на одном.</summary>
    [SkippableFact]
    public void Available_OpensAtMinLevelAndStays()
    {
        ImportedData.RequireRules();
        var all = Tables.Value.LeaderUpgrades;
        const int levelMax = 20;
        int transitions = 0;
        foreach (var lord in all.Select(u => u.LordId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int previous = 0;
            for (int level = 1; level <= levelMax; level++)
            {
                var available = LeaderUpgrades.Available(all, lord, level);
                Assert.All(available, u => Assert.True(u.MinLevel <= level));
                Assert.True(available.Count >= previous, $"{lord}: набор сузился на уровне {level}");
                if (available.Count > previous) transitions++;
                previous = available.Count;
            }
            Assert.Equal(all.Count(u => string.Equals(u.LordId, lord, StringComparison.OrdinalIgnoreCase)),
                         previous);
        }
        Assert.Empty(LeaderUpgrades.Available(all, "base:lord/human-warrior-lord", 1));
        Assert.Equal(4, LeaderUpgrades.Available(all, "base:lord/human-warrior-lord", 2).Count);
        Assert.Equal(27, LeaderUpgrades.Available(all, "base:lord/human-warrior-lord", 15).Count);
        output.WriteLine($"пятнадцать лордов, {transitions} уровней с прибавкой набора; " +
                         "g000LR0001: 0 на 1-м, 4 на 2-м, 27 на 15-м");
    }

    /// <summary>Способность лидера — это модификатор, и складывает её та же цепочка. Все 391
    /// строка сворачиваются на настоящем юните и на настоящей атаке без исключений; ни одного
    /// собственного механизма роста лидера здесь нет.</summary>
    [SkippableFact]
    public void EveryLeaderUpgrade_FoldsThroughTheModifierChain()
    {
        ImportedData.RequireRules();
        var unit = Rules.Value.Units["base:unit/squire-empire"];
        var attack = Rules.Value.Attacks["base:attack/squire-empire"];
        int folded = 0, changedUnit = 0, changedAttack = 0;
        foreach (var upgrade in Tables.Value.LeaderUpgrades)
        {
            var chain = new[] { Tables.Value.Modifiers[upgrade.ModifId] };
            var stats = EffectiveStats.Of(unit, chain, regeneration: 5);
            var grown = EffectiveStats.OfAttack(attack, chain);
            if (stats.HitPoints != unit.HitPoints || stats.Armor != unit.Armor
                || stats.Regeneration != 5 || stats.Immunities.Count > 0
                || stats.LeaderAbilities.Count > 0 || stats.Leadership != 0) changedUnit++;
            if (grown.Attack != attack || grown.DrainPercent != 0) changedAttack++;
            folded++;
        }
        Assert.Equal(391, folded);
        Assert.True(changedUnit > 0 && changedAttack > 0,
                    "ни одна способность ничего не изменила — сворачивание проверено вхолостую");
        output.WriteLine($"свёрнуто способностей {folded}: меняют юнита {changedUnit}, " +
                         $"атаку {changedAttack}");
    }

    /// <summary>«Leader Upgrade: Toughness» (<c>g000um4017</c>, <c>TYPE 6</c> Hp, 20 %) выдаётся с
    /// восьмого уровня и даёт ровно то, что обещает подпись, — на выращенном юните, а не на базовом.</summary>
    [SkippableFact]
    public void Toughness_AppliesOnTopOfLevelGrowth()
    {
        ImportedData.RequireRules();
        var toughness = Tables.Value.LeaderUpgrades
            .Where(u => u.ModifId.Equals("base:modifier/skill-toughness", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(toughness);
        Assert.All(toughness, u => Assert.Equal(8, u.MinLevel));

        var s = Of("base:unit/squire-empire");
        var grown = Grow(s, 8);                                // 7 шагов: 100 + 70
        Assert.Equal(170, grown.Type.HitPoints);
        var chain = new[] { Tables.Value.Modifiers["base:modifier/skill-toughness"] };
        Assert.Equal(204, EffectiveStats.Of(grown.Type, chain).HitPoints);
        output.WriteLine($"Toughness с {toughness[0].MinLevel} уровня: Squire L8 170 -> 204");
    }

    /// <summary><c>PRIORITY</c>: ключом порядка она быть не может — в 49 группах «лорд ×
    /// <c>MIN_LEVEL</c>» из 208 значения повторяются. Семь значений, все не выше 99. Утверждение
    /// проверяемое, в отличие от догадки о смысле (ранг 3, см. <see cref="LeaderUpgrades"/>).</summary>
    [SkippableFact]
    public void Priority_IsNotAnOrderingKey()
    {
        ImportedData.RequireRules();
        var all = Tables.Value.LeaderUpgrades;
        var values = all.Select(u => u.Priority).ToHashSet();
        Assert.Equal(new[] { 60, 70, 80, 85, 90, 95, 99 }, values.OrderBy(v => v).ToList());

        var groups = all.GroupBy(u => (u.LordId, u.MinLevel)).ToList();
        int ambiguous = groups.Count(g => g.Select(u => u.Priority).Distinct().Count() != g.Count());
        Assert.Equal((208, 49), (groups.Count, ambiguous));
        output.WriteLine($"PRIORITY: значений {values.Count} ({string.Join(", ", values.OrderBy(v => v))}), " +
                         $"групп «лорд × MIN_LEVEL» {groups.Count}, из них с повтором приоритета {ambiguous}");
    }
}
