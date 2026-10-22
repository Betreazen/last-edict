using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using D2.Data.Dbf;
using D2.Rules.Battle;
using D2.Rules.Battle.Effects;
using D2.Rules.Map;
using D2.Rules.Units;

using D2.Data.Content.Schema;

namespace D2.Data.Content;

/// <summary>
/// Импорт таблиц оригинала (<c>Globals/*.dbf</c> копии игры или их выгрузки <c>data/rules/*.json</c>) в содержимое. Столбцы и
/// ссылки — по атрибутам записей схемы (<see cref="LegacyAttribute"/>, <see cref="RefAttribute"/>, <see cref="LegacyGroupAttribute"/>),
/// подчинённые таблицы (иммунитеты, превращения, элементы модификаторов, имена героев, сообщения ИИ, постройки и исследования лордов) —
/// вложенными списками. Id сущностей — из таблицы <c>legacy-ids.json</c> (<c>tools/content_ids.py</c>), сущность без строки в ней —
/// <c>&lt;ns&gt;:&lt;вид&gt;/&lt;id оригинала&gt;</c>; ссылка на объект, которого нет среди импортированных, — <c>legacy:&lt;id&gt;</c>.
/// <para><b>Без потерь.</b> Пустая клетка остаётся «не задано» (<c>null</c>), а не нулём; пустая ссылка (<c>""</c>, <c>g000000000</c>) —
/// «нет». Отброшены только строки подчинённых таблиц без хозяина (7 строк <c>GmodifL</c>, 3 — <c>Gimmu</c>/<c>GLabi</c> на юнитов,
/// которых нет в <c>Gunits</c>): их правила и так не читали.</para>
/// </summary>
public static class LegacyContent
{
    private const string EmptyId = "g000000000";

    /// <summary>Таблицы, из которых собирается содержимое.</summary>
    public static readonly IReadOnlyList<string> Tables =
    [
        "Gunits", "GDynUpgr", "Gimmu", "GimmuC", "GLabi", "GMabi", "Gattacks", "Gtransf", "Gmodif", "GmodifL", "Gspells", "GItem", "Gbuild",
        "Grace", "Tleader", "TAiMsg", "GcityInf", "Glord", "GbuiList", "GspellR", "GleaUpg", "GLmark", "Gaction", "GAI", "GVars",
    ];

    /// <summary>Есть ли в каталоге таблицы оригинала (хотя бы <c>Gunits</c>).</summary>
    public static bool IsTablesDir(string dir) => GameTables.Has(dir, "Gunits.dbf");

