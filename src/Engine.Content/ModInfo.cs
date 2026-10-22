namespace D2.Data.Content;

/// <summary>Мод-слой: каталог с <c>mod.json</c>. <see cref="Id"/> — устойчивый идентификатор (по нему мод
/// поминают в отчётах и в списке включённых), <see cref="Dir"/> — полный путь каталога. <see cref="Dependencies"/> — моды, без которых
/// этот не работает и после которых накладывается, <see cref="LoadAfter"/> — моды, после которых накладывается, если они есть
/// (<see cref="ContentLayers.Order"/>).</summary>
public sealed record ModInfo(string Id, string Name, string Dir)
{
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<string> LoadAfter { get; init; } = [];
}
