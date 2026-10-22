using D2.Data.Ui;
using Xunit;
using Xunit.Abstractions;

namespace D2.Data.Tests;

/// <summary>Экраны порта: импорт старой разметки (<c>Interf/Interf.dlg</c>) и собственный формат экранов.
/// Числа сверены пересчётом самого файла (GOG 3.01).</summary>
public class UiScreensTests(ITestOutputHelper output)
{
    private static string Interf => Path.Combine(GamePaths.GameDir, "Interf", "Interf.dlg");

    [SkippableFact]
    public void Import_ReadsEveryScreenAndElement()
    {
        Skip.IfNot(File.Exists(Interf), $"нет разметки: {Interf}");
        var screens = DlgImport.Load(Interf);
        var kinds = screens.SelectMany(s => s.Elements).GroupBy(e => e.Kind)
                           .ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine($"экранов {screens.Count}, элементов {kinds.Values.Sum()}: " +
                         string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));

        Assert.Equal(128, screens.Count);
        Assert.Equal(470, kinds[UiKind.Text]);
        Assert.Equal(446, kinds[UiKind.Image]);
        Assert.Equal(356, kinds[UiKind.Button]);
        Assert.Equal(70, kinds[UiKind.Toggle]);
        Assert.Equal(35, kinds[UiKind.List]);
        Assert.Equal(18, kinds[UiKind.TextList]);
        Assert.Equal(14, kinds[UiKind.Edit]);
        Assert.Equal(13, kinds[UiKind.Spin]);
        Assert.Equal(7, kinds[UiKind.RadioGroup]);
        Assert.All(screens, s => Assert.NotEmpty(s.Id));
    }

    /// <summary>Первый экран файла разобран поимённо: размер как ширина и высота, кадры кнопки по состояниям,
    /// подсказка — id строки.</summary>
    [SkippableFact]
    public void ActionResult_HasSizeSpritesAndTextId()
    {
        Skip.IfNot(File.Exists(Interf), $"нет разметки: {Interf}");
        var screen = DlgImport.Load(Interf).Single(s => s.Id == "DLG_ACTION_RESULT");
        Assert.Equal(new UiRect(0, 0, 573, 586), screen.Design);
        Assert.Equal("_PG0250IX", screen.Background);
        Assert.Equal("_CUDEFAUL", screen.Cursor);

        var button = screen.Elements.Single(e => e.Kind == UiKind.Button);
        Assert.Equal("BTN_YES", button.Id);
        Assert.Equal(new UiRect(421, 452, 49, 94), button.Rect);
        Assert.Equal("DLG_ACTION_RESULT_SEAL_OK_N", button.Sprites!.Normal);
        Assert.Equal("DLG_ACTION_RESULT_SEAL_OK_H", button.Sprites.Hover);
        Assert.Equal("DLG_ACTION_RESULT_SEAL_OK_C", button.Sprites.Pressed);
        Assert.Equal("X100TA0020", button.TextId);
        Assert.Equal(2, screen.Elements.Count(e => e.Kind == UiKind.Text));
    }

    /// <summary>Группа переключателей — это список имён, без прямоугольника: в старом формате у <c>RADIO</c>
    /// координат нет вовсе.</summary>
    [SkippableFact]
    public void RadioGroup_KeepsMembersWithoutRect()
    {
        Skip.IfNot(File.Exists(Interf), $"нет разметки: {Interf}");
        var group = DlgImport.Load(Interf).SelectMany(s => s.Elements)
                             .First(e => e.Kind == UiKind.RadioGroup && e.Id == "RAD_LORD");
        Assert.Equal(["TOG_LORD_WARRIOR", "TOG_LORD_MAGE", "TOG_LORD_DIPLOMAT"], group.Members);
        Assert.Equal(default, group.Rect);
    }

    /// <summary>Список картинок (<c>LBOX</c>): ячейка, зазоры, кнопки прокрутки, кнопка подтверждения и рамка
    /// выделения. Ячейки сходятся с прямоугольником: 1 столбец × 5 строк по 90 точек с зазором 10 = 490.</summary>
    [SkippableFact]
    public void List_ReadsCellsLinksAndSelection()
    {
        Skip.IfNot(File.Exists(Interf), $"нет разметки: {Interf}");
        var list = DlgImport.Load(Interf).Single(s => s.Id == "DLG_TRADE_ITEM").Elements
                            .Single(e => e.Id == "MLBOX_RINVENTORY").List!;
        Assert.Equal((314, 90, 0, 10), (list.CellWidth, list.CellHeight, list.GapX, list.GapY));
        Assert.Equal(("BTN_RINVENTORY_UP", "BTN_RINVENTORY_DOWN"), (list.Up, list.Down));
        Assert.Equal(("BTN_PG_UP", "BTN_PG_DN"), (list.PageUp, list.PageDown));
        Assert.Null(list.Left);
        Assert.Equal("BTN_TRADE", list.Confirm);
        Assert.Equal("DLG_TRADE_ITEM_TRADE_SELECTION", list.Selection);
    }

    /// <summary>Текстовый список (<c>TLBOX</c>): ячейки в разметке нет (высота строки — от шрифта), зато есть
    /// оформление выделенной и обычной строки.</summary>
    [SkippableFact]
    public void TextList_ReadsLinksAndRowStyles()
    {
        Skip.IfNot(File.Exists(Interf), $"нет разметки: {Interf}");
        var list = DlgImport.Load(Interf).Single(s => s.Id == "DLG_CHOOSE_CAMPAIGN").Elements
                            .Single(e => e.Id == "LBOX_CAMPAIGNS").List!;
        Assert.Equal(("BTN_UP", "BTN_DOWN", "BTN_CONTINUE"), (list.Up, list.Down, list.Confirm));
        Assert.Equal(0, list.CellHeight);
        Assert.Equal(@"\hC;\b255;255;255;", list.SelectedStyle);
        Assert.Equal(@"\hC;", list.Style);
    }

    /// <summary>Счётчик: кнопка «больше» идёт в разметке первой, «меньше» — второй (как
    /// <c>increment</c>/<c>decrement</c> в <c>CSpinButtonInterfData</c> тулсета).</summary>
    [SkippableFact]
    public void Spin_ReadsIncrementAndDecrement()
    {
        Skip.IfNot(File.Exists(Interf), $"нет разметки: {Interf}");
        var spin = DlgImport.Load(Interf).Single(s => s.Id == "DLG_OPTIONS").Elements
                            .Single(e => e.Id == "SPIN_ACKNO").Spin!;
        Assert.Equal(("BTN_ACKNORIGHT", "BTN_ACKNOLEFT"), (spin.Increment, spin.Decrement));
    }

    /// <summary>Привязка считается по центру элемента: экран делится на трети по каждой оси. Правило проверяется
    /// на чистых числах, без файлов игры, — это и есть его определение.</summary>
    [Fact]
    public void Anchor_FollowsThirdsOfDesignBox()
    {
        var design = new UiRect(0, 0, 900, 600);
        Assert.Equal(UiAnchor.TopLeft, DlgImport.Anchor(new UiRect(0, 0, 100, 100), design));
        Assert.Equal(UiAnchor.BottomRight, DlgImport.Anchor(new UiRect(800, 500, 100, 100), design));
        Assert.Equal(UiAnchor.Center, DlgImport.Anchor(new UiRect(400, 250, 100, 100), design));
        // Фон во весь экран: центр посередине — значит середина, а не угол.
        Assert.Equal(UiAnchor.Center, DlgImport.Anchor(new UiRect(0, 0, 900, 600), design));
        // Граница трети принадлежит средней полосе: центр 290 — ещё слева, центр 300 — уже посередине.
        Assert.Equal(UiAnchor.Left, DlgImport.Anchor(new UiRect(240, 250, 100, 100), design));
        Assert.Equal(UiAnchor.Center, DlgImport.Anchor(new UiRect(250, 250, 100, 100), design));
    }

    /// <summary>Свой формат: запись и чтение JSON не теряют экран, а мод накладывается поверх по имени.</summary>
    [Fact]
    public void Json_RoundTrips_AndOverlayReplacesById()
    {
        var baseSet = new List<UiScreen>
        {
            new() { Id = "SCREEN_A", Elements = [new UiElement { Id = "BTN", Kind = UiKind.Button,
                        Rect = new UiRect(1, 2, 3, 4), Anchor = UiAnchor.BottomRight,
                        Sprites = new UiSprites("n", "h"), TextId = "X000TA0001" }] },
            new() { Id = "SCREEN_B" },
        };
        // Списки внутри записей сравниваются по ссылке, поэтому проверка — по тексту: он и есть формат хранения.
        var json = UiScreens.ToJson(baseSet);
        var back = UiScreens.FromJson(json);
        Assert.Equal(json, UiScreens.ToJson(back));
        Assert.Equal("X000TA0001", back[0].Elements[0].TextId);
        Assert.Equal(UiAnchor.BottomRight, back[0].Elements[0].Anchor);

        var mod = new List<UiScreen> { new() { Id = "SCREEN_B", Background = "MOD_BG" }, new() { Id = "SCREEN_C" } };
        var merged = UiScreens.Merge(back, mod);
        Assert.Equal(["SCREEN_A", "SCREEN_B", "SCREEN_C"], merged.Select(s => s.Id));
        Assert.Equal("MOD_BG", merged[1].Background);
    }

    // --- data/ui/screens.json: набор экранов порта, лежащий в git ---

    private static string ScreensJson => ImportedData.PathOf("ui/screens.json");

    private static IReadOnlyList<UiScreen> Screens => UiScreens.Load(ScreensJson);

    /// <summary>Файл читается своим же загрузчиком и содержит ровно то, что даёт перевод обоих исходников:
    /// 128 экранов <c>Interf.dlg</c> плюс 4 экрана сетевого лобби. Числа сверены пересчётом файлов (GOG 3.01).</summary>
    [SkippableFact]
    public void Screens_Json_LoadsWithExpectedCounts()
    {
        var screens = Screens;
        var kinds = screens.SelectMany(s => s.Elements).GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine($"экранов {screens.Count}, элементов {kinds.Values.Sum()}: " +
                         string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}")));

        Assert.True(screens.Count >= 128, $"экранов {screens.Count}");
        Assert.Equal(132, screens.Count);
        Assert.Equal(1475, kinds.Values.Sum());
        // Interf.dlg + CustomLobby.dlg: 470+10, 446+7, 356+20, 70, 35+2, 18+1, 14+6, 13, 7.
        Assert.Equal(480, kinds[UiKind.Text]);
        Assert.Equal(453, kinds[UiKind.Image]);
        Assert.Equal(376, kinds[UiKind.Button]);
        Assert.Equal(70, kinds[UiKind.Toggle]);
        Assert.Equal(37, kinds[UiKind.List]);
        Assert.Equal(19, kinds[UiKind.TextList]);
        Assert.Equal(20, kinds[UiKind.Edit]);
        Assert.Equal(13, kinds[UiKind.Spin]);
        Assert.Equal(7, kinds[UiKind.RadioGroup]);
        Assert.All(screens, s => Assert.NotEmpty(s.Id));
        Assert.Equal(screens.Count, screens.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Внутри экрана имена элементов уникальны: на элемент ссылаются по имени и логика, и мод, и группы
    /// переключателей — два одинаковых имени сделали бы ссылку двусмысленной.</summary>
    [SkippableFact]
    public void Screens_ElementIds_AreUniqueWithinScreen()
    {
        foreach (var screen in Screens)
        {
            var dup = screen.Elements.GroupBy(e => e.Id, StringComparer.Ordinal).Where(g => g.Count() > 1)
                            .Select(g => g.Key).ToList();
            Assert.True(dup.Count == 0, $"{screen.Id}: повторы имён {string.Join(", ", dup)}");
            Assert.All(screen.Elements, e => Assert.NotEmpty(e.Id));
        }
    }

    /// <summary>Каждый участник группы переключателей есть на том же экране: группа — это ссылки по именам,
    /// а не вложенные элементы.</summary>
    [SkippableFact]
    public void Screens_RadioMembers_ExistInSameScreen()
    {
        foreach (var screen in Screens)
        {
            var ids = screen.Elements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var group in screen.Elements.Where(e => e.Kind == UiKind.RadioGroup))
            {
                Assert.NotEmpty(group.Members);
                foreach (var member in group.Members)
                    Assert.True(ids.Contains(member), $"{screen.Id}/{group.Id}: нет участника {member}");
            }
        }
    }

    /// <summary>Кнопки, на которые ссылаются списки и счётчики, есть на том же экране и это кнопки: список
    /// прокручивается ими, и ссылка в пустоту молча оставила бы его без прокрутки.</summary>
    [SkippableFact]
    public void Screens_ListAndSpinLinks_PointToButtonsInSameScreen()
    {
        int links = 0;
        foreach (var screen in Screens)
        {
            var buttons = screen.Elements.Where(e => e.Kind == UiKind.Button).Select(e => e.Id)
                                .ToHashSet(StringComparer.Ordinal);
            foreach (var e in screen.Elements)
            {
                string?[] targets = e.List is { } l ? [l.Up, l.Down, l.Left, l.Right, l.PageUp, l.PageDown, l.Confirm]
                                  : e.Spin is { } s ? [s.Increment, s.Decrement] : [];
                foreach (var t in targets.Where(t => t is not null))
                {
                    links++;
                    Assert.True(buttons.Contains(t!), $"{screen.Id}/{e.Id}: нет кнопки {t}");
                }
            }
            Assert.All(screen.Elements.Where(e => e.Kind is UiKind.List or UiKind.TextList),
                       e => Assert.NotNull(e.List));
            Assert.All(screen.Elements.Where(e => e.Kind == UiKind.Spin), e => Assert.NotNull(e.Spin));
        }
        output.WriteLine($"ссылок на кнопки: {links}");
        Assert.True(links > 100, $"ссылок {links}");
    }

    /// <summary>Прямоугольники неотрицательны и не вылезают за размер экрана: отрицательная ширина означала бы
    /// перепутанные углы при переводе из старого формата (там задавались две точки).</summary>
    [SkippableFact]
    public void Screens_Rects_AreNonNegativeAndInsideDesign()
    {
        foreach (var screen in Screens)
        {
            Assert.True(screen.Design.Width > 0 && screen.Design.Height > 0, $"{screen.Id}: пустой размер");
            foreach (var e in screen.Elements)
            {
                Assert.True(e.Rect.X >= 0 && e.Rect.Y >= 0 && e.Rect.Width >= 0 && e.Rect.Height >= 0,
                            $"{screen.Id}/{e.Id}: {e.Rect}");
                Assert.True(e.Rect.Right <= screen.Design.Width && e.Rect.Bottom <= screen.Design.Height,
                            $"{screen.Id}/{e.Id}: {e.Rect} вне {screen.Design}");
            }
        }
    }

    /// <summary>Файл в git — это в точности результат перевода исходников: повторный прогон
    /// <c>tools/UiImport</c> даёт тот же текст. Тест нужен, чтобы правка <c>.dlg</c> или правил перевода не
    /// прошла молча мимо файла. Перевод строк перед сравнением приводится к <c>\n</c>: git на Windows
    /// переписывает их при выгрузке.</summary>
    [SkippableFact]
    public void Screens_Json_IsUpToDate()
    {
        var sources = DlgImport.Sources();
        Skip.If(sources.Count < 2, $"нет исходников разметки: найдено {sources.Count} из 2");
        output.WriteLine(string.Join(Environment.NewLine, sources));

        var expected = UiScreens.ToJson(DlgImport.LoadAll(sources)).ReplaceLineEndings("\n");
        var actual = File.ReadAllText(ScreensJson).ReplaceLineEndings("\n");
        Assert.True(expected == actual, FirstDifference(expected, actual));
    }

    /// <summary>Место первого расхождения словами: сравнивать два полумегабайтных текста глазами нельзя.</summary>
    private static string FirstDifference(string expected, string actual)
    {
        var (e, a) = (expected.Split('\n'), actual.Split('\n'));
        for (int i = 0; i < Math.Min(e.Length, a.Length); i++)
            if (e[i] != a[i])
                return $"data/ui/screens.json устарел, строка {i + 1}: ожидалось «{e[i].Trim()}», в файле «{a[i].Trim()}». " +
                       "Пересоберите: dotnet run --project tools/UiImport";
        return $"data/ui/screens.json устарел: строк {a.Length}, ожидалось {e.Length}. " +
               "Пересоберите: dotnet run --project tools/UiImport";
    }
}
