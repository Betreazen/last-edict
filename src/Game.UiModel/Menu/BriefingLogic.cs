using LastEdict.Data.Ui;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Menu;

/// <summary>Брифинг сценария из меню (<c>DLG_SCENARIO_BRIEFING</c>, <c>CBriefingInterf</c>): та же логика, что у «Задания» из партии
/// (<see cref="GameBriefingLogic"/>: общий <c>CBriefingBase</c> — текст <c>0x480659</c>, прокрутка по точке, мышь и клавиши), цель — из
/// сценария; вверху — ролик брифинга, «Повтор» запускает его заново.
///
/// <para>В истории из файла сценария (<c>BRIEFLONG1…5</c>) стоят <c>\r</c> и <c>_</c> — последний посреди слов
/// («refus_e»), то есть это не текст, а служебная пометка редактора; оба убираются.
/// <c>ponytail:</c> смысл <c>_</c> не проверен — если окажется разрывом страницы, заменить на него.</para></summary>
internal static class BriefingLogic
{
    private const string Video = "IMG_BRIEFING";

    /// <summary>Логика брифинга меню; сценарий не выбран — null (экран по разметке).</summary>
    public static IScreenLogic? Create(MenuContext context) =>
        context.Setup.Scenario is { } scenario
            ? new GameBriefingLogic(id => context.Strings.TryGetValue(id, out var s) ? s : id, scenario, "", ui => PlayVideo(context, ui))
            : null;

    /// <summary>Ролик: у главы саги — свой (<c>CScen.SMK_BRIEF</c>, <c>0x480499</c> ищет его по текущему сценарию), иначе — ролик
    /// расы.</summary>
    private static void PlayVideo(MenuContext context, UiScreenState ui)
    {
        string video = context.Setup.Scenario is { } s && context.Catalog.ChapterOf(s.Id) is { } ch && ch.Chapter.BriefingVideo.Length > 0
            ? ch.Chapter.BriefingVideo
            : context.Catalog.Race(context.Setup.Race).BriefingVideo;
        if (video.Length > 0) ui.SetVideo(Video, video);
    }

    internal static string Clean(string text) => text.Replace("\r", "").Replace("_", "");
}
