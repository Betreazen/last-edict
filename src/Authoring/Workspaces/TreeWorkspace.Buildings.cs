using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Форма постройки в Студии: что автор задаёт полями, а не JSON. Название и описание — строки на языке окна (у своей постройки);
/// требуемая, уровень, цена, категория, ветка юнитов (у найма) и ветка окна построек (нет — по категории или требуемой).</summary>
public sealed record BuildingForm
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Requires { get; init; }
    public int Level { get; init; } = 1;
    public IReadOnlyDictionary<string, long> Cost { get; init; } = new Dictionary<string, long> { ["gold"] = 500 };
    public BuildingCategory Category { get; init; } = BuildingCategory.Guild;
    public BranchKind? UnitBranch { get; init; }
    public string? TreeBranch { get; init; }
}

/// <summary>Постройки мода в «Дереве построек» (Студия 2): создание формой с картинкой, правка, переименование и удаление своей — каждое
/// одной командой отмены (<see cref="Batch"/>); новая постройка сама встаёт в списки построек лордов.</summary>
public sealed partial class TreeWorkspace
{
    /// <summary>Постройка, открытая в форме (выбрана в графе или на живой странице); null — форма новой.</summary>
    public string? Selected { get; private set; }

    public void Select(string? id)
    {
        Selected = id;
        Revision++;
    }

    /// <summary>Ветки окна построек в содержимом (для выбора ветки в форме).</summary>
    public IReadOnlyList<BuildingBranchDef> TreeBranches => Session.Report.Content?.Of<BuildingBranchDef>() ?? [];

    /// <summary>Своя постройка-каталог мода: у неё правятся название, описание, картинка, имя; у чужой — только поля правил слоем мода.</summary>
    public bool IsOwnFolder(string id) => Session.IsMine(id) && Session.History.Current.Find(id) is { Source: EntitySource.Folder };

    /// <summary>Значок постройки из документа (несохранённый тоже); нет — null.</summary>
    public ImmutableArray<byte>? IconOf(string id) =>
        Session.History.Current.Find(id)?.Files.TryGetValue(EntityFolders.IconFile, out var bytes) == true ? bytes : null;

    /// <summary>Форма по итогу (база с модом) постройки <paramref name="id"/>.</summary>
    public BuildingForm FormOf(string id)
    {
        if (Session.Report.Content?.Find(id) is not BuildingDef b) return new BuildingForm();
        return new BuildingForm
        {
            Title = Session.NameOf(id), Description = DescriptionOf(id, b), Requires = b.Requires, Level = b.Level,
            Cost = b.Cost ?? Prices.Free, Category = b.Category, UnitBranch = b.Branch, TreeBranch = b.Tree?.Branch,
        };
    }

    private string DescriptionOf(string id, BuildingDef b)
    {
        if (Session.History.Current.Find(id) is { Source: EntitySource.Folder } own
            && (own.Strings.GetValueOrDefault(Session.Language) ?? own.Strings.GetValueOrDefault("en")) is { } map
            && map.TryGetValue("description", out var text))
            return text;
        return b.DescriptionText is { } key ? Session.Text(key) ?? "" : "";
    }

    /// <summary>Форма новой постройки под требуемой: уровень выше, категория, ветка юнитов и ветка окна — как у неё.</summary>
    public BuildingForm NewForm(string? requires)
    {
        if (requires is null || Session.Report.Content?.Find(requires) is not BuildingDef parent) return new BuildingForm();
        return new BuildingForm
        {
            Requires = requires, Level = parent.Level + 1, Category = parent.Category, UnitBranch = parent.Branch,
            Cost = parent.Cost ?? new Dictionary<string, long> { ["gold"] = 500 },
        };
    }

    /// <summary>Свободное имя каталога новой постройки по названию.</summary>
    public string NameFor(string title) =>
        EntityNames.Free(Session.History.Current, Session.Report.Content, "building", EntityNames.FromTitle(title, "building"));

