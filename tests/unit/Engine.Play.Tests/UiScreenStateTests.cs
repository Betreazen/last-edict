using D2.Data.Ui;
using Xunit;

namespace D2.Data.Tests;

/// <summary>Живые элементы экрана без движка: состояния кнопки, переключатели и группы, счётчики, списки,
/// горячие клавиши. Экраны собраны здесь же — поведение проверяется на чистых числах; модель состояний взята из
/// реверса тулсета (<c>ButtonState</c>, <c>ToggleButtonState</c>, <c>CRadioButtonInterfData</c>,
/// <c>CSpinButtonInterfData</c>, <c>CListBoxInterfData</c> в <c>tools/d2probe/mss32/include</c>).</summary>
public class UiScreenStateTests
{
    private static UiElement Button(string id, int x, int y, params int[] keys) => new()
    {
        Id = id, Kind = UiKind.Button, Rect = new UiRect(x, y, 40, 20),
        Sprites = new UiSprites($"{id}_N", $"{id}_H", $"{id}_C", $"{id}_D"), Hotkeys = keys,
    };

    private static UiElement Toggle(string id, int x, int y) => new()
    {
        Id = id, Kind = UiKind.Toggle, Rect = new UiRect(x, y, 40, 20),
        Sprites = new UiSprites($"{id}_N", $"{id}_H", $"{id}_C", $"{id}_D"),
        Checked = new UiSprites($"{id}_ON", $"{id}_ONH", $"{id}_ONC"),
    };

    private static UiScreenState State(params UiElement[] elements) =>
        new(new UiScreen { Id = "TEST", Elements = elements });

    // --- кнопка ---

    [Fact]
    public void Button_HoverPressRelease_ChangesFrameAndClicksOnRelease()
    {
        var ui = State(Button("BTN_OK", 100, 100));
        Assert.Equal("BTN_OK_N", ui.Frame("BTN_OK"));

        Assert.Empty(ui.Move(110, 110));
        Assert.Equal(UiLook.Hover, ui.Look("BTN_OK"));
        Assert.Equal("BTN_OK_H", ui.Frame("BTN_OK"));

        Assert.Empty(ui.Press(110, 110));
        Assert.Equal("BTN_OK_C", ui.Frame("BTN_OK"));

        Assert.Equal([new UiClicked("BTN_OK")], ui.Release(110, 110));
        Assert.Equal("BTN_OK_H", ui.Frame("BTN_OK"));
    }

    /// <summary>Нажали, увели мышь и отпустили вне кнопки — щелчка нет; пока мышь вне, кнопка не нажата.</summary>
    [Fact]
    public void Button_ReleasedOutside_DoesNotClick()
    {
        var ui = State(Button("BTN_OK", 100, 100));
        ui.Press(110, 110);
        ui.Move(10, 10);
        Assert.Equal(UiLook.Normal, ui.Look("BTN_OK"));
        Assert.Empty(ui.Release(10, 10));
        Assert.Equal(UiLook.Normal, ui.Look("BTN_OK"));
    }

    [Fact]
    public void Button_Disabled_IgnoresPointerAndHotkey()
    {
        var ui = State(Button("BTN_OK", 100, 100, 13));
        ui.SetEnabled("BTN_OK", false);
        Assert.Equal("BTN_OK_D", ui.Frame("BTN_OK"));
        ui.Move(110, 110);
        Assert.Equal(UiLook.Disabled, ui.Look("BTN_OK"));
        ui.Press(110, 110);
        Assert.Empty(ui.Release(110, 110));
        Assert.Empty(ui.Key(13));
    }

    [Fact]
    public void Hotkey_ClicksButton()
    {
        var ui = State(Button("BTN_OK", 100, 100, 13), Button("BTN_BACK", 200, 100, 27));
        Assert.Equal([new UiClicked("BTN_BACK")], ui.Key(27));
        Assert.Empty(ui.Key(65));
    }

