namespace LastEdict.Data.Ui;

/// <summary>Совершённый переход: с какого экрана, от какого элемента, что сделано и куда. <see cref="Via"/> —
/// промежуточный экран (анимация смены меню, «подождите»), <see cref="Phase"/> — фаза после перехода,
/// <see cref="Message"/> — id текста окна сообщения, если переход его показал.</summary>
public sealed record UiTransition(string From, string? Element, UiFlowAction Action, string? Target, string? Via,
                                  UiPhase? Phase, string? Message);

/// <summary>Исполнение графа экранов (<see cref="UiFlow"/>): стек открытых экранов и фаза игры. Сцена сообщает,
/// какой элемент верхнего экрана сработал, навигатор находит переход в графе и меняет стек: <c>Open</c> кладёт цель
/// поверх, <c>Goto</c> заменяет верхний экран целью, <c>Close</c> снимает верхний, <c>Panel</c> и <c>None</c> стек
/// не трогают. Пустой стек — конец игры (<see cref="Quit"/>).
///
/// <para>У элемента бывает несколько исходов («загрузить» ведёт в партию или к окну ошибки). Какой из них
/// случился, решает логика экрана — она передаёт свой выбор в <see cref="Activate"/>. Без него берётся переход со
/// сменой экрана, из них — встреченный в журналах чаще: исход «ничего не случилось» в графе обычно означает
/// нажатие, которое игра отклонила (не выбран сценарий, повторный щелчок).</para></summary>
public sealed class UiNavigator
{
    private readonly UiFlow _flow;
    private readonly List<string> _stack;

    public UiNavigator(UiFlow flow, string start, UiPhase phase = UiPhase.Menu)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentException.ThrowIfNullOrEmpty(start);
        _flow = flow;
        _stack = [start];
        Phase = phase;
    }

    /// <summary>Открытые экраны снизу вверх.</summary>
    public IReadOnlyList<string> Stack => _stack;

    public string? Top => _stack.Count == 0 ? null : _stack[^1];

    public UiPhase Phase { get; private set; }

    /// <summary>Снят последний экран: игра окончена.</summary>
    public bool Quit => _stack.Count == 0;

    /// <summary>Переходы элемента верхнего экрана по графу; пусто — элемент экран не меняет. <c>null</c> — переходы без элемента: экран
    /// уходит по любому щелчку или клавише (титры <c>DLG_CREDITS</c>).</summary>
    public IReadOnlyList<UiFlowEdge> EdgesOf(string? element)
    {
        var screen = _flow.Screens.FirstOrDefault(s => string.Equals(s.Id, Top, StringComparison.OrdinalIgnoreCase));
        return screen is null ? [] : [.. screen.Edges.Where(e => e.Element == element)];
    }

    /// <summary>Сработал элемент верхнего экрана. <paramref name="choose"/> — выбор исхода логикой экрана;
    /// <c>null</c> от него — переход не случился. Возвращает совершённый переход или <c>null</c>.</summary>
    public UiTransition? Activate(string? element, Func<IReadOnlyList<UiFlowEdge>, UiFlowEdge?>? choose = null)
    {
        if (Top is not { } from) return null;
        var edges = EdgesOf(element);
        if (edges.Count == 0) return null;
        var edge = (choose ?? Default)(edges);
        if (edge is null) return null;

        switch (edge.Action)
        {
            case UiFlowAction.Open when edge.Target is { } open:
                _stack.Add(open);
                break;
            case UiFlowAction.Goto when edge.Target is { } target:
                _stack[^1] = target;
                break;
            case UiFlowAction.Close:
                _stack.RemoveAt(_stack.Count - 1);
                break;
        }
        if (edge.Phase is { } phase) Phase = phase;
        return new UiTransition(from, element, edge.Action, edge.Target, edge.Via, edge.Phase, edge.Message);
    }

    /// <summary>Открыть экран поверх не по графу, а по решению игры (свой город при входе отряда, окно вопроса по просьбе
    /// логики окна). Фаза не меняется.</summary>
    public void Push(string screen)
    {
        ArgumentException.ThrowIfNullOrEmpty(screen);
        _stack.Add(screen);
    }

    /// <summary>Снять верхний экран (окно закрыло себя само). Нижний — корень — не снимается.</summary>
    /// <returns>Снят ли экран.</returns>
    public bool Pop()
    {
        if (_stack.Count < 2) return false;
        _stack.RemoveAt(_stack.Count - 1);
        return true;
    }

    /// <summary>Исход по умолчанию: со сменой экрана и самый частый; если смены нет ни в одном — первый.</summary>
    private static UiFlowEdge? Default(IReadOnlyList<UiFlowEdge> edges) =>
        edges.Where(e => e.Action != UiFlowAction.None).OrderByDescending(e => e.Seen).FirstOrDefault()
        ?? edges.FirstOrDefault();
}
