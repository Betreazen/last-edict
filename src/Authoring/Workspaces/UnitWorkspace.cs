using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Форма юнита в Студии: характеристики юнита и его атаки, найм, повышение, графика. Числа атаки (инициатива, точность, урон,
/// лечение) — у своей атаки юнита: правка общей атаки базы создаёт юниту свою копию, другие юниты базы не меняются.</summary>
public sealed record UnitForm
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public UnitRole Role { get; init; }
    public string? Race { get; init; }
    public BranchKind Branch { get; init; }
    public int Level { get; init; } = 1;
    public int HitPoints { get; init; } = 100;
    public int Armor { get; init; }
    public int Regeneration { get; init; }
    public int XpForKill { get; init; }
    public int XpForNextLevel { get; init; }
    public int? Leadership { get; init; }
    public int? Move { get; init; }
    public bool Small { get; init; } = true;
    public IReadOnlyDictionary<string, long> HireCost { get; init; } = Prices.Free;
    /// <summary>В списке найма расы (солдат — столица и города, лидер — лидеры столицы).</summary>
    public bool Hireable { get; init; }
    public string? HireBuilding { get; init; }
    /// <summary>Повышение: из кого вырастает этот юнит и какая постройка это открывает.</summary>
    public string? PreviousLevel { get; init; }
    public string? UpgradeBuilding { get; init; }
    /// <summary>Чья графика (анимации боя, значок карты; портрет и лицо — свои, если выбраны картинки).</summary>
    public string? GraphicsFrom { get; init; }
    public string? Attack { get; init; }
    public int Initiative { get; init; }
    public int Accuracy { get; init; }
    public int? Damage { get; init; }
    public int? Heal { get; init; }
}

/// <summary>
/// Рабочее пространство Студии «Юниты» (Студия 2, задача 2): новый юнит «на основе существующего» — копия с его атакой (своей, чтобы правка
/// чисел не задела базу), графика базового (анимации — ссылкой <c>baseUnit</c>), свои портрет, лицо и картина — PNG в каталоге сущности
/// (<see cref="EntityArt"/>); правка формой, найм (список расы и постройка), повышение (из кого и чьей постройкой), переименование и
/// удаление своего. Каждая правка — одна команда отмены.
/// </summary>
public sealed class UnitWorkspace
{
    public UnitWorkspace(StudioSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        session.Refreshed += () => Revision++;
    }

    public StudioSession Session { get; }

    public int Revision { get; private set; }

    public string? Selected { get; private set; }

    /// <summary>Фильтр списка: раса (id; пусто — все) и текст.</summary>
    public string RaceFilter { get; private set; } = "";

    public string Filter { get; private set; } = "";

    private ContentSet Content => Session.Report.Content ?? Session.BaseContent;

    public void Select(string? id)
    {
        Selected = id;
        Revision++;
    }

    public void Find(string race, string text)
    {
        (RaceFilter, Filter) = (race ?? "", text ?? "");
        Revision++;
    }

    /// <summary>Юниты по фильтру: свои — первыми.</summary>
    public IReadOnlyList<EntityRow> Units() =>
        [.. Content.Units
            .Where(u => RaceFilter.Length == 0 || u.Race == RaceFilter)
            .Select(u => new EntityRow(u.Id, Session.NameOf(u.Id),
                Session.IsMine(u.Id) ? EntityOrigin.Mine : Session.History.Current.Find(u.Id) is not null ? EntityOrigin.Patched : EntityOrigin.Base))
            .Where(r => Filter.Length == 0 || r.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Origin == EntityOrigin.Mine ? 0 : 1)];

    public IReadOnlyList<RaceDef> Races => Content.Of<RaceDef>();

    public IReadOnlyList<BuildingDef> Buildings => Content.Buildings;

    public bool IsOwnFolder(string id) => Session.IsMine(id) && Session.History.Current.Find(id) is { Source: EntitySource.Folder };

