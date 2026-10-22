using System.Collections.Concurrent;
using System.Security.Cryptography;

/// <summary>Журналы без побайтных повторов: corpus/sample/one_battle.jsonl — копия
/// 20260906-v2/battle-001.jsonl, и считать её вторым боем — двойной счёт. Общий хелпер для
/// харнессов; C1HarnessTests держит свой Sha256 (файл Task 4), переезд сюда — одна строка.</summary>
public static class JournalSet
{
    /// <summary>Порядок <paramref name="files"/> сохраняется: из повторов остаётся первый.</summary>
    public static (IReadOnlyList<string> Unique, int Duplicates) Distinct(IEnumerable<string> files)
    {
        var list = files.ToList();
        var digests = Parallel(list, Digest);
        var seen = new HashSet<string>();
        var unique = new List<string>();
        int duplicates = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (seen.Add(digests[i])) unique.Add(list[i]);
            else duplicates++;
        }
        return (unique, duplicates);
    }

    /// <summary>SHA-256 файла — один раз на процесс (ключ — полный путь, время записи и размер, как у <c>SgFile.Open</c>):
    /// <see cref="Distinct"/> зовут полтора десятка харнессов по всему корпусу (2 ГБ журналов на 20.10), и каждый прежде заново
    /// хешировал все файлы.</summary>
    private static string Digest(string file)
    {
        string full = Path.GetFullPath(file);
        var info = new FileInfo(full);
        string key = $"{full}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        return Digests.GetOrAdd(key, _ => new Lazy<string>(() =>
        {
            using var stream = File.OpenRead(full);
            return Convert.ToHexString(SHA256.HashData(stream));
        })).Value;
    }

    private static readonly ConcurrentDictionary<string, Lazy<string>> Digests = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><paramref name="map"/> по элементам параллельно, ответ — в порядке <paramref name="items"/>; единственная ошибка
    /// бросается сама (как при последовательном проходе), а не обёрнутой в <see cref="AggregateException"/>.</summary>
    public static List<TOut> Parallel<TIn, TOut>(IReadOnlyList<TIn> items, Func<TIn, TOut> map)
    {
        try { return [.. items.AsParallel().AsOrdered().Select(map)]; }
        catch (AggregateException e) when (e.InnerExceptions.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            throw;
        }
    }
}
