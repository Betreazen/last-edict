using LastEdict.Data.Game;
using LastEdict.Data.Ui;

namespace LastEdict.Data.Menu;

/// <summary>Выбор расы (<c>DLG_CHOOSE_RACE</c>): фон — бог расы, подпись — имя расы. Стрелки ходят по кругу рас
/// сценария (без сценария — всех играбельных) в порядке «Следующей расы» — месте расы в круге (<see cref="RaceInfo.ChoiceOrder"/>, вид
/// расы в содержимом, Э5): у рас оригинала Империя → Нежить → Эльфы → Кланы → Легионы, расы модов — где их поставил мод; первой
/// показывается первая в круге (Империя, если она есть; оба правила — со слов пользователя). Между соседями полного круга рас оригинала
/// играет ролик смены бога <c>GOD_X2Y</c> — ролики есть ровно для этих пар; у рас модов ролика нет.
///
/// <para>Направление берётся из подписи кнопки, а не из имени: в разметке «Следующая раса» (<c>X100TA0129</c>) —
/// это <c>BTN_PREV</c>, а «Предыдущая» (<c>X100TA0128</c>) — <c>BTN_NEXT</c>.</para></summary>
internal sealed class ChooseRaceLogic(MenuContext context) : IScreenLogic
{
    private const string NextText = "X100TA0129";
    private const string PreviousText = "X100TA0128";

    /// <summary>Круг выбора: id рас по месту в круге.</summary>
    private string[] _races = [];

    /// <summary>Порядок рисования: картинки (вода и светлячки) — под кнопками. В разметке <c>IMG_WATER</c> (628,431 172×169) идёт после
    /// <c>BTN_CONTINUE</c> (598,548 70×47) и закрывает 1880 из 3290 его точек непрозрачной водой; в оригинале кнопка лежит поверх воды —
    /// так она стоит и на кадрах роликов смены бога <c>GOD_X2Y</c> (800×600, весь экран игры). Без этого «Продолжить» было видно только
    /// у Кланов и эльфов, у которых воды нет (замечание пользователя 11.10).</summary>
    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return screen with { Elements = [.. screen.Elements.OrderBy(e => e.Kind == UiKind.Image ? 0 : 1)] };
    }

    /// <summary>Полный круг: расы по месту в круге; играбельная раса без места (мод его не задал) — в конце, по id.</summary>
    private IEnumerable<RaceInfo> Ring => context.Catalog.Races.Where(r => r.ChoiceOrder is not null || r.Playable)
                                                .OrderBy(r => r.ChoiceOrder ?? int.MaxValue).ThenBy(r => r.Id, StringComparer.Ordinal);

    public void Open(UiScreenState ui)
    {
        var setup = context.Setup;
        if (setup.Saga) setup.Scenario = null;
        // Сага — расы со своей сагой (вид расы, 0x4ee685 у оригинала); своя кампания — раса её заголовка; иначе расы сценария.
        IReadOnlyCollection<string> available = setup.Saga
            ? [.. Ring.Where(r => Saga(r) is not null).Select(r => r.Id)]
            : setup.RaceLimit ?? setup.Scenario?.RaceIds ?? [.. context.Catalog.Races.Where(r => r.Playable).Select(r => r.Id)];
        _races = [.. Ring.Where(r => available.Contains(r.Id, StringComparer.OrdinalIgnoreCase)).Select(r => r.Id)];
        if (_races.Length == 0) return;
        // Сага открывается на эльфах (0x4edc01: индекс 3 списка Империя, Легионы, Кланы, Эльфы, Нежить), квест — на первой расе круга.
        string? elves = context.Catalog.Races.FirstOrDefault(r => r.Original && r.Type == LastEdict.Rules.Map.LegacyRaces.Elf)?.Id;
        setup.RaceId = setup.Saga && elves is not null && _races.Contains(elves) ? elves : _races[0];
        foreach (var arrow in Arrows(ui)) ui.SetEnabled(arrow.Id, _races.Length > 1);
        Show(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: var element } || _races.Length < 2) return;
        string? text = ui.Screen.Elements.First(x => x.Id == element).TextId;
        int step = text == NextText ? 1 : text == PreviousText ? -1 : 0;
        if (step == 0) return;

        var from = context.Catalog.Race(context.Setup.RaceId);
        int at = Array.IndexOf(_races, from.Id);
        var to = context.Catalog.Race(_races[(at + step + _races.Length) % _races.Length]);
        context.Setup.RaceId = to.Id;
        Show(ui);
        ui.SetVideo(UiScreenState.BackgroundId, Neighbours(from, to) ? $"GOD_{from.Code}2{to.Code}" : null);
    }

    private CampaignInfo? Saga(RaceInfo race) =>
        race.Saga is { } id ? context.Catalog.Campaigns.FirstOrDefault(c => c.Id == id && c.Chapters.Count > 0) : null;

    private void Show(UiScreenState ui)
    {
        var race = context.Catalog.Race(context.Setup.RaceId);
        // Сага: раса выбирает кампанию, партия начнётся с её первой главы (0x668d59 — начало списка глав).
        if (context.Setup.Saga && Saga(race) is { } saga)
        {
            context.Setup.Scenario = saga.Chapters[0].Scenario;
            context.Setup.Campaign = new Play.CampaignRun(saga.Id, 0, saga.Chapters.Count);
        }
        ui.SetUnderlay($"DLG_CHOOSE_RACE_GOD{race.Code}_BG");   // под рамкой BG_CHOOSE_RACE (94 % прозрачных точек)
        // 0x4edc9f/0x4edcd3: анимации воды и светлячков по расе (вид расы); нет имени — картинка снимается (у Кланов и эльфов — обе).
        ui.SetImage("IMG_WATER", race.ChoiceWater);
        ui.SetImage("IMG_FIREFLY", race.ChoiceFireflies);
        // 0x4ee08f: шаблон «\fMedium;%RACE%» (X005TA0484) и полное имя расы; нет его — «Not available» (X005TA0479).
        string title = context.Text(race.TitleText.Length > 0 ? race.TitleText : NotAvailable);
        ui.SetText("TXT_RACE_DESC", context.Text(RaceTemplate).Replace("%RACE%", title, StringComparison.Ordinal));
    }

    private const string RaceTemplate = "X005TA0484", NotAvailable = "X005TA0479";

    /// <summary>Соседи в полном круге рас оригинала — для них есть ролик смены бога.</summary>
    private bool Neighbours(RaceInfo a, RaceInfo b)
    {
        if (!a.Original || !b.Original) return false;
        var ring = Ring.Where(r => r.Original).Select(r => r.Id).ToList();
        int i = ring.IndexOf(a.Id), j = ring.IndexOf(b.Id);
        return i >= 0 && j >= 0 && (Math.Abs(i - j) is 1 || Math.Abs(i - j) == ring.Count - 1);
    }

    private static IEnumerable<UiElement> Arrows(UiScreenState ui) =>
        ui.Screen.Elements.Where(e => e.TextId is NextText or PreviousText);
}