    /// <summary>Картинка юнита из документа (<c>face.png</c>…); нет — null.</summary>
    public ImmutableArray<byte>? PictureOf(string id, string file) =>
        Session.History.Current.Find(id)?.Files.TryGetValue(file, out var bytes) == true ? bytes : null;

    // ---- форма

    public UnitForm FormOf(string id)
    {
        if (Content.Find(id) is not UnitDef u) return new UnitForm();
        var attack = u.Attack is { } a ? Content.Find(a) as AttackDef : null;
        var race = u.Race is { } r ? Content.Find(r) as RaceDef : null;
        return new UnitForm
        {
            Title = Session.NameOf(id), Description = Text(id, "description", u.DescriptionText), Role = u.Role, Race = u.Race, Branch = u.Branch,
            Level = u.Level, HitPoints = u.HitPoints, Armor = u.Armor, Regeneration = u.Regeneration, XpForKill = u.XpForKill,
            XpForNextLevel = u.XpForNextLevel, Leadership = u.Leadership, Move = u.Move, Small = u.Small ?? true, HireCost = u.HireCost ?? Prices.Free,
            Hireable = race is not null && (race.Soldiers.Contains(id) || race.Leaders.Contains(id)), HireBuilding = u.HireBuilding,
            PreviousLevel = u.PreviousLevel, UpgradeBuilding = u.UpgradeBuilding, GraphicsFrom = u.BaseUnit, Attack = u.Attack,
            Initiative = attack?.Initiative ?? 0, Accuracy = attack?.Power ?? 0, Damage = attack?.Damage, Heal = attack?.Heal,
        };
    }

    private string Text(string id, string key, string? textKey)
    {
        if (Session.History.Current.Find(id) is { Source: EntitySource.Folder } own
            && (own.Strings.GetValueOrDefault(Session.Language) ?? own.Strings.GetValueOrDefault("en")) is { } map && map.TryGetValue(key, out var t))
            return t;
        return textKey is not null ? Session.Text(textKey) ?? "" : "";
    }

    public string NameFor(string title) =>
        EntityNames.Free(Session.History.Current, Session.Report.Content, "unit", EntityNames.FromTitle(title, "unit"));

