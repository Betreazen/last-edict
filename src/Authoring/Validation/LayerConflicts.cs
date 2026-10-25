using LastEdict.Data.Content;

namespace LastEdict.Authoring.Validation;

/// <summary>Конфликт слоёв: поле сущности правят несколько модов — действует последний по порядку наложения (<see cref="Winner"/>).</summary>
public sealed record LayerConflict(string Entity, string Field, IReadOnlyList<string> Mods, string Winner);

/// <summary>
/// Конфликты слоёв модов (Студия 2, задача 6) по отчёту наложения (<see cref="LayerChange"/>, порядок — порядок слоёв): одно поле одной
/// сущности правят два мода и больше, или один мод убирает сущность, которую правит другой. Кто главнее — последний слой; порядок задают
/// <c>dependencies</c> и <c>loadAfter</c> в <c>mod.json</c> (Студия правит его командой).
/// </summary>
public static class LayerConflicts
{
    /// <summary>Убранная сущность — поле «(убрана)»: конфликт с любой правкой её полей другим модом.</summary>
    public const string RemovedField = "(убрана)";

    /// <summary>Конфликты по отчёту; <paramref name="involving"/> — только с этим модом.</summary>
    public static IReadOnlyList<LayerConflict> Of(IReadOnlyList<LayerChange> layers, string? involving = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var touched = new Dictionary<(string Id, string Field), List<string>>();
        var removedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var change in layers)
        {
            if (change.Action == LayerAction.Removed)
            {
                (removedBy.TryGetValue(change.Id, out var list) ? list : removedBy[change.Id] = []).Add(change.Mod);
                continue;
            }
            if (change.Action != LayerAction.Patched) continue;
            foreach (string field in change.Fields)
            {
                var key = (change.Id, field);
                var mods = touched.TryGetValue(key, out var m) ? m : touched[key] = [];
                if (!mods.Contains(change.Mod)) mods.Add(change.Mod);
            }
        }
        var result = new List<LayerConflict>();
        foreach (var ((id, field), mods) in touched.Where(p => p.Value.Count > 1))
            result.Add(new LayerConflict(id, field, mods, mods[^1]));
        foreach (var (id, removers) in removedBy)
        {
            var patchers = touched.Where(p => p.Key.Id == id).SelectMany(p => p.Value).Distinct().Except(removers).ToList();
            if (patchers.Count == 0 && removers.Count < 2) continue;
            var mods = patchers.Concat(removers).Distinct().ToList();
            result.Add(new LayerConflict(id, RemovedField, mods, removers[^1]));
        }
        return [.. result.Where(c => involving is null || c.Mods.Contains(involving)).OrderBy(c => c.Entity, StringComparer.Ordinal)
            .ThenBy(c => c.Field, StringComparer.Ordinal)];
    }
}
