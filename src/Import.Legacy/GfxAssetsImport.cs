using System.Security.Cryptography;
using System.Text;

namespace LastEdict.Data.Import;

/// <summary>Графика карты и боя: архивы <c>Imgs/*.ff</c> → <c>assets/gfx/&lt;Архив&gt;/{plain,frames,anims}/…webp</c> и описи
/// <c>data/gfx/&lt;Архив&gt;.json</c> (перенос <c>tools/gfx-assets/build_gfx_assets.py</c>; описи — те же байты). Листы-атласы на диск не пишутся;
/// кадр — только занятой частью холста, если она не больше половины (угол — поле <c>at</c>), кроме анимаций с кадрами разного холста;
/// смешение кадра — поле <c>opacity</c> (когда не 255).</summary>
public static class GfxAssetsImport
{
    public const string Step = "gfx";
    private const int Opaque = 255;

    /// <exception cref="DirectoryNotFoundException">Нет архивов <c>Imgs</c>.</exception>
    public static void Run(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string imgs = ctx.Game("Imgs");
        var paths = Directory.Exists(imgs)
            ? Directory.GetFiles(imgs, "*.ff").Order(StringComparer.Ordinal).ToList() : [];
        if (paths.Count == 0) throw new DirectoryNotFoundException($"нет архивов в {imgs}");
        string final = Path.Combine(ctx.AssetsDir, "gfx");
        ImportFiles.Recover(final);
        string built = ImportFiles.Fresh(final + ".new");
        var plans = new List<(FfArchive Archive, List<FrameItem> Items)>();
        try
        {
            foreach (string path in paths)
            {
                var ff = new FfArchive(path);
                plans.Add((ff, Plan(ff)));
            }
            int total = plans.Sum(p => p.Items.Count), written = 0;
            ctx.Say($"разобрано оглавлений: {plans.Count}, кадров к записи {total}");
            ctx.Report(Step, 0, total);
            var done = new Dictionary<string, FrameDone>(StringComparer.Ordinal);
            // Самые большие архивы — первыми, как у прежнего сборщика (на итог не влияет).
            foreach (var (archive, items) in plans.OrderByDescending(p => p.Items.Count))
                foreach (var kv in FrameWriter.Render(ctx, archive, items, built, Step, n => ctx.Report(Step, Interlocked.Add(ref written, n), total)))
                    done[kv.Key] = kv.Value;
            ctx.Say($"записано: {done.Count} картинок; сверено заново: {FrameWriter.Verify(ctx, built, done)} файлов, расхождений 0");
            var texts = new List<(string Stem, string Text)>();
            foreach (var (archive, items) in plans.OrderBy(p => Path.GetFileNameWithoutExtension(p.Archive.Path), StringComparer.Ordinal))
            {
                string stem = Path.GetFileNameWithoutExtension(archive.Path);
                var (doc, keyed, images, anims, frames) = Describe(archive, items, done);
                texts.Add((stem, doc));
                ctx.Say($"{Path.GetFileName(archive.Path)}: картинок {images}, анимаций {anims} ({frames} кадров), точек ключа {keyed}");
            }
            ImportFiles.Swap(built, final);
            string dataDir = Path.Combine(ctx.DataDir, "gfx");
            foreach (var (stem, text) in texts) ImportFiles.WriteText(Path.Combine(dataDir, stem + ".json"), text);
        }
        finally
        {
            foreach (var p in plans) p.Archive.Dispose();
        }
    }