    /// <summary>Новый юнит мода на основе <paramref name="source"/>: копия его записи (без id оригинала), графика — его (<c>baseUnit</c>), атака —
    /// своя копия его атаки, название и описание — строками мода (описание — как у основы, пока автор не поправит). Одна команда.</summary>
    public bool CreateFrom(string source, string name, string title)
    {
        if (Content.Find(source) is not UnitDef from) return Session.Refuse($"юнита «{source}» нет");
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("unit"), name);
        var body = Node(from);
        foreach (string drop in new[] { "id", "legacyId", "nameText", "descriptionText", "abilityText" }) body.Remove(drop);
        body["baseUnit"] = from.BaseUnit ?? from.Id;
        var commands = new List<IEditCommand>();
        if (from.Attack is { } attack && Content.Find(attack) is AttackDef a)
        {
            string attackName = EntityNames.Free(Session.History.Current, Session.Report.Content, "attack", name);
            commands.AddRange(CopyAttack(a, attackName));
            body["attack"] = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("attack"), attackName);
        }
        commands.Insert(0, new AddEntity("unit", name, body));
        if (title.Length > 0) commands.Add(new SetText(id, Session.Language, "name", title));
        if (Session.Text(from.DescriptionText ?? "") is { Length: > 0 } description) commands.Add(new SetText(id, Session.Language, "description", description));
        if (!Session.Do(new Batch($"новый юнит «{(title.Length > 0 ? title : name)}» на основе «{Session.NameOf(source)}»", commands))) return false;
        Select(id);
        return true;
    }

    /// <summary>Своя копия атаки каталогом: запись без id оригинала, имя — строкой мода.</summary>
    private IEnumerable<IEditCommand> CopyAttack(AttackDef attack, string name)
    {
        var body = Node(attack);
        foreach (string drop in new[] { "id", "legacyId", "nameText", "descriptionText" }) body.Remove(drop);
        yield return new AddEntity("attack", name, body);
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("attack"), name);
        if (Session.Text(attack.NameText ?? "") is { Length: > 0 } title) yield return new SetText(id, Session.Language, "name", title);
    }

    private static JsonObject Node(ContentDef def) => (JsonObject)JsonSerializer.SerializeToNode(def, def.GetType(), ContentJson.Options)!;

    /// <summary>Правка юнита по форме: изменившиеся поля — правками слоя; числа атаки — у своей атаки (общая атака базы — сперва своя копия);
    /// найм — список расы. Одна команда.</summary>
    public bool Edit(string id, UnitForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (Content.Find(id) is not UnitDef unit) return Session.Refuse($"юнита «{id}» нет");
        var was = FormOf(id);
        var commands = new List<IEditCommand>();
        void Set<T>(string path, T now, T then, Func<T, JsonNode?> json)
        {
            if (!EqualityComparer<T>.Default.Equals(now, then)) commands.Add(FormJson.Field(Session, id, path, json(now)));
        }
        Set("role", form.Role, was.Role, v => FormJson.Enum(v));
        Set("race", form.Race, was.Race, v => v is null ? null : JsonValue.Create(v));
        Set("branch", form.Branch, was.Branch, v => FormJson.Enum(v));
        Set("level", form.Level, was.Level, v => JsonValue.Create(v));
        Set("hitPoints", form.HitPoints, was.HitPoints, v => JsonValue.Create(v));
        Set("armor", form.Armor, was.Armor, v => JsonValue.Create(v));
        Set("regeneration", form.Regeneration, was.Regeneration, v => JsonValue.Create(v));
        Set("xpForKill", form.XpForKill, was.XpForKill, v => JsonValue.Create(v));
        Set("xpForNextLevel", form.XpForNextLevel, was.XpForNextLevel, v => JsonValue.Create(v));
        Set("leadership", form.Leadership, was.Leadership, v => v is { } x ? JsonValue.Create(x) : null);
        Set("move", form.Move, was.Move, v => v is { } x ? JsonValue.Create(x) : null);
        Set("small", form.Small, was.Small, v => JsonValue.Create(v));
        Set("hireBuilding", form.HireBuilding, was.HireBuilding, v => v is null ? null : JsonValue.Create(v));
        Set("previousLevel", form.PreviousLevel, was.PreviousLevel, v => v is null ? null : JsonValue.Create(v));
        Set("upgradeBuilding", form.UpgradeBuilding, was.UpgradeBuilding, v => v is null ? null : JsonValue.Create(v));
        Set("baseUnit", form.GraphicsFrom, was.GraphicsFrom, v => v is null ? null : JsonValue.Create(v));
        if (!FormJson.SameCost(form.HireCost, was.HireCost)) commands.Add(FormJson.Field(Session, id, "hireCost", FormJson.Cost(form.HireCost)));
        if (IsOwnFolder(id))
        {
            if (form.Title != was.Title) commands.Add(new SetText(id, Session.Language, "name", form.Title.Length > 0 ? form.Title : null));
            if (form.Description != was.Description) commands.Add(new SetText(id, Session.Language, "description", form.Description.Length > 0 ? form.Description : null));
        }
        if (form.Hireable != was.Hireable) commands.AddRange(HireList(id, unit, form.Hireable, form.Race ?? unit.Race));
        if (form.Attack != was.Attack) commands.Add(FormJson.Field(Session, id, "attack", form.Attack is null ? null : JsonValue.Create(form.Attack)));
        else if (form.Initiative != was.Initiative || form.Accuracy != was.Accuracy || form.Damage != was.Damage || form.Heal != was.Heal)
            commands.AddRange(AttackNumbers(id, unit, form, was));
        if (commands.Count == 0) return true;
        return Session.Do(new Batch($"юнит «{Session.NameOf(id)}»", commands));
    }

    /// <summary>Числа атаки: своя атака — правкой, атака базы — своей копией (юнит переходит на неё).</summary>
    private IEnumerable<IEditCommand> AttackNumbers(string id, UnitDef unit, UnitForm form, UnitForm was)
    {
        if (unit.Attack is not { } attackId || Content.Find(attackId) is not AttackDef attack) yield break;
        if (!Session.IsMine(attackId))
        {
            string name = EntityNames.Free(Session.History.Current, Session.Report.Content, "attack", id[(id.LastIndexOf('/') + 1)..]);
            foreach (var c in CopyAttack(attack, name)) yield return c;
            attackId = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("attack"), name);
            yield return new SetField(id, "attack", JsonValue.Create(attackId));
        }
        if (form.Initiative != was.Initiative) yield return new SetField(attackId, "initiative", JsonValue.Create(form.Initiative));
        if (form.Accuracy != was.Accuracy) yield return new SetField(attackId, "power", JsonValue.Create(form.Accuracy));
        if (form.Damage != was.Damage) yield return FormJson.Field(Session, attackId, "damage", form.Damage is { } d ? JsonValue.Create(d) : null);
        if (form.Heal != was.Heal) yield return FormJson.Field(Session, attackId, "heal", form.Heal is { } h ? JsonValue.Create(h) : null);
    }

    /// <summary>Найм: юнит в списке расы (лидер — лидеров, прочие — солдат) или вне его.</summary>
    private IEnumerable<IEditCommand> HireList(string id, UnitDef unit, bool hire, string? raceId)
    {
        if (raceId is null || Content.Find(raceId) is not RaceDef race) yield break;
        bool leader = unit.Role == UnitRole.Leader;
        var list = (leader ? race.Leaders : race.Soldiers).Where(u => u != id).ToList();
        if (hire) list.Add(id);
        string path = leader ? "leaders" : "soldiers";
        var node = new JsonArray([.. list.Select(u => (JsonNode)JsonValue.Create(u)!)]);
        bool asBase = ModReferences.BaseValue(Session, race.Id, path) is { } b && JsonNode.DeepEquals(b, node);
        yield return asBase ? new SetField(race.Id, path, null, Unset: true) : new SetField(race.Id, path, node);
    }

    /// <summary>Картинка своего юнита (<c>face.png</c>, <c>portrait.png</c>, <c>picture.png</c> — <see cref="EntityArt"/>), уже вписанная в
    /// место; null — убрать.</summary>
    public bool SetPicture(string id, string file, ImmutableArray<byte>? png) => Session.Do(new SetFile(id, file, png));

    public bool Rename(string id, string name)
    {
        if (!Session.Do(new RenameEntity(id, name))) return false;
        if (Selected == id) Select(EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("unit"), name));
        return true;
    }

    /// <summary>Удалить свой юнит: из списков найма — вычеркнуть, повышения из него — снять; своя атака, которой больше никто не бьёт, — тоже.</summary>
    public bool Remove(string id)
    {
        if (!Session.IsMine(id) || Session.History.Current.Find(id) is null) return Session.Refuse($"удалить можно только свой юнит; «{id}» не он");
        var commands = ModReferences.Detach(Session, id).ToList();
        commands.Add(new RemoveEntity(id));
        if ((Content.Find(id) as UnitDef)?.Attack is { } attack && Session.IsMine(attack)
            && !Content.Units.Any(u => u.Id != id && (u.Attack == attack || u.SecondAttack == attack)))
            commands.Add(new RemoveEntity(attack));
        if (!Session.Do(new Batch($"удалить юнит «{Session.NameOf(id)}»", commands))) return false;
        if (Selected == id) Select(null);
        return true;
    }

    public string Line() =>
        $"units: selected={Selected ?? "-"} own={Content.Units.Count(u => Session.IsMine(u.Id))} " +
        $"errors={Session.Report.Errors} refusal={(Session.Refusal ?? "-").Replace(' ', '_')}";
}
