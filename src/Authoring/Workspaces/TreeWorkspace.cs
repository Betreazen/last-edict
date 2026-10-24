using System.Globalization;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Authoring.Workspaces;

/// <summary>Постройка в графе цепочек: имя, юнит постройки (тот, кого она открывает), ветка окна, место в графе (точки), своя ли (мода).</summary>
public sealed record TreeGraphNode(string Building, string Name, string? Unit, string Branch, double X, double Y, bool Mine);

/// <summary>Связь графа: <see cref="From"/> требуется для <see cref="To"/>.</summary>
public sealed record TreeGraphLink(string From, string To);

/// <summary>
/// Рабочее пространство Студии «Дерево построек» (Э6, Р20; UX — <c>ui/buildtree.py</c> старой утилиты): граф цепочек требований построек
/// выбранного лорда с юнитом каждой постройки, живая страница окна построек (то же дерево, что в игре: <see cref="BuildTreePreview"/>) и
/// панель ошибок (<see cref="StudioSession.Report"/>). Связь в графе — требование (<c>requires</c>), круг — отказ с текстом; перенос значка на
/// живой странице — место в дереве (<c>tree.column</c>/<c>tree.row</c>: место мода главнее автораскладки). Всё — командами ядра (общая
/// история сессии): отмена, повтор, журнал. Решений в окне Godot нет — оно рисует это и зовёт эти методы.
/// </summary>
public sealed class TreeWorkspace
{
    public TreeWorkspace(StudioSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        session.Refreshed += Rebuild;
        Rebuild();
    }

    public StudioSession Session { get; }

    /// <summary>Лорды с постройками (по порядку содержимого).</summary>
    public IReadOnlyList<LordDef> Lords { get; private set; } = [];

    /// <summary>Лорд, чьё дерево открыто.</summary>
    public string Lord { get; private set; } = "";

    public BuildTree? Tree { get; private set; }

    public IReadOnlyList<TreeGraphNode> Nodes { get; private set; } = [];

    public IReadOnlyList<TreeGraphLink> Links { get; private set; } = [];

    /// <summary>Живая страница окна построек по текущему документу.</summary>
    public BuildTreePreview Preview { get; } = new();

    /// <summary>Номер перерисовки пространства.</summary>
    public int Revision { get; private set; }

    /// <summary>Граф идёт слева направо (порты узлов — слева и справа): уровень дерева — столбец графа (<see cref="GraphLevel"/>), место в
    /// уровне — строка (<see cref="GraphLine"/>), ветки — друг под другом с зазором (точки графа).</summary>
    public const double GraphLevel = 300, GraphLine = 110, GraphBranchGap = 70;

    public void SelectLord(string lordId)
    {
        Lord = lordId;
        Rebuild();
    }

    private void Rebuild()
    {
        var content = Session.Report.Content;
        Lords = content is null ? [] : [.. content.Lords.Where(l => l.Buildings.Count > 0)];
        if (Lords.Count > 0 && Lords.All(l => l.Id != Lord))
            Lord = Lords.FirstOrDefault(l => l.Buildings.Any(Session.IsMine))?.Id ?? Lords[0].Id;
        Tree = content is null || Lord.Length == 0 ? null : BuildTree.OfLord(content, Lord);
        BuildGraph(content);
        Preview.Show(Tree, Session.NameOf, Session.IsMine, Session.Text);
        Revision++;
    }

