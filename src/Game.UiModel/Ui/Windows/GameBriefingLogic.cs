using System.Globalization;
using System.Text.RegularExpressions;
using LastEdict.Data.Game;
using LastEdict.Data.Menu;

namespace LastEdict.Data.Ui.Windows;

/// <summary>
/// Брифинг сценария <c>DLG_SCENARIO_BRIEFING</c> — «Задание» из партии (<c>BTN_REPORT</c>, F1 → <c>CBriefingInGame</c> <c>0x482103</c>) и
/// брифинг из меню перед партией (<c>CBriefingInterf</c> <c>0x6d34d4</c>): оба — <c>CBriefingBase</c> с общим конструктором <c>0x480194</c>,
/// текстом <c>0x480659</c> и обработчиками мыши и клавиш; флаг «из партии» (<c>+0x3c</c>) меняет только звук и музыку. Есть длинная
/// история (<c>BRIEFLONG</c>) — <c>X005TA0832</c> (имя, история, текущая цель) в прокручиваемом тексте, служебные <c>\r</c> и <c>_</c>
/// убираются, метки <c>%3%</c>…<c>%7%</c> — столько же переводов строки, ведущая <c>%NN%</c> — параметр прокрутки (здесь убирается); нет —
/// <c>X005TA0517</c> (имя, описание, цель) в <c>TXT_DESC</c>. Цель — сменённая событием (<c>L_CHANGE_OBJECTIVE</c>), иначе из сценария.
/// В партии «Продолжить» возвращает на карту, «Повтор» спрятан; в меню (<paramref name="replay"/> задан) — ролик и «Повтор», «Продолжить» —
/// переходом графа меню.
/// Автопрокрутка (<c>CScrollTextBoxInterf</c> <c>0x533c53</c>, тик <c>0x533f47</c>) — по точке: текст стоит на 45 точек ниже верха рамки
/// (<see cref="StartOffset"/>), через 1500 мс едет вверх на точку за N тиков по 66 мс (N = 2, или число из приставки <c>%NN%</c> истории,
/// 0 → 2) и стоит, пока его низ — на 76 точках от верха рамки (<see cref="StopBottom"/>): тик каждый раз сам проверяет конец, защёлки нет
/// (<c>0x533cba</c>) — оттащенный назад текст едет снова. <c>BTN_SCROLL_UP</c> — на 10 точек вперёд, <c>BTN_SCROLL_DOWN</c> — на 10 назад
/// (<c>0x481f8e</c>/<c>0x481f9d</c>, имена кнопок у движка обратные); обе снимают задержку старта. Левая кнопка в рамке текста
/// (<c>0x481fac</c>) — тащение: движение мыши (<c>0x482062</c>, по всему экрану, пока кнопка не отпущена) сдвигает текст за мышью по
/// вертикали (<c>0x534040</c>/<c>0x534083</c>: не дальше конца и старта, задержку снимают), автопрокрутка не останавливается. Сразу к концу
/// (<c>0x53409e</c>): левая кнопка вне рамки текста, двойной щелчок и правая кнопка где угодно, кроме кнопок, любая клавиша, кроме
/// служебных (<c>0x48201d</c>, <see cref="Key"/>).
/// </summary>
public sealed partial class GameBriefingLogic(Func<string, string> text, ScenarioInfo scenario, string objective,
                                              Action<UiScreenState>? replay = null) : IWindowLogic, IUiTicking
{
    public const int StartDelayMs = 1500, TickMs = 66, ButtonStep = 10, StartOffset = 45, StopBottom = 76;
    private const string Scroll = "TXT_TEXT_SCROLL";
    private int _waited, _ticks, _pixels, _lastY;

    /// <summary>Миллисекунды, не набравшие тика (66 мс): кадр сцены — 16 мс, и целое деление каждого кадра на 66 давало 0 — текст в
    /// игре не ехал вовсе, пока щелчок не отправлял его сразу к концу (замечание пользователя 11.10: «кадр меняется с названия на цели»).</summary>
    private int _spare;
    private bool _started, _dragging;

    /// <summary>Брифинг из меню: ролик и «Повтор»; «Продолжить» — граф меню.</summary>
    public bool InMenu => replay is not null;

    /// <summary>Тиков на точку: число приставки <c>%NN%</c> истории (0 → 2), иначе 2 (<c>0x4807c7</c>).</summary>
    public int TicksPerPixel =>
        Prefix().Match(scenario.BriefingLong.TrimStart()) is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var n and > 0
            ? n : 2;

    /// <summary>Сколько точек прокручено — для проверок.</summary>
    public int Pixels => _pixels;

    /// <summary>Текст тащат левой кнопкой — для проверок.</summary>
    public bool Dragging => _dragging;

    [GeneratedRegex(@"^%(\d{1,2})%")]
    private static partial Regex Prefix();

    private bool Long => scenario.BriefingLong.Trim().Length > 0;

    public void Tick(int elapsedMs, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!Long) return;
        if (!_started)
        {
            _waited += elapsedMs;
            if (_waited < StartDelayMs) return;
            _started = true;
            elapsedMs = _waited - StartDelayMs;
        }
        if (End(ui) is { } end && _pixels >= end)
        {
            (_ticks, _spare) = (0, 0);
            return;
        }
        _spare += Math.Max(0, elapsedMs);
        _ticks += _spare / TickMs;
        _spare %= TickMs;
        int step = TicksPerPixel;
        if (_ticks < step) return;
        Move(ui, _ticks / step);
        _ticks %= step;
    }

    /// <summary>Сдвиг на <paramref name="pixels"/> точек между стартом (0) и концом (низ текста на <see cref="StopBottom"/>). Высоту текста
    /// сообщает сцена после вёрстки (<see cref="UiScreenState.TextHeight"/>); пока её нет, конец не известен.</summary>
    private void Move(UiScreenState ui, int pixels)
    {
        _pixels = Math.Max(0, _pixels + pixels);
        if (End(ui) is { } end) _pixels = Math.Min(_pixels, end);
        ui.SetTextOffset(Scroll, StartOffset - _pixels);
    }

    /// <summary>Сдвиг конца прокрутки: верх текста на 45, низ — на 76 точках от верха рамки; текст ещё не свёрстан — null.</summary>
    private static int? End(UiScreenState ui) =>
        ui.TextHeight(Scroll) is var h and > 0 ? Math.Max(0, StartOffset + h - StopBottom) : null;

    /// <summary>Левая кнопка: в рамке текста — начало тащения, вне рамки — сразу к концу (кнопки забирают нажатие сами).</summary>
    public void Press(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Hovered is not null) return;
        var r = ui.Screen.Elements.First(e => e.Id == Scroll).Rect;
        if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height)
        {
            (_dragging, _lastY) = (true, y);
            return;
        }
        ToEnd(ui);
    }

    /// <summary>Тащение (<c>0x482062</c>): текст за мышью — вверх, если мышь ушла вверх; задержка старта снята.</summary>
    public void Motion(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!_dragging) return;
        int dy = _lastY - y;
        _lastY = y;
        if (!Long) return;
        _started = true;
        Move(ui, dy);
    }

    public void Release(int x, int y, UiScreenState ui) => _dragging = false;

    /// <summary>Двойной щелчок и правая кнопка — сразу к концу в любом месте, и в рамке текста, кроме кнопок (<c>0x481fac</c>:
    /// сообщения <c>0x203</c>, <c>0x204</c>/<c>0x205</c>).</summary>
    public void DoubleClick(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Hovered is null) ToEnd(ui);
    }

    public void RightPress(int x, int y, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Hovered is null) ToEnd(ui);
    }

    /// <summary>Служебные клавиши (<c>0x48201d</c>): пробел, Enter, Esc, стрелки вверх и вниз, страницы — к горячим клавишам кнопок
    /// разметки; любая другая — сразу к концу (и съедена, даже при коротком «Задании»).</summary>
    private static readonly int[] ButtonKeys = [0x20, 0x0D, 0x1B, 0x26, 0x28, 0x22, 0x21];

    public bool Key(int vk, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (ButtonKeys.Contains(vk)) return false;
        ToEnd(ui);
        return true;
    }

    /// <summary>Сразу к концу прокрутки (<c>0x53409e</c>); короткое «Задание» или текст не свёрстан — ничего.</summary>
    private void ToEnd(UiScreenState ui)
    {
        if (!Long || End(ui) is not { } end) return;
        _started = true;
        Move(ui, end - _pixels);
    }

    public const string Screen = "DLG_SCENARIO_BRIEFING";
    private readonly UiRequests _requests = new();

    public UiRequest? TakeRequest() => _requests.Take();

    public string Summary => $"задание={scenario.Id.Replace(' ', '_')} длинное={(Long ? 1 : 0)} прокручено={_pixels}";

    /// <summary>Цель на экране: сменённая событием или из сценария.</summary>
    public string Objective => objective.Length > 0 ? objective : scenario.Briefing;

    public bool Allows(string element, UiScreenState ui) =>
        element is not ("BTN_SCROLL_UP" or "BTN_SCROLL_DOWN") && (InMenu || element != "BTN_CONTINUE");

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (replay is null) ui.SetHidden("BTN_REPLAY", true);
        else replay(ui);
        string story = BriefingLogic.Clean(scenario.BriefingLong).Trim();
        if (story.Length > 0)
        {
            ui.SetText("TXT_TEXT_SCROLL", Breaks(UiTemplate.Fill(text("X005TA0832"), ("NAME", scenario.Name), ("BRIEFING", story),
                                                                ("OBJECTIVE", Objective))));
            ui.SetTextOffset(Scroll, StartOffset);
            ui.SetText("TXT_DESC", "");
            return;
        }
        ui.SetText("TXT_TEXT_SCROLL", "");
        ui.SetText("TXT_DESC", UiTemplate.Fill(text("X005TA0517"), ("NAME", scenario.Name), ("DESC", scenario.Description), ("OBJECTIVE", Objective)));
    }

    /// <summary><c>%3%</c>…<c>%7%</c> — столько же переводов строки; прочие <c>%NN%</c> (параметр прокрутки) убираются.</summary>
    public static string Breaks(string s) =>
        Marks().Replace(s, m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var n and >= 3 and <= 7
            ? string.Concat(Enumerable.Repeat(@"\n", n)) : "");

    [GeneratedRegex(@"%(\d{1,2})%")]
    private static partial Regex Marks();

    public void Handle(UiEvent e, UiScreenState ui)
    {
        switch (e)
        {
            case UiClicked { Element: "BTN_CONTINUE" } when !InMenu:
                _requests.Add(new UiClose());
                break;
            case UiClicked { Element: "BTN_REPLAY" } when replay is not null:
                replay(ui);
                break;
            case UiClicked { Element: "BTN_SCROLL_UP" or "BTN_SCROLL_DOWN" } b:
                _started = true;
                Move(ui, b.Element == "BTN_SCROLL_UP" ? ButtonStep : -ButtonStep);
                break;
        }
    }
}
