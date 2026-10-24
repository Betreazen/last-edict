namespace LastEdict.Data.Content;

/// <summary>Мод-слой: каталог с <c>mod.json</c>. <see cref="Id"/> — устойчивый идентификатор (по нему мод
/// поминают в отчётах и в списке включённых), <see cref="Dir"/> — полный путь каталога. <see cref="Dependencies"/> — моды, без которых
/// этот не работает и после которых накладывается, <see cref="LoadAfter"/> — моды, после которых накладывается, если они есть
/// (<see cref="ContentLayers.Order"/>).</summary>
public sealed record ModInfo(string Id, string Name, string Dir)
{
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> LoadAfter { get; init; } = [];

    /// <summary>Сборки C#-плагина мода (<c>plugins/*.dll</c>, Р21): механики, которых нет в данных. Есть хоть одна — мод <b>исполняет код</b>
    /// (<see cref="ExecutesCode"/>): игра и Студия помечают его так, включать такой мод — только доверенный (репозиторий публичный, Р23).</summary>
    public IReadOnlyList<string> Plugins { get; init; } = [];

    public bool ExecutesCode => Plugins.Count > 0;

    /// <summary>Сборки плагина в каталоге мода <paramref name="dir"/> (<c>plugins/*.dll</c>), по имени.</summary>
    public static IReadOnlyList<string> PluginsIn(string dir)
    {
        string plugins = Path.Combine(dir, "plugins");
        return Directory.Exists(plugins) ? [.. Directory.EnumerateFiles(plugins, "*.dll").Order(StringComparer.OrdinalIgnoreCase)] : [];
    }
}