    /// <summary>Содержимое из таблиц каталога <paramref name="dir"/> (<c>.dbf</c> или <c>.json</c> порта — как у <see cref="GameTables.Read"/>).</summary>
    public static ContentSet Import(string dir, string ns = "base")
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);
        var tables = Tables.ToDictionary(t => t, t => GameTables.Read(dir, t + ".dbf"), StringComparer.OrdinalIgnoreCase);
        return new Importer(tables, ns).Run();
    }

    private sealed class Importer(IReadOnlyDictionary<string, DbfTable> tables, string ns)
    {
        /// <summary>id оригинала (нижний регистр) → id содержимого, по видам.</summary>
        private readonly Dictionary<string, Dictionary<string, string>> _ids = [];

        public ContentSet Run()
        {
            AssignIds();
            var byKind = new Dictionary<Type, IReadOnlyList<ContentDef>>
            {
                [typeof(EffectDef)] = Effects(),
                [typeof(UnitDef)] = Units(),
                [typeof(GrowthDef)] = Keyed<GrowthDef>("growth"),
                [typeof(AttackDef)] = Attacks(),
                [typeof(ModifierDef)] = Modifiers(),
                [typeof(SpellDef)] = Keyed<SpellDef>("spell"),
                [typeof(ItemDef)] = Keyed<ItemDef>("item"),
                [typeof(BuildingDef)] = Keyed<BuildingDef>("building"),
                [typeof(RaceDef)] = Races(),
                [typeof(CityLevelDef)] = Cities(),
                [typeof(LordDef)] = Lords(),
                [typeof(LeaderSkillDef)] = LeaderSkills(),
                [typeof(LandmarkDef)] = Keyed<LandmarkDef>("landmark"),
                [typeof(ThiefActionDef)] = Keyed<ThiefActionDef>("thief-action"),
                [typeof(AiAttitudeDef)] = AiAttitudes(),
                [typeof(GlobalsDef)] = [Read<GlobalsDef>(Rows("GVars") is [var only] ? only
                    : throw new InvalidDataException($"GVars: строк {Rows("GVars").Count}, а глобальные величины — одна строка"), $"{ns}:globals", null)],
            };
            return new ContentSet(byKind);
        }

        private IReadOnlyList<DbfRow> Rows(string table) => tables[table].Rows;

        // ---- id

        private void AssignIds()
        {
            foreach (var kind in ContentSet.Kinds)
            {
                if (kind.Type.GetCustomAttribute<LegacyTableAttribute>() is not { Key.Length: > 0 } table) continue;
                var ids = _ids[kind.Name] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var taken = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in Rows(table.Table))
                {
                    string legacy = row.GetString(table.Key);
                    string id = ns == "base" && Known.TryGetValue(legacy.ToLowerInvariant(), out var known) && known.StartsWith($"base:{kind.Name}/", StringComparison.Ordinal)
                        ? known : $"{ns}:{kind.Name}/{legacy.ToLowerInvariant()}";
                    if (!ids.TryAdd(legacy, id))
                        throw new InvalidDataException($"{table.Table}: {table.Key} «{legacy}» встречается дважды");
                    if (!taken.Add(id)) throw new InvalidDataException($"{table.Table}: id «{id}» у двух строк");
                }
            }
        }

        private string IdOf(string kind, string legacy) => _ids[kind][legacy];

        /// <summary>Ссылка вида <paramref name="kind"/>: пусто — null, есть среди импортированных — её id, нет — <c>legacy:…</c>.</summary>
        private string? Ref(string kind, string legacy)
        {
            legacy = legacy.Trim();
            if (legacy.Length == 0 || legacy.Equals(EmptyId, StringComparison.OrdinalIgnoreCase)) return null;
            return _ids.TryGetValue(kind, out var ids) && ids.TryGetValue(legacy, out var id) ? id : ContentSet.LegacyPrefix + legacy;
        }

        private static readonly IReadOnlyDictionary<string, string> Known = LoadKnown();

        private static Dictionary<string, string> LoadKnown()
        {
            using var stream = typeof(LegacyContent).Assembly.GetManifestResourceStream("D2.Data.Content.Legacy.legacy-ids.json")
                               ?? throw new InvalidOperationException("нет ресурса legacy-ids.json");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }

        // ---- сущности

        private List<ContentDef> Keyed<T>(string kind) where T : ContentDef
        {
            var table = typeof(T).GetCustomAttribute<LegacyTableAttribute>()!;
            return [.. Rows(table.Table).Select(r => (ContentDef)Read<T>(r, IdOf(kind, r.GetString(table.Key)), r.GetString(table.Key)))];
        }

        private List<ContentDef> Units()
        {
            ILookup<string, DbfRow> By(string table) => Rows(table).ToLookup(r => r.GetString("UNIT_ID"), StringComparer.OrdinalIgnoreCase);
            var bySource = By("Gimmu");
            var byClass = By("GimmuC");
            var abilities = By("GLabi");
            var moves = By("GMabi");
            return [.. Rows("Gunits").Select(r =>
            {
                string legacy = r.GetString("UNIT_ID");
                return (ContentDef)(Read<UnitDef>(r, IdOf("unit", legacy), legacy) with
                {
                    Immunities = [.. bySource[legacy].Select(i => new SourceImmunity(EnumOf<AttackSource>(i, "IMMUNITY", $"Gimmu «{legacy}»"),
                                                                                       EnumOf<ImmunityLevel>(i, "IMMUNECAT", $"Gimmu «{legacy}»")))],
                    ClassImmunities = [.. byClass[legacy].Select(i => new ClassImmunity(EnumOf<AttackClass>(i, "IMMUNITY", $"GimmuC «{legacy}»"),
                                                                                       EnumOf<ImmunityLevel>(i, "IMMUNECAT", $"GimmuC «{legacy}»")))],
                    LeaderAbilities = [.. abilities[legacy].Select(a => EnumOf<LeaderAbility>(a, "L_ABILITY", $"GLabi «{legacy}»"))],
                    MoveAbilities = [.. moves[legacy].Select(a => EnumOf<MoveAbility>(a, "M_ABILITY", $"GMabi «{legacy}»"))],
                });
            })];
        }

        /// <summary>Эффекты оригинала — двадцать три встроенных поведения с числами ИИ и обликом движка (<see cref="AttackEffects.Vanilla"/>):
        /// в таблицах их нет, импорт выпускает их из кода, чтобы моды могли править данные эффекта. Id — <c>base:effect/boost-damage</c>.</summary>
        private List<ContentDef> Effects() =>
            [.. AttackEffects.Vanilla.Select(e => (ContentDef)new EffectDef { Id = EffectId(e), Behavior = e.Behavior, Ai = e.Tuning, Looks = e.Looks })];

        private string EffectId(AttackEffect effect) => AttackEffect.BuiltInId(effect.Behavior, ns);

        /// <summary>Эффект атаки по классу оригинала (<c>Gattacks.CLASS</c>). Класса нет в <c>LattC</c> (мод оригинала) — ссылка
        /// <c>legacy:class/N</c>: правила грузятся, а удар такой атакой отказывает с внятным текстом (<see cref="UnknownEffect"/>), как
        /// прежде.</summary>
        private string EffectOf(DbfRow row)
        {
            if (!row.Has("CLASS")) throw new InvalidDataException($"Gattacks «{row.GetString("ATT_ID")}»: в таблице нет столбца CLASS");
            int cls = row.GetInt("CLASS");
            var effect = AttackEffects.Of((AttackClass)cls);
            return effect is UnknownEffect ? $"{ContentSet.LegacyPrefix}class/{cls}" : EffectId(effect);
        }

        private List<ContentDef> Attacks()
        {
            var units = Rows("Gtransf")
                .Select(r => (Attack: r.GetString("ATTACK_ID"), Unit: Ref("unit", r.GetString("TRANSF_ID"))))
                .Where(t => Ref("attack", t.Attack) is not null && t.Unit is not null)
                .ToLookup(t => t.Attack, t => t.Unit!, StringComparer.OrdinalIgnoreCase);
            return [.. Rows("Gattacks").Select(r =>
            {
                string legacy = r.GetString("ATT_ID");
                return (ContentDef)(Read<AttackDef>(r, IdOf("attack", legacy), legacy) with { Effect = EffectOf(r), Units = [.. units[legacy]] });
            })];
        }

        private List<ContentDef> Modifiers()
        {
            var elements = Rows("GmodifL").ToLookup(r => r.GetString("BELONGS_TO"), StringComparer.OrdinalIgnoreCase);
            return [.. Rows("Gmodif").Select(r =>
            {
                string legacy = r.GetString("MODIF_ID");
                return (ContentDef)(Read<ModifierDef>(r, IdOf("modifier", legacy), legacy) with
                {
                    Elements = [.. elements[legacy].Select(e => ReadRecord<ModifierElementDef>(e, $"GmodifL «{legacy}»"))],
                });
            })];
        }

        private List<ContentDef> Races()
        {
            var names = Rows("Tleader").ToLookup(r => r.GetString("RACE_ID"), StringComparer.OrdinalIgnoreCase);
            var messages = Rows("TAiMsg").ToLookup(r => r.GetString("RACE_ID"), StringComparer.OrdinalIgnoreCase);
            return [.. Rows("Grace").Select(r =>
            {
                string legacy = r.GetString("RACE_ID");
                var race = Read<RaceDef>(r, IdOf("race", legacy), legacy);
                int kind = (int)race.Kind;
                return (ContentDef)(race with
                {
                    // Признаки рода движка (LegacyRaces) — явно, чтобы раса в данных была полной и мод мог их править.
                    Land = (TerrainKind)LegacyRaces.Terrain(kind), TurnOrder = LegacyRaces.TurnOrder(kind),
                    EventFlag = LegacyRaces.EventFlag(kind), Neutral = kind == LegacyRaces.Neutral,
                    HeroNames = [.. names[legacy].Select(n => new HeroName
                    {
                        Text = n.GetString("TEXT"), Male = n.GetBool("SEX_M"), Verified = Bool(n, "VERIFIED"), Context = Text(n, "CONTEXT"),
                    })],
                    AiMessages = [.. messages[legacy].Select(m => new AiMessageLine(EnumOf<AiMessageKind>(m, "MSG_CAT", $"TAiMsg «{legacy}»"), m.GetString("TEXT")))],
                });
            })];
        }

        private List<ContentDef> Cities()
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            return [.. Rows("GcityInf").Select(r =>
            {
                var city = Read<CityLevelDef>(r, "", null);
                string race = city.Race is { } id ? Tail(id) : "any";
                return (ContentDef)(city with { Id = Unique(taken, $"{ns}:city/{race}-{city.Size}") });
            })];
        }

        private List<ContentDef> Lords()
        {
            var buildings = Rows("GbuiList").ToLookup(r => r.GetString("LORD_ID"), StringComparer.OrdinalIgnoreCase);
            var research = Rows("GspellR").ToLookup(r => r.GetString("LORD_ID"), StringComparer.OrdinalIgnoreCase);
            return [.. Rows("Glord").Select(r =>
            {
                string legacy = r.GetString("LORD_ID");
                return (ContentDef)(Read<LordDef>(r, IdOf("lord", legacy), legacy) with
                {
                    Buildings = [.. buildings[legacy].Select(b => Ref("building", b.GetString("BUILD_ID")) ?? throw Missing("GbuiList", "BUILD_ID", legacy))],
                    Research = [.. research[legacy].Select(s => new SpellResearch
                    {
                        Spell = Ref("spell", s.GetString("SPELL_ID")) ?? throw Missing("GspellR", "SPELL_ID", legacy),
                        Cost = PriceOf(s.GetString("RESEARCH")),
                    })],
                });
            })];
        }

        private List<ContentDef> LeaderSkills()
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            return [.. Rows("GleaUpg").Select(r =>
            {
                var skill = Read<LeaderSkillDef>(r, "", null);
                string lord = skill.Lord is { } l ? Tail(l) : "any", modifier = skill.Modifier is { } m ? Tail(m) : "none";
                return (ContentDef)(skill with { Id = Unique(taken, $"{ns}:leader-skill/{lord}--{modifier}") });
            })];
        }

        private List<ContentDef> AiAttitudes() =>
            [.. Rows("GAI").Select(r =>
            {
                var attitude = Read<AiAttitudeDef>(r, "", null);
                return (ContentDef)(attitude with { Id = $"{ns}:ai/{JsonNamingPolicy.KebabCaseLower.ConvertName(attitude.Attitude.ToString())}" });
            })];

        private static string Tail(string id) =>
            id.StartsWith(ContentSet.LegacyPrefix, StringComparison.Ordinal) ? id[ContentSet.LegacyPrefix.Length..].ToLowerInvariant()
                : id[(id.IndexOf('/', StringComparison.Ordinal) + 1)..];

        private static string Unique(Dictionary<string, int> taken, string id)
        {
            int n = taken[id] = taken.GetValueOrDefault(id) + 1;
            return n == 1 ? id : $"{id}-{n}";
        }

        private static InvalidDataException Missing(string table, string column, string owner) =>
            new($"{table}: пустой {column} у «{owner}»");

        // ---- строка → запись по атрибутам

        private T Read<T>(DbfRow row, string id, string? legacy) where T : ContentDef
        {
            string table = TableOf(typeof(T));
            var def = ReadRecord<T>(row, $"{table} «{legacy ?? Owner(row, table)}»");
            return def with { Id = id, LegacyId = legacy };
        }

        private static readonly ConcurrentDictionary<Type, string> Tables = new();

        private static string TableOf(Type type) =>
            Tables.GetOrAdd(type, static t => t.GetCustomAttribute<LegacyTableAttribute>()?.Table ?? t.Name);

        /// <summary>Чья строка — для сообщений: первый столбец строки (у таблиц без id — ключ или раса).</summary>
        private string Owner(DbfRow row, string table) =>
            tables[table].Columns is [var first, ..] ? $"{first.Name} {row.GetString(first.Name)}" : "?";

        private T ReadRecord<T>(DbfRow row, string where) => (T)ReadRecord(typeof(T), row, where);

        private object ReadRecord(Type type, DbfRow row, string where)
        {
            object record = Activator.CreateInstance(type)!;
            foreach (var (property, columns, refKind, group, optional) in Mapped(type))
            {
                foreach (var column in columns)
                    if (!optional && !row.Has(column))
                        throw new InvalidDataException($"{where}: в таблице нет столбца {column}");
                object? value = group ? ReadRecord(property.PropertyType, row, where)
                    : columns.Count == 1 ? Cell(property.PropertyType, row, columns[0], refKind, where)
                    : List(property.PropertyType, row, columns, refKind, where);
                property.SetValue(record, value);
            }
            return record;
        }

        /// <summary>Перечни, значение вне которых прежние загрузчики правил отвергали (уровень иммунитета — <c>LImmune</c>, тип элемента
        /// модификатора — <c>LmodifE</c>, категории предмета и заклинания — <c>LmagItm</c>, <c>Lspell</c>): здесь так же. Прочие
        /// прежние загрузчики читали числом без проверки (мод оригинала с незнакомым значением грузился) — импорт сохраняет число как
        /// есть, а схема JSON пишет его числом.</summary>
        private static readonly HashSet<Type> StrictEnums =
            [typeof(ImmunityLevel), typeof(ModifierElementType), typeof(D2.Rules.Magic.ItemCategory), typeof(D2.Rules.Magic.SpellCategory)];

        private static object EnumValue(Type type, int n, string where, string column) =>
            !StrictEnums.Contains(type) || Enum.IsDefined(type, n) ? Enum.ToObject(type, n)
                : throw new InvalidDataException($"{where}: {column} = {n} — нет такого значения {type.Name}");

        private object? Cell(Type type, DbfRow row, string column, string? refKind, string where)
        {
            object? raw = row.Has(column) ? row[column] : null;
            var plain = Nullable.GetUnderlyingType(type);
            if (refKind is not null) return raw is null ? null : Ref(refKind, row.GetString(column));
            if (type == typeof(IReadOnlyDictionary<string, long>)) return raw is null ? null : PriceOf(row.GetString(column));
            if (type == typeof(string)) return raw is null ? null : Text(row, column);
            if (raw is null && plain is not null) return null;
            var target = plain ?? type;
            if (target == typeof(int)) return raw is null ? 0 : row.GetInt(column);
            if (target == typeof(bool)) return raw is not null && row.GetBool(column);
            if (target.IsEnum)
            {
                int n = raw is null ? 0 : row.GetInt(column);
                // Необязательный перечень: ноль вне перечня — «не задано» (так его и читали правила: GetInt, 0); моды оригинала
                // пишут 0 в незаполненный столбец (Gspells.AI_CAT).
                if (n == 0 && plain is not null && !Enum.IsDefined(target, 0)) return null;
                return EnumValue(target, n, where, column);
            }
            throw new NotSupportedException($"{where}.{column}: тип свойства {type.Name} импорт не знает");
        }

        private object List(Type type, DbfRow row, IReadOnlyList<string> columns, string? refKind, string where)
        {
            if (refKind is not null)
                return columns.Select(c => row.Has(c) ? Ref(refKind, row.GetString(c)) : null).OfType<string>().ToList();
            if (type == typeof(IReadOnlyList<int>)) return columns.Select(c => row.Has(c) ? row.GetInt(c) : 0).ToList();
            throw new NotSupportedException($"{where}: список типа {type.Name} импорт не знает");
        }

        private static string? Text(DbfRow row, string column)
        {
            if (!row.Has(column) || row[column] is null) return null;
            string text = row.GetString(column);
            return text.Length == 0 ? null : text;
        }

        private static bool? Bool(DbfRow row, string column) => row.Has(column) && row[column] is not null ? row.GetBool(column) : null;

        private static IReadOnlyDictionary<string, long>? PriceOf(string text)
        {
            var price = Prices.FromLegacy(text);
            return price.Count == 0 ? null : price;
        }

        private static T EnumOf<T>(DbfRow row, string column, string where) where T : struct, Enum =>
            (T)EnumValue(typeof(T), row.GetInt(column), where, column);
    }

    /// <summary>Свойства записи, которые читаются из таблицы: столбцы, вид ссылки, группа, необязательный ли столбец.</summary>
    internal static IReadOnlyList<(PropertyInfo Property, IReadOnlyList<string> Columns, string? Ref, bool Group, bool Optional)> Mapped(Type type) =>
        MappedCache.GetOrAdd(type, static t => [.. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Legacy: p.GetCustomAttribute<LegacyAttribute>(), Ref: p.GetCustomAttribute<RefAttribute>()?.Kind,
                          Group: p.GetCustomAttribute<LegacyGroupAttribute>() is not null))
            .Where(p => p.Legacy is not null || p.Group)
            .Select(p => (p.Property, p.Legacy?.Columns ?? [], p.Ref, p.Group, p.Legacy?.Optional ?? false))]);

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(PropertyInfo, IReadOnlyList<string>, string?, bool, bool)>> MappedCache = new();
}
