// Загрузчики правил ДО схемы содержимого (main 639961ee): правила читались прямо из строк таблиц оригинала. Эталон сверки
// «старая модель = новая» (ContentRulesParityTests) — не править; удаляется вместе с ней, когда таблицы уйдут из проверок.
using D2.Data.Dbf;
using D2.Rules.Battle;
using D2.Rules.Map;
using D2.Rules.Units;

namespace D2.Data.Tests.LegacyLoaders;

/// <summary>Gunits/Gattacks/GVars/Gimmu/GimmuC -> GameRules. Ключи регистронезависимы: в .dbf
/// нижний регистр, в корпусе — верхний.</summary>
public static class LegacyRulesLoader
{
    public static GameRules Load(string globalsDir)
    {
        var attacks = new Dictionary<string, AttackType>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gattacks.dbf").Rows)
        {
            var id = r.GetString("ATT_ID");
            // SOURCE и WARD1..4 дочитаны задачей 3: без источника не проверить Gimmu, а варды —
            // это модификаторы, которые кладёт на цель класс BestowWards. Задача 2 объявила оба
            // поля и оставила пустыми, потому что правка Load ей поручена не была.
            attacks[id] = new AttackType(id, r.GetInt("INITIATIVE"), (AttackClass)r.GetInt("CLASS"),
                r.GetInt("POWER"), r.GetInt("REACH"), r.GetInt("QTY_DAM"), r.GetInt("QTY_HEAL"),
                r.GetBool("CRIT_HIT"), r.GetInt("LEVEL"), r.GetBool("INFINITE"),
                // ALT_ATTACK дочитана задачей 8 по долгу задачи 7: на неё целиком уходит вторая
                // ветвь Doppelganger и TransformSelf (адреса и ранг — AttackType.HasAltAttack).
                // Ref() сводит "g000000000" к пустой строке, чтобы «запасной нет» не путалось с
                // висячей ссылкой; непустых в ванили ровно две.
                r.GetInt("SOURCE"), Wards(r), Ref(r.GetString("ALT_ATTACK")));
        }

        var bySource = ImmunityGrants(globalsDir, "Gimmu.dbf");
        var byClass = ImmunityGrants(globalsDir, "GimmuC.dbf");
        var units = new Dictionary<string, UnitType>(StringComparer.OrdinalIgnoreCase);
        // Рост по уровню читается здесь же, а не только в LoadExtras: без него не собрать
        // порождённые прокачкой реализации (UnitGenerator), а их кладёт в бой сам движок.
        var growth = new Dictionary<string, GrowthRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gunits.dbf").Rows)
        {
            var id = r.GetString("UNIT_ID");
            growth[id] = new GrowthRef(Ref(r.GetString("DYN_UPG1")), r.GetInt("DYN_UPG_LV"),
                                       Ref(r.GetString("DYN_UPG2")));
            units[id] = new UnitType(id, r.GetInt("LEVEL"), r.GetInt("HIT_POINT"), r.GetInt("ARMOR"),
                r.GetString("ATTACK_ID"), r.GetString("ATTACK2_ID"), r.GetBool("ATCK_TWICE"),
                r.GetBool("SIZE_SMALL"), r.GetInt("XP_KILLED"), r.GetInt("XP_NEXT"),
                bySource.TryGetValue(id, out var s) ? s : [],
                byClass.TryGetValue(id, out var c) ? c : [],
                // PREV_ID дочитан задачей 6: в этот тип превращает цель класс DrainLevel
                // (getPrevUnitImplId движка). Ref() сводит "g000000000" к пустой строке — иначе
                // «предыдущего уровня нет» неотличимо от висячей ссылки.
                Ref(r.GetString("PREV_ID")))
            {
                // Категория и «лидер» — для бегства ИИ (AiRetreat): LEADER_CAT пуст у всех не-лидеров.
                Category = r.GetInt("UNIT_CAT"),
                Branch = r.GetInt("BRANCH"),
                IsLeader = r.GetString("LEADER_CAT").Length > 0,
            };
        }

        var gvars = GameTables.Read(globalsDir, "GVars.dbf").Rows[0];
        var tables = LegacyModifierLoader.Load(globalsDir);
        // BATBOOSTD1..4, BATLOWERD1..2, BATLOWERI1 дочитаны задачей 5: проценты буста урона,
        // снижения урона и снижения инициативы (классы BoostDamage, LowerDamage, LowerInitiative).
        // Числа лежат в самой таблице игры (25/50/75/100, 50/33, 50) — ранг 1, а не константы из
        // чужого реверса, поэтому и читаются, а не зашиваются.
        return new GameRules(units, attacks, new RulesConstants(gvars.GetInt("BAT_INIT"), gvars.GetInt("BAT_DAMAGE"),
                                                                DefendBonus: gvars.GetInt("DFENDBONUS"),
                                                                BatBreak: gvars.GetInt("BAT_BREAK"), BatBmodif: gvars.GetInt("BAT_BMODIF"),
                                                                BatBoostDamage: [gvars.GetInt("BATBOOSTD1"), gvars.GetInt("BATBOOSTD2"),
                                                                                 gvars.GetInt("BATBOOSTD3"), gvars.GetInt("BATBOOSTD4")],
                                                                BatLowerDamage: [gvars.GetInt("BATLOWERD1"), gvars.GetInt("BATLOWERD2")],
                                                                BatLowerIni: gvars.GetInt("BATLOWERI1"), BatRound: gvars.GetInt("BAT_ROUND")),
                             Summons(globalsDir),
                             // Модификаторы дочитаны задачей 8: без них классу BestowWards нечего
                             // класть на цель — Gattacks.WARD1..4 несут только id строк Gmodif.
                             tables.Modifiers,
                             // CUnitGenerator: журнал зовёт прокачанного юнита порождённым id
                             // (G000UG****), которого в Gunits нет вовсе. Без генератора такой бой
                             // не грузится целиком.
                             new UnitGenerator(units.Keys, growth, DynUpgrades(globalsDir)),
                             // Задача 16: что из цепочки выдаётся уровнем, а что надевается руками —
                             // без этого различия нельзя понять, какой снимок .sg описывает бой.
                             tables.LeaderUpgrades.Select(u => u.ModifId)
                                   .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary><c>Gtransf.dbf</c>: ATTACK_ID → список TRANSF_ID. Дочитана задачей 6 — из неё берёт
    /// кандидатов класс <c>Summon</c> (<c>fillAttackTransformIdList</c>, GOG <c>0x597b00</c>).
    /// Таблица маленькая (43 живые строки, 28 атак) и служит ещё и превращениям задачи 7.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Summons(string globalsDir)
    {
        var byAttack = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gtransf.dbf").Rows)
        {
            var attackId = Ref(r.GetString("ATTACK_ID"));
            var unitId = Ref(r.GetString("TRANSF_ID"));
            if (attackId.Length == 0 || unitId.Length == 0) continue;
            if (!byAttack.TryGetValue(attackId, out var list)) byAttack[attackId] = list = [];
            list.Add(unitId);
        }
        return byAttack.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value,
                                     StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Колонки <c>Gattacks</c>, которых нет в <see cref="AttackType"/>: источник атаки
    /// (<c>SOURCE</c> — по нему проверяется иммунитет из <c>Gimmu</c>), альтернативная атака и варды.
    /// Варды — это модификаторы (<c>Gmodif</c>), их накладывает класс <c>BestowWards</c>.</summary>
    public sealed record AttackExtra(string Id, int Source, string AltAttackId, int QtyWards,
                                     IReadOnlyList<string> Wards, string NameTxt);

    /// <summary>Колонки <c>Gunits</c>, которых нет в <see cref="UnitType"/>. <c>UpgradeBuildingId</c> —
    /// это <c>Gbuild</c> (<c>g000bb…</c>), а не <c>GDynUpgr</c>: постройка, открывающая улучшение.
    /// Уровневый рост несут <c>DynUpg1Id</c> до уровня <c>DynUpgLevel</c> и дальше <c>DynUpg2Id</c>.
    /// Колонки <c>SOURCE</c> в <c>Gunits</c> нет вовсе (в плане Ф6 она названа ошибочно).
    /// <c>Category</c> — <c>UNIT_CAT</c> (<c>LunitC.dbf</c>: 0 <c>L_SOLDIER</c>, 1 <c>L_NOBLE</c> — вор, 2 <c>L_LEADER</c>,
    /// 4 <c>L_SUMMON</c>, 5 <c>L_ILLUSION</c>, 8 <c>L_GUARDIAN</c>); по ней отбираются получатели опыта (задача 4 Ф7).
    /// <c>LeaderCategory</c> — <c>LEADER_CAT</c> (<c>LleadC.dbf</c>: 1 <c>L_FIGHTER</c>, 2 <c>L_EXPLORER</c>,
    /// 3 <c>L_MAGE</c>, 4 <c>L_ROD</c>, 5 <c>L_NOBLE</c> — вор, задача 12 Ф7); у не-лидера столбец пуст и
    /// приходит нулём. <c>Negotiate</c> — <c>NEGOTIATE</c>, сопротивление отряда действию вора
    /// (<see cref="D2.Rules.Map.Thieves.StackResistance"/>); растёт с уровнем шагами
    /// <c>GDynUpgr.NEGOTIATE</c>. <c>Subrace</c> — <c>SUBRACE</c> типа (<c>LSubRace</c>: 1 Human … 5 Neutral … 14 Elf).
    /// <c>LifeTime</c> — <c>LIFE_TIME</c>: сколько дней живёт призванный (у призывов 1, у иллюзий 3; у прочих 0 — срока нет).</summary>
    public sealed record UnitExtra(string Id, string UpgradeBuildingId, string DynUpg1Id, int DynUpgLevel,
                                   string DynUpg2Id, int Leadership, int Scout, int Move, int Regen, int Category,
                                   int LeaderCategory, int Negotiate, int Subrace = 0, string Race = "", bool WaterOnly = false,
                                   int LifeTime = 0);

    /// <summary>Строка <c>Gimmu.dbf</c> (иммунитет к источнику атаки) или <c>GimmuC.dbf</c>
    /// (к классу атаки). Поля названы по колонкам <c>.dbf</c>, а смысл у них <b>обратный</b> тому,
    /// что говорил комментарий задачи 1: <c>Immunity</c> (колонка <c>IMMUNITY</c>) — это
    /// <b>категория</b> (источник <c>LattS</c> 0..7 в <c>Gimmu</c>, класс <c>LattC</c> в
    /// <c>GimmuC</c>), а <c>ImmuneCat</c> (колонка <c>IMMUNECAT</c>) — <b>уровень</b>
    /// <c>LImmune</c>: 1 Notimmune, 2 Once, 3 Always. Ранг 1, разбор — <see cref="ImmunityGrant"/>.</summary>
    public sealed record ImmunityRow(string UnitId, int Immunity, int ImmuneCat);

    /// <summary>Строка <c>Gtransf.dbf</c>: во что превращает атака (<c>TransformSelf</c>/<c>TransformOther</c>).</summary>
    public sealed record Transformation(string AttackId, string TransformUnitId);

    /// <summary>То, что Ф6 дочитывает поверх <see cref="GameRules"/>. Отдельной записью, а не полями
    /// <c>GameRules</c>, потому что <c>GameRules</c> живёт в <c>D2.Rules</c>: расширяет её задача 2,
    /// а задача 1 не трогает <c>D2.Rules</c> и не ломает бит в бит поведение Ф4/Ф5.</summary>
    public sealed record RulesExtras(IReadOnlyDictionary<string, AttackExtra> Attacks,
                                     IReadOnlyDictionary<string, UnitExtra> Units,
                                     IReadOnlyList<ImmunityRow> ImmunityBySource,
                                     IReadOnlyList<ImmunityRow> ImmunityByClass,
                                     IReadOnlyDictionary<string, DynUpgrade> DynUpgrades,
                                     IReadOnlyList<Transformation> Transformations);

    public static RulesExtras LoadExtras(string globalsDir)
    {
        var attacks = new Dictionary<string, AttackExtra>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gattacks.dbf").Rows)
        {
            var id = r.GetString("ATT_ID");
            attacks[id] = new AttackExtra(id, r.GetInt("SOURCE"), Ref(r.GetString("ALT_ATTACK")),
                r.GetInt("QTY_WARDS"), Wards(r), r.GetString("NAME_TXT"));
        }

        var units = new Dictionary<string, UnitExtra>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gunits.dbf").Rows)
        {
            var id = r.GetString("UNIT_ID");
            units[id] = new UnitExtra(id, Ref(r.GetString("UPGRADE_B")), Ref(r.GetString("DYN_UPG1")),
                r.GetInt("DYN_UPG_LV"), Ref(r.GetString("DYN_UPG2")), r.GetInt("LEADERSHIP"),
                r.GetInt("SCOUT"), r.GetInt("MOVE"), r.GetInt("REGEN"), r.GetInt("UNIT_CAT"),
                r.GetInt("LEADER_CAT"), r.GetInt("NEGOTIATE"), r.GetInt("SUBRACE"), r.GetString("RACE_ID").ToUpperInvariant(),
                r.GetBool("WATER_ONLY"), r.GetInt("LIFE_TIME"));
        }

        return new RulesExtras(attacks, units, Immunities(globalsDir, "Gimmu.dbf"),
            Immunities(globalsDir, "GimmuC.dbf"), DynUpgrades(globalsDir),
            GameTables.Read(globalsDir, "Gtransf.dbf").Rows
                .Select(r => new Transformation(r.GetString("ATTACK_ID"), r.GetString("TRANSF_ID"))).ToList());
    }

    /// <summary><c>Gimmu.dbf</c>/<c>GimmuC.dbf</c> -> иммунитеты по юниту, в форме, которую понимают
    /// правила. Строки на юнитов, которых нет в <c>Gunits</c> (<c>g000uu0000</c>, <c>g000uu5167</c>),
    /// сюда попадают, но <see cref="Load"/> их не спрашивает — они просто ни к чему не привязаны.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<ImmunityGrant>> ImmunityGrants(
        string globalsDir, string file)
    {
        var byUnit = new Dictionary<string, List<ImmunityGrant>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Immunities(globalsDir, file))
        {
            if (!Enum.IsDefined(typeof(ImmunityLevel), row.ImmuneCat))
                throw new InvalidDataException(
                    $"{file}: у юнита \"{row.UnitId}\" IMMUNECAT {row.ImmuneCat}, которого нет в Globals/LImmune.dbf");
            if (!byUnit.TryGetValue(row.UnitId, out var list)) byUnit[row.UnitId] = list = [];
            list.Add(new ImmunityGrant(row.Immunity, (ImmunityLevel)row.ImmuneCat));
        }
        return byUnit.ToDictionary(p => p.Key, p => (IReadOnlyList<ImmunityGrant>)p.Value,
                                   StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ImmunityRow> Immunities(string globalsDir, string file) =>
        GameTables.Read(globalsDir, file).Rows
            .Select(r => new ImmunityRow(r.GetString("UNIT_ID"), r.GetInt("IMMUNITY"), r.GetInt("IMMUNECAT")))
            .ToList();

    private static IReadOnlyDictionary<string, DynUpgrade> DynUpgrades(string globalsDir)
    {
        var upgrades = new Dictionary<string, DynUpgrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "GDynUpgr.dbf").Rows)
        {
            var id = r.GetString("UPGRADE_ID");
            if (!upgrades.TryAdd(id, new DynUpgrade(id, Bank.Parse(r.GetString("ENROLL_C")),
                    r.GetInt("HIT_POINT"), r.GetInt("ARMOR"), r.GetInt("REGEN"), r.GetInt("XP_KILLED"),
                    r.GetInt("XP_NEXT"), r.GetInt("MOVE"), r.GetInt("NEGOTIATE"), r.GetInt("DAMAGE"),
                    r.GetInt("HEAL"), r.GetInt("INITIATIVE"), r.GetInt("POWER"))))
                throw new InvalidDataException($"GDynUpgr.DBF: UPGRADE_ID \"{id}\" встречается дважды");
        }
        return upgrades;
    }

    /// <summary>Пустая ссылка в <c>.dbf</c> — те же десять знаков, что и в <c>.sg</c>; приводится
    /// к пустой строке, чтобы «ссылки нет» проверялось одним способом (как в <c>LegacyEconomyLoader</c>).</summary>
    internal const string EmptyId = "g000000000";

    internal static string Ref(string id) =>
        id.Length == 0 || id.Equals(EmptyId, StringComparison.OrdinalIgnoreCase) ? "" : id;

    /// <summary>Колонки <c>WARD1..WARD4</c> (одинаковы в <c>Gattacks</c> и <c>Gspells</c>) —
    /// список модификаторов без пустых.</summary>
    internal static IReadOnlyList<string> Wards(DbfRow r) =>
        new[] { "WARD1", "WARD2", "WARD3", "WARD4" }
            .Select(c => Ref(r.GetString(c))).Where(w => w.Length > 0).ToList();

    /// <summary>Регистр имени файла на диске непредсказуем (GVars.dbf/GVars.DBF) — ищем без учёта
    /// регистра, а не полагаемся на нечувствительность файловой системы (её нет на Linux).</summary>
    internal static string FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException($"{name} не найден в {dir}");
}
