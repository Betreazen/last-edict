using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using D2.Data.Battle;
using D2.Data.Content;
using D2.Data.Content.Schema;
using D2.Data.Dbf;
using D2.Rules.Battle.Effects;

namespace D2.Data.Tests;

/// <summary>Схема содержимого (<c>docs/content-schema.md</c>): импорт таблиц оригинала без потерь, запись и чтение мода, слои модов,
/// схемы JSON. Сверка «старая модель правил = новая» — <c>ContentRulesParityTests</c>.</summary>
public class ContentSchemaTests
{
    private static string Rules => Path.Combine(ImportedData.Dir, "rules");

    private static ContentSet Imported()
    {
        ImportedData.Require("rules/Gunits.json");
        return LegacyContent.Import(Rules);
    }

    /// <summary>Эффекты содержимого <c>base</c> — те же записи, что встроенные по умолчанию (id, числа ИИ, облик): атака из содержимого
    /// равна атаке с эффектом по классу.</summary>
    [SkippableFact]
    public void BaseEffects_EqualTheBuiltInOnes()
    {
        var effects = RulesLoader.Effects(Imported());
        Assert.Equal(AttackEffects.Vanilla.Count, effects.Count);
        foreach (var builtIn in AttackEffects.Vanilla) Assert.Equal(builtIn, effects[builtIn.Id]);
    }

    [SkippableFact]
    public void Import_EveryRowOfEveryKeyedTable_BecomesOneEntity()
    {
        var set = Imported();
        foreach (var kind in ContentSet.Kinds)
        {
            if (kind.Type.GetCustomAttribute<LegacyTableAttribute>()?.Table is not { } table) continue;   // эффекты — из кода
            int rows = GameTables.Read(Rules, table + ".dbf").Rows.Count;
            Assert.Equal(rows, set.Of(kind.Type).Count);
        }
        Assert.Equal("base:unit/knight", set.Units.Single(u => u.LegacyId == "g000uu0002").Id);
        Assert.Equal(23, set.Effects.Count);   // классы LattC — встроенные эффекты
        Assert.Equal("base:effect/damage", set.Attacks.Single(a => a.LegacyId == "g000aa0002").Effect);
        Assert.All(set.Units.Concat<ContentDef>(set.Attacks).Concat(set.Spells), d => Assert.StartsWith("base:", d.Id, StringComparison.Ordinal));
    }

    /// <summary>База содержимого в данных порта (<c>data/content/base</c>) и схемы (<c>data/content/schema</c>) — ровно то, что выпускает
    /// импорт таблиц сейчас: правка схемы или таблицы id без перевыпуска базы — провал (<c>dotnet run --project tools/RulesImport</c>).</summary>
    [SkippableFact]
    public void CommittedBase_IsTheImportOfTheTables()
    {
        ImportedData.Require("rules/Gunits.json", "content/base/units.json");
        var set = LegacyContent.Import(Rules);
        string dir = Path.Combine(ImportedData.Dir, "content");
        foreach (var kind in ContentSet.Kinds)
        {
            Assert.Equal(ContentJson.Write(kind, set.Of(kind.Type), $"../schema/{ContentFiles.SchemaFile(kind)}"),
                         File.ReadAllText(Path.Combine(dir, "base", kind.File)).ReplaceLineEndings("\n"));
            Assert.Equal(ContentFiles.Schema(kind).ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n",
                         File.ReadAllText(Path.Combine(dir, "schema", ContentFiles.SchemaFile(kind))).ReplaceLineEndings("\n"));
        }
    }

