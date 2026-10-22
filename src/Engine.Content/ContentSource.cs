using System.Collections.Concurrent;

namespace D2.Data.Content;

/// <summary>
/// Содержимое правил по каталогу — то, что загрузчики правил (<c>RulesLoader</c>, <c>EconomyLoader</c>, <c>GameBook</c>…) получают
/// вместо таблиц: каталог базы содержимого (<c>data/content/base</c>, файлы видов) читается слоем без модов, каталог таблиц оригинала
/// (<c>Globals</c> копии игры с <c>.dbf</c>, выгрузка <c>data/rules</c> с <c>.json</c>) импортируется в памяти (<see cref="LegacyContent"/>).
/// Так харнессы, которые берут правила прямо из копии игры, идут через ту же схему, что игра, — и прежние числа харнессов доказывают,
/// что схема ничего не потеряла.
/// <para>Кэш на процесс: ключ — полный путь и отметки файлов каталога (время записи и длина), переписанный файл читается заново.
/// Набор неизменяем, общий экземпляр делить безопасно.</para>
/// </summary>
public static class ContentSource
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, Lazy<ContentSet>> Cache = new(Paths);

    public static ContentSet Open(string dir)
    {
        ArgumentException.ThrowIfNullOrEmpty(dir);
        string full = Path.GetFullPath(dir);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"нет каталога правил {full}");
        string key = full + "|" + Stamp(full);
        var lazy = Cache.GetOrAdd(key, _ =>
        {
            // Каталог переписан — прежний набор того же каталога больше не нужен (иначе каждая перезапись оставляла бы в памяти всю игру).
            foreach (var stale in Cache.Keys.Where(k => Paths.Equals(k[..k.LastIndexOf('|')], full) && !Paths.Equals(k, key)))
                Cache.TryRemove(stale, out Lazy<ContentSet>? _);
            return new Lazy<ContentSet>(() => Load(full));
        });
        try { return lazy.Value; }
        catch
        {
            Cache.TryRemove(new KeyValuePair<string, Lazy<ContentSet>>(key, lazy));   // сбой чтения не запоминается
            throw;
        }
    }

    /// <summary>Каталог — база содержимого (есть файл юнитов) или таблицы оригинала.</summary>
    private static ContentSet Load(string dir) =>
        File.Exists(Path.Combine(dir, ContentSet.KindNamed("unit").File)) ? ContentLayers.Load(dir, [])
        : LegacyContent.IsTablesDir(dir) ? LegacyContent.Import(dir)
        : throw new FileNotFoundException($"в {dir} нет ни содержимого ({ContentSet.KindNamed("unit").File}), ни таблиц оригинала (Gunits)");

    private static long Stamp(string dir)
    {
        long stamp = 17;
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
            stamp = unchecked(stamp * 31 + file.LastWriteTimeUtc.Ticks * 7 + file.Length + file.Name.GetHashCode(StringComparison.OrdinalIgnoreCase));
        return stamp;
    }
}
