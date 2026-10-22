using System.Collections.Concurrent;
using D2.Data.Map;
using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>Карта и объекты сохранения (<see cref="MapLoader.Load(string)"/>, <see cref="SiteLoader.Load(string)"/>) — один раз на процесс
/// D2.Rules.Tests. Харнессы правил сверяют по одним и тем же сохранениям корпуса: на 20.10 — 62 тыс. открытий 1256 сохранений, а разбор
/// карты из <c>.sg</c> (~16 мс) дороже самого чтения файла. Только для чтения: харнесс, который меняет карту (земля заклинаний у
/// <c>C4MoveHarnessTests</c>), берёт свою копию через <see cref="MapLoader.Load(string)"/>. Ключ — полный путь, время записи и длина
/// (как у <c>SgFile.Open</c>); ошибка разбора запоминается и бросается снова при каждом обращении, как бросал бы разбор.</summary>
public static class SaveMaps
{
    private static readonly ConcurrentDictionary<string, Lazy<MapState>> Maps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<MapSite>>> Places = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Карта сохранения или сценария (<see cref="MapLoader.Load(string)"/>), общая на процесс.</summary>
    public static MapState Map(string path) => Get(Maps, path, MapLoader.Load);

    /// <summary>Объекты сохранения (<see cref="SiteLoader.Load(string)"/>), общие на процесс.</summary>
    public static IReadOnlyList<MapSite> Sites(string path) => Get(Places, path, SiteLoader.Load);

    private static T Get<T>(ConcurrentDictionary<string, Lazy<T>> cache, string path, Func<string, T> load)
    {
        string full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        string key = info.Exists ? $"{full}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : full;
        return cache.GetOrAdd(key, _ => new Lazy<T>(() => load(path))).Value;
    }
}
