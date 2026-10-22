using LastEdict.Data.Content;
using LastEdict.Data.Menu;
using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Tests;

/// <summary>Ролики «Вступления» (<see cref="CinematicLogic"/>): что показывается при запуске и по кнопке, пропуск и конец на секундном тике —
/// по разбору <c>CAnimInterf</c> (<c>0x4c66be</c>) и журналу зонда <c>20260919-human</c>.</summary>
public class CinematicLogicTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static (CinematicLogic Logic, UiScreenState Ui) Open(IReadOnlyList<CinematicClip> clips)
    {
        var screens = UiScreens.Load(ImportedData.PathOf("ui/screens.json"));
        var logic = new CinematicLogic(clips);
        var ui = new UiScreenState(logic.Shape(screens.Single(s => s.Id == CinematicLogic.Screen)));
        logic.Open(ui);
        return (logic, ui);
    }

    /// <summary>Мс от открытия до ухода (просьбы <see cref="UiLeave"/>) шагами по 1 мс; <paramref name="act"/> — что сделать в миллисекунду.</summary>
    private static int LeaveAt(CinematicLogic logic, UiScreenState ui, Action<int> act)
    {
        for (int ms = 1; ms <= 20_000; ms++)
        {
            act(ms);
            logic.Tick(1, ui);
            if (logic.TakeRequest() is UiLeave) return ms;
        }
        return -1;
    }

    [SkippableFact]
    public void Startup_SfThenIntro_ByIniIntro_MenuIntroAlone()
    {
        Assert.Equal([new CinematicClip("SF", false), new CinematicClip("INTRO", true)], Cinematic.Startup(intro: true));
        Assert.Equal([new CinematicClip("SF", false)], Cinematic.Startup(intro: false));
        Assert.Equal([new CinematicClip("INTRO", true)], Cinematic.Menu);
        var videos = UiVideos.ByName(UiVideos.Load(ImportedData.PathOf("video/videos.json")));
        Assert.Equal((800, 600), (videos["SF"].W, videos["SF"].H));          // без рамки — во весь экран
        Assert.Equal((640, 360), (videos["INTRO"].W, videos["INTRO"].H));    // в рамке — место IMG_ANIM 640×360
    }

    /// <summary>Без рамки — чёрный экран и ролик во весь экран; в рамке — фон <c>_CINEMATIC</c>, ролик в <c>IMG_ANIM</c>, рамка
    /// <c>_CINEMATIC_OVER</c> поверх роликов (на месте <c>IMG_ANIM</c>).</summary>
    [SkippableFact]
    public void Clips_FramedAndFullScreen()
    {
        Skip.IfNot(File.Exists(Path.Combine(Data, "ui", "screens.json")), "нет экранов");
        var (logic, ui) = Open(Cinematic.Startup(intro: true));
        Assert.Equal("SF", ui.VideoOf(CinematicLogic.Full)?.Name);
        Assert.Null(ui.VideoOf(CinematicLogic.Framed));
        Assert.Equal("", ui.Background);
        Assert.True(ui.IsHidden(CinematicLogic.Over));
        var over = ui.Screen.Elements.Single(e => e.Id == CinematicLogic.Over);
        Assert.True(over.OverVideo);
        Assert.Equal(ui.Screen.Elements.Single(e => e.Id == CinematicLogic.Framed).Rect, over.Rect);
        Assert.Equal(new UiRect(0, 0, 800, 600), ui.Screen.Elements.Single(e => e.Id == CinematicLogic.Full).Rect);
        logic.Press(10, 10, ui);
        logic.Tick(CinematicLogic.TickMs, ui);
        Assert.Equal("INTRO", ui.VideoOf(CinematicLogic.Framed)?.Name);
        Assert.Null(ui.VideoOf(CinematicLogic.Full));
        Assert.Equal("_CINEMATIC", ui.Background);
        Assert.False(ui.IsHidden(CinematicLogic.Over));
        Assert.Equal(CinematicLogic.OverFrame, ui.Frame(CinematicLogic.Over));
    }

    /// <summary>Журнал зонда: заставка показана в 328 мс, снята в 2328 — пропуск щелчком между 1-м и 2-м тиками уходит ровно на 2-м
    /// (Intro=0: SF — единственный ролик); ролик 2,0 с, доигравший до конца, уходит на 3-м тике (credits: 10656 → 13641).</summary>
    [SkippableFact]
    public void SkipAndEnd_TakeEffectOnSecondTicks()
    {
        Skip.IfNot(File.Exists(Path.Combine(Data, "ui", "screens.json")), "нет экранов");
        var (sf, ui) = Open(Cinematic.Startup(intro: false));
        Assert.Equal(2000, LeaveAt(sf, ui, ms => { if (ms == 1500) sf.Press(400, 300, ui); }));

        var (ended, ui2) = Open([new CinematicClip("CREDITS", false)]);
        Assert.Equal(3000, LeaveAt(ended, ui2, ms => { if (ms == 2010) ended.Handle(new UiVideoEnded(CinematicLogic.Full), ui2); }));

        // Клавиши пропуска — Enter, пробел, Esc (0x4c668f); прочие — мимо (логика их не берёт).
        var (keys, ui3) = Open(Cinematic.Menu);
        Assert.False(keys.Key(65, ui3));
        Assert.True(keys.Key(27, ui3));
        Assert.Equal(1000, LeaveAt(keys, ui3, _ => { }));
        Assert.Null(keys.Current);

        // Два ролика: пропуск первого на тике 1000, второй доиграл в 4200 — уход на тике 5000.
        var (both, ui4) = Open(Cinematic.Startup(intro: true));
        Assert.Equal(5000, LeaveAt(both, ui4, ms =>
        {
            if (ms == 300) both.Key(13, ui4);
            if (ms == 4200) both.Handle(new UiVideoEnded(CinematicLogic.Framed), ui4);
        }));
    }
}
