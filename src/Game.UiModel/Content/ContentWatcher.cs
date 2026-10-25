namespace LastEdict.Data.Content;

/// <summary>Что поменялось в модах: правила (<c>content/</c>), экраны и строки (<c>data/ui</c>, <c>data/game</c>), графика и звук
/// (<c>assets/</c>, <c>data/gfx</c>, <c>data/sound</c>), код (<c>plugins/</c> — нужен перезапуск процесса).</summary>
[Flags]
public enum ContentChange
{
    None = 0,
    Rules = 1,
    Screens = 2,
    Art = 4,
    Code = 8,
}

/// <summary>Пачка изменений модов: что задето, сколько файлов, номер ревизии реестра после неё.</summary>
public sealed record ContentReload(ContentChange Changes, IReadOnlyList<string> Files, int Revision);

/// <summary>
/// Горячая перезагрузка модов (Э6, roadmap §3.4; игра с <c>--watch</c>): <see cref="FileSystemWatcher"/> по каталогам модов, изменения
/// копятся, пока файлы пишутся, и отдаются пачкой, когда <see cref="Quiet"/> не было новых (<see cref="Take"/> — с главного потока игры).
/// Каждая пачка поднимает <see cref="Revision"/> — ревизию реестра содержимого: кто держит прочитанное по старой ревизии, перечитывает.
/// Временные файлы атомарной записи (<c>*.tmp</c>) и служебный каталог Студии (<c>.studio</c>) не в счёт.
/// </summary>
public sealed class ContentWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Dictionary<string, string> _roots = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly SortedSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _last;
    private readonly Func<DateTime> _now;

    public ContentWatcher(IEnumerable<string> dirs, TimeSpan? quiet = null, Func<DateTime>? now = null)
    {
        ArgumentNullException.ThrowIfNull(dirs);
        Quiet = quiet ?? TimeSpan.FromMilliseconds(400);
        _now = now ?? (() => DateTime.UtcNow);
        foreach (string dir in dirs.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            _roots[dir] = dir;
            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            };
            watcher.Changed += (_, e) => Note(e.FullPath);
            watcher.Created += (_, e) => Note(e.FullPath);
            watcher.Deleted += (_, e) => Note(e.FullPath);
            watcher.Renamed += (_, e) => Note(e.FullPath);
            // Переполнение буфера наблюдателя теряет изменения — тогда перечитывается всё (отметка содержимого мода).
            watcher.Error += (_, _) => Note(Path.Combine(dir, "content", "-"));
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    /// <summary>Сколько ждать тишины после последнего изменения.</summary>
    public TimeSpan Quiet { get; }

    /// <summary>Ревизия реестра: растёт с каждой отданной пачкой.</summary>
    public int Revision { get; private set; }

    /// <summary>Отметить изменённый файл (зовёт наблюдатель; открыто для проверок).</summary>
    public void Note(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string slashed = path.Replace('\\', '/');
        if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || slashed.Contains("/.studio/", StringComparison.Ordinal)
            || slashed.EndsWith("/.studio", StringComparison.Ordinal)) return;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;   // зовётся из потока наблюдателя: исключение здесь уронило бы процесс
        }
        lock (_lock)
        {
            _pending.Add(full);
            _last = _now();
        }
    }

    /// <summary>Пачка изменений, если файлы перестали меняться; иначе null. Пачка, что ничего не задевает (файлы вне содержимого, данных,
    /// графики и плагинов мода), не отдаётся и ревизию не поднимает.</summary>
    public ContentReload? Take()
    {
        lock (_lock)
        {
            if (_pending.Count == 0 || _now() - _last < Quiet) return null;
            var files = _pending.ToList();
            _pending.Clear();
            var changes = files.Aggregate(ContentChange.None, (c, f) => c | Classify(f));
            if (changes == ContentChange.None) return null;
            Revision++;
            return new ContentReload(changes, files, Revision);
        }
    }

    /// <summary>Что задевает файл мода — по его месту в каталоге мода.</summary>
    public ContentChange Classify(string path)
    {
        string full = Path.GetFullPath(path).Replace('\\', '/');
        string? root = _roots.Keys.Select(r => r.Replace('\\', '/')).Where(r => full.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Length).FirstOrDefault();
        string rel = root is null ? full : full[(root.Length + 1)..];
        // Корень наблюдения — каталог модов или сам мод: первая часть пути может быть именем мода.
        string[] parts = rel.Split('/');
        int at = Array.FindIndex(parts, p => p is "content" or "data" or "assets" or "plugins");
        if (at < 0) return ContentChange.None;
        return parts[at] switch
        {
            // Картинка сущности-каталога (icon.png…, EntityArt) — графика: кэш кадров с тем же именем сбрасывается.
            "content" when full.EndsWith(".png", StringComparison.OrdinalIgnoreCase) => ContentChange.Rules | ContentChange.Art,
            "content" => ContentChange.Rules,
            "plugins" => ContentChange.Code,
            "assets" => ContentChange.Art,
            _ when at + 1 < parts.Length && parts[at + 1] is "gfx" or "sound" or "video" or "map" => ContentChange.Art,
            _ => ContentChange.Screens,
        };
    }

    public void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
