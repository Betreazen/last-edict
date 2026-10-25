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
        string dir = outDir is null ? project.PathOf(Path.Combine(".studio", "pack")) : Path.GetFullPath(outDir);
        string inside = Path.GetRelativePath(project.Dir, dir).Replace('\\', '/');
        if (!inside.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(inside) && inside != ".studio" && !inside.StartsWith(".studio/", StringComparison.Ordinal))
            throw new InvalidOperationException($"архив внутри мода — только в .studio (иначе он попал бы в себя): {dir}");
        var files = Directory.EnumerateFiles(project.Dir, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(project.Dir, f).Replace('\\', '/')))
            .Where(f => !Skipped(f.Relative))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();
        string version = doc.Manifest?["version"]?.GetValue<string>() is { Length: > 0 } v ? v : "0";
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
            ["format"] = 1, ["id"] = project.Id, ["name"] = project.Info.Name, ["version"] = version, ["executesCode"] = project.ExecutesCode,
            ["rulesetHash"] = hash, ["files"] = entries,
        };
        Directory.CreateDirectory(dir);
        string archive = Path.Combine(dir, $"{project.Id}-{version}.zip");
        string temp = archive + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var (full, relative) in files) zip.CreateEntryFromFile(full, relative, CompressionLevel.Optimal);
            using var writer = new StreamWriter(zip.CreateEntry(ManifestFile, CompressionLevel.Optimal).Open());
            writer.Write(manifest.ToJsonString(ContentJson.Options).ReplaceLineEndings("\n") + "\n");
        }
        File.Move(temp, archive, overwrite: true);
        return new PackResult(archive, files.Count, bytes, hash);
    }

    /// <summary>Сверить архив с его описью: каждый файл описи есть, размер и SHA-256 — те же. Несовпадения — списком.</summary>
    public static IReadOnlyList<string> Verify(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        var manifestEntry = zip.GetEntry(ManifestFile) ?? throw new InvalidDataException($"{archive}: нет {ManifestFile}");
        JsonNode? manifest;
        using (var stream = manifestEntry.Open()) manifest = JsonNode.Parse(stream);
        var problems = new List<string>();
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
