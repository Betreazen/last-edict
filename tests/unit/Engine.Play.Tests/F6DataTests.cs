using D2.Data;
using D2.Data.Battle;
using D2.Data.Magic;
using D2.Data.Map;
using D2.Data.Tests.LegacyLoaders;
using D2.Rules.Units;
using Xunit.Abstractions;

/// <summary>
/// Задача 1 фазы Ф6: семь таблиц магии, предметов и модификаторов читаются, и их ссылки сходятся.
/// Проверка целиком «на столе» — игра не запускается. Счётчики печатаются: пустая проверка
/// ничего не доказывает, поэтому каждая связь падает при «проверено = 0».
/// <para>Это факты о самих таблицах оригинала (висячие строки, счёт строк), поэтому таблицы читают прежние загрузчики
/// (<c>LegacyLoaders</c>): правила игры строятся из содержимого, где висячих строк уже нет.</para>
/// </summary>
public class F6DataTests(ITestOutputHelper output)
{
    private static string Globals => GamePaths.Globals;

    /// <summary>Число записей в заголовке <c>.dbf</c> (смещение 4, 4 байта) — вместе с удалёнными.
    /// Живые считает <see cref="D2.Data.Dbf.DbfTable"/>, который записи <c>0x2A</c> пропускает.</summary>
    private static int HeaderCount(string file)
    {
        using var s = File.OpenRead(RulesLoaderFile(file));
        Span<byte> head = stackalloc byte[8];
        s.ReadExactly(head);
        return BitConverter.ToInt32(head[4..8]);
    }

    private static string RulesLoaderFile(string name) =>
        Directory.EnumerateFiles(Globals).First(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Step 2. Точные живые числа строк. В плане Ф6 три из них были взяты из заголовка
    /// <c>.dbf</c> вместе с удалёнными записями — та же ошибка, которую план нашёл у ТЗ:
    /// <c>Gimmu</c> 348 → живых 227, <c>GimmuC</c> 240 → 13, <c>Gtransf</c> 48 → 43.
    /// Тест утверждает оба числа, чтобы расхождение было видно, а не заметено.
    /// </summary>
    [SkippableFact]
    public void RowCounts_MatchLiveRecords()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        (string File, int Header, int Live)[] expected =
        [
            ("Gmodif.dbf", 256, 178), ("GmodifL.dbf", 222, 187), ("GleaUpg.dbf", 403, 391),
            ("Gspells.dbf", 120, 120), ("GItem.dbf", 339, 336), ("Gimmu.dbf", 348, 227),
            ("GimmuC.dbf", 240, 13), ("GDynUpgr.dbf", 503, 502), ("Gtransf.dbf", 48, 43),
            ("Gattacks.dbf", 354, 350), ("Gunits.dbf", 359, 356), ("Glord.dbf", 42, 18),
        ];
        foreach (var (file, header, live) in expected)
        {
            var actualHeader = HeaderCount(file);
            var actualLive = D2.Data.Dbf.DbfTable.Read(RulesLoaderFile(file)).Rows.Count;
            output.WriteLine($"{file,-14} заголовок {actualHeader,4} (ждали {header,4})  живых {actualLive,4} (ждали {live,4})");
            Assert.True(header == actualHeader && live == actualLive,
                $"{file}: заголовок {actualHeader} (ждали {header}), живых {actualLive} (ждали {live})");
        }
    }

    /// <summary>
    /// Step 3. Ссылочная целостность. Пустая ссылка (<c>g000000000</c> или пусто) — это «ссылки нет»,
    /// а не висячая: загрузчики приводят её к пустой строке, и в счёт «проверено» она не идёт.
    /// Именно из-за неё разведка насчитала «46 из 47» у <c>Gspells.MODIF_ID</c> и «41 из 42»
    /// у <c>GItem.MOD_EQUIP</c>: настоящих ссылок 46 и 41, и все они разрешаются.
    /// </summary>
    [SkippableFact]
    public void References_Resolve()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var mods = LegacyModifierLoader.Load(Globals);
        var spells = LegacySpellLoader.Load(Globals);
        var items = LegacyItemLoader.Load(Globals);
        var rules = LegacyRulesLoader.Load(Globals);
        var extras = LegacyRulesLoader.LoadExtras(Globals);
        var economy = LegacyEconomyLoader.Load(Globals);

        bool Modif(string id) => mods.Modifiers.ContainsKey(id);
        bool Unit(string id) => rules.Units.ContainsKey(id);

        // Строки GmodifL, чей Gmodif удалён: не молчаливый пропуск, а поимённый список.
        var owned = mods.Modifiers.Values.Sum(m => m.Elements.Count);
        var orphanIds = mods.Orphans.Select(o => o.BelongsTo).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        output.WriteLine($"{"GmodifL.BELONGS_TO",-21}: проверено {owned + mods.Orphans.Count,4} разрешилось " +
                         $"{owned,4} висячих {mods.Orphans.Count,3} -> {string.Join(", ", orphanIds)}");
        Assert.Equal(new[] { "g000um0004", "g000um0008", "g000um0014", "g000um0015", "g000um0019",
                             "g000um1067", "g000um1068" }, orphanIds);

