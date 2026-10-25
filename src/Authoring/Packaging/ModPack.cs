using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Validation;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Packaging;

/// <summary>Итог упаковки: архив, файлов, байт (несжатых), хеш правил (null — функции хеша ещё нет).</summary>
public sealed record PackResult(string Archive, int Files, long Bytes, string? RulesetHash);

/// <summary>
/// Упаковка мода (Студия 2, задача 6; <c>docs/tools/modding-kit/spec.md</c> §10): мод проверяется (<see cref="ModCheck"/>; с ошибками — отказ),
/// файлы мода (без <c>.studio/</c>, <c>.git</c>, временных) идут в архив <c>&lt;id&gt;-&lt;версия&gt;.zip</c> по порядку имён, рядом с ними —
/// <c>manifest.json</c>: id, имя, версия, «исполняет код», опись файлов (путь, размер, SHA-256) и хеш правил партии (<c>rulesetHash</c> —
/// функция сетевой игры Э7, <c>RulesetHash.Of(mod)</c>; пока её нет в сборке — <c>null</c>, место оставлено). Архив по умолчанию — в
/// <c>&lt;мод&gt;/.studio/pack</c>: Студия пишет только в свой мод.
/// </summary>
public static class ModPack
{
    public const string ManifestFile = "manifest.json";

    /// <summary>Каталоги и файлы, которые в архив не идут.</summary>
    private static bool Skipped(string relative) =>
        relative.StartsWith(".studio/", StringComparison.Ordinal) || relative.StartsWith(".git/", StringComparison.Ordinal)
        || relative == ".git" || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

    /// <summary>Упаковать мод. <paramref name="outDir"/> — каталог архива (нет — <c>&lt;мод&gt;/.studio/pack</c>); внутри мода — только
    /// <c>.studio</c> (иначе архив попал бы в себя). <paramref name="rulesetHash"/> — функция хеша правил (Э7), нет — без хеша.</summary>
    public static PackResult Pack(ModProject project, string baseDir, IReadOnlyList<ModInfo>? others = null, string? outDir = null,
                                  Func<ModInfo, string>? rulesetHash = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var doc = ModDocument.Load(project);
        var report = ModCheck.Run(doc, project, baseDir, others);
        if (report.Errors > 0)
            throw new InvalidOperationException($"мод с ошибками не упаковывается ({report.Errors}): {report.Items.First(i => i.Level == CheckLevel.Error)}");
        string dir = Path.TrimEndingDirectorySeparator(outDir is null ? project.PathOf(Path.Combine(".studio", "pack")) : Path.GetFullPath(outDir));
        if (Within(dir, project.Dir) && !Within(dir, Path.Combine(project.Dir, ".studio")))
            throw new InvalidOperationException($"архив внутри мода — только в .studio (иначе он попал бы в себя): {dir}");
        if (File.Exists(Path.Combine(project.Dir, ManifestFile)))
            throw new InvalidOperationException($"{ManifestFile} в корне мода — имя описи архива: переименуйте файл мода");
        var files = Directory.EnumerateFiles(project.Dir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(project.Dir, f).Replace('\\', '/')))
            .Where(f => !Skipped(f.Relative))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();
        string version = doc.Manifest?["version"] is JsonValue jv && jv.TryGetValue<string>(out var v) && LastEdict.Authoring.Commands.SetManifest.ValidVersion(v) ? v : "0";
        string name = doc.Manifest?["name"] is JsonValue nv && nv.TryGetValue<string>(out var n) ? n : project.Info.Name;
        var entries = new JsonArray();
        long bytes = 0;
        foreach (var (full, relative) in files)
        {
            var data = File.ReadAllBytes(full);
            bytes += data.Length;
            entries.Add(new JsonObject { ["file"] = relative, ["size"] = data.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(data)) });
        }
        string? hash = rulesetHash?.Invoke(project.Info);
        var manifest = new JsonObject
        {
            ["format"] = 1, ["id"] = project.Id, ["name"] = name, ["version"] = version, ["executesCode"] = project.ExecutesCode,
            ["rulesetHash"] = hash, ["files"] = entries,
        };
        Directory.CreateDirectory(dir);
        string archive = Path.Combine(dir, $"{project.Id}-{version}.zip");
        if (!Within(archive, dir)) throw new InvalidOperationException($"имя архива вне каталога: {archive}");
        string temp = archive + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            // Время записей — одно на всех: тот же мод — тот же архив байт в байт.
            foreach (var (full, relative) in files)
            {
                var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = Stamp;
                using var to = entry.Open();
                using var from = File.OpenRead(full);
                from.CopyTo(to);
            }
            var manifestEntry = zip.CreateEntry(ManifestFile, CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = Stamp;
            using var writer = new StreamWriter(manifestEntry.Open());
            writer.Write(manifest.ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n");
        }
        File.Move(temp, archive, overwrite: true);
        return new PackResult(archive, files.Count, bytes, hash);
    }

    /// <summary>Время записей архива (одно на всех).</summary>
    private static readonly DateTimeOffset Stamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Путь внутри каталога (или он сам) — по полным путям, а не по началу строки «..».</summary>
    private static bool Within(string path, string dir)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase) || full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Сверить архив с его описью: каждый файл описи есть, размер и SHA-256 — те же, лишних записей нет. Несовпадения — списком.</summary>
    public static IReadOnlyList<string> Verify(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        var manifestEntry = zip.GetEntry(ManifestFile) ?? throw new InvalidDataException($"{archive}: нет {ManifestFile}");
        JsonNode? manifest;
        using (var stream = manifestEntry.Open()) manifest = JsonNode.Parse(stream);
        var problems = new List<string>();
        if (manifest?["format"]?.GetValue<int>() != 1 || manifest["id"]?.GetValue<string>() is not { Length: > 0 }) problems.Add("опись без формата или id");
        var listed = (manifest?["files"] as JsonArray ?? []).OfType<JsonObject>().Select(f => f["file"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(zip.Entries.Select(e => e.FullName).Where(n => n != ManifestFile && !listed.Contains(n)).Select(n => $"лишняя запись {n}"));
        foreach (var file in (manifest?["files"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string name = file["file"]!.GetValue<string>();
            if (zip.GetEntry(name) is not { } entry)
            {
                problems.Add($"нет {name}");
                continue;
            }
            using var s = entry.Open();
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            if (copy.Length != file["size"]!.GetValue<long>() || Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())) != file["sha256"]!.GetValue<string>())
                problems.Add($"{name}: размер или хеш не те");
        }
        return problems;
    }
}
