using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Battle;
using LastEdict.Rules.Battle.Effects;
using LastEdict.Rules.Map;
using LastEdict.Rules.Units;

namespace LastEdict.Data.Battle;

/// <summary>Содержимое (<see cref="ContentSet"/>: юниты, атаки, рост, модификаторы, глобальные величины) → <see cref="GameRules"/>.
/// Каталог — база содержимого или таблицы оригинала (<see cref="ContentSource.Open"/>). Ключи правил — id содержимого
/// (<c>base:unit/knight</c>, <see cref="ContentDef.Key"/>), словари регистронезависимы.</summary>
public static class RulesLoader
{
    public static GameRules Load(string globalsDir) => Load(ContentSource.Open(globalsDir));

    public static GameRules Load(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var effects = Effects(content);
        var attacks = new Dictionary<string, AttackType>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in content.Attacks)
        {
            var effect = a.Effect is { } id && effects.TryGetValue(id, out var e) ? e
                : a.Effect is { } legacy && legacy.StartsWith(LegacyClass, StringComparison.Ordinal) && int.TryParse(legacy[LegacyClass.Length..], out int cls)
                    ? AttackEffects.Of((AttackClass)cls)   // класс вне LattC — удар отказывает, правила грузятся
                    : throw new InvalidDataException($"атака «{a.Id}»: эффект «{a.Effect}» не найден среди эффектов содержимого");
            // Класс атаки — класс поведения эффекта: его номер пишут журналы и сохранения.
            attacks[a.Key] = new AttackType(a.Key, a.Initiative, effect.LegacyClass, a.Power, (int)a.Reach, a.Damage ?? 0, a.Heal ?? 0,
                a.CriticalHit ?? false, a.Level ?? 0, a.Infinite ?? false,
                // Источник (иммунитеты Gimmu), обереги (класс BestowWards кладёт их модификаторы на цель) и запасная атака
                // (вторая ветвь Doppelganger и TransformSelf — AttackType.HasAltAttack); «запасной нет» — пустая строка.
                (int)a.Source, Wards(content, a.Wards), content.KeyOf(a.AltAttack)) { Effect = effect };
        }