    /// <summary>Пустой кадр состояния заменяется обычным: у половины кнопок игры кадра «запрет» нет.</summary>
    [Fact]
    public void Frame_FallsBackToNormal_WhenStateSpriteIsEmpty()
    {
        var bare = new UiElement
        {
            Id = "BTN", Kind = UiKind.Button, Rect = new UiRect(0, 0, 10, 10), Sprites = new UiSprites("BTN_N"),
        };
        var ui = State(bare);
        ui.Move(5, 5);
        Assert.Equal("BTN_N", ui.Frame("BTN"));
    }

    /// <summary>Из перекрывающихся элементов мышь достаётся верхнему — последнему в списке экрана.</summary>
    [Fact]
    public void Pointer_HitsTopmostElement()
    {
        var ui = State(Button("BELOW", 100, 100), Button("ABOVE", 110, 105));
        ui.Move(120, 110);
        Assert.Equal(UiLook.Hover, ui.Look("ABOVE"));
        Assert.Equal(UiLook.Normal, ui.Look("BELOW"));
    }

    // --- переключатель и группа ---

    [Fact]
    public void Toggle_Click_FlipsAndUsesCheckedFrames()
    {
        var ui = State(Toggle("TOG_A", 0, 0));
        ui.Press(5, 5);
        Assert.Equal([new UiToggled("TOG_A", true)], ui.Release(5, 5));
        Assert.True(ui.IsChecked("TOG_A"));
        Assert.Equal("TOG_A_ONH", ui.Frame("TOG_A"));
        ui.Move(500, 500);
        Assert.Equal("TOG_A_ON", ui.Frame("TOG_A"));

        ui.Press(5, 5);
        Assert.Equal([new UiToggled("TOG_A", false)], ui.Release(5, 5));
        Assert.Equal("TOG_A_H", ui.Frame("TOG_A"));
    }

    [Fact]
    public void RadioGroup_KeepsExactlyOneChecked()
    {
        var group = new UiElement { Id = "RAD", Kind = UiKind.RadioGroup, Members = ["TOG_A", "TOG_B", "TOG_C"] };
        var ui = State(Toggle("TOG_A", 0, 0), Toggle("TOG_B", 50, 0), Toggle("TOG_C", 100, 0), group);
        Assert.Equal(0, ui.GroupIndex("RAD"));
        Assert.True(ui.IsChecked("TOG_A"));

        ui.Press(55, 5);
        Assert.Equal([new UiGroupChanged("RAD", 1, "TOG_B")], ui.Release(55, 5));
        Assert.Equal((false, true, false), (ui.IsChecked("TOG_A"), ui.IsChecked("TOG_B"), ui.IsChecked("TOG_C")));

        // Повторный щелчок по выбранному не снимает выбор.
        ui.Press(55, 5);
        Assert.Empty(ui.Release(55, 5));
        Assert.True(ui.IsChecked("TOG_B"));

        ui.SetGroupIndex("RAD", 2);
        Assert.Equal((false, false, true), (ui.IsChecked("TOG_A"), ui.IsChecked("TOG_B"), ui.IsChecked("TOG_C")));

        // Отключение группы отключает всех участников и видно по самой группе.
        ui.SetEnabled("RAD", false);
        Assert.False(ui.IsEnabled("RAD"));
        Assert.False(ui.IsEnabled("TOG_A"));
        ui.Press(5, 5);
        Assert.Empty(ui.Release(5, 5));
    }

    // --- счётчик ---

    [Fact]
    public void Spin_Buttons_StepOptionWithinRange()
    {
        var spin = new UiElement
        {
            Id = "SPIN", Kind = UiKind.Spin, Rect = new UiRect(50, 0, 40, 20), Spin = new UiSpin("BTN_MORE", "BTN_LESS"),
        };
        var ui = State(Button("BTN_LESS", 0, 0), spin, Button("BTN_MORE", 100, 0));
        ui.SetSpin("SPIN", options: 3, selected: 1);

        ui.Press(105, 5);
        Assert.Equal([new UiClicked("BTN_MORE"), new UiSpinChanged("SPIN", 2)], ui.Release(105, 5));
        // Последнее значение — «больше» гаснет (0x539e6a) и не жмётся.
        Assert.False(ui.IsEnabled("BTN_MORE"));
        Assert.Equal(UiLook.Disabled, ui.Look("BTN_MORE"));
        ui.Press(105, 5);
        Assert.Empty(ui.Release(105, 5));
        Assert.Equal(2, ui.SpinOption("SPIN"));

        ui.Press(5, 5);
        ui.Release(5, 5);
        Assert.Equal(1, ui.SpinOption("SPIN"));
    }

