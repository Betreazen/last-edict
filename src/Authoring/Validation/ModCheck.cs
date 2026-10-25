using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Project;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Authoring.Validation;

public enum CheckLevel { Error, Warning, Info }

/// <summary>Находка проверки: правило (<c>MOD010</c>), уровень, сущность или файл, текст по-русски.</summary>
public sealed record CheckItem(string Rule, CheckLevel Level, string Target, string Message)
{
    /// <summary>Поле сущности, к которому относится находка (путь через точку, <c>requires</c>, <c>research[1].spell</c>); не известно — null.
    /// Формы Студии показывают ошибку у этого поля.</summary>
    public string? Field { get; init; }

    public override string ToString() => $"{Rule} {Level.ToString().ToLowerInvariant()} {Target}: {Message}";
}

/// <summary>Итог проверки и содержимое мода поверх базы (нет — слой не накладывается, причина — ошибка <c>MOD001</c>).</summary>
public sealed record CheckReport(IReadOnlyList<CheckItem> Items, ContentSet? Content)
{
    /// <summary>Отчёт наложения слоёв (кто добавил, поправил, убрал и чью правку перекрыл) — для окна конфликтов.</summary>
    public IReadOnlyList<LayerChange> Layers { get; init; } = [];

    public int Errors => Items.Count(i => i.Level == CheckLevel.Error);

    public int Warnings => Items.Count(i => i.Level == CheckLevel.Warning);
}

/// <summary>
/// Проверка мода — общая для окна Студии и <c>d2mod validate</c> (<c>docs/tools/data-contract.md</c> §5). Документ (и несохранённый)
/// накладывается на базу теми же слоями, что у игры (<see cref="ContentLayers"/>): что не грузится в игре, здесь — ошибка с тем же текстом.
/// Дальше — правила дерева построек (круги требований, постройка вне списков лордов, два значка в одной клетке) и пометка «исполняет код».
/// </summary>
public static partial class ModCheck
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^«(?<id>[^»]+)»[^:]*: (?<path>[A-Za-z][\w.\[\]]*) (→|—)")]
    private static partial System.Text.RegularExpressions.Regex WhereFrom();

    /// <summary>Проверить документ <paramref name="doc"/> мода <paramref name="project"/> поверх базы <paramref name="baseDir"/> и модов
    /// <paramref name="others"/> (зависимости — их слои под этим).</summary>
    public static CheckReport Run(ModDocument doc, ModProject project, string baseDir, IReadOnlyList<ModInfo>? others = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(project);
        var items = new List<CheckItem>();
        if (project.ExecutesCode)
            items.Add(new("MOD090", CheckLevel.Info, project.Id,
                          $"исполняет код: C#-плагин ({string.Join(", ", project.Info.Plugins.Select(Path.GetFileName))}) — включать только доверенный"));
        ContentSet? content = null;
        IReadOnlyList<LayerChange> layers = [];
        string temp = Path.Combine(Path.GetTempPath(), "le-studio-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Слой — из документа (Студия проверяет и несохранённое): копия мода во временном каталоге.
            var copy = ModProject.Create(Path.Combine(temp, project.Id), project.Id, project.Info.Name);
            doc.Save(copy);
            // Описание мода — из документа (порядок loadAfter, зависимости — и несохранённые); плагины — с диска мода.
            var info = ModProject.Open(copy.Dir).Info with { Plugins = project.Info.Plugins };
            content = ContentLayers.Load(baseDir, [.. others ?? [], info], out layers);
        }
        catch (InvalidDataException e)
        {
            string message = e.Message.Replace(temp, "", StringComparison.OrdinalIgnoreCase);
            // Ошибка ссылки называет сущность и поле: «id» (мод): путь → «значение» — … — её цель и поле, иначе — мод.
            var at = WhereFrom().Match(message);
            items.Add(new("MOD001", CheckLevel.Error, at.Success ? at.Groups["id"].Value : project.Id, message)
            {
                Field = at.Success ? at.Groups["path"].Value : null,
            });
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
        items.AddRange(Cycles(doc, content));
        if (content is not null) items.AddRange(Tree(doc, content));
        items.AddRange(LayerConflicts.Of(layers, project.Id).Select(c => new CheckItem("MOD020", CheckLevel.Info, c.Entity,
            $"поле {c.Field} правят {string.Join(" и ", c.Mods)} — действует {c.Winner}") { Field = c.Field }));
        return new CheckReport(items, content) { Layers = layers };
    }

    /// <summary>Круги требований построек (<c>MOD010</c>): у постройки мода цепочка <c>requires</c> возвращается к ней самой.</summary>
    private static IEnumerable<CheckItem> Cycles(ModDocument doc, ContentSet? content)
    {
        var requires = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (content is not null)
            foreach (var b in content.Buildings) requires[b.Id] = b.Requires;
        // Слой не наложился — круг ищется по самому документу (поле requires правок и новых построек).
        foreach (var e in doc.Of("building"))
            if (e.Get("requires") is { } r && r.GetValueKind() == System.Text.Json.JsonValueKind.String) requires[e.Id] = r.GetValue<string>();
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in doc.Of("building"))
        {
            var chain = new List<string> { e.Id };
            for (string? at = requires.GetValueOrDefault(e.Id); at is not null; at = requires.GetValueOrDefault(at))
            {
                if (at == e.Id)
                {
                    if (chain.Count(reported.Add) == chain.Count)   // круг — один раз, от первой его постройки
                        yield return new("MOD010", CheckLevel.Error, e.Id, "требования по кругу: " + string.Join(" → ", chain.Append(e.Id)));
                    break;
                }
                if (chain.Contains(at) || chain.Count > 256) break;
                chain.Add(at);
            }
        }
    }

    /// <summary>Постройки мода в деревьях лордов: вне всех списков (<c>MOD011</c>), два значка в одной клетке (<c>MOD012</c>).</summary>
    private static IEnumerable<CheckItem> Tree(ModDocument doc, ContentSet content)
    {
        var mine = doc.Of("building").Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var listed = content.Lords.SelectMany(l => l.Buildings).ToHashSet(StringComparer.Ordinal);
        foreach (string id in mine.Where(id => content.Find(id) is BuildingDef && !listed.Contains(id)).Order(StringComparer.Ordinal))
            yield return new("MOD011", CheckLevel.Warning, id, "постройки нет ни у одного лорда (lords[].buildings) — в окне построек её не будет");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lord in content.Lords.Where(l => l.Buildings.Any(mine.Contains)))
        {
            var tree = BuildTree.Of(content, lord.Buildings);
            foreach (var branch in tree.Branches)
                foreach (var cell in branch.Nodes.GroupBy(n => (n.Column, n.Row)).Where(g => g.Count() > 1 && g.Any(n => mine.Contains(n.Building))))
                {
                    string key = $"{branch.Id}|{cell.Key}";
                    if (!seen.Add(key)) continue;
                    yield return new("MOD012", CheckLevel.Warning, cell.First(n => mine.Contains(n.Building)).Building,
                        $"в ветке {branch.Id} в клетке {cell.Key.Column},{cell.Key.Row} — {string.Join(" и ", cell.Select(n => n.Building))}");
                }
        }
    }
}
