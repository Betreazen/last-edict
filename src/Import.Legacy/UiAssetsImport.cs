namespace D2.Data.Import;

/// <summary>Графика интерфейса: архивы <c>Interf/*.ff</c> → <c>assets/ui/&lt;Архив&gt;/frames|anims/…webp</c> и опись <c>data/ui/assets.json</c>
/// (перенос <c>tools/ui-assets/build_ui_assets.py</c>; опись — те же байты). Интерфейс не обрезается: рантайм берёт его картинки целыми.
/// <c>MenuAnim.ff</c> не берётся — в нём только ролики Bink (<see cref="VideoImport"/>).</summary>
public static class UiAssetsImport
{
    public const string Step = "ui";
    public static readonly string[] Archives = ["Interf/Interf.ff", "Interf/IntfScen.ff", "Interf/MenuImgs.ff"];

    /// <exception cref="FileNotFoundException">Нет архива интерфейса в копии игры.</exception>
    public static void Run(ImportContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        foreach (string rel in Archives)
            if (!File.Exists(ctx.Game(rel))) throw new FileNotFoundException($"нет {rel} в {ctx.GameDir}", ctx.Game(rel));
        string final = Path.Combine(ctx.AssetsDir, "ui");
        ImportFiles.Recover(final);
        string built = ImportFiles.Fresh(final + ".new");
        var rows = new List<PyObj>();
        var plans = new List<(string rel, FfArchive archive)>();
        try
        {
            foreach (string rel in Archives) plans.Add((rel, new FfArchive(ctx.Game(rel))));
            var all = plans.Select(p => (p.rel, p.archive, entries: Plan(p.archive))).ToList();
            int total = all.Sum(p => p.entries.Count), written = 0;
            ctx.Report(Step, 0, total);
            var done = new Dictionary<string, FrameDone>(StringComparer.Ordinal);
            foreach (var (_, archive, entries) in all)
                foreach (var kv in FrameWriter.Render(ctx, archive, [.. entries.Select(e => e.Item)], built, Step,
                                                      n => ctx.Report(Step, Interlocked.Add(ref written, n), total)))
                    done[kv.Key] = kv.Value;
            int verified = FrameWriter.Verify(ctx, built, done);
            ctx.Say($"сверено заново: {verified} файлов, расхождений 0");
            foreach (var (rel, _, entries) in all)
            {
                long keyed = 0;
                string archiveName = Path.GetFileName(rel);
                foreach (var (item, name, anim) in entries)
                {
                    var d = done[item.Rel];
                    keyed += d.Keyed;
                    var row = new PyObj { ["name"] = name, ["archive"] = archiveName, ["file"] = item.Rel, ["w"] = d.Width, ["h"] = d.Height, ["sha256"] = d.Sha };
                    if (anim is not null) row["anim"] = anim;
                    rows.Add(row);
                }
                ctx.Say($"{archiveName}: кадров {entries.Count}, анимаций {entries.Select(e => e.Anim).Where(a => a is not null).Distinct().Count()}, точек ключа {keyed}");
            }
        }
        finally
        {
            foreach (var p in plans) p.archive.Dispose();
        }
        if (rows.Count != rows.Select(r => ((string)r["archive"]!, (string)r["name"]!)).Distinct().Count())
            throw new InvalidDataException("повторы пары архив+имя в описи интерфейса");
        var sorted = rows.OrderBy(r => (string)r["archive"]!, StringComparer.Ordinal).ThenBy(r => (string)r["name"]!, StringComparer.Ordinal).ToList();
        ImportFiles.Swap(built, final);
        ImportFiles.WriteText(Path.Combine(ctx.DataDir, "ui", "assets.json"), "[\n" + string.Join(",\n", sorted.Select(PyJson.Compact)) + "\n]\n");
        ctx.Say($"data/ui/assets.json: {sorted.Count} кадров");
    }

    /// <summary>Кадры архива к записи: (кадр, имя в описи, анимация или <c>null</c>). Кадр анимации адресуется «&lt;АНИМ&gt;/&lt;NNN&gt;»: внутриигровые
    /// имена у них односимвольные и повторяются между анимациями.</summary>
    public static List<(FrameItem Item, string Name, string? Anim)> Plan(FfArchive ff)
    {
        ArgumentNullException.ThrowIfNull(ff);
        string stem = Path.GetFileNameWithoutExtension(ff.Path);
        var inAnims = ff.Anims.Values.SelectMany(n => n).ToHashSet(StringComparer.Ordinal);
        var built = ff.Frames.Where(kv => ff.Buildable(kv.Value)).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        var out_ = new List<(FrameItem, string, string?)>();
        foreach (string name in built.Where(n => !inAnims.Contains(n)).Order(StringComparer.Ordinal))
            out_.Add((new FrameItem(false, name, $"{stem}/frames/{name}{KeyedFrames.Ext}", false), name, null));
        foreach (var (anim, names) in ff.Anims)
            for (int i = 0; i < names.Count; i++)
                if (built.Contains(names[i]))
                    out_.Add((new FrameItem(false, names[i], $"{stem}/anims/{anim}/{i:000}_{names[i]}{KeyedFrames.Ext}", false), $"{anim}/{i:000}", anim));
        return out_;
    }
}