    // --- список ---

    /// <summary>Список 1×3 строки по 20 точек, всего 10 пунктов: щелчок выбирает строку под мышью, стрелки
    /// двигают выбор и прокручивают так, чтобы выбранное было видно; «подтвердить» отдаёт выбранный.</summary>
    private static UiScreenState ListScreen()
    {
        var list = new UiElement
        {
            Id = "LIST", Kind = UiKind.List, Rect = new UiRect(0, 100, 100, 60),
            List = new UiList
            {
                CellWidth = 100, CellHeight = 20, Up = "BTN_UP", Down = "BTN_DOWN",
                PageUp = "BTN_PGUP", PageDown = "BTN_PGDN", Confirm = "BTN_OK",
            },
        };
        var ui = State(list, Button("BTN_UP", 200, 0, 38), Button("BTN_DOWN", 200, 30, 40),
                       Button("BTN_PGUP", 200, 60, 33), Button("BTN_PGDN", 200, 90, 34), Button("BTN_OK", 200, 120, 13));
        ui.SetList("LIST", count: 10);
        return ui;
    }

    /// <summary>Все экраны игры: кнопка подтверждения списка (<c>UiList.Confirm</c>) — одно нажатие, одно событие <c>UiClicked</c>, без
    /// <c>UiListConfirmed</c>. Партия 16.10, пометка 5: логики окон ловили оба и действовали дважды (кража, продажа, применение посоха,
    /// загрузка).</summary>
    [SkippableFact]
    public void EveryConfirmButton_OnePress_OneEvent()
    {
        string path = ImportedData.PathOf("ui/screens.json");
        int checkedButtons = 0;
        foreach (var screen in UiScreens.Load(path))
            foreach (var list in screen.Elements.Where(e => e.List?.Confirm is not null))
            {
                var button = screen.Elements.FirstOrDefault(e => e.Id == list.List!.Confirm);
                if (button is null) continue;
                var ui = new UiScreenState(screen);
                ui.SetList(list.Id, count: 3);
                ui.Select(list.Id, 1);
                var r = button.Rect;
                (int x, int y) = (r.X + r.Width / 2, r.Y + r.Height / 2);
                ui.Press(x, y);
                var events = ui.Release(x, y);
                if (events.Count == 0) continue;   // кнопку закрыл другой элемент разметки
                Assert.Equal([new UiClicked(button.Id)], events);
                checkedButtons++;
            }
        Assert.True(checkedButtons >= 15, $"проверено кнопок: {checkedButtons}");
    }

    [Fact]
    public void List_ClickSelectsRow_DoubleClickConfirms()
    {
        var ui = ListScreen();
        Assert.Equal(-1, ui.ListSelected("LIST"));
        Assert.Equal([new UiListSelected("LIST", 1)], ui.Press(10, 125));
        ui.Release(10, 125);
        Assert.Equal([new UiListConfirmed("LIST", 1)], ui.Press(10, 125, doubleClick: true));
        // Ниже последней видимой строки — мимо списка.
        Assert.Empty(ui.Press(10, 165));
    }

    [Fact]
    public void List_ArrowsAndPages_MoveSelectionAndScroll()
    {
        var ui = ListScreen();
        ui.Select("LIST", 0);
        Assert.False(ui.IsEnabled("BTN_UP"));   // выбран первый ряд — «вверх» погасла (0x535e3c)
        Assert.Equal([new UiClicked("BTN_DOWN"), new UiListSelected("LIST", 1)], ui.Key(40));
        Assert.True(ui.IsEnabled("BTN_UP"));
        ui.Key(40);
        ui.Key(40);
        Assert.Equal((3, 1), (ui.ListSelected("LIST"), ui.ListTop("LIST")));

        ui.Key(34);
        Assert.Equal((6, 4), (ui.ListSelected("LIST"), ui.ListTop("LIST")));
        ui.Key(34);
        ui.Key(34);
        Assert.Equal((9, 7), (ui.ListSelected("LIST"), ui.ListTop("LIST")));

        ui.Key(33);
        Assert.Equal((6, 6), (ui.ListSelected("LIST"), ui.ListTop("LIST")));
        // Кнопка подтверждения — одно событие: логика окна ловит щелчок сама (прежде шло ещё и UiListConfirmed — действие дважды).
        Assert.Equal([new UiClicked("BTN_OK")], ui.Key(13));
    }

