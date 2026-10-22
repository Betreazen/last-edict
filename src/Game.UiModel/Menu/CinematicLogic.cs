using D2.Data.Ui;
using D2.Data.Ui.Windows;

namespace D2.Data.Menu;

/// <summary>Ролик показа <c>DLG_CINEMATIC</c>: <see cref="Video"/> — имя в описи роликов; <see cref="Framed"/> — в рамке экрана (фон
/// <c>_CINEMATIC</c>, ролик в месте <c>IMG_ANIM</c>, поверх — <c>_CINEMATIC_OVER</c>), иначе — по центру чёрного экрана без рамки.</summary>
public sealed record CinematicClip(string Video, bool Framed);

/// <summary>Что показывает движок роликами «Вступления» (<c>CIntroAnim</c>, <c>CAnimInterf</c>, <c>CMenuFullScreenAnimSnd</c>).</summary>
public static class Cinematic
{
    /// <summary>Запуск игры (<c>0x40455e</c>, до меню и загрузки глобальных таблиц): <c>CIntroAnim("SF", Intro ? "Intro" : null)</c> — заставка
    /// <c>SF</c> без рамки (первый ролик, флаг рамки 0: <c>0x4c67bd</c>), затем, если в настройках <c>Intro=1</c> (<c>settings+0x157</c>,
    /// <c>0x404580</c>), <c>INTRO</c> в рамке (второй ролик всегда в рамке: <c>0x4c67c7</c>). Конец — меню через <c>TRANS_INTRO2MAIN</c>
    /// (<c>0x4045cb</c> → … <c>0x4028ed</c> → <c>0x4ea1f3(1)</c>).</summary>
    public static IReadOnlyList<CinematicClip> Startup(bool intro) =>
        intro ? [new("SF", false), new("INTRO", true)] : [new("SF", false)];

    /// <summary>Кнопка «Вступление» главного меню (<c>BTN_INTRO</c> → состояние меню 2, <c>0x4f0908</c> → <c>0x4eb201</c>): музыка затухает
    /// 1500 мс, затем <c>CMenuFullScreenAnimSnd("intro", рамка)</c> (<c>0x4eac53(1)</c>); конец — меню через <c>TRANS_INTRO2MAIN</c>
    /// (<c>0x4eb909</c>).</summary>
    public static IReadOnlyList<CinematicClip> Menu { get; } = [new("INTRO", true)];

    /// <summary>Затухание музыки перед роликом меню, мс (<c>0x4eb21a</c>: <c>0x5dc</c>).</summary>
    public const int MenuMusicFadeMs = 1500;
}

/// <summary>
/// Показ роликов <c>DLG_CINEMATIC</c> (<c>CAnimInterf</c>, конструктор <c>0x4c635a</c>): ролики по очереди (<see cref="CinematicClip"/>). Всё решает
/// таймер раз в 1000 мс от открытия (<c>0x4c6474</c> → <c>0x4c66be</c>): на тике ролик сменяется следующим, если его пропустили или он
/// показал последний кадр; последний — уход по переходу графа без элемента (<see cref="UiLeave"/>, у движка <c>+0x88</c>). Пропуск
/// (<c>+0x24</c>) — нажатие левой кнопки мыши (<c>0x4c6668</c>: <c>WM_LBUTTONDOWN</c>) или клавиша Enter, пробел, Esc (<c>0x4c668f</c>); у обоих
/// показов пропуск разрешён (<c>+0x40</c> = 1). Так журнал зонда: заставка, пропущенная щелчком, ушла ровно на втором тике (<c>20260919-human</c>
/// seq 17 → 21: 328 → 2328 мс), «credits» по концу 2-секундного ролика — на третьем (seq 45 → 46: 2985 мс).
/// </summary>
public sealed class CinematicLogic(IReadOnlyList<CinematicClip> clips) : IWindowLogic, IUiTicking
{
    public const string Screen = "DLG_CINEMATIC";

    /// <summary>Место ролика в рамке (разметка) и добавленные: весь экран — ролик без рамки, рамка поверх ролика.</summary>
    public const string Framed = "IMG_ANIM", Full = "IMG_FULL", Over = "IMG_ANIM_OVER";

    /// <summary>Рамка поверх ролика в рамке (<c>+0x48</c>, <c>0x4c63fb</c>).</summary>
    public const string OverFrame = "_CINEMATIC_OVER";

    /// <summary>Период таймера показа, мс (<c>0x3e8</c>).</summary>
    public const int TickMs = 1000;

    /// <summary>Клавиши пропуска: Enter, пробел, Esc.</summary>
    private static readonly int[] SkipKeys = [13, 32, 27];

    private readonly UiRequests _requests = new();
    private int _clip, _clock;
    private bool _skip, _ended, _left;

    public UiRequest? TakeRequest() => _requests.Take();

    /// <summary>Идущий ролик; показ кончился — null.</summary>
    public CinematicClip? Current => _left || _clip >= clips.Count ? null : clips[_clip];

    public string Summary =>
        $"ролик={Current?.Video ?? "-"} рамка={(Current?.Framed == true ? 1 : 0)} пропуск={(_skip ? 1 : 0)} конец={(_ended ? 1 : 0)} ушёл={(_left ? 1 : 0)}";

    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var anim = screen.Elements.FirstOrDefault(e => e.Id == Framed)?.Rect ?? new UiRect(80, 94, 640, 360);
        return screen with
        {
            Elements =
            [
                .. screen.Elements,
                new UiElement { Id = Full, Kind = UiKind.Image, Rect = screen.Design },
                new UiElement { Id = Over, Kind = UiKind.Image, Rect = anim, Image = OverFrame, OverVideo = true },
            ],
        };
    }

    public void Open(UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (clips.Count == 0)
        {
            Leave();
            return;
        }
        Start(ui);
    }

    private void Start(UiScreenState ui)
    {
        var clip = clips[_clip];
        _skip = false;
        _ended = false;
        // В рамке — фон экрана, ролик в IMG_ANIM и рамка поверх; без рамки — чёрный экран и ролик по центру (у SF он во весь экран).
        ui.SetBackground(clip.Framed ? null : "");
        ui.SetVideo(Framed, clip.Framed ? clip.Video : null);
        ui.SetVideo(Full, clip.Framed ? null : clip.Video);
        ui.SetHidden(Over, !clip.Framed);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is UiVideoEnded { Element: Framed or Full }) _ended = true;
    }

    public void Press(int x, int y, UiScreenState ui) => _skip = true;

    public bool Key(int vk, UiScreenState ui)
    {
        if (!SkipKeys.Contains(vk)) return false;
        _skip = true;
        return true;
    }

    public void Tick(int elapsedMs, UiScreenState ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (_left) return;
        _clock += elapsedMs;
        while (_clock >= TickMs && !_left)
        {
            _clock -= TickMs;
            if (!_skip && !_ended) continue;
            if (++_clip < clips.Count) Start(ui);
            else
            {
                ui.SetVideo(Framed, null);
                ui.SetVideo(Full, null);
                Leave();
            }
        }
    }

    private void Leave()
    {
        _left = true;
        _requests.Add(new UiLeave());
    }
}
