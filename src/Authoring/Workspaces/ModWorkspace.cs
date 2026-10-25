using System.Text.Json.Nodes;
using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Packaging;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Validation;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Workspaces;

/// <summary>
/// Рабочее пространство «Мод» (Студия 2, задача 6): описание мода (<c>mod.json</c>: имя, версия, описание) и порядок наложения — «этот мод
/// главнее» другого (<c>loadAfter</c>) — командами с отменой; конфликты слоёв с модами игры и модами рядом (каталоги с <c>mod.json</c> в той
/// же папке, что этот: так лежат моды автора, например <c>../le-mods</c>); упаковка (<see cref="ModPack"/>) — в <c>.studio/pack</c> мода.
/// </summary>
public sealed class ModWorkspace(StudioSession session)
{
    public StudioSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

    private IReadOnlyList<ModInfo>? _others;

    /// <summary>Моды рядом с этим (соседние каталоги с <c>mod.json</c>) и моды игры, кроме этого, — без повторов по id. Кэш — до
    /// <see cref="RefreshOthers"/> (каталоги не перебираются на каждую правку).</summary>
    public IReadOnlyList<ModInfo> Others() => _others ??= FindOthers();

    public void RefreshOthers() => _others = null;

    private IReadOnlyList<ModInfo> FindOthers()
    {
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Session.Project.Dir));
        var near = parent is null ? [] : Directory.EnumerateDirectories(parent)
            .Where(d => File.Exists(Path.Combine(d, ModProject.ManifestFile)) && !Path.GetFullPath(d).Equals(Session.Project.Dir, StringComparison.OrdinalIgnoreCase))
            .Select(TryOpen).OfType<ModInfo>();
        return [.. Session.Others.Concat(near).Where(m => m.Id != Session.Project.Id).DistinctBy(m => m.Id).OrderBy(m => m.Id, StringComparer.Ordinal)];
    }

    private static ModInfo? TryOpen(string dir)
    {
        try
        {
            return ModProject.Open(dir).Info;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return null;   // битый соседний мод — не повод не показывать свой
        }
    }

    /// <summary>Поле описания мода (имя, версия, описание).</summary>
    public string Field(string field) => Session.History.Current.Manifest?[field] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public bool SetField(string field, string value) =>
        Session.Do(new SetManifest(field, value.Length == 0 ? null : JsonValue.Create(value)));

    /// <summary>Этот мод главнее <paramref name="other"/> (стоит после него: <c>loadAfter</c>).</summary>
    public bool IsAbove(string other) =>
        Session.History.Current.Manifest?["loadAfter"] is JsonArray after && after.Any(v => v?.GetValue<string>() == other);

    /// <summary>Сделать этот мод главнее <paramref name="other"/> или снять это (тогда порядок — по списку модов). Одна команда.</summary>
    public bool SetAbove(string other, bool above)
    {
        var list = (Session.History.Current.Manifest?["loadAfter"] as JsonArray)?.Select(v => v?.GetValue<string>()).OfType<string>().ToList() ?? [];
        list.Remove(other);
        if (above) list.Add(other);
        return Session.Do(new SetManifest("loadAfter", list.Count == 0 ? null : new JsonArray([.. list.Select(i => (JsonNode)JsonValue.Create(i)!)])));
    }

    /// <summary>Конфликты этого мода со всеми модами рядом и игры (несохранённое — тоже).</summary>
    public IReadOnlyList<LayerConflict> Conflicts()
    {
        RefreshOthers();
        var report = ModCheck.Run(Session.History.Current, Session.Project, Session.BaseDir, Others());
        return LayerConflicts.Of(report.Layers, Session.Project.Id);
    }

    /// <summary>Записать мод и упаковать; итог — строкой для статуса.</summary>
    public string Pack()
    {
        try
        {
            if (Session.History.Dirty) Session.Save();
            var r = ModPack.Pack(Session.Project, Session.BaseDir, Session.Others);
            var problems = ModPack.Verify(r.Archive);
            return problems.Count == 0 ? $"упаковано: {r.Archive} — файлов {r.Files}, {r.Bytes / 1024} КБ" : $"архив не сошёлся с описью: {string.Join("; ", problems)}";
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Session.Refuse(e.Message);
            return e.Message;
        }
    }
}