    /// <summary>Сетка: ширина 3 ячейки по 30 с зазором 5 (3·30 + 2·5 = 100) — вверх/вниз шагают на строку,
    /// то есть на три пункта, влево/вправо — на один.</summary>
    [Fact]
    public void GridList_UpDownStepByRow_LeftRightByOne()
    {
        var list = new UiElement
        {
            Id = "GRID", Kind = UiKind.List, Rect = new UiRect(0, 0, 100, 30),
            List = new UiList { CellWidth = 30, CellHeight = 30, GapX = 5, Down = "BTN_DOWN", Right = "BTN_RIGHT" },
        };
        var ui = State(list, Button("BTN_DOWN", 200, 0, 40), Button("BTN_RIGHT", 200, 30, 39));
        ui.SetList("GRID", count: 7);
        Assert.Equal([new UiListSelected("GRID", 2)], ui.Press(75, 5));
        Assert.Empty(ui.Press(32, 5)); // зазор между ячейками — мимо
        ui.Key(40);
        Assert.Equal((5, 3), (ui.ListSelected("GRID"), ui.ListTop("GRID")));
        // Правая колонка — «вправо» гаснет, на следующий ряд не переносит (0x535e3c); последний ряд — гаснет «вниз».
        Assert.False(ui.IsEnabled("BTN_RIGHT"));
        Assert.Empty(ui.Key(39));
        Assert.Equal(5, ui.ListSelected("GRID"));
        ui.Key(40);
        Assert.Equal(6, ui.ListSelected("GRID"));
        Assert.False(ui.IsEnabled("BTN_DOWN"));
        Assert.False(ui.IsEnabled("BTN_RIGHT"));   // последний пункт
    }

    /// <summary>Список без выбора (сумка окна): стрелки листают прокрутку ряд за рядом и гаснут у её краёв; пустой — обе погасли.</summary>
    [Fact]
    public void UnselectedList_ArrowsScrollAndGreyAtEdges()
    {
        var list = new UiElement
        {
            Id = "BAG", Kind = UiKind.List, Rect = new UiRect(0, 0, 100, 30),
            List = new UiList { CellWidth = 30, CellHeight = 30, GapX = 5, PageUp = "BTN_UP", PageDown = "BTN_DOWN" },
        };
        var ui = State(list, Button("BTN_UP", 200, 0, 33), Button("BTN_DOWN", 200, 30, 34));
        Assert.False(ui.IsEnabled("BTN_UP"));
        Assert.False(ui.IsEnabled("BTN_DOWN"));
        ui.SetCards("BAG", [.. Enumerable.Range(0, 7).Select(_ => new UiCard(null, new UiPoint(0, 0), null, new UiRect(0, 0, 0, 0)))]);
        Assert.False(ui.IsEnabled("BTN_UP"));
        Assert.True(ui.IsEnabled("BTN_DOWN"));
        Assert.Equal([new UiClicked("BTN_DOWN")], ui.Key(34));
        Assert.Equal((-1, 3), (ui.ListSelected("BAG"), ui.ListTop("BAG")));
        ui.Key(34);
        Assert.Equal(6, ui.ListTop("BAG"));
        Assert.False(ui.IsEnabled("BTN_DOWN"));
        Assert.True(ui.IsEnabled("BTN_UP"));
        ui.Key(33);
        ui.Key(33);
        Assert.Equal(0, ui.ListTop("BAG"));
    }

