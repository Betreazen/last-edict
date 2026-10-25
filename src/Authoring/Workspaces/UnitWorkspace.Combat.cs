using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Battle;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Форма атаки в Студии: эффект из реестра (урон, яд, ожог, паралич, окаменение, лечение, превращение…), источник, дальность и
/// цели, числа, уровень эффекта, «до конца боя», обереги (модификаторы, которые атака даёт цели) и кого призывает или в кого превращает.</summary>
public sealed record AttackForm
{
    public string Title { get; init; } = "";
    public string? Effect { get; init; }
    public AttackSource Source { get; init; }
    public ReachKind Reach { get; init; } = ReachKind.Adjacent;
    public int Initiative { get; init; } = 50;
    public int Accuracy { get; init; } = 80;
    public int? Damage { get; init; }
    public int? Heal { get; init; }
    public int? Level { get; init; }
    public bool Infinite { get; init; }
    public bool CriticalHit { get; init; }
    public IReadOnlyList<string> Wards { get; init; } = [];
    public IReadOnlyList<string> Units { get; init; } = [];
}

/// <summary>Бой юнита в «Юнитах» (Студия 2, задача 3): атака формой (своя копия при правке общей), иммунитеты и защиты, ауры, свой
/// модификатор (элементы — броня, урон, точность, инициатива, иммунитеты…). Каждая правка — одна команда отмены.</summary>
public sealed partial class UnitWorkspace
{
    /// <summary>Эффекты атак в содержимом (встроенные и модов).</summary>
    public IReadOnlyList<EffectDef> Effects => Content.Effects;

    /// <summary>Эффект урона содержимого (поведение <c>damage</c>) — у новой атаки по умолчанию.</summary>
    private string? DamageEffect => Content.Effects.FirstOrDefault(e => e.Behavior == "damage")?.Id;

    /// <summary>Модификаторы в содержимом (для оберегов и аур).</summary>
    public IReadOnlyList<ModifierDef> Modifiers => Content.Modifiers;

    /// <summary>Форма атаки юнита (<paramref name="second"/> — вторая атака); атаки нет — форма новой.</summary>
    public AttackForm AttackFormOf(string unitId, bool second = false)
    {
        if (Content.Find(unitId) is not UnitDef u || (second ? u.SecondAttack : u.Attack) is not { } id || Content.Find(id) is not AttackDef a)
            return new AttackForm { Effect = DamageEffect, Damage = 25 };
        return new AttackForm
        {
            Title = Session.NameOf(id), Effect = a.Effect, Source = a.Source, Reach = a.Reach, Initiative = a.Initiative, Accuracy = a.Power,
            Damage = a.Damage, Heal = a.Heal, Level = a.Level, Infinite = a.Infinite ?? false, CriticalHit = a.CriticalHit ?? false,
            Wards = a.Wards, Units = a.Units,
        };
    }