    /// <summary>Что записать из архива: самостоятельные картинки (не листы-атласы), кадры вне анимаций, кадры анимаций по порядку. Кадры без
    /// листа или с нулевым размером пропускаются; не обрезаются кадры анимаций с разным холстом.</summary>
    public static List<FrameItem> Plan(FfArchive ff)
    {
        ArgumentNullException.ThrowIfNull(ff);
        string stem = Path.GetFileNameWithoutExtension(ff.Path);
        var sheets = ff.BaseSheets();
        var inAnims = ff.Anims.Values.SelectMany(n => n).ToHashSet(StringComparer.Ordinal);
        var items = new List<FrameItem>();
        foreach (var e in ff.Entries)
            if (e.Name.ToUpperInvariant().EndsWith(".PNG", StringComparison.Ordinal) && !sheets.Contains(e.Name))
                items.Add(new FrameItem(true, e.Name, $"{stem}/plain/{StemOf(e.Name)}{KeyedFrames.Ext}", true));
        var built = ff.Frames.Where(kv => ff.Buildable(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        foreach (var (name, _) in ff.Frames)
            if (built.ContainsKey(name) && !inAnims.Contains(name))
                items.Add(new FrameItem(false, name, $"{stem}/frames/{name}{KeyedFrames.Ext}", true));
        foreach (var (anim, names) in ff.Anims)
        {
            int canvases = names.Where(built.ContainsKey).Select(n => (built[n].Width, built[n].Height)).Distinct().Count();
            for (int i = 0; i < names.Count; i++)
                if (built.ContainsKey(names[i]))
                    items.Add(new FrameItem(false, names[i], $"{stem}/anims/{anim}/{i:000}_{names[i]}{KeyedFrames.Ext}", canvases <= 1));
        }
        return items;
    }

    /// <summary>Опись архива по записанным кадрам (текст как у <c>manifest_text</c>), точек ключа, картинок, анимаций, кадров анимаций.</summary>
    private static (string Text, long Keyed, int Images, int Anims, int Frames) Describe(FfArchive ff, List<FrameItem> items,
                                                                                         Dictionary<string, FrameDone> done)
    {
        long keyed = items.Sum(i => (long)done[i.Rel].Keyed);
        var images = new List<string>();
        var byAnim = new List<(string Anim, List<string> Rels)>();
        foreach (var item in items)
        {
            var parts = item.Rel.Split('/');
            if (parts[1] != "anims")
            {
                images.Add(item.Rel);
                continue;
            }
            int at = byAnim.FindIndex(a => a.Anim == parts[2]);
            if (at < 0) byAnim.Add((parts[2], [item.Rel]));
            else byAnim[at].Rels.Add(item.Rel);
        }
        var built = ff.Frames.Where(kv => ff.Buildable(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var records = new List<PyObj>();
        // Порядок — как у прежней описи: по имени файла, при равном имени кадр (frames) раньше самостоятельной картинки (plain).
        foreach (string rel in images.OrderBy(r => r[(r.LastIndexOf('/') + 1)..], StringComparer.Ordinal).ThenBy(r => r.Split('/')[1] == "plain"))
        {
            var d = done[rel];
            var record = new PyObj { ["name"] = StemOf(rel[(rel.LastIndexOf('/') + 1)..]), ["file"] = rel, ["w"] = d.Width, ["h"] = d.Height, ["sha256"] = d.Sha };
            if (d.At is { } a) record["at"] = new[] { a.X, a.Y };
            records.Add(Mark(record, built.GetValueOrDefault((string)record["name"]!)?.Opacity));
        }
        var anims = new List<PyObj>();
        int frameCount = 0;
        foreach (var (anim, rels) in byAnim.OrderBy(a => a.Anim.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var frames = rels.OrderBy(r => r.ToLowerInvariant(), StringComparer.Ordinal).ToList();
            frameCount += frames.Count;
            var digest = new StringBuilder();
            foreach (string f in frames) digest.Append(done[f].Sha);
            var first = done[frames[0]];
            var record = new PyObj
            {
                ["name"] = anim, ["dir"] = $"{Path.GetFileNameWithoutExtension(ff.Path)}/anims/{anim}", ["ext"] = KeyedFrames.Ext,
                ["w"] = first.Width, ["h"] = first.Height, ["frames"] = frames.Select(f => StemOf(f[(f.LastIndexOf('/') + 1)..])).ToList(),
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(digest.ToString())))[..KeyedFrames.ShaLength],
            };
            if (frames.All(f => done[f].At is not null)) record["at"] = frames.SelectMany(f => new[] { done[f].At!.Value.X, done[f].At!.Value.Y }).ToList();
            var names = ff.Anims.GetValueOrDefault(anim) ?? [];
            anims.Add(Mark(record, names.Count > 0 ? built.GetValueOrDefault(names[0])?.Opacity : null));
        }
        var text = new StringBuilder();
        text.Append("{\"archive\":").Append(PyJson.Compact(Path.GetFileName(ff.Path))).Append(",\n\"images\":[\n");
        text.Append(string.Join(",\n", records.Select(PyJson.Compact)));
        text.Append("\n],\n\"anims\":[\n");
        text.Append(string.Join(",\n", anims.Select(PyJson.Compact)));
        text.Append("\n]}\n");
        return (text.ToString(), keyed, records.Count, anims.Count, frameCount);
    }

    private static PyObj Mark(PyObj record, int? opacity)
    {
        if (opacity is { } o && o != Opaque) record["opacity"] = o;
        return record;
    }

    /// <summary>Имя без последнего расширения (как <c>Path(...).stem</c>).</summary>
    private static string StemOf(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }
}
