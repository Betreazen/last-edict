using D2.Data.Menu;

namespace D2.Data.Ui.Windows;

/// <summary>Просьба логики окна к хозяину (экран карты держит стек окон): открыть поверх окно, спросить «Да/Нет»,
/// показать сообщение, закрыть себя.</summary>
public abstract record UiRequest;

/// <summary>Вопрос в окне сообщения (<c>DLG_MESSAGE_BOX</c>, кнопки «Да»/«Нет»): «Да» исполняет <see cref="Yes"/>.</summary>
public sealed record UiAsk(string Text, Action Yes) : UiRequest;

/// <summary>Сообщение с одной кнопкой «ОК».</summary>
public sealed record UiTell(string Text) : UiRequest;

/// <summary>Открыть окно <see cref="Screen"/> поверх, с логикой <see cref="Logic"/>.</summary>
public sealed record UiOpen(string Screen, IScreenLogic Logic) : UiRequest;

/// <summary>Закрыть это окно.</summary>
public sealed record UiClose : UiRequest;

/// <summary>Экран уходит по переходу графа без элемента (ролики вступления <c>DLG_CINEMATIC</c> → главное меню).</summary>
public sealed record UiLeave : UiRequest;

/// <summary>Выделить отряд на карте и показать его (вывод героя из города, <c>0x48e2c2</c>, <c>0x540c38</c>).</summary>
public sealed record UiFocus(string StackId) : UiRequest;

/// <summary>Звук интерфейса (архив <c>Midgard</c>, <see cref="D2.Data.Sound.WindowSounds"/>), когда его решает логика, а не нажатие само
/// по себе: смена лица лорда звучит, только если нажатие принято (<c>CHNGFACE</c>, <c>0x4e6945</c>).</summary>
public sealed record UiSound(string Name) : UiRequest;

/// <summary>Мелодия поверх музыки (дорожка каталога <c>Music</c>, громкостью музыки): музыка затухает <see cref="FadeMs"/>, затем мелодия
/// играет один раз; её конец хозяин сообщает логике (<see cref="ITuneListener.TuneEnded"/>). Звук эпилога (<c>0x4bca73</c>).</summary>
public sealed record UiTune(string Name, int FadeMs) : UiRequest;

/// <summary>Логика, которая ждёт конца своей мелодии (<see cref="UiTune"/>).</summary>
public interface ITuneListener
{
    void TuneEnded(UiScreenState ui);
}

/// <summary>
/// Логика окна игры поверх карты: сверх <see cref="IScreenLogic"/> — своя форма экрана (элементы, которых нет в разметке,
/// например ячейки групп юнитов), мышь вне элементов разметки (перетаскивание юнитов) и просьбы к хозяину.
/// Хозяин зовёт <see cref="Resume"/>, когда окно снова наверху (закрылось окно над ним).
/// </summary>
public interface IWindowLogic : IScreenLogic
{
    /// <summary>Нажатие левой кнопки в точке экрана (координаты разметки).</summary>
    void Press(int x, int y, UiScreenState ui)
    {
    }

    /// <summary>Отпускание левой кнопки.</summary>
    void Release(int x, int y, UiScreenState ui)
    {
    }

    /// <summary>Мышь двинулась (координаты разметки; кнопка может быть зажата) — перетаскивание текста «Задания» (<c>0x482062</c>).</summary>
    void Motion(int x, int y, UiScreenState ui)
    {
    }

    /// <summary>Двойной щелчок левой кнопкой (второе нажатие) — после <see cref="Press"/>.</summary>
    void DoubleClick(int x, int y, UiScreenState ui)
    {
    }

    /// <summary>Нажатие правой кнопки (карточка юнита, пока кнопка держится).</summary>
    void RightPress(int x, int y, UiScreenState ui)
    {
    }

    /// <summary>Отпускание правой кнопки.</summary>
    void RightRelease(int x, int y, UiScreenState ui)
    {
    }

    /// <summary>Нажатие клавиши (виртуальный код Windows; клавиша без кода — 0) раньше горячих клавиш кнопок разметки: true — логика
    /// её съела, горячие клавиши не срабатывают (у движка — свой обработчик клавиатуры окна, например «Задание» <c>0x48201d</c>). Пока у
    /// поля ввода фокус, клавиши сюда не приходят.</summary>
    bool Key(int vk, UiScreenState ui) => false;

    /// <summary>Что сейчас тащат и примет ли его место под мышью (<c>CDragAndDropInterf</c> <c>0x56c158</c>): true — примет (курсор
    /// обычный), false — нет или вне мест броска (<c>NODRAGDROP</c>); null — ничего не тащат.</summary>
    bool? DropAccepts(int x, int y, UiScreenState ui) => null;