        // Вардов у ванильных заклинаний нет вовсе — печатаем, чтобы «ноль» был измерением, а не умолчанием.
        output.WriteLine($"{"Gspells.WARD1..4",-21}: проверено {spells.Values.Sum(s => s.Wards.Count),4} " +
                         "(в ванили ни одно заклинание вардов не несёт)");

        Check("Gspells.MODIF_ID", spells.Values.Select(s => s.ModifierId), Modif);
        Check("GItem.MOD_EQUIP", items.Values.Select(i => i.EquipModifierId), Modif);
        Check("GItem.MOD_POTION", items.Values.Select(i => i.PotionModifierId), Modif);
        Check("Gattacks.WARD1..4", extras.Attacks.Values.SelectMany(a => a.Wards), Modif);
        Check("GcityInf.PROTECTION", economy.Cities.Select(c => c.ProtectionId), Modif);
        Check("Grace.PROTECTION", economy.Races.Values.Select(r => r.ProtectionId), Modif);
        Check("GleaUpg.MODIF_ID", mods.LeaderUpgrades.Select(l => l.ModifId), Modif);
        // Долг задачи 1: связь была замерена вручную (391 из 391), но загрузчика Glord не было,
        // и утверждение оставалось непроверяемым. Теперь оно такое же, как остальные шестнадцать.
        Check("GleaUpg.BELONGS_TO", mods.LeaderUpgrades.Select(l => l.LordId), mods.Lords.ContainsKey);
        Check("Gunits.DYN_UPG1", extras.Units.Values.Select(u => u.DynUpg1Id), extras.DynUpgrades.ContainsKey);
        Check("Gunits.DYN_UPG2", extras.Units.Values.Select(u => u.DynUpg2Id), extras.DynUpgrades.ContainsKey);
        // UPGRADE_B — постройка (Gbuild), а не GDynUpgr, как значилось в плане: id вида g000bb….
        Check("Gunits.UPGRADE_B", extras.Units.Values.Select(u => u.UpgradeBuildingId), economy.Buildings.ContainsKey);
        Check("Gattacks.ALT_ATTACK", extras.Attacks.Values.Select(a => a.AltAttackId), rules.Attacks.ContainsKey);
        Check("Gtransf.ATTACK_ID", extras.Transformations.Select(t => t.AttackId), rules.Attacks.ContainsKey);
        Check("Gtransf.TRANSF_ID", extras.Transformations.Select(t => t.TransformUnitId), Unit);
        // Известные исключения: строка-заглушка Gimmu и один осиротевший юнит GimmuC.
        Check("Gimmu.UNIT_ID", extras.ImmunityBySource.Select(i => i.UnitId), Unit, "g000uu0000");
        Check("GimmuC.UNIT_ID", extras.ImmunityByClass.Select(i => i.UnitId), Unit, "g000uu5167");
    }

    private void Check(string label, IEnumerable<string> values, Func<string, bool> resolves,
                       params string[] knownDangling)
    {
        var checkedIds = values.Where(v => v.Length > 0).ToList();
        var dangling = checkedIds.Where(v => !resolves(v)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        output.WriteLine($"{label,-21}: проверено {checkedIds.Count,4} разрешилось " +
                         $"{checkedIds.Count(v => resolves(v)),4} висячих {dangling.Length,3}" +
                         (dangling.Length > 0 ? $" -> {string.Join(", ", dangling)}" : ""));
        Assert.True(checkedIds.Count > 0, $"{label}: проверено 0 ссылок — связь не проверена");
        Assert.Equal(knownDangling.OrderBy(s => s, StringComparer.Ordinal).ToArray(), dangling);
    }

    /// <summary>
    /// Step 4. Каждое значение <c>GmodifL.TYPE</c> — известный <c>ModifierElementType</c>
    /// (<c>Globals/LmodifE.dbf</c>). Сам отказ на неизвестном значении переехал в загрузчик:
    /// задача 3 свела записи в <c>D2.Rules.Units</c>, и <c>ModifierLoader.ReadElement</c> разбирает
    /// <c>TYPE</c> на месте, бросая <c>InvalidDataException</c>. Здесь остаётся утверждение о
    /// распределении, ради которого тест и заводился.
    /// </summary>
    [SkippableFact]
    public void ModifierTypes_AreKnown()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var mods = LegacyModifierLoader.Load(Globals);
        var all = mods.Modifiers.Values.SelectMany(m => m.Elements)
            .Concat(mods.Orphans.Select(o => o.Element)).ToList();

        foreach (var g in all.GroupBy(e => e.Type).OrderBy(g => (int)g.Key))
            output.WriteLine($"TYPE {(int)g.Key,2} {g.Key,-14} строк {g.Count(),3}");
        Assert.Equal(187, all.Count);
        // Распределение из разведки плана — эталон; ошибка в любой строке видна поимённо.
        Assert.Equal(
            new Dictionary<ModifierElementType, int>
            {
                [ModifierElementType.ScoutingRange] = 1, [ModifierElementType.Leadership] = 7,
                [ModifierElementType.Power] = 16, [ModifierElementType.QtyDamage] = 29,
                [ModifierElementType.Armor] = 28, [ModifierElementType.Hp] = 5,
                [ModifierElementType.MoveAllowance] = 11, [ModifierElementType.Initiative] = 17,
                [ModifierElementType.MoveAbility] = 8, [ModifierElementType.LeaderAbility] = 23,
                [ModifierElementType.Immunity] = 37, [ModifierElementType.Regeneration] = 2,
                [ModifierElementType.AttackDrain] = 1, [ModifierElementType.FastRetreat] = 1,
                [ModifierElementType.LowerCost] = 1,
            },
            all.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count()));
    }

    /// <summary>
    /// «Не задано» и «задано нулём» в <c>GmodifL</c> — разные вещи (<c>ModifierValue.initialized</c>,
    /// <c>ummodifier.h</c>), и различие в данных настоящее: у <c>g000um0079</c> поле <c>IMMUNITY</c>
    /// равно нулю, у <c>g000um2109</c> нулю равно <c>MOVE</c>, а у остальных они пусты. Без этого
    /// теста <c>int</c> вместо <c>int?</c> прошёл бы незамеченным.
    /// </summary>
    [SkippableFact]
    public void ModifierValues_DistinguishUnsetFromZero()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var mods = LegacyModifierLoader.Load(Globals);

        var ward = Assert.Single(mods.Modifiers["g000um0001"].Elements);      // Air ward (H1)
        Assert.Equal(ModifierElementType.Immunity, ward.Type);
        Assert.Null(ward.Percent);
        Assert.Null(ward.Number);
        Assert.Equal(7, ward.Immunity);
        Assert.Equal(2, ward.ImmuneCat);

        var zeroImmunity = Assert.Single(mods.Modifiers["g000um0079"].Elements);
        Assert.Equal(0, zeroImmunity.Immunity);
        Assert.Null(zeroImmunity.Move);
        Assert.Equal(0, Assert.Single(mods.Modifiers["g000um2109"].Elements).Move);

        var counts = (Unset: 0, Zero: 0);
        foreach (var e in mods.Modifiers.Values.SelectMany(m => m.Elements))
        {
            foreach (var v in new[] { e.Percent, e.Number, e.Ability, e.Immunity, e.ImmuneCat, e.Move })
                if (v is null) counts.Unset++; else if (v == 0) counts.Zero++;
        }
        output.WriteLine($"GmodifL: полей «не задано» {counts.Unset}, полей «задано нулём» {counts.Zero}");
        Assert.True(counts.Zero > 0 && counts.Unset > 0, $"не задано {counts.Unset}, ноль {counts.Zero}");
    }

    /// <summary>Заклинания и предметы читаются целиком: казна разбирается, ссылки на месте.</summary>
    [SkippableFact]
    public void SpellsAndItems_LoadWithCosts()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var spells = LegacySpellLoader.Load(Globals);
        var items = LegacyItemLoader.Load(Globals);
        Assert.Equal(120, spells.Count);
        Assert.Equal(336, items.Count);

        var ward = spells["g000ss0001"];                      // g0000:r0000:y0100:e0000:w0000
        Assert.Equal((100L, 0L), (ward.CastingCost.Life, ward.CastingCost.Gold));
        Assert.Equal("g000um0001", ward.ModifierId);

        var potion = items["G000IG0002"];                     // зелье с модификатором
        Assert.Equal("g000um2002", potion.PotionModifierId);
        Assert.Equal(200L, potion.Value.Gold);
        output.WriteLine($"Gspells {spells.Count}, с MODIF_ID {spells.Values.Count(s => s.ModifierId.Length > 0)}; " +
                         $"GItem {items.Count}, MOD_EQUIP {items.Values.Count(i => i.EquipModifierId.Length > 0)}, " +
                         $"MOD_POTION {items.Values.Count(i => i.PotionModifierId.Length > 0)}");
    }

    /// <summary>Дочитанные колонки <c>Gattacks</c>/<c>Gunits</c> и четыре новые таблицы.
    /// <c>Load</c> прежней формы не тронут — Ф4/Ф5 не должны заметить задачу 1.</summary>
    [SkippableFact]
    public void Extras_LoadAlongsideGameRules()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var extras = LegacyRulesLoader.LoadExtras(Globals);
        Assert.Equal(350, extras.Attacks.Count);
        Assert.Equal(356, extras.Units.Count);
        Assert.Equal(227, extras.ImmunityBySource.Count);
        Assert.Equal(13, extras.ImmunityByClass.Count);
        Assert.Equal(502, extras.DynUpgrades.Count);
        Assert.Equal(43, extras.Transformations.Count);

        var withWards = extras.Attacks.Values.Count(a => a.Wards.Count > 0);
        output.WriteLine($"Gattacks: с вардами {withWards}, вардов всего {extras.Attacks.Values.Sum(a => a.Wards.Count)}; " +
                         $"Gunits: с постройкой улучшения {extras.Units.Values.Count(u => u.UpgradeBuildingId.Length > 0)}");
        Assert.Equal(3, withWards);
    }
}