    /// <summary>Правка атаки юнита формой: своя атака, которой бьёт только он, — правкой; общая или базы — своей копией (юнит переходит на
    /// неё, другие не меняются); атаки нет — новая своя. Одна команда.</summary>
    public bool EditAttack(string unitId, AttackForm form, bool second = false)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (Content.Find(unitId) is not UnitDef unit) return Session.Refuse($"юнита «{unitId}» нет");
        if (form.Effect is { } e && Content.Find(e) is not EffectDef) return Session.Refuse($"эффекта «{e}» нет");
        var was = AttackFormOf(unitId, second);
        string field = second ? "secondAttack" : "attack";
        string? source = second ? unit.SecondAttack : unit.Attack;
        var (commands, attackId) = OwnAttack(unitId, source, field);
        // Атаки не было — новая пустая, пишется всё; копия общей или базы уже несёт поля основы — сравнение с ними (форма основы).
        if (source is null || Content.Find(source) is not AttackDef) was = new AttackForm { Initiative = -1, Accuracy = -1, Title = form.Title + "~" };
        void Set<T>(string path, T now, T then, Func<T, JsonNode?> json)
        {
            if (!EqualityComparer<T>.Default.Equals(now, then)) commands.Add(FormJson.Field(Session, attackId, path, json(now)));
        }
        Set("effect", form.Effect, was.Effect, v => v is null ? null : JsonValue.Create(v));
        Set("source", form.Source, was.Source, v => FormJson.Enum(v));
        Set("reach", form.Reach, was.Reach, v => FormJson.Enum(v));
        Set("initiative", form.Initiative, was.Initiative, v => JsonValue.Create(v));
        Set("power", form.Accuracy, was.Accuracy, v => JsonValue.Create(v));
        Set("damage", form.Damage, was.Damage, v => v is { } x ? JsonValue.Create(x) : null);
        Set("heal", form.Heal, was.Heal, v => v is { } x ? JsonValue.Create(x) : null);
        Set("level", form.Level, was.Level, v => v is { } x ? JsonValue.Create(x) : null);
        Set("infinite", form.Infinite, was.Infinite, v => v ? JsonValue.Create(true) : null);
        Set("criticalHit", form.CriticalHit, was.CriticalHit, v => v ? JsonValue.Create(true) : null);
        if (!form.Wards.SequenceEqual(was.Wards)) commands.Add(FormJson.Field(Session, attackId, "wards", Ids(form.Wards)));
        if (!form.Units.SequenceEqual(was.Units)) commands.Add(FormJson.Field(Session, attackId, "units", Ids(form.Units)));
        if (form.Title.Length > 0 && form.Title != was.Title) commands.Add(new SetText(attackId, Session.Language, "name", form.Title));
        if (commands.Count == 0) return true;
        return Session.Do(new Batch($"атака «{Session.NameOf(unitId)}»", commands));
    }

    /// <summary>Атака, которую можно править, не задевая других: своя и только этого юнита — она; иначе своя копия (у атаки нет — пустая
    /// новая), юнит переходит на неё. Команды подготовки и id атаки.</summary>
    private (List<IEditCommand> Commands, string AttackId) OwnAttack(string unitId, string? attackId, string field)
    {
        var commands = new List<IEditCommand>();
        // Своя и только этой атакой этого юнита (не другого и не второй атакой его же) — правится на месте.
        string otherField = field == "secondAttack" ? "attack" : "secondAttack";
        bool exclusive = attackId is not null && Session.IsMine(attackId)
                         && !Content.Units.Any(u => u.Id != unitId && (u.Attack == attackId || u.SecondAttack == attackId))
                         && (Content.Find(unitId) as UnitDef) is { } self && (otherField == "attack" ? self.Attack : self.SecondAttack) != attackId;
        if (exclusive) return (commands, attackId!);
        string name = EntityNames.Free(Session.History.Current, Session.Report.Content, "attack",
                                       unitId[(unitId.LastIndexOf('/') + 1)..] + (field == "secondAttack" ? "-2nd" : ""));
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("attack"), name);
        if (attackId is not null && Content.Find(attackId) is AttackDef attack) commands.AddRange(CopyAttack(attack, name));
        else commands.Add(new AddEntity("attack", name, new JsonObject
        {
            ["initiative"] = 50, ["source"] = "weapon", ["effect"] = DamageEffect, ["power"] = 80, ["reach"] = "adjacent",
        }));
        commands.Add(new SetField(unitId, field, JsonValue.Create(id)));
        return (commands, id);
    }

    private static JsonArray? Ids(IReadOnlyList<string> ids) =>
        ids.Count == 0 ? null : new JsonArray([.. ids.Select(i => (JsonNode)JsonValue.Create(i)!)]);

    /// <summary>Иммунитеты и защиты юнита: к источникам атак и к эффектам (уровень — один раз или всегда). Одна команда.</summary>
    public bool SetImmunities(string unitId, IReadOnlyList<SourceImmunity> sources, IReadOnlyList<ClassImmunity> classes)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(classes);
        if (Content.Find(unitId) is not UnitDef unit) return Session.Refuse($"юнита «{unitId}» нет");
        var commands = new List<IEditCommand>();
        // Порядок записи не важен: те же иммунитеты в другом порядке — не правка.
        if (!sources.ToHashSet().SetEquals(unit.Immunities)) commands.Add(FormJson.Field(Session, unitId, "immunities", Array(sources)));
        if (!classes.ToHashSet().SetEquals(unit.ClassImmunities)) commands.Add(FormJson.Field(Session, unitId, "classImmunities", Array(classes)));
        return commands.Count == 0 || Session.Do(new Batch($"иммунитеты «{Session.NameOf(unitId)}»", commands));
    }

    /// <summary>Ауры юнита (модификатор и кому). Одна команда.</summary>
    public bool SetAuras(string unitId, IReadOnlyList<UnitAuraDef> auras)
    {
        ArgumentNullException.ThrowIfNull(auras);
        if (Content.Find(unitId) is not UnitDef unit) return Session.Refuse($"юнита «{unitId}» нет");
        foreach (var a in auras)
        {
            if (Content.Find(a.Modifier) is not ModifierDef modifier) return Session.Refuse($"модификатора «{a.Modifier}» нет");
            if (AuraProblem(modifier) is { } problem) return Session.Refuse($"модификатор «{a.Modifier}» аурой не годится: {problem}");
        }
        if (auras.SequenceEqual(unit.Auras)) return true;
        return Session.Do(new Batch($"ауры «{Session.NameOf(unitId)}»", [FormJson.Field(Session, unitId, "auras", Array(auras))]));
    }

    /// <summary>Годится ли модификатор аурой: правила боя читают его элементы без отказа (<see cref="LastEdict.Rules.Units.EffectiveStats"/>:
    /// у брони — число, у урона — проценты…); нет — текст причины. Иначе битая аура роняла бы бой.</summary>
    private string? AuraProblem(ModifierDef modifier)
    {
        try
        {
            var rules = LastEdict.Data.Battle.ModifierLoader.Load(Content).Modifiers;
            if (!rules.TryGetValue(modifier.Key, out var m)) return "его нет в правилах";
            var probe = new LastEdict.Rules.Battle.UnitType("probe", 1, 100, 0, "", "", false, true, 0, 0);
            _ = LastEdict.Rules.Units.EffectiveStats.Of(probe, [m]);
            _ = LastEdict.Rules.Units.EffectiveStats.OfAttack(new LastEdict.Rules.Battle.AttackType("probe", 50, LastEdict.Rules.Battle.AttackClass.Damage,
                80, 3, 10, 0, false, 0), [m]);
            return null;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException)
        {
            return e.Message;
        }
    }

    /// <summary>Свой модификатор каталогом: элементы и подпись (комментарий). Возвращает id или null (отказ).</summary>
    public string? CreateModifier(string title, IReadOnlyList<ModifierElementDef> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        string name = EntityNames.Free(Session.History.Current, Session.Report.Content, "modifier", EntityNames.FromTitle(title, "modifier"));
        var body = new JsonObject { ["scope"] = "unit", ["elements"] = Array(elements) ?? [] };
        if (title.Length > 0) body["comment"] = title;
        return Session.Do(new AddEntity("modifier", name, body)) ? EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("modifier"), name) : null;
    }

    /// <summary>Простой элемент модификатора: число — в поле, которым его читают правила (броня и лидерство — единицами, урон, точность,
    /// инициатива, регенерация, ход, здоровье — процентами).</summary>
    public static ModifierElementDef Element(LastEdict.Rules.Units.ModifierElementType type, int value) =>
        type is LastEdict.Rules.Units.ModifierElementType.Armor or LastEdict.Rules.Units.ModifierElementType.Leadership
            or LastEdict.Rules.Units.ModifierElementType.ScoutingRange
            ? new ModifierElementDef { Type = type, Number = value }
            : new ModifierElementDef { Type = type, Percent = value };

    private static JsonArray? Array<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? null : JsonSerializer.SerializeToNode(items, ContentJson.Options) as JsonArray;
}