    private void BuildGraph(ContentSet? content)
    {
        if (Tree is null || content is null)
        {
            (Nodes, Links) = ([], []);
            return;
        }
        var units = content.Units.Where(u => u.UpgradeBuilding is not null).GroupBy(u => u.UpgradeBuilding!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodes = new List<TreeGraphNode>();
        double top = 0;
        foreach (var branch in Tree.Branches.Where(b => b.Nodes.Count > 0))
        {
            foreach (var n in branch.Nodes)
                nodes.Add(new TreeGraphNode(n.Building, Session.NameOf(n.Building),
                    units.TryGetValue(n.Building, out var unit) ? Session.NameOf(unit.Id) : null, branch.Id,
                    n.Row * GraphLevel, top + (n.Column * GraphLine), Session.IsMine(n.Building)));
            top += (branch.Columns * GraphLine) + GraphBranchGap;
        }
        Nodes = nodes;
        // Связи — все требования между постройками лорда, и между ветками тоже: граф цепочек, а не окно.
        var shown = nodes.Select(n => n.Building).ToHashSet(StringComparer.Ordinal);
        Links = [.. content.Buildings.Where(b => shown.Contains(b.Id) && b.Requires is { } r && shown.Contains(r))
            .Select(b => new TreeGraphLink(b.Requires!, b.Id))];
    }

    // ---- правки

    /// <summary>Связь в графе: <paramref name="to"/> требует <paramref name="from"/>. Круг — отказ с текстом, документ прежний.</summary>
    public bool Connect(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        if (from == to) return Session.Refuse("постройка не может требовать саму себя");
        var content = Session.Report.Content;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? at = from; at is not null && seen.Add(at); at = (content?.Find(at) as BuildingDef)?.Requires)
            if (at == to)
                return Session.Refuse($"требования по кругу: «{Session.NameOf(to)}» уже нужна для «{Session.NameOf(from)}»");
        return Session.Do(new SetField(to, "requires", JsonValue.Create(from)));
    }

    /// <summary>Снять требование постройки (стрелку в неё).</summary>
    public bool Disconnect(string to) => Session.Do(new SetField(to, "requires", null));

    /// <summary>Место значка в окне построек: столбец и строка сетки (место мода главнее автораскладки).</summary>
    public bool Place(string building, double column, int row) =>
        Session.Do(new Batch($"место «{Session.NameOf(building)}» — {column.ToString("0.##", CultureInfo.InvariantCulture)},{row}",
        [
            new SetField(building, "tree.column", JsonValue.Create(column)),
            new SetField(building, "tree.row", JsonValue.Create(row)),
        ]));

    /// <summary>Вернуть значок к автораскладке (убрать место из слоя мода).</summary>
    public bool Unplace(string building) =>
        Session.Do(new Batch($"место «{Session.NameOf(building)}» — авто",
            [new SetField(building, "tree.column", null, Unset: true), new SetField(building, "tree.row", null, Unset: true)]));

    /// <summary>Новая постройка мода каталогом у текущего лорда, под требуемой (если задана; ветка — её), с именем на языке окна.</summary>
    public bool AddBuilding(string name, string title, string? requires)
    {
        var body = new JsonObject { ["level"] = 2, ["category"] = "guild", ["cost"] = new JsonObject { ["gold"] = 500 } };
        if (requires is not null && Session.Report.Content?.Find(requires) is BuildingDef parent)
        {
            body["requires"] = requires;
            body["level"] = parent.Level + 1;
            body["category"] = JsonValue.Create(System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(parent.Category.ToString()));
            if (parent.Branch is { } kind) body["branch"] = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
        }
        string id = EntityFolders.IdOf(Session.Project.Id, ContentSet.KindNamed("building"), name);
        var commands = new List<IEditCommand> { new AddEntity("building", name, body) };
        if (title.Length > 0) commands.Add(new SetText(id, Session.Language, "name", title));
        if (Session.Report.Content?.Find(Lord) is LordDef lord)
            commands.Add(new SetField(lord.Id, "buildings", new JsonArray([.. lord.Buildings.Append(id).Select(b => (JsonNode)JsonValue.Create(b)!)])));
        return Session.Do(new Batch($"новая постройка «{(title.Length > 0 ? title : name)}»", commands));
    }

    /// <summary>Строка проверки окна без глаз.</summary>
    public string Line() =>
        $"studio: mod={Session.Project.Id} lord={Lord} branches={Tree?.Branches.Count ?? 0} nodes={Nodes.Count} links={Links.Count} " +
        $"errors={Session.Report.Errors} warnings={Session.Report.Warnings} undo={(Session.History.CanUndo ? 1 : 0)} " +
        $"redo={(Session.History.CanRedo ? 1 : 0)} dirty={(Session.History.Dirty ? 1 : 0)} executes_code={(Session.Project.ExecutesCode ? 1 : 0)} " +
        $"refusal={(Session.Refusal ?? "-").Replace(' ', '_')}";
}