    /// <summary>Окно перерисовывает сумку после каждого события — и после щелчка стрелки: новые карточки прокрутку не сбрасывают, а
    /// подрезают по новому числу (прежде <see cref="UiScreenState.SetCards"/> ставил верх в 0, и стрелки сумки и склада не листали —
    /// аудит 13.10).</summary>
    [Fact]
    public void SetCards_KeepsScroll_ClampedToNewCount()
    {
        var list = new UiElement
        {
            Id = "BAG", Kind = UiKind.List, Rect = new UiRect(0, 0, 100, 30),
            List = new UiList { CellWidth = 30, CellHeight = 30, GapX = 5, PageUp = "BTN_UP", PageDown = "BTN_DOWN" },
        };
        var ui = State(list, Button("BTN_UP", 200, 0, 33), Button("BTN_DOWN", 200, 30, 34));
        UiCard[] Cards(int n) => [.. Enumerable.Range(0, n).Select(_ => new UiCard(null, new UiPoint(0, 0), null, new UiRect(0, 0, 0, 0)))];
        ui.SetCards("BAG", Cards(7));
        ui.Key(34);
        ui.Key(34);
        Assert.Equal(6, ui.ListTop("BAG"));

        ui.SetCards("BAG", Cards(7));
        Assert.Equal(6, ui.ListTop("BAG"));
        ui.SetCards("BAG", Cards(5));
        Assert.Equal(3, ui.ListTop("BAG"));
        ui.SetCards("BAG", Cards(2));
        Assert.Equal(0, ui.ListTop("BAG"));
    }

    /// <summary>Автоповтор (флаг `.dlg` в <see cref="UiElement.Legacy"/>): нажатие щелчка не даёт, удержание — щелчок каждые 250 мс,
    /// отпускание над кнопкой — ещё один; мышь ушла с кнопки — повторов нет; у кнопки без флага повторов нет.</summary>
    [Fact]
    public void Autorepeat_ClicksEvery250msWhileHeld()
    {
        var spin = new UiElement
        {
            Id = "SPIN", Kind = UiKind.Spin, Rect = new UiRect(50, 0, 40, 20), Spin = new UiSpin("BTN_MORE", "BTN_LESS"),
        };
        var more = Button("BTN_MORE", 100, 0) with { Legacy = ["1"] };
        var ui = State(Button("BTN_LESS", 0, 0), spin, more);
        ui.SetSpin("SPIN", options: 10, selected: 0);
        Assert.True(more.Autorepeat);

        Assert.Empty(ui.Press(105, 5));
        Assert.Empty(ui.Hold(200));
        Assert.Equal([new UiClicked("BTN_MORE"), new UiSpinChanged("SPIN", 1)], ui.Hold(60));
        Assert.Equal(4, ui.Hold(500).Count);   // ещё два щелчка — по два события
        Assert.Equal([new UiClicked("BTN_MORE"), new UiSpinChanged("SPIN", 4)], ui.Release(105, 5));

        ui.Press(105, 5);
        ui.Move(300, 300);
        Assert.Empty(ui.Hold(1000));
        ui.Release(300, 300);
        ui.Press(5, 5);
        Assert.Empty(ui.Hold(1000));   // «меньше» без флага
        ui.Release(5, 5);
        Assert.Equal(3, ui.SpinOption("SPIN"));

        ui.SetSpin("SPIN", options: 10, selected: 8);
        ui.Press(105, 5);
        ui.Hold(1000);   // до последнего значения и погасла
        Assert.Equal(9, ui.SpinOption("SPIN"));
        Assert.False(ui.IsEnabled("BTN_MORE"));
    }

