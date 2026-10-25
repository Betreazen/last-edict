using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Rules.Magic;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Форма предмета в Студии: название, описание, категория, цена, что даёт надетый (модификатор), зелье (модификатор и лечение),
/// заклинание свитка или посоха, атака сферы или талисмана.</summary>
public sealed record ItemForm
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public ItemCategory Category { get; init; } = ItemCategory.Jewel;
    public IReadOnlyDictionary<string, long> Value { get; init; } = new Dictionary<string, long> { ["gold"] = 100 };
    public string? EquipModifier { get; init; }
    public string? PotionModifier { get; init; }
    public int? PotionHeal { get; init; }
    public string? Spell { get; init; }
    public string? Attack { get; init; }
}

/// <summary>
/// Рабочее пространство «Предметы» (Студия 2, задача 4): список, новый на основе выбранного, правка формой, значок 70×85 (<c>icon.png</c>,
/// архив <c>IconItem</c>, HD), переименование и удаление своего (ссылки мода — следом). Каждая правка — одна команда отмены.
/// </summary>
public sealed class ItemWorkspace
{
    public ItemWorkspace(StudioSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        session.Refreshed += () => Revision++;
    }

    public StudioSession Session { get; }

    public int Revision { get; private set; }

    public string? Selected { get; private set; }

    public string Filter { get; private set; } = "";

    private ContentSet Content => Session.Report.Content ?? Session.BaseContent;

    public IReadOnlyList<ModifierDef> Modifiers => Content.Modifiers;

    public IReadOnlyList<SpellDef> Spells => Content.Spells;

    public IReadOnlyList<AttackDef> Attacks => Content.Attacks;

    public void Select(string? id)
    {
        Selected = id;
        Revision++;
    }

    public void Find(string text)
    {
        Filter = text ?? "";
        Revision++;
    }

    public IReadOnlyList<EntityRow> Items() =>
        [.. Content.Items
            .Select(i => new EntityRow(i.Id, Session.NameOf(i.Id),
                Session.IsMine(i.Id) ? EntityOrigin.Mine : Session.History.Current.Find(i.Id) is not null ? EntityOrigin.Patched : EntityOrigin.Base))
            .Where(r => Filter.Length == 0 || r.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Origin == EntityOrigin.Mine ? 0 : 1)];

    public bool IsOwnFolder(string id) => Session.IsMine(id) && Session.History.Current.Find(id) is { Source: EntitySource.Folder };

    public ImmutableArray<byte>? IconOf(string id) =>
        Session.History.Current.Find(id)?.Files.TryGetValue(EntityFolders.IconFile, out var bytes) == true ? bytes : null;

    public ItemForm FormOf(string id)
    {
        if (Content.Find(id) is not ItemDef i) return new ItemForm();
        return new ItemForm
        {
            Title = Session.NameOf(id), Description = Text(id, i.DescriptionText), Category = i.Category, Value = i.Value ?? Prices.Free,
            EquipModifier = i.EquipModifier, PotionModifier = i.PotionModifier, PotionHeal = i.PotionHeal, Spell = i.Spell, Attack = i.Attack,
        };
    }

    private string Text(string id, string? key)
    {
        if (Session.History.Current.Find(id) is { Source: EntitySource.Folder } own
            && (own.Strings.GetValueOrDefault(Session.Language) ?? own.Strings.GetValueOrDefault("en")) is { } map && map.TryGetValue("description", out var t))
            return t;
        return key is not null ? Session.Text(key) ?? "" : "";
    }

    public string NameFor(string title) =>
        EntityNames.Free(Session.History.Current, Session.Report.Content, "item", EntityNames.FromTitle(title, "item"));

    /// <summary>Новый предмет мода на основе <paramref name="source"/> (копия без id оригинала, строк и картинки) или пустой, со своим
    /// значком (без него в окнах — пустая клетка). Одна команда.</summary>
    public bool Create(string name, string title, string? source = null, ImmutableArray<byte>? icon = null)
    {
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("item"), name);
        JsonObject body = source is not null && Content.Find(source) is ItemDef from
            ? (JsonObject)JsonSerializer.SerializeToNode(from, ContentJson.Options)!
            : new JsonObject { ["category"] = "jewel", ["value"] = new JsonObject { ["gold"] = 100 } };
        foreach (string drop in new[] { "id", "legacyId", "nameText", "descriptionText", "image" }) body.Remove(drop);
        var commands = new List<IEditCommand> { new AddEntity("item", name, body) };
        if (title.Length > 0) commands.Add(new SetText(id, Session.Language, "name", title));
        if (icon is { } png) commands.Add(new SetFile(id, EntityFolders.IconFile, png));
        if (!Session.Do(new Batch($"новый предмет «{(title.Length > 0 ? title : name)}»", commands))) return false;
        Select(id);
        return true;
    }

    /// <summary>Правка предмета формой: изменившиеся поля — правками слоя. Одна команда.</summary>
    public bool Edit(string id, ItemForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (Content.Find(id) is not ItemDef) return Session.Refuse($"предмета «{id}» нет");
        var was = FormOf(id);
        var commands = new List<IEditCommand>();
        void Set<T>(string path, T now, T then, Func<T, JsonNode?> json)
        {
            if (!EqualityComparer<T>.Default.Equals(now, then)) commands.Add(FormJson.Field(Session, id, path, json(now)));
        }
        Set("category", form.Category, was.Category, v => FormJson.Enum(v));
        Set("equipModifier", form.EquipModifier, was.EquipModifier, v => v is null ? null : JsonValue.Create(v));
        Set("potionModifier", form.PotionModifier, was.PotionModifier, v => v is null ? null : JsonValue.Create(v));
        Set("potionHeal", form.PotionHeal, was.PotionHeal, v => v is { } x ? JsonValue.Create(x) : null);
        Set("spell", form.Spell, was.Spell, v => v is null ? null : JsonValue.Create(v));
        Set("attack", form.Attack, was.Attack, v => v is null ? null : JsonValue.Create(v));
        if (!FormJson.SameCost(form.Value, was.Value)) commands.Add(FormJson.Field(Session, id, "value", FormJson.Cost(form.Value)));
        if (IsOwnFolder(id))
        {
            if (form.Title != was.Title) commands.Add(new SetText(id, Session.Language, "name", form.Title.Length > 0 ? form.Title : null));
            if (form.Description != was.Description) commands.Add(new SetText(id, Session.Language, "description", form.Description.Length > 0 ? form.Description : null));
        }
        return commands.Count == 0 || Session.Do(new Batch($"предмет «{Session.NameOf(id)}»", commands));
    }

    public bool SetIcon(string id, ImmutableArray<byte>? png) => Session.Do(new SetFile(id, EntityFolders.IconFile, png));

    public bool Rename(string id, string name)
    {
        if (!Session.Do(new RenameEntity(id, name))) return false;
        if (Selected == id) Select(EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("item"), name));
        return true;
    }

    public bool Remove(string id)
    {
        if (!Session.IsMine(id) || Session.History.Current.Find(id) is null) return Session.Refuse($"удалить можно только свой предмет; «{id}» не он");
        var commands = ModReferences.Detach(Session, id).ToList();
        commands.Add(new RemoveEntity(id));
        if (!Session.Do(new Batch($"удалить предмет «{Session.NameOf(id)}»", commands))) return false;
        if (Selected == id) Select(null);
        return true;
    }

    public string Line() =>
        $"items: selected={Selected ?? "-"} own={Content.Items.Count(i => Session.IsMine(i.Id))} errors={Session.Report.Errors}";
}