    /// <summary>Новая постройка мода каталогом по форме, со значком (PNG; нет — без картинки) — в списках построек открытого лорда и, если
    /// <paramref name="raceLords"/>, всех лордов его расы. Одна команда отмены.</summary>
    public bool CreateBuilding(string name, BuildingForm form, ImmutableArray<byte>? icon = null, bool raceLords = true)
    {
        ArgumentNullException.ThrowIfNull(form);
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("building"), name);
        if (form.Requires is { } r && Session.Report.Content?.Find(r) is not BuildingDef)
            return Session.Refuse($"требуемой постройки «{r}» нет");
        var body = new JsonObject { ["level"] = form.Level, ["category"] = Enum(form.Category) };
        if (form.Requires is { } requires) body["requires"] = requires;
        if (form.UnitBranch is { } branch) body["branch"] = Enum(branch);
        if (CostNode(form.Cost) is { } cost) body["cost"] = cost;
        if (form.TreeBranch is { } tree) body["tree"] = new JsonObject { ["branch"] = tree };
        var commands = new List<IEditCommand> { new AddEntity("building", name, body) };
        if (form.Title.Length > 0) commands.Add(new SetText(id, Session.Language, "name", form.Title));
        if (form.Description.Length > 0) commands.Add(new SetText(id, Session.Language, "description", form.Description));
        if (icon is { } png) commands.Add(new SetFile(id, EntityFolders.IconFile, png));
        foreach (var lord in LordsFor(raceLords))
            commands.Add(new SetField(lord.Id, "buildings", new JsonArray([.. lord.Buildings.Append(id).Select(b => (JsonNode)JsonValue.Create(b)!)])));
        if (!Session.Do(new Batch($"новая постройка «{(form.Title.Length > 0 ? form.Title : name)}»", commands))) return false;
        Select(id);
        return true;
    }

    /// <summary>Открытый лорд и (если <paramref name="raceLords"/>) прочие лорды его расы.</summary>
    private IEnumerable<LordDef> LordsFor(bool raceLords)
    {
        var content = Session.Report.Content;
        if (content?.Find(Lord) is not LordDef open) return [];
        return raceLords && open.Race is { } race ? content.Lords.Where(l => l.Id == open.Id || l.Race == race) : [open];
    }

    /// <summary>Правка постройки по форме: изменившиеся поля — правками слоя (у чужой — поверх базы), название и описание — строками своей.
    /// Требование по кругу — отказ. Одна команда отмены.</summary>
    public bool EditBuilding(string id, BuildingForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (Session.Report.Content?.Find(id) is not BuildingDef) return Session.Refuse($"постройки «{id}» нет");
        var was = FormOf(id);
        if (form.Requires != was.Requires && form.Requires is { } r)
        {
            if (Session.Report.Content.Find(r) is not BuildingDef) return Session.Refuse($"требуемой постройки «{r}» нет");
            if (Cycle(r, id) is { } refusal) return Session.Refuse(refusal);
        }
        var commands = new List<IEditCommand>();
        if (form.Level != was.Level) commands.Add(new SetField(id, "level", JsonValue.Create(form.Level)));
        if (form.Category != was.Category) commands.Add(new SetField(id, "category", Enum(form.Category)));
        if (form.Requires != was.Requires) commands.Add(Field(id, "requires", form.Requires is { } req ? JsonValue.Create(req) : null));
        if (form.UnitBranch != was.UnitBranch) commands.Add(Field(id, "branch", form.UnitBranch is { } ub ? Enum(ub) : null));
        if (form.TreeBranch != was.TreeBranch) commands.Add(Field(id, "tree.branch", form.TreeBranch is { } tb ? JsonValue.Create(tb) : null));
        if (!SameCost(form.Cost, was.Cost)) commands.Add(Field(id, "cost", CostNode(form.Cost)));
        if (IsOwnFolder(id))
        {
            if (form.Title != was.Title) commands.Add(new SetText(id, Session.Language, "name", form.Title.Length > 0 ? form.Title : null));
            if (form.Description != was.Description)
                commands.Add(new SetText(id, Session.Language, "description", form.Description.Length > 0 ? form.Description : null));
        }
        if (commands.Count == 0) return true;
        return Session.Do(new Batch($"постройка «{Session.NameOf(id)}»", commands));
    }

    /// <summary>Значок своей постройки (PNG, уже вписанный окном в размер места — <see cref="EntityArt.Fit"/>); null — убрать.</summary>
    public bool SetIcon(string id, ImmutableArray<byte>? png) => Session.Do(new SetFile(id, EntityFolders.IconFile, png));

    /// <summary>Переименовать свою постройку (каталог и id); ссылки мода на неё — следом.</summary>
    public bool RenameBuilding(string id, string name)
    {
        if (!Session.Do(new RenameEntity(id, name))) return false;
        if (Selected == id) Select(EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("building"), name));
        return true;
    }

    /// <summary>Удалить свою постройку: из списков лордов — вычеркнуть, у построек, что её требовали, требуемой становится её требуемая
    /// (цепочка не рвётся), прочие ссылки мода — снять. Одна команда отмены.</summary>
    public bool RemoveBuilding(string id)
    {
        if (!Session.IsMine(id) || Session.History.Current.Find(id) is null) return Session.Refuse($"удалить можно только свою постройку; «{id}» не она");
        string? parent = (Session.Report.Content?.Find(id) as BuildingDef)?.Requires;
        var commands = ModReferences.Detach(Session, id, r => r.Path == "requires" ? parent : null).ToList();
        commands.Add(new RemoveEntity(id));
        if (!Session.Do(new Batch($"удалить постройку «{Session.NameOf(id)}»", commands))) return false;
        if (Selected == id) Select(null);
        return true;
    }

    /// <summary>Поле-ссылка или значение: null у своей сущности — убрать поле, у правки чужой — null слоя (поле базы снимается).</summary>
    private SetField Field(string id, string path, JsonNode? value) =>
        value is null && Session.IsMine(id) ? new SetField(id, path, null, Unset: true) : new SetField(id, path, value);

    /// <summary>Сделает ли требование «<paramref name="building"/> требует <paramref name="requires"/>» круг; да — текст отказа.</summary>
    private string? Cycle(string requires, string building)
    {
        if (requires == building) return "постройка не может требовать саму себя";
        var content = Session.Report.Content;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = requires; at is not null && seen.Add(at); at = (content?.Find(at) as BuildingDef)?.Requires)
            if (at == building)
                return $"требования по кругу: «{Session.NameOf(building)}» уже нужна для «{Session.NameOf(requires)}»";
        return null;
    }

    private static JsonValue Enum<T>(T value) where T : struct, System.Enum =>
        JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

    private static JsonObject? CostNode(IReadOnlyDictionary<string, long> cost)
    {
        var node = new JsonObject();
        foreach (var (currency, amount) in cost.Where(c => c.Value != 0).OrderBy(c => Prices.Currencies.ToList().IndexOf(c.Key) is var i and >= 0 ? i : int.MaxValue))
            node[currency] = amount;
        return node.Count == 0 ? null : node;
    }

    private static bool SameCost(IReadOnlyDictionary<string, long> a, IReadOnlyDictionary<string, long> b) =>
        a.Where(p => p.Value != 0).OrderBy(p => p.Key, StringComparer.Ordinal)
         .SequenceEqual(b.Where(p => p.Value != 0).OrderBy(p => p.Key, StringComparer.Ordinal));
}
