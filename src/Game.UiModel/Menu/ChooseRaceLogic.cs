using LastEdict.Data.Ui;

namespace LastEdict.Data.Menu;

/// <summary>Выбор расы (<c>DLG_CHOOSE_RACE</c>): фон — бог расы, подпись — имя расы. Стрелки ходят по кругу рас
/// сценария (без сценария — всех играбельных) в порядке «Следующей расы»: Империя → Нежить → Эльфы → Кланы →
/// Легионы; первой показывается Империя, если она есть (оба правила — со слов пользователя). Между соседями полного
/// круга играет ролик смены бога <c>GOD_X2Y</c> — ролики есть ровно для этих пар.
///
/// <para>Направление берётся из подписи кнопки, а не из имени: в разметке «Следующая раса» (<c>X100TA0129</c>) —
/// это <c>BTN_PREV</c>, а «Предыдущая» (<c>X100TA0128</c>) — <c>BTN_NEXT</c>.</para></summary>
internal sealed class ChooseRaceLogic(MenuContext context) : IScreenLogic
{
    private const string NextText = "X100TA0129";
    private const string PreviousText = "X100TA0128";
    private const int Empire = LastEdict.Rules.Map.LegacyRaces.Human, Elves = LastEdict.Rules.Map.LegacyRaces.Elf;

    /// <summary>Полный круг рас по «Следующей расе» (типы <c>LRaceCategory</c>).</summary>
    private static readonly int[] Ring = [0, 1, 5, 3, 2];

    private int[] _races = [];

    /// <summary>Порядок рисования: картинки (вода и светлячки) — под кнопками. В разметке <c>IMG_WATER</c> (628,431 172×169) идёт после
    /// <c>BTN_CONTINUE</c> (598,548 70×47) и закрывает 1880 из 3290 его точек непрозрачной водой; в оригинале кнопка лежит поверх воды —
    /// так она стоит и на кадрах роликов смены бога <c>GOD_X2Y</c> (800×600, весь экран игры). Без этого «Продолжить» было видно только
    /// у Кланов и эльфов, у которых воды нет (замечание пользователя 11.10).</summary>
    public UiScreen Shape(UiScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        return screen with { Elements = [.. screen.Elements.OrderBy(e => e.Kind == UiKind.Image ? 0 : 1)] };
    }

    /// <summary>Сага расы (<c>0x4ee685</c>, зашито в коде: Империя → CC0002, Кланы → CC0003, Легионы → CC0004, Нежить → CC0005,
    /// Эльфы → CC0006; «Стражи Света» CC0012–15 — заглушки, меню их не предлагает).</summary>
    private static readonly Dictionary<int, string> SagaOfRace = new()
    {
        [0] = "C000CC0002", [3] = "C000CC0003", [2] = "C000CC0004", [1] = "C000CC0005", [5] = "C000CC0006",
    };

    public void Open(UiScreenState ui)
    {
        if (context.Setup.Saga) context.Setup.Scenario = null;
        var available = context.Setup.Saga
            ? [.. SagaOfRace.Where(p => Saga(p.Key) is not null).Select(p => p.Key)]
            : context.Setup.RaceLimit ?? context.Setup.Scenario?.Races ?? [.. context.Catalog.Races.Where(r => r.Playable).Select(r => r.Type)];
        _races = [.. Ring.Where(available.Contains)];
        if (_races.Length == 0) return;
        // Сага открывается на эльфах (0x4edc01: индекс 3 списка Империя, Легионы, Кланы, Эльфы, Нежить), квест — на Империи.
        context.Setup.Race = context.Setup.Saga && _races.Contains(Elves) ? Elves : _races.Contains(Empire) ? Empire : _races[0];
        foreach (var arrow in Arrows(ui)) ui.SetEnabled(arrow.Id, _races.Length > 1);
        Show(ui);
    }

    public void Handle(UiEvent e, UiScreenState ui)
    {
        if (e is not UiClicked { Element: var element } || _races.Length < 2) return;
        string? text = ui.Screen.Elements.First(x => x.Id == element).TextId;
        int step = text == NextText ? 1 : text == PreviousText ? -1 : 0;
        if (step == 0) return;

        int from = context.Setup.Race;
        int at = Array.IndexOf(_races, from);
        int to = _races[(at + step + _races.Length) % _races.Length];
        context.Setup.Race = to;
        Show(ui);
        ui.SetVideo(UiScreenState.BackgroundId, Neighbours(from, to) ? $"GOD_{Code(from)}2{Code(to)}" : null);
    }

    private Game.CampaignInfo? Saga(int race) =>
        SagaOfRace.TryGetValue(race, out var id) ? context.Catalog.Campaigns.FirstOrDefault(c => c.Id == id && c.Chapters.Count > 0) : null;

    private void Show(UiScreenState ui)
    {
        // Сага: раса выбирает кампанию, партия начнётся с её первой главы (0x668d59 — начало списка глав).
        if (context.Setup.Saga && Saga(context.Setup.Race) is { } saga)
        {
            context.Setup.Scenario = saga.Chapters[0].Scenario;
            context.Setup.Campaign = new Play.CampaignRun(saga.Id, 0, saga.Chapters.Count);
        }
        var race = context.Catalog.Race(context.Setup.Race);
        ui.SetUnderlay($"DLG_CHOOSE_RACE_GOD{race.Code}_BG");   // под рамкой BG_CHOOSE_RACE (94 % прозрачных точек)
        // 0x4edc9f/0x4edcd3: анимации воды и светлячков по расе; нет имени — картинка снимается (у Кланов и эльфов — обе).
        ui.SetImage("IMG_WATER", WaterOfRace.GetValueOrDefault(context.Setup.Race));
        ui.SetImage("IMG_FIREFLY", context.Setup.Race == Empire ? "_FIREFLY_HUMAN" : null);
        // 0x4ee08f: шаблон «\fMedium;%RACE%» (X005TA0484) и полное имя расы; нет его — «Not available» (X005TA0479).
        string title = context.Text(race.TitleText.Length > 0 ? race.TitleText : NotAvailable);
        ui.SetText("TXT_RACE_DESC", context.Text(RaceTemplate).Replace("%RACE%", title, StringComparison.Ordinal));
    }

    private const string RaceTemplate = "X005TA0484", NotAvailable = "X005TA0479";

    /// <summary>Вода выбора расы (<c>0x4ede4e</c>): Нежить, Империя, Легионы; у Кланов и эльфов её нет.</summary>
    private static readonly Dictionary<int, string> WaterOfRace = new()
    {
        [1] = "_WATER_UNDEAD", [0] = "_WATER_HUMAN", [2] = "_WATER_HERETIC",
    };

    private static bool Neighbours(int a, int b)
    {
        int i = Array.IndexOf(Ring, a), j = Array.IndexOf(Ring, b);
        return Math.Abs(i - j) is 1 || Math.Abs(i - j) == Ring.Length - 1;
    }

    private string Code(int type) => context.Catalog.Race(type).Code;

    private static IEnumerable<UiElement> Arrows(UiScreenState ui) =>
        ui.Screen.Elements.Where(e => e.TextId is NextText or PreviousText);
}
