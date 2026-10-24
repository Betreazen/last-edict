using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Data.Ui.Windows;

/// <summary>Постройка в дереве ветки: клетка сетки (столбец может быть дробным — середина между детьми) и требуемая постройка той же ветки
/// (<see cref="Parent"/>, связь в дереве; требуемая в другой ветке или не у лорда — корень).</summary>
public sealed record BuildTreeNode(string Building, double Column, int Row, string? Parent);

/// <summary>Ветка окна построек: вкладка с постройками в клетках сетки.</summary>
public sealed record BuildTreeBranch(string Id, string? NameText, string? Toggle, IReadOnlyList<BuildTreeNode> Nodes)
{
    /// <summary>Столбцов и строк сетки (по крайней клетке).</summary>
    public double Columns => Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Column) + 1;

    public int Rows => Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Row) + 1;

    public BuildTreeNode? Node(string building) =>
        Nodes.FirstOrDefault(n => n.Building.Equals(building, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Дерево построек столицы данными (Э6, roadmap §3.3): ветки — сущности содержимого <see cref="BuildingBranchDef"/> (их число — из данных,
/// моды добавляют свои), постройки — из списка лорда, связь — <see cref="BuildingDef.Requires"/>. Ветка постройки: <c>tree.branch</c>
/// постройки, иначе ветка, чья категория или ветка юнитов совпала, иначе ветка требуемой постройки, иначе первая. Место — автораскладкой:
/// строка — по уровню (не выше строки требуемой + 1), столбцы — дерево «листья по порядку, родитель — над серединой детей»; заданные модом
/// <c>tree.column</c>/<c>tree.row</c> главнее. Требования по кругу (ошибка данных) рвутся: постройка, вернувшая круг, становится корнем.
/// </summary>
public sealed class BuildTree
{
    public IReadOnlyList<BuildTreeBranch> Branches { get; }

    private BuildTree(IReadOnlyList<BuildTreeBranch> branches) => Branches = branches;

    public BuildTreeBranch? Branch(string id) => Branches.FirstOrDefault(b => b.Id.Equals(id, StringComparison.Ordinal));

    /// <summary>Ветка постройки; нет в дереве — null.</summary>
    public BuildTreeBranch? BranchOf(string building) => Branches.FirstOrDefault(b => b.Node(building) is not null);

    /// <summary>Дерево построек лорда <paramref name="lordId"/> (нет лорда — пустые ветки).</summary>
    public static BuildTree OfLord(ContentSet content, string lordId)
    {
        ArgumentNullException.ThrowIfNull(content);
        // Лорд игрока в картах импорта — id оригинала (G000LR0001), у карт своей версии — id содержимого.
        var lord = content.Find(lordId) as LordDef
                   ?? content.Lords.FirstOrDefault(l => string.Equals(l.LegacyId, lordId, StringComparison.OrdinalIgnoreCase));
        return Of(content, lord?.Buildings ?? []);
    }

    /// <summary>Дерево построек <paramref name="buildings"/> (по порядку: он задаёт порядок корней и детей).</summary>
    public static BuildTree Of(ContentSet content, IEnumerable<string> buildings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(buildings);
        var defs = buildings.Select(content.Find).OfType<BuildingDef>().DistinctBy(b => b.Id).ToList();
        var branches = content.BuildingBranches;
        var byId = defs.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var branchOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var def in defs) branchOf[def.Id] = BranchId(def, branches, byId, branchOf, []);
        return new BuildTree([.. branches.Select(b => new BuildTreeBranch(b.Id, b.NameText, b.Toggle,
            Layout([.. defs.Where(d => branchOf[d.Id] == b.Id)])))]);
    }

    private static string BranchId(BuildingDef def, IReadOnlyList<BuildingBranchDef> branches, IReadOnlyDictionary<string, BuildingDef> byId,
                                   Dictionary<string, string> known, HashSet<string> seen)
    {
        if (known.TryGetValue(def.Id, out var done)) return done;
        if (def.Tree?.Branch is { } own && branches.Any(b => b.Id == own)) return own;
        var match = def.Category == BuildingCategory.Unit
            ? branches.FirstOrDefault(b => def.Branch is { } kind && b.Units.Contains(kind))
            : branches.FirstOrDefault(b => b.Categories.Contains(def.Category));
        if (match is not null) return match.Id;
        if (def.Requires is { } required && byId.TryGetValue(required, out var parent) && seen.Add(def.Id))
            return BranchId(parent, branches, byId, known, seen);
        return branches.Count > 0 ? branches[0].Id : "";
    }

    /// <summary>Клетки построек одной ветки.</summary>
    private static List<BuildTreeNode> Layout(List<BuildingDef> defs)
    {
        if (defs.Count == 0) return [];
        var byId = defs.ToDictionary(d => d.Id, StringComparer.Ordinal);
        // Родитель — требуемая постройка в этой же ветке; круг требований рвётся у постройки, которая его замкнула.
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var def in defs)
        {
            string? p = def.Requires is { } r && byId.ContainsKey(r) ? r : null;
            for (string? up = p; up is not null; up = parent.GetValueOrDefault(up))
            {
                if (up != def.Id) continue;
                p = null;
                break;
            }
            parent[def.Id] = p;
        }
        var children = defs.ToDictionary(d => d.Id, d => defs.Where(c => parent[c.Id] == d.Id).ToList(), StringComparer.Ordinal);
        var roots = defs.Where(d => parent[d.Id] is null).ToList();
        int minLevel = roots.Count > 0 ? roots.Min(r => r.Level) : defs.Min(d => d.Level);

        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        var columns = new Dictionary<string, double>(StringComparer.Ordinal);
        double next = 0;
        foreach (var root in roots) Place(root, -1);

        return [.. defs.Select(d => new BuildTreeNode(d.Id, columns[d.Id], rows[d.Id], parent[d.Id]))];

        void Place(BuildingDef def, int parentRow)
        {
            int row = Math.Max(0, def.Tree?.Row ?? Math.Max(parentRow + 1, def.Level - minLevel));   // место мода — не левее и не выше сетки
            rows[def.Id] = row;
            var kids = children[def.Id];
            foreach (var kid in kids) Place(kid, row);
            double auto = kids.Count == 0 ? next++ : (columns[kids[0].Id] + columns[kids[^1].Id]) / 2;
            columns[def.Id] = Math.Max(0, def.Tree?.Column ?? auto);
            if (def.Tree?.Column is { } pinned) next = Math.Max(next, Math.Floor(Math.Max(0, pinned)) + 1);
        }
    }
}

/// <summary>Размеры клетки дерева в точках окна (логических, 800×600): значок, плашка подписи под ним, поля вокруг.</summary>
public static class BuildTreeGeometry
{
    public const int CellWidth = 132, CellHeight = 146, Margin = 28, IconWidth = 77, IconHeight = 74, PlateWidth = 124, PlateHeight = 22,
        PlateGap = 6;

    /// <summary>Значок постройки в клетке: по середине столбца, сверху строки.</summary>
    public static UiRect Icon(BuildTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        int cx = Margin + (int)Math.Round((node.Column * CellWidth) + (CellWidth / 2.0));
        return new UiRect(cx - (IconWidth / 2), Margin + (node.Row * CellHeight), IconWidth, IconHeight);
    }

    /// <summary>Плашка подписи под значком.</summary>
    public static UiRect Plate(BuildTreeNode node)
    {
        var icon = Icon(node);
        return new UiRect(icon.X + (icon.Width / 2) - (PlateWidth / 2), icon.Bottom + PlateGap, PlateWidth, PlateHeight);
    }

    /// <summary>Размер содержимого ветки (поля с обеих сторон).</summary>
    public static (int Width, int Height) Content(BuildTreeBranch branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return ((int)Math.Ceiling(branch.Columns * CellWidth) + (2 * Margin),
                (branch.Rows * CellHeight) - (CellHeight - IconHeight - PlateGap - PlateHeight) + (2 * Margin));
    }

    /// <summary>Клетка сетки под точкой содержимого: столбец — с шагом в половину (между столбцами), строка — ближайшая; не меньше нуля.</summary>
    public static (double Column, int Row) Cell(double x, double y)
    {
        double column = Math.Round(((x - Margin) / CellWidth - 0.5) * 2, MidpointRounding.AwayFromZero) / 2;
        int row = (int)Math.Round((y - Margin - (IconHeight / 2.0)) / CellHeight, MidpointRounding.AwayFromZero);
        return (Math.Max(0, column), Math.Max(0, row));
    }

    /// <summary>Постройка, чей значок или плашка под точкой содержимого; нет — null.</summary>
    public static BuildTreeNode? At(BuildTreeBranch branch, double x, double y, Func<string, bool>? shown = null)
    {
        ArgumentNullException.ThrowIfNull(branch);
        return branch.Nodes.LastOrDefault(n => (shown?.Invoke(n.Building) ?? true) && (Contains(Icon(n), x, y) || Contains(Plate(n), x, y)));
    }

    private static bool Contains(UiRect r, double x, double y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
}