    /// <summary>Курсор инструмента окна в точке (лечение, воскрешение, отпуск, «Остановить продвижение» окна армии: <c>HEALVAL</c>/<c>HEALINV</c> и
    /// т. п. из <c>IsoCursr</c>); null — обычный. Спрашивается, пока ничего не тащат.</summary>
    string? ToolCursor(int x, int y, UiScreenState ui) => null;

    /// <summary>Кадр того, что тащат (портрет воина, значок предмета), — его движок кладёт на курсор тащения (<c>CDDUnitGroup</c>
    /// <c>0x56efc5</c>/<c>0x56f025</c>, <c>CDDFoldedInv</c> <c>0x56d84d</c>: картинка по середине курсора, <c>0x532df9</c>); ничего не тащат — null.</summary>
    string? DragImage => null;

    /// <summary>Окно снова наверху: содержимое могло измениться.</summary>
    void Resume(UiScreenState ui)
    {
    }

    /// <summary>Следующая просьба к хозяину; нет — null.</summary>
    UiRequest? TakeRequest();

    /// <summary>Строка чисел окна для проверки сцены без окна (<c>tools/ui-render-check.py</c>).</summary>
    string Summary => "";
}

/// <summary>Очередь просьб окна — общая часть логик окон.</summary>
public sealed class UiRequests
{
    private readonly Queue<UiRequest> _queue = new();

    public void Add(UiRequest request) => _queue.Enqueue(request);

    public UiRequest? Take() => _queue.Count > 0 ? _queue.Dequeue() : null;

    public int Count => _queue.Count;
}

/// <summary>
/// Окно сообщения (<c>DLG_MESSAGE_BOX</c>) по просьбе логики: вопрос — «Да»/«Нет» (кнопка «ОК» спрятана), сообщение —
/// одна «ОК». Окно закрывает себя само (<see cref="UiClose"/>), а не переходом графа: у <c>BTN_YES</c>/<c>BTN_OK</c> в графе
/// есть и переходы в чужие окна (выбор героя, конец партии), и какой из них «самый частый», решает корпус, а не это окно.
/// «Да» до закрытия исполняет действие вопроса, «Нет» и «ОК» — <paramref name="no"/> (у движка свой обработчик окна: «ОК» окна
/// «расторгните альянс» <c>0x4d201e</c> и «Нет» сносу жезла <c>0x4d1f06</c> стирают путь отряда, реестр Ин82).
/// </summary>
public sealed class MessageBoxLogic(string text, Action? yes, Action? no = null) : IWindowLogic
{
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public bool Allows(string element, UiScreenState ui) => element is not ("BTN_YES" or "BTN_NO" or "BTN_OK");

    public const string Screen = "DLG_MESSAGE_BOX";

    public static MessageBoxLogic For(UiAsk ask) => new(ask.Text, ask.Yes);

    public static MessageBoxLogic For(UiTell tell) => new(tell.Text, null);

    public bool Question => yes is not null;

    /// <summary>Вид для проверок: начало текста буквами и числа текста (подставленные цены, <c>%GOLD%</c>) через запятую, нет — «-».</summary>
    public string Summary
    {
        get
        {
            string plain = UiTextStyle.Parse(text).Text;
            string[] numbers = new string([.. plain.Select(c => char.IsAsciiDigit(c) ? c : ' ')]).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return $"сообщение={(Question ? "вопрос" : "ОК")} текст={new string([.. plain.Where(char.IsLetter).Take(24)])} " +
                   $"числа={(numbers.Length > 0 ? string.Join(',', numbers) : "-")}";
        }
    }

    public void Open(UiScreenState ui)
    {
        ui.SetText("TXT_INFO", text);
        Buttons(ui, Question);
    }

    /// <summary>Кнопки окна по <c>yesNo</c> <c>CMidgardMsgBox</c> (<c>0x5c7771</c>): вопрос — «Да»/«Нет», сообщение — одна «ОК»
    /// (корпус: <c>ui_msgbox</c> с <c>yes_no=false</c> — одна печать, снимок 19.10 свитка <c>X005TA0458</c>).</summary>
    public static void Buttons(UiScreenState ui, bool question)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ui.SetHidden("BTN_OK", question);
        ui.SetHidden("BTN_YES", !question);
        ui.SetHidden("BTN_NO", !question);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: "BTN_YES" or "BTN_NO" or "BTN_OK" } click) return;
        if (click.Element == "BTN_YES") yes?.Invoke();
        else no?.Invoke();
        _requests.Add(new UiClose());
    }
}

/// <summary>Подстановка значений в строку игры: <c>%NAME%</c> → значение.</summary>
public static class UiTemplate
{
    public static string Fill(string template, params (string Key, object Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(template);
        foreach (var (key, value) in values)
            template = template.Replace($"%{key}%", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                                        StringComparison.Ordinal);
        return template;
    }
}
