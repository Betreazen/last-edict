using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace LastEdict.Data.Ui;

/// <summary>Что делает переход с экрана. Свой перечень, а не события зонда: порту нужен смысл, а не вызовы
/// менеджера интерфейса.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiFlowAction
{
    /// <summary>Цель встаёт поверх текущего экрана (окно, всплывающее сообщение); текущий остаётся под ней.</summary>
    Open,

    /// <summary>Текущий экран уходит, цель встаёт на его место (смена меню, вход в город, начало партии).</summary>
    Goto,

    /// <summary>Текущий экран закрывается, наверху оказывается тот, что был под ним.</summary>
    Close,

    /// <summary>Внутри текущего экрана сменилась вложенная панель (закладка постройки, панель ожидания хода).</summary>
    Panel,

    /// <summary>Обработчик сработал, экран не сменился (прокрутка, выбор в списке, переключатель).</summary>
    None,
}

/// <summary>Фаза игры: меню (до партии), партия (карта, города, окна) и бой.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiPhase { Menu, Game, Battle }

/// <summary>Переход с экрана. <see cref="Element"/> — Id элемента в <c>screens.json</c>; <c>null</c> — переход не
/// от элемента интерфейса (щелчок по карте, правая кнопка, таймер, ход ИИ, событие сценария).
/// <see cref="Target"/> — Id экрана, на который ведёт <see cref="UiFlowAction.Open"/>/<see cref="UiFlowAction.Goto"/>/
/// <see cref="UiFlowAction.Panel"/>; <see cref="Via"/> — промежуточный экран (анимация смены меню, «подождите»);
/// <see cref="Phase"/> — фаза, в которую переход переводит игру; <see cref="Message"/> — id текста окна сообщения,
/// которое переход показал; <see cref="Seen"/> — сколько раз переход встретился в журналах (для правки руками
/// не нужен, мод может не писать).</summary>
public sealed record UiFlowEdge
{
    public string? Element { get; init; }
    public required UiFlowAction Action { get; init; }
    public string? Target { get; init; }
    public string? Via { get; init; }
    public UiPhase? Phase { get; init; }
    public string? Message { get; init; }
    public int Seen { get; init; }
}

/// <summary>Переходы одного экрана и фазы, в которых он встречается.</summary>
public sealed record UiFlowScreen
{
    public required string Id { get; init; }
    public IReadOnlyList<UiPhase> Phases { get; init; } = [];
    public IReadOnlyList<UiFlowEdge> Edges { get; init; } = [];
}

/// <summary>Экран движка без своей разметки в <c>screens.json</c> (заставка, загрузка, всплывающая энциклопедия):
/// на него ведут переходы, но элементов у него нет. <see cref="Kind"/> — вид по зонду
/// (<c>menu</c>/<c>popup</c>/<c>fullscreen</c>/<c>other</c>).</summary>
public sealed record UiFlowEngineScreen(string Id, string Kind);

/// <summary>Граф экранов: какой элемент какого экрана куда ведёт. <see cref="Sources"/> — журналы, из которых
/// граф выведен (пути от корня корпуса).</summary>
public sealed record UiFlow
{
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<UiFlowEngineScreen> EngineScreens { get; init; } = [];
    public IReadOnlyList<UiFlowScreen> Screens { get; init; } = [];
}

/// <summary>Граф экранов: чтение, запись, наложение мода. Формат — как у <see cref="UiScreens"/>.</summary>
public static class UiFlows
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyLists } },
    };

    private static void SkipEmptyLists(JsonTypeInfo type)
    {
        foreach (var p in type.Properties)
            p.ShouldSerialize = (_, value) =>
                value is not null && (value is not System.Collections.ICollection list || list.Count > 0);
    }

    public static string ToJson(UiFlow flow) => JsonSerializer.Serialize(flow, Json).ReplaceLineEndings("\n");

    public static UiFlow FromJson(string json) =>
        JsonSerializer.Deserialize<UiFlow>(json, Json) ?? throw new FormatException("пустой граф экранов");

    public static UiFlow Load(string path) => FromJson(File.ReadAllText(path));

    public static void Save(string path, UiFlow flow) => File.WriteAllText(path, ToJson(flow));

    /// <summary>Наложить граф мода поверх базового. Экран опознаётся по <c>Id</c>, переход — по паре
    /// (экран, <c>Element</c>): все переходы мода с этим элементом заменяют все базовые с тем же элементом
    /// (у одного элемента бывает несколько исходов — «загрузить» ведёт в игру или к окну ошибки). Прочие переходы
    /// экрана остаются. <see cref="UiFlowScreen.Phases"/> мода, если заданы, заменяют базовые. Новые экраны
    /// и экраны движка добавляются в конец.</summary>
    public static UiFlow Merge(UiFlow baseFlow, UiFlow overlay)
    {
        ArgumentNullException.ThrowIfNull(baseFlow);
        ArgumentNullException.ThrowIfNull(overlay);
        var over = overlay.Screens.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var screens = baseFlow.Screens.Select(s => over.Remove(s.Id, out var o) ? MergeScreen(s, o) : s).ToList();
        screens.AddRange(over.Values);

        var engine = baseFlow.EngineScreens.ToList();
        engine.AddRange(overlay.EngineScreens.Where(e =>
            !engine.Any(b => string.Equals(b.Id, e.Id, StringComparison.OrdinalIgnoreCase))));

        return baseFlow with { EngineScreens = engine, Screens = screens };
    }

    private static UiFlowScreen MergeScreen(UiFlowScreen b, UiFlowScreen o)
    {
        var replaced = o.Edges.Select(e => e.Element).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var edges = b.Edges.Where(e => !replaced.Contains(e.Element)).Concat(o.Edges).ToList();
        return b with { Phases = o.Phases.Count > 0 ? o.Phases : b.Phases, Edges = edges };
    }
}