        var units = new Dictionary<string, UnitType>(StringComparer.OrdinalIgnoreCase);
        // Рост по уровню — и здесь, а не только в LoadExtras: без него не вырастить юнита уровнем выше своего типа (UnitGenerator).
        var growth = new Dictionary<string, GrowthRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in content.Units)
        {
            growth[u.Key] = new GrowthRef(content.KeyOf(u.Growth.Early), u.Growth.SwitchLevel, content.KeyOf(u.Growth.Late));
            units[u.Key] = new UnitType(u.Key, u.Level, u.HitPoints, u.Armor, content.KeyOf(u.Attack), content.KeyOf(u.SecondAttack),
                u.AttacksTwice ?? false, u.Small ?? false, u.XpForKill, u.XpForNextLevel,
                [.. u.Immunities.Select(i => new ImmunityGrant((int)i.Source, i.Level))],
                [.. u.ClassImmunities.Select(i => new ImmunityGrant((int)i.AttackClass, i.Level))],
                // В тип уровнем ниже превращает цель класс DrainLevel (getPrevUnitImplId движка); «нет» — пустая строка.
                content.KeyOf(u.PreviousLevel))
            {
                // Категория и «лидер» — для бегства ИИ (AiRetreat): категория лидера пуста у всех не-лидеров.
                Category = (int)u.Role,
                Branch = (int)u.Branch,
                IsLeader = u.LeaderCategory is not null,
                MageLeader = u.MageLeader ?? LegacyUnits.IsMageLeader(u.LegacyId),
                DamageBonus = u.DamageBonus ?? LegacyUnits.HasDamageBonus(u.LegacyId),
                Copyable = u.Copyable ?? LegacyUnits.IsCopyable(u.LegacyId),
                Auras = u.Auras.Count == 0 ? [] : [.. u.Auras.Select(a => new UnitAura(content.KeyOf(a.Modifier), a.Reach))],
            };
        }

        var battle = content.Globals.Battle;
        var tables = ModifierLoader.Load(content);
        return new GameRules(units, attacks, new RulesConstants(battle.InitiativeRandom, battle.DamageRandom,
                                                                DefendBonus: battle.DefendBonus,
                                                                BatBreak: battle.Break, BatBmodif: battle.BreakModifier,
                                                                BatBoostDamage: battle.BoostDamagePercent,
                                                                BatLowerDamage: battle.LowerDamagePercent,
                                                                BatLowerIni: battle.LowerInitiativePercent, BatRound: battle.Rounds),
                             Summons(content),
                             tables.Modifiers,
                             new UnitGenerator(growth, DynUpgrades(content)),
                             // Что из цепочки выдаётся уровнем, а что надевается руками — без этого различия нельзя понять,
                             // какой снимок .sg описывает бой.
                             tables.LeaderUpgrades.Select(u => u.ModifId).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Ссылка атаки на класс оригинала вне <c>LattC</c> (мод оригинала): <c>legacy:class/N</c>.</summary>
    private const string LegacyClass = ContentSet.LegacyPrefix + "class/";

    /// <summary>Эффекты содержимого по id: поведение из реестра (<see cref="AttackEffects.Find"/>) с данными сущности — числами ИИ и обликом.
    /// Поведения нет в реестре (мод с кодом не подключён) — отказ с именем.</summary>
    public static IReadOnlyDictionary<string, AttackEffect> Effects(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Effects.ToDictionary(d => d.Id, d => (AttackEffects.Find(d.Behavior) ?? throw new InvalidDataException(
            $"эффект «{d.Id}»: поведения «{d.Behavior}» нет в реестре (встроенные — {string.Join(", ", AttackEffects.Vanilla.Select(v => v.Behavior))})"))
            with { Id = d.Id, Tuning = d.Ai, Looks = d.Looks }, StringComparer.Ordinal);
    }

    /// <summary>Атака → юниты, в которых превращает или которых призывает (<c>Gtransf</c>): из них берёт кандидатов класс <c>Summon</c>
    /// (<c>fillAttackTransformIdList</c>, GOG <c>0x597b00</c>), им служат превращения.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Summons(ContentSet content) =>
        content.Attacks.Where(a => a.Units.Count > 0)
            .ToDictionary(a => a.Key, a => (IReadOnlyList<string>)[.. a.Units.Select(content.KeyOf)], StringComparer.OrdinalIgnoreCase);

    /// <summary>То, чего нет в <see cref="AttackType"/>: источник, запасная атака, обереги и имя.</summary>
    public sealed record AttackExtra(string Id, int Source, string AltAttackId, int QtyWards,
                                     IReadOnlyList<string> Wards, string NameTxt);

    /// <summary>То, чего нет в <see cref="UnitType"/>. <c>UpgradeBuildingId</c> — постройка (<c>g000bb…</c>), открывающая улучшение.
    /// Уровневый рост несут <c>DynUpg1Id</c> до уровня <c>DynUpgLevel</c> и дальше <c>DynUpg2Id</c>. <c>Category</c> — роль юнита
    /// (<see cref="UnitRole"/>: 0 солдат, 1 вор, 2 лидер, 4 призванный, 5 иллюзия, 8 страж); по ней отбираются получатели опыта.
    /// <c>LeaderCategory</c> — 1 воин, 2 следопыт, 3 маг, 4 жезлоносец, 5 вор; у не-лидера — 0. <c>Negotiate</c> — сопротивление
    /// отряда действию вора (<see cref="LastEdict.Rules.Map.Thieves.StackResistance"/>), растёт с уровнем шагами роста. <c>Subrace</c> —
    /// подраса (<see cref="Subrace"/>). <c>LifeTime</c> — сколько дней живёт призванный (у призывов 1, у иллюзий 3; у прочих 0 — срока
    /// нет).</summary>
    public sealed record UnitExtra(string Id, string UpgradeBuildingId, string DynUpg1Id, int DynUpgLevel,
                                   string DynUpg2Id, int Leadership, int Scout, int Move, int Regen, int Category,
                                   int LeaderCategory, int Negotiate, int Subrace = 0, string Race = "", bool WaterOnly = false,
                                   int LifeTime = 0);

    /// <summary>То, что правила читают поверх <see cref="GameRules"/>. Отдельной записью, потому что <c>GameRules</c> живёт в
    /// <c>LastEdict.Rules</c>.</summary>
    public sealed record RulesExtras(IReadOnlyDictionary<string, AttackExtra> Attacks,
                                     IReadOnlyDictionary<string, UnitExtra> Units,
                                     IReadOnlyDictionary<string, DynUpgrade> DynUpgrades);

    public static RulesExtras LoadExtras(string globalsDir) => LoadExtras(ContentSource.Open(globalsDir));

    public static RulesExtras LoadExtras(ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var attacks = new Dictionary<string, AttackExtra>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in content.Attacks)
            attacks[a.Key] = new AttackExtra(a.Key, (int)a.Source, content.KeyOf(a.AltAttack), a.WardCount ?? 0, Wards(content, a.Wards),
                                             a.NameText ?? "");

        var units = new Dictionary<string, UnitExtra>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in content.Units)
            units[u.Key] = new UnitExtra(u.Key, content.KeyOf(u.UpgradeBuilding), content.KeyOf(u.Growth.Early), u.Growth.SwitchLevel,
                content.KeyOf(u.Growth.Late), u.Leadership ?? 0, u.Scout ?? 0, u.Move ?? 0, u.Regeneration, (int)u.Role,
                (int?)u.LeaderCategory ?? 0, u.Negotiate ?? 0, (int)u.Subrace, content.KeyOf(u.Race),
                u.WaterOnly ?? false, u.LifetimeDays ?? 0);

        return new RulesExtras(attacks, units, DynUpgrades(content));
    }

    private static IReadOnlyDictionary<string, DynUpgrade> DynUpgrades(ContentSet content)
    {
        var upgrades = new Dictionary<string, DynUpgrade>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in content.Growth)
            upgrades[g.Key] = new DynUpgrade(g.Key, Prices.ToBank(g.HireCost), g.HitPoints, g.Armor ?? 0, g.Regeneration ?? 0,
                g.XpForKill, g.XpForNextLevel, g.Move ?? 0, g.Negotiate, g.Damage ?? 0, g.Heal ?? 0, g.Initiative ?? 0, g.Power ?? 0);
        return upgrades;
    }

    /// <summary>Пустая ссылка в таблицах и <c>.sg</c> оригинала — десять знаков; правила пишут «нет» пустой строкой.</summary>
    internal const string EmptyId = "g000000000";

    internal static string Ref(string id) =>
        id.Length == 0 || id.Equals(EmptyId, StringComparison.OrdinalIgnoreCase) ? "" : id;

    /// <summary>Обереги атаки или заклинания — ключи модификаторов по порядку.</summary>
    internal static IReadOnlyList<string> Wards(ContentSet content, IReadOnlyList<string> wards) => [.. wards.Select(content.KeyOf)];

    /// <summary>Регистр имени файла на диске непредсказуем (GVars.dbf/GVars.DBF) — ищем без учёта
    /// регистра, а не полагаемся на нечувствительность файловой системы (её нет на Linux).</summary>
    internal static string FindFile(string dir, string name) =>
        Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException($"{name} не найден в {dir}");
}