    /// <summary>Подсказка ждёт 250 мс покоя (движение над элементом отсчёт перезапускает), показанная — при переходе на другой элемент
    /// с подсказкой меняется сразу; нажатие мыши и место без подсказки её прячут. Рамка — снизу слева от мыши, прижата к краям.</summary>
    [Fact]
    public void Tooltip_DelayRestartSwitchAndPlace()
    {
        var tip = new UiTooltip();
        tip.Move("BTN_A", "A");
        Assert.False(tip.Tick(200));
        tip.Move("BTN_A", "A");   // движение — отсчёт заново
        Assert.False(tip.Tick(200));
        Assert.True(tip.Tick(60));
        Assert.True(tip.Shown);
        tip.Move("BTN_B", "B");
        Assert.True(tip.Shown);
        Assert.Equal("B", tip.Text);
        tip.Hide();
        Assert.False(tip.Shown);
        tip.Move("BTN_B", "B");
        tip.Move(null, null);
        Assert.False(tip.Tick(1000));

        var screen = new UiRect(0, 0, 800, 600);
        Assert.Equal(new UiRect(296, 204, 108, 16), UiTooltip.Place(400, 200, 100, 14, screen));
        Assert.Equal(new UiRect(0, 204, 108, 16), UiTooltip.Place(50, 200, 100, 14, screen));      // у левого края
        Assert.Equal(new UiRect(296, 584, 108, 16), UiTooltip.Place(400, 590, 100, 14, screen));   // у нижнего
        Assert.Equal(508, UiTooltip.Place(700, 10, 900, 90, screen).Width);                        // текст не шире 500
    }

    /// <summary>Подсказка: у кнопки и картинки — их строка, у подписи и пустого места — нет; спрятанная не отвечает; картинка без
    /// подсказки не заслоняет кнопку под собой.</summary>
    [Fact]
    public void TooltipAt_ButtonsAndImagesOnly()
    {
        var ok = Button("BTN_OK", 0, 0) with { TextId = "X100TA0020" };
        var image = new UiElement { Id = "IMG_A", Kind = UiKind.Image, Rect = new UiRect(100, 0, 40, 20), TextId = "X100TA0077" };
        var label = new UiElement { Id = "TXT_A", Kind = UiKind.Text, Rect = new UiRect(200, 0, 40, 20), TextId = "X005TA0001" };
        var cover = new UiElement { Id = "IMG_COVER", Kind = UiKind.Image, Rect = new UiRect(0, 0, 10, 10) };
        var ui = State(ok, image, label, cover);
        Assert.Equal(("BTN_OK", "X100TA0020"), ui.TooltipAt(5, 5));
        Assert.Equal(("IMG_A", "X100TA0077"), ui.TooltipAt(110, 5));
        Assert.Null(ui.TooltipAt(210, 5));
        Assert.Null(ui.TooltipAt(500, 500));
        ui.SetHidden("IMG_A", true);
        Assert.Null(ui.TooltipAt(110, 5));
    }

    /// <summary>Подсказка, которая не разбирается как id строки, не ставится вовсе (<c>0x5c868e</c>): «255» у <c>IMG_MAIN</c> вида карты
    /// и имя кадра у боевого экрана — картинка с такой подсказкой не заслоняет кнопку под собой.</summary>
    [Fact]
    public void TooltipAt_SkipsStringsThatAreNotIds()
    {
        var ok = Button("BTN_OK", 0, 0) with { TextId = "X100TA0020" };
        var map = new UiElement { Id = "IMG_MAIN", Kind = UiKind.Image, Rect = new UiRect(0, 0, 640, 600), TextId = "255" };
        var frame = new UiElement { Id = "IMG_B", Kind = UiKind.Image, Rect = new UiRect(700, 0, 40, 20), TextId = "DLG_BATTLE_A_LITEMSBG" };
        var ui = State(ok, map, frame);
        Assert.Equal(("BTN_OK", "X100TA0020"), ui.TooltipAt(5, 5));
        Assert.Null(ui.TooltipAt(300, 300));
        Assert.Null(ui.TooltipAt(710, 5));
        Assert.True(UiElement.IsTextId("x005ta0001"));
        Assert.True(UiElement.IsTextId("G000UU0001"));
        Assert.False(UiElement.IsTextId("Login"));
        Assert.False(UiElement.IsTextId("A100TA0020"));
    }

    /// <summary>Текстовый список: высоты строки в разметке нет, её задаёт шрифт — сцена передаёт её сама.</summary>
    [Fact]
    public void TextList_RowHeightComesFromCaller()
    {
        var list = new UiElement
        {
            Id = "TL", Kind = UiKind.TextList, Rect = new UiRect(0, 0, 100, 50), List = new UiList(),
        };
        var ui = State(list);
        ui.SetList("TL", count: 5, rowHeight: 10);
        Assert.Equal([new UiListSelected("TL", 3)], ui.Press(50, 35));
    }

