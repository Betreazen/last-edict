using System.Collections.Concurrent;

namespace LastEdict.Data.Import;

/// <summary>Кадр к записи: вид (самостоятельная картинка архива или кадр из кусков листа), имя в архиве, путь от корня выхода, обрезать ли
/// по занятой части.</summary>
public readonly record struct FrameItem(bool Plain, string Name, string Rel, bool Trim);

/// <summary>Сборка и запись кадров архива пулом потоков (перенос <c>keyed_webp.render</c> и <c>verify</c>): кадр собирается, ключ вырезается,
/// WebP без потерь пишется в каталог сборки. Кадры одного листа идут подряд, поэтому у куска свой кэш листов.</summary>
public static class FrameWriter
{
    private const int Chunk = 128, SheetCache = 16, VerifyEvery = 211;

    /// <summary>Записать кадры <paramref name="items"/> архива <paramref name="archive"/> под <paramref name="outRoot"/>. Возвращает итог по пути.</summary>
    public static IReadOnlyDictionary<string, FrameDone> Render(ImportContext ctx, FfArchive archive, IReadOnlyList<FrameItem> items, string outRoot,
                                                              string step, Action<int>? done = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(items);
        var result = new ConcurrentDictionary<string, FrameDone>(StringComparer.Ordinal);
        var chunks = Enumerable.Range(0, (items.Count + Chunk - 1) / Chunk).ToList();
        var options = new ParallelOptions { MaxDegreeOfParallelism = ctx.Threads, CancellationToken = ctx.Cancel };
        Parallel.ForEach(chunks, options, c =>
        {
            var cache = new Dictionary<(int, int?), RgbaImage>();
            int end = Math.Min(items.Count, (c + 1) * Chunk);
            for (int i = c * Chunk; i < end; i++)
            {
                ctx.Cancel.ThrowIfCancellationRequested();
                if (cache.Count > SheetCache) cache.Clear();
                var item = items[i];
                var image = item.Plain ? archive.PlainImage(item.Name)
                    : archive.BuildFrame(archive.Frames[item.Name], cache) ?? throw new InvalidDataException($"{archive.Path}: кадр {item.Name} не собирается");
                var (part, frame) = KeyedFrames.Prepare(item.Rel, image, item.Trim);
                ImportFiles.WriteBytes(Path.Combine(outRoot, item.Rel.Replace('/', Path.DirectorySeparatorChar)), ctx.Encoder.EncodeWebpLossless(part));
                result[item.Rel] = frame;
            }
            done?.Invoke(end - (c * Chunk));
        });
        return result;
    }

    /// <summary>Выборочная сверка: каждый 211-й файл (по пути) разбирается заново и даёт тот же холст и хеш, что ушли в опись.</summary>
    /// <exception cref="InvalidDataException">Точки после записи не совпали.</exception>
    public static int Verify(ImportContext ctx, string root, IReadOnlyDictionary<string, FrameDone> done)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(done);
        var sample = done.Keys.Order(StringComparer.Ordinal).Where((_, i) => i % VerifyEvery == 0).ToList();
        var bad = new List<string>();
        int checkedCount = 0;
        foreach (string rel in sample)
        {
            var image = ctx.Encoder.DecodeWebp(File.ReadAllBytes(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar))));
            if (image is null)
            {
                bad.Add(rel + " (не разбирается)");
                continue;
            }
            var d = done[rel];
            if (d.At is { } at) image = KeyedFrames.Restore(image, at, d.Width, d.Height);
            if (image.Width != d.Width || image.Height != d.Height || KeyedFrames.PixelSha(image) != d.Sha) bad.Add(rel);
            checkedCount++;
        }
        if (bad.Count > 0)
            throw new InvalidDataException($"точки после записи не совпали у {bad.Count} из {sample.Count}: {string.Join(", ", bad.Take(5))}");
        return checkedCount;
    }
}