    /// <summary>Каждый столбец каждой таблицы чем-то прочитан: атрибутом записи или подчинённым списком. Новый столбец в таблицах
    /// (мод оригинала) без места в схеме — провал этого теста, а не тихая потеря.</summary>
    [SkippableFact]
    public void Schema_CoversEveryColumnOfEveryTable()
    {
        ImportedData.Require("rules/Gunits.json");
        var mapped = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string table, IEnumerable<string> columns)
        {
            if (!mapped.TryGetValue(table, out var set)) mapped[table] = set = new(StringComparer.OrdinalIgnoreCase);
            set.UnionWith(columns);
        }
        IEnumerable<string> Columns(Type type) => LegacyContent.Mapped(type)
            .SelectMany(m => m.Group ? Columns(m.Property.PropertyType) : m.Columns);
        foreach (var kind in ContentSet.Kinds)
        {
            if (kind.Type.GetCustomAttribute<LegacyTableAttribute>() is not { } table) continue;
            Add(table.Table, Columns(kind.Type).Append(table.Key).Where(c => c.Length > 0));
        }
        Add("GmodifL", Columns(typeof(ModifierElementDef)).Append("BELONGS_TO"));
        Add("Gimmu", ["UNIT_ID", "IMMUNITY", "IMMUNECAT"]);
        Add("GimmuC", ["UNIT_ID", "IMMUNITY", "IMMUNECAT"]);
        Add("GLabi", ["UNIT_ID", "L_ABILITY"]);
        Add("GMabi", ["UNIT_ID", "M_ABILITY"]);
        Add("Gtransf", ["ATTACK_ID", "TRANSF_ID"]);
        Add("Gattacks", ["CLASS"]);   // класс → эффект (AttackDef.Effect)
        Add("Tleader", ["RACE_ID", "SEX_M", "TEXT", "VERIFIED", "CONTEXT"]);
        Add("TAiMsg", ["RACE_ID", "MSG_CAT", "TEXT"]);
        Add("GbuiList", ["LORD_ID", "BUILD_ID"]);
        Add("GspellR", ["LORD_ID", "SPELL_ID", "RESEARCH"]);
        var missing = LegacyContent.Tables
            .SelectMany(t => GameTables.Read(Rules, t + ".dbf").Columns.Select(c => (Table: t, c.Name)))
            .Where(c => !mapped.TryGetValue(c.Table, out var set) || !set.Contains(c.Name))
            .Select(c => $"{c.Table}.{c.Name}")
            .ToList();
        Assert.True(missing.Count == 0, "столбцы без места в схеме: " + string.Join(", ", missing));
    }

    [SkippableFact]
    public void WrittenMod_ReadsBackToTheSameContent()
    {
        var set = Imported();
        string dir = Path.Combine(Path.GetTempPath(), "d2-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            ContentFiles.WriteMod(set, dir, "base", "Disciples II");
            var back = ContentLayers.Load(dir, []);
            foreach (var kind in ContentSet.Kinds)
                Assert.Equal(ContentJson.Write(kind, set.Of(kind.Type), null), ContentJson.Write(kind, back.Of(kind.Type), null));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [SkippableFact]
    public void Layers_PatchAddRemove_AndReportWhoOverrodeWhom()
    {
        var set = Imported();
        string root = Path.Combine(Path.GetTempPath(), "d2-layers-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseDir = Path.Combine(root, "base");
            ContentFiles.WriteMod(set, baseDir, "base", "Disciples II");
            string a = Mod(root, "a", """{ "id": "a" }""",
                """
                { "units": [ { "id": "base:unit/knight", "hitPoints": 999, "growth": { "switchLevel": 3 } },
                             { "id": "a:unit/halberdier", "role": "soldier", "level": 1, "hitPoints": 120, "attack": "base:attack/knight" } ] }
                """);
            string b = Mod(root, "b", """{ "id": "b", "dependencies": ["a"] }""",
                """{ "units": [ { "id": "base:unit/knight", "armor": 15 }, { "id": "base:unit/flame-caster", "$remove": true } ] }""");
            var mods = new[] { GameContent.ReadMod(b), GameContent.ReadMod(a) };   // b перечислен первым, но зависит от a

            var layered = ContentLayers.Load(baseDir, mods, out var report);

            var knight = layered.Units.Single(u => u.Id == "base:unit/knight");
            var original = set.Units.Single(u => u.Id == "base:unit/knight");
            Assert.Equal((999, 15, 3), (knight.HitPoints, knight.Armor, knight.Growth.SwitchLevel));
            Assert.Equal(original.Growth.Early, knight.Growth.Early);   // объект слит вглубь, а не заменён
            Assert.Equal("g000uu0002", knight.LegacyId);
            Assert.Null(layered.Find("base:unit/flame-caster"));
            Assert.Equal("a:unit/halberdier", layered.Units[^1].Id);
            Assert.Equal("a:unit/halberdier", layered.Units[^1].Key);   // своя сущность мода — ключ правил её id
            Assert.Equal(
                ["a patched base:unit/knight over base", "a added a:unit/halberdier over ", "b patched base:unit/knight over a",
                 "b removed base:unit/flame-caster over base"],
                report.Select(r => $"{r.Mod} {r.Action.ToString().ToLowerInvariant()} {r.Id} over {r.Over}"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Ошибки слоя называют мод, файл и сущность, а не создают пустышки и не ломают бой позже: правка несуществующей сущности
    /// (опечатка), удаление того, на что ссылаются, смена <c>legacyId</c>, <c>$remove</c> не логическим, повтор ключа правил.</summary>
    [SkippableTheory]
    [InlineData("""{ "units": [ { "id": "base:unit/knigt", "hitPoints": 180 } ] }""", "такой сущности нет")]
    [InlineData("""{ "units": [ { "id": "base:unit/squire-empire", "$remove": true } ] }""", "previousLevel")]
    [InlineData("""{ "units": [ { "id": "base:unit/knight", "legacyId": "g000uu0099" } ] }""", "legacyId правкой не меняется")]
    [InlineData("""{ "units": [ { "id": "base:unit/knight", "$remove": "true" } ] }""", "только true или false")]
    [InlineData("""{ "units": [ { "id": "bad:unit/twin", "role": "soldier", "legacyId": "G000UU0002" } ] }""", "один ключ правил")]
    [InlineData("""{ "units": [ { "id": "bad:unit/x", "attack": "base:unit/knight" } ] }""", "это unit, а нужен attack")]
    public void Layers_BadPatch_IsAnErrorWithItsSource(string units, string expected)
    {
        var set = Imported();
        string root = Path.Combine(Path.GetTempPath(), "d2-bad-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseDir = Path.Combine(root, "base");
            ContentFiles.WriteMod(set, baseDir, "base", "Disciples II");
            string bad = Mod(root, "bad", """{ "id": "bad" }""", units);
            var ex = Assert.Throws<InvalidDataException>(() => ContentLayers.Load(baseDir, [GameContent.ReadMod(bad)]));
            Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
            Assert.Contains("bad", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Мод оригинала с незнакомыми числами грузится, как у прежних загрузчиков: область модификатора 2 (в <c>LModifS</c> её нет)
    /// хранится числом и переживает запись в JSON; класс атаки 99 — ссылка <c>legacy:class/99</c>, правила грузятся, а удар отказывает.</summary>
    [SkippableFact]
    public void Import_KeepsUnknownNumbers_UnknownAttackClassFailsOnlyOnHit()
    {
        ImportedData.Require("rules/Gunits.json");
        using var mod = new ModRules();
        mod.Row("Gmodif", new Dictionary<string, object?> { ["MODIF_ID"] = "g999um0001", ["SOURCE"] = 2, ["COMMENTS"] = "mod" })
           .Row("Gattacks", new Dictionary<string, object?>
           {
               ["ATT_ID"] = "g999aa0001", ["NAME_TXT"] = "x", ["DESC_TXT"] = "x", ["INITIATIVE"] = 50, ["SOURCE"] = 0, ["CLASS"] = 99,
               ["POWER"] = 80, ["REACH"] = 2, ["ALT_ATTACK"] = "g000000000",
           });
        var set = LegacyContent.Import(mod.Dir);

        Assert.Equal((ModifierScope)2, set.Modifiers.Single(m => m.LegacyId == "g999um0001").Scope);
        string json = ContentJson.Write(ContentSet.KindNamed("modifier"), set.Modifiers, null);
        Assert.Contains("\"scope\": 2", json, StringComparison.Ordinal);
        Assert.Equal("legacy:class/99", set.Attacks.Single(a => a.LegacyId == "g999aa0001").Effect);
        var attack = D2.Data.Battle.RulesLoader.Load(set).Attacks["g999aa0001"];
        Assert.IsType<D2.Rules.Battle.Effects.UnknownEffect>(attack.Effect);
    }

    /// <summary>Эффект мода на встроенном поведении (этап Э2, задача D): своя сущность эффекта со своими числами ИИ и звуком, атака базы
    /// переведена на неё — правила берут поведение яда и данные мода. Поведения нет в реестре — ошибка с именем эффекта.</summary>
    [SkippableFact]
    public void ModEffect_ReachesTheAttack_UnknownBehaviorRefused()
    {
        var set = Imported();
        string root = Path.Combine(Path.GetTempPath(), "d2-effects-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseDir = Path.Combine(root, "base");
            ContentFiles.WriteMod(set, baseDir, "base", "Disciples II");
            var knight = set.Attacks.Single(a => a.Id == "base:attack/knight");
            string mod = Path.Combine(root, "acid");
            Directory.CreateDirectory(Path.Combine(mod, "content"));
            File.WriteAllText(Path.Combine(mod, "mod.json"), """{ "id": "acid" }""");
            File.WriteAllText(Path.Combine(mod, "content", "effects.json"),
                """{ "effects": [ { "id": "acid:effect/acid", "behavior": "poison", "ai": { "secondFactor": 2 }, "looks": { "sound": "ACID" } } ] }""");
            File.WriteAllText(Path.Combine(mod, "content", "attacks.json"),
                """{ "attacks": [ { "id": "base:attack/knight", "effect": "acid:effect/acid" } ] }""");

            var rules = D2.Data.Battle.RulesLoader.Load(ContentLayers.Load(baseDir, [GameContent.ReadMod(mod)]));
            var attack = rules.Attacks[knight.Key];
            Assert.Equal((D2.Rules.Battle.AttackClass.Poison, "acid:effect/acid", "ACID", 2.0),
                         (attack.Class, attack.Effect.Id, attack.Effect.Looks.Sound, attack.Effect.Tuning.SecondFactor));
            Assert.True(attack.Effect.IsDot);

            File.WriteAllText(Path.Combine(mod, "content", "effects.json"),
                """{ "effects": [ { "id": "acid:effect/acid", "behavior": "acidRain" } ] }""");
            var ex = Assert.Throws<InvalidDataException>(() => D2.Data.Battle.RulesLoader.Load(ContentLayers.Load(baseDir, [GameContent.ReadMod(mod)])));
            Assert.Contains("acidRain", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Признаки расы — данные (этап Э2, задача E): мод меняет Кланам землю, место в очереди и флаг событий и делает их нейтралами,
    /// а нейтралов — нет; снятый признак (<c>null</c> в патче) берётся по роду расы, как у оригинала.</summary>
    [SkippableFact]
    public void RaceTraits_ComeFromContent_MissingOnesFollowTheKind()
    {
        var set = Imported();
        string root = Path.Combine(Path.GetTempPath(), "d2-races-" + Guid.NewGuid().ToString("N"));
        try
        {
            string baseDir = Path.Combine(root, "base");
            ContentFiles.WriteMod(set, baseDir, "base", "Disciples II");
            string mod = Path.Combine(root, "lands");
            Directory.CreateDirectory(Path.Combine(mod, "content"));
            File.WriteAllText(Path.Combine(mod, "mod.json"), """{ "id": "lands" }""");
            File.WriteAllText(Path.Combine(mod, "content", "races.json"), """
                { "races": [
                  { "id": "base:race/mountain-clans", "land": "elf", "turnOrder": 9, "eventFlag": -1, "neutral": true },
                  { "id": "base:race/neutral", "neutral": false },
                  { "id": "base:race/empire", "land": null, "turnOrder": null } ] }
                """);
            var races = D2.Data.Map.EconomyLoader.Load(ContentLayers.Load(baseDir, [GameContent.ReadMod(mod)])).Races;
            string Key(string id) => set.Races.Single(r => r.Id == id).Key;
            var clans = races[Key("base:race/mountain-clans")];
            Assert.Equal((6, 9, -1, true), (clans.Terrain, clans.TurnOrder, clans.EventFlag, clans.Neutral));
            Assert.False(races[Key("base:race/neutral")].Neutral);
            var empire = races[Key("base:race/empire")];
            Assert.Equal((1, 0), (empire.Terrain, empire.TurnOrder));   // по роду human: L_HU, первая в очереди
            Assert.False(D2.Rules.Map.ScenarioEvents.RaceFlag([true, true, true, true, true, true], clans));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Цена с валютой мода (этап Э2, задача E): шесть валют оригинала — в поля казны, новая — в <see cref="D2.Rules.Map.Bank.Extra"/>,
    /// и обратно в цену без потерь; прежде такая цена была ошибкой данных.</summary>
    [Fact]
    public void Price_WithModCurrency_ReachesTheBankAndBack()
    {
        var price = new Dictionary<string, long> { ["gold"] = 50, ["life"] = 10, ["mymod:amber"] = 2 };
        var bank = Prices.ToBank(price);
        Assert.Equal((50L, 10L, 2L), (bank.Gold, bank.Life, bank["mymod:amber"]));
        Assert.Equal(price.OrderBy(p => p.Key), Prices.Of(bank).OrderBy(p => p.Key));
        Assert.Null(Prices.ToBank(new Dictionary<string, long> { ["gold"] = 5 }).Extra);
        var saved = System.Text.Json.JsonSerializer.Deserialize<D2.Rules.Map.Bank>(
            System.Text.Json.JsonSerializer.Serialize(bank, D2.Data.Play.SaveJson.Options), D2.Data.Play.SaveJson.Options)!;
        Assert.Equal(bank, saved);   // путь сохранения партии (SaveJson) — та же казна
    }

    [Fact]
    public void Layers_MissingDependencyOrCycle_IsAnError()
    {
        var a = new ModInfo("a", "a", "") { Dependencies = ["missing"] };
        Assert.Contains("missing", Assert.Throws<InvalidDataException>(() => ContentLayers.Order([a])).Message, StringComparison.Ordinal);
        var x = new ModInfo("x", "x", "") { LoadAfter = ["y"] };
        var y = new ModInfo("y", "y", "") { LoadAfter = ["x"] };
        Assert.Contains("по кругу", Assert.Throws<InvalidDataException>(() => ContentLayers.Order([x, y])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Schemas_DescribeEveryKind_WithFieldDescriptions()
    {
        foreach (var kind in ContentSet.Kinds)
        {
            var schema = ContentFiles.Schema(kind);
            var items = schema["properties"]![ContentJson.ArrayName(kind)]!["items"]!;
            Assert.NotNull(items["properties"]?["id"]);
        }
        var unit = ContentFiles.Schema(ContentSet.KindNamed("unit"))["properties"]!["units"]!["items"]!["properties"]!;
        Assert.Equal("Опыт до следующего уровня.", unit["xpForNextLevel"]!["description"]!.GetValue<string>());
        Assert.Contains("boostDamage", unit.ToJsonString(), StringComparison.Ordinal);   // перечни — строками
    }

    [Fact]
    public void UnknownField_InContent_IsAnError()
    {
        var kind = ContentSet.KindNamed("unit");
        var node = JsonNode.Parse("""{ "id": "x:unit/a", "hitPionts": 5 }""")!.AsObject();
        Assert.Contains("hitPionts", Assert.Throws<InvalidDataException>(() => ContentJson.ToDef(kind, node, "test")).Message,
                        StringComparison.Ordinal);
    }

    private static string Mod(string root, string id, string modJson, string units)
    {
        string dir = Path.Combine(root, id);
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        File.WriteAllText(Path.Combine(dir, "mod.json"), modJson);
        File.WriteAllText(Path.Combine(dir, "content", "units.json"), units);
        return dir;
    }
}
