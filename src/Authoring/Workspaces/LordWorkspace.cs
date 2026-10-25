using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Map;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Форма лорда в Студии: название, описание, раса, вид лорда (маг, воин, дипломат) и исследования — заклинания по порядку с ценой.
/// Постройки лорда правятся деревом построек.</summary>
public sealed record LordForm
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Race { get; init; }
    public LordCategory Category { get; init; }
    public IReadOnlyList<SpellResearch> Research { get; init; } = [];
}

/// <summary>
/// Рабочее пространство «Лорды» (Студия 2, задача 4): новый лорд на основе выбранного (копия его построек и исследований), правка формой,
/// переименование и удаление своего (ссылки мода — следом). Каждая правка — одна команда отмены.
/// </summary>
public sealed class LordWorkspace
{
    public LordWorkspace(StudioSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        session.Refreshed += () => Revision++;
    }

    public StudioSession Session { get; }

    public int Revision { get; private set; }

    public string? Selected { get; private set; }

    private ContentSet Content => Session.Report.Content ?? Session.BaseContent;

    public IReadOnlyList<RaceDef> Races => Content.Of<RaceDef>();

    public IReadOnlyList<SpellDef> Spells => Content.Spells;

    public void Select(string? id)
    {
        Selected = id;
        Revision++;
    }

    public IReadOnlyList<EntityRow> Lords() =>
        [.. Content.Lords.Select(l => new EntityRow(l.Id, Session.NameOf(l.Id),
            Session.IsMine(l.Id) ? EntityOrigin.Mine : Session.History.Current.Find(l.Id) is not null ? EntityOrigin.Patched : EntityOrigin.Base))];

    public bool IsOwnFolder(string id) => Session.IsMine(id) && Session.History.Current.Find(id) is { Source: EntitySource.Folder };

    public LordForm FormOf(string id)
    {
        if (Content.Find(id) is not LordDef l) return new LordForm();
        string description = Session.History.Current.Find(id) is { Source: EntitySource.Folder } own
                             && (own.Strings.GetValueOrDefault(Session.Language) ?? own.Strings.GetValueOrDefault("en")) is { } map
                             && map.TryGetValue("description", out var t) ? t
            : l.DescriptionText is { } key ? Session.Text(key) ?? "" : "";
        return new LordForm { Title = Session.NameOf(id), Description = description, Race = l.Race, Category = l.Category, Research = l.Research };
    }

    public string NameFor(string title) =>
        EntityNames.Free(Session.History.Current, Session.Report.Content, "lord", EntityNames.FromTitle(title, "lord"));

    /// <summary>Новый лорд мода на основе <paramref name="source"/>: его раса, вид, постройки и исследования; название — строкой мода. Одна команда.</summary>
    public bool CreateFrom(string source, string name, string title)
    {
        if (Content.Find(source) is not LordDef from) return Session.Refuse($"лорда «{source}» нет");
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("lord"), name);
        var body = (JsonObject)JsonSerializer.SerializeToNode(from, ContentJson.Options)!;
        foreach (string drop in new[] { "id", "legacyId", "nameText", "descriptionText" }) body.Remove(drop);
        var commands = new List<IEditCommand> { new AddEntity("lord", name, body) };
        if (title.Length > 0) commands.Add(new SetText(id, Session.Language, "name", title));
        if (!Session.Do(new Batch($"новый лорд «{(title.Length > 0 ? title : name)}»", commands))) return false;
        Select(id);
        return true;
    }

    /// <summary>Правка лорда формой (исследования — списком целиком). Одна команда.</summary>
    public bool Edit(string id, LordForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (Content.Find(id) is not LordDef) return Session.Refuse($"лорда «{id}» нет");
        foreach (var r in form.Research)
            if (Content.Find(r.Spell) is not SpellDef) return Session.Refuse($"заклинания «{r.Spell}» нет");
        var was = FormOf(id);
        var commands = new List<IEditCommand>();
        if (form.Race != was.Race) commands.Add(FormJson.Field(Session, id, "race", form.Race is null ? null : JsonValue.Create(form.Race)));
        if (form.Category != was.Category) commands.Add(FormJson.Field(Session, id, "category", FormJson.Enum(form.Category)));
        if (!form.Research.SequenceEqual(was.Research, ResearchComparer.Instance))
            commands.Add(FormJson.Field(Session, id, "research", form.Research.Count == 0 ? null
                : JsonSerializer.SerializeToNode(form.Research, ContentJson.Options)));
        if (IsOwnFolder(id))
        {
            if (form.Title != was.Title) commands.Add(new SetText(id, Session.Language, "name", form.Title.Length > 0 ? form.Title : null));
            if (form.Description != was.Description) commands.Add(new SetText(id, Session.Language, "description", form.Description.Length > 0 ? form.Description : null));
        }
        return commands.Count == 0 || Session.Do(new Batch($"лорд «{Session.NameOf(id)}»", commands));
    }

    public bool Rename(string id, string name)
    {
        if (!Session.Do(new RenameEntity(id, name))) return false;
        if (Selected == id) Select(EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("lord"), name));
        return true;
    }

    public bool Remove(string id)
    {
        if (!Session.IsMine(id) || Session.History.Current.Find(id) is null) return Session.Refuse($"удалить можно только своего лорда; «{id}» не он");
        var commands = ModReferences.Detach(Session, id).ToList();
        commands.Add(new RemoveEntity(id));
        if (!Session.Do(new Batch($"удалить лорда «{Session.NameOf(id)}»", commands))) return false;
        if (Selected == id) Select(null);
        return true;
    }

    public string Line() => $"lords: selected={Selected ?? "-"} own={Content.Lords.Count(l => Session.IsMine(l.Id))} errors={Session.Report.Errors}";

    /// <summary>Исследования равны: заклинание и цена (словарь — по парам).</summary>
    private sealed class ResearchComparer : IEqualityComparer<SpellResearch>
    {
        public static readonly ResearchComparer Instance = new();

        public bool Equals(SpellResearch? x, SpellResearch? y) =>
            x?.Spell == y?.Spell && FormJson.SameCost(x?.Cost ?? Prices.Free, y?.Cost ?? Prices.Free);

        public int GetHashCode(SpellResearch obj) => obj.Spell.GetHashCode(StringComparison.Ordinal);
    }
}