    // --- содержимое, которое задаёт логика экрана ---

    /// <summary>Текст, картинка, фон и ролик задаются логикой поверх разметки; каждая смена поднимает
    /// <see cref="UiScreenState.Revision"/> — по нему сцена понимает, что перерисовать.</summary>
    [Fact]
    public void Content_OverridesMarkup_AndBumpsRevision()
    {
        var image = new UiElement { Id = "IMG", Kind = UiKind.Image, Image = "MARKUP" };
        var text = new UiElement { Id = "TXT", Kind = UiKind.Text, TextId = "X000" };
        var ui = new UiScreenState(new UiScreen { Id = "S", Background = "BG", Elements = [image, text] });
        int r0 = ui.Revision;

        ui.SetImage("IMG", "LOGIC");
        ui.SetText("TXT", "привет");
        ui.SetBackground("BG2");
        ui.SetVideo("IMG", "FACE_HU_0", loop: true);

        Assert.Equal("LOGIC", ui.Frame("IMG"));
        Assert.Equal("привет", ui.TextOf("TXT"));
        Assert.Equal("BG2", ui.Background);
        Assert.Equal(new UiVideoRef("FACE_HU_0", true, 1), ui.VideoOf("IMG"));
        Assert.True(ui.Revision > r0);

        // Тот же ролик ещё раз — новый номер запуска: сцена проиграет его заново («Повтор» брифинга).
        ui.SetVideo("IMG", "FACE_HU_0", loop: true);
        Assert.Equal(2, ui.VideoOf("IMG")!.Run);
        // Снять и тут же задать снова — номер всё равно новый, иначе сцена сочла бы ролик уже идущим.
        ui.SetVideo("IMG", null);
        ui.SetVideo("IMG", "FACE_HU_0", loop: true);
        Assert.Equal(4, ui.VideoOf("IMG")!.Run);
        ui.SetImage("IMG", null);
        Assert.Equal("MARKUP", ui.Frame("IMG"));
    }

    [Fact]
    public void ListItems_SetCountAndTexts()
    {
        var list = new UiElement { Id = "L", Kind = UiKind.TextList, Rect = new UiRect(0, 0, 100, 40), List = new UiList() };
        var ui = State(list);
        ui.SetItems("L", ["первый", "второй", "третий"], rowHeight: 20);
        Assert.Equal(["первый", "второй", "третий"], ui.Items("L"));
        Assert.Equal([new UiListSelected("L", 1)], ui.Press(5, 25));
        ui.Select("L", 2);
        Assert.Equal((2, 1), (ui.ListSelected("L"), ui.ListTop("L")));
    }

    /// <summary>Поле ввода: щелчок даёт фокус, символы и Backspace правят текст, длина — не больше предела
    /// (игра даёт ввести 15 символов имени).</summary>
    [Fact]
    public void Edit_FocusTypeBackspaceAndLimit()
    {
        var edit = new UiElement { Id = "EDIT", Kind = UiKind.Edit, Rect = new UiRect(0, 0, 100, 20) };
        var ui = State(edit, Button("BTN_OK", 200, 0, 13));
        ui.SetText("EDIT", "ab");
        Assert.Empty(ui.Type('c'));                // без фокуса ввод не идёт
        ui.Press(5, 5);
        ui.Release(5, 5);
        Assert.Equal("EDIT", ui.Focused);
        Assert.Equal([new UiEdited("EDIT", "abc")], ui.Type('c'));
        Assert.Equal([new UiEdited("EDIT", "ab")], ui.Key(8));
        foreach (char c in "0123456789abcdefgh") ui.Type(c);
        Assert.Equal(UiScreenState.EditLimit, ui.TextOf("EDIT")!.Length);
        Assert.Equal([new UiClicked("BTN_OK")], ui.Key(13)); // Enter по-прежнему жмёт кнопку
        Assert.Empty(ui.Type(''));           // управляющие символы не вводятся
    }

    [Fact]
    public void UnknownElement_Throws()
    {
        var ui = State(Button("BTN_OK", 0, 0));
        Assert.Throws<KeyNotFoundException>(() => ui.SetEnabled("NOPE", false));
    }
}
