using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using D2.Data.Gfx;
using D2.Data.Menu;
using D2.Data.Ui;
using D2.Data.Ui.Windows;
using Godot;

namespace D2.Godot;

/// <summary>Заклинания человека на карте: книга (<c>DLG_STRATEGIC.BTN_CAST_SPELL</c>), исследование в столице и прицел
/// (<c>CTaskCastSpell</c>): панель команд меняется на <c>DLG_STRAT_SPELL</c>, карта перестаёт принимать щелчки как ход, под
/// курсором — квадрат области и отметка цели, курсор «можно»/«нельзя» (<see cref="SpellAimView"/>), щелчок по клетке бросает
/// заклинание без вопроса (ответ пользователя 25.09); отказ цели — сообщением (<see cref="CastSpellLogic.TargetMessage"/>), прицел
/// остаётся. «Закрыть» панели — отмена.
/// Квадрат обычных заклинаний — под туманом, как у движка (слой 895 против 1052, общий порядок сцены карты; проверка — шаг
/// <c>order aim</c>, <c>под_туманом=</c>); курсоры — <see cref="AnimatedCursor"/>.</summary>
public partial class MapScreen
{
    private IReadOnlyDictionary<string, SpellText>? _spellTexts;
    private string? _aimSpell;
    private UiScreenView? _aimPanel;
    private Node2D? _aimArea;
    private SpellAim? _aimInput;
    private AimView? _aimView;
    private readonly Dictionary<string, Texture2D?> _aimTextures = [];
    private AnimatedCursor? _cursor;

    /// <summary>Курсор режимов карты (прицел, мешок) — один узел на экран.</summary>
    private AnimatedCursor Cursor
    {
        get
        {
            if (_cursor is null)
            {
                _cursor = new AnimatedCursor { Name = "Cursor" };
                AddChild(_cursor);
            }
            return _cursor;
        }
    }

    private IReadOnlyDictionary<string, SpellText> SpellTexts => _spellTexts ??= SpellBookLogic.LoadTexts(GameData.Rules);

    /// <summary>Книга заклинаний; ничего не изучено — <c>X005TA0406</c>.</summary>
    private void OpenSpellBook()
    {
        if (Windows is not { } context || _dialogViews.Count > 0 || _aimSpell is not null || _dropStack is not null) return;
        if (!CastSpellLogic.AnyKnown(context))
        {
            OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(Text("X005TA0406"), null));
            return;
        }
        OpenWindow(CastSpellLogic.Screen, new CastSpellLogic(context, SpellTexts));
    }

    /// <summary>Исследование в столице; нет постройки магии — <c>X005TA0405</c> (<c>0x4af8d8</c>).</summary>
    /// <summary>«Посох или свиток» (<c>BTN_WAND_SCROLL</c>, <c>0x48f6c2</c>): у выбранного отряда лидер жив и умеет — окно
    /// <see cref="WandScrollLogic"/>, иначе <c>X005TA0027</c>.</summary>
    private void OpenWandScroll()
    {
        if (Windows is not { } context) return;
        string? stack = _map.SelectedStack?.Id;
        if (!WandScrollLogic.CanOpen(context, stack))
        {
            OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(Text("X005TA0027"), null));
            return;
        }
        OpenWindow(WandScrollLogic.Screen, new WandScrollLogic(context, stack!, SpellTexts, _stackTexts ??= StackTexts.Load(GameData.Rules)));
    }

    private void OpenResearch()
    {
        if (Windows is not { } context) return;
        if (!ResearchSpellLogic.HasMagic(context))
        {
            OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(Text("X005TA0405"), null));
            return;
        }
        OpenWindow(ResearchSpellLogic.Screen, new ResearchSpellLogic(context, SpellTexts));
    }

    /// <summary>Посох или свиток, которым целятся (<see cref="WandScrollLogic"/>); заклинание книги — null.</summary>
    private (string Stack, string Item)? _aimItem;

    /// <summary>Прицел заклинанием (или посохом/свитком <paramref name="stackId"/>/<paramref name="itemId"/>): панель <c>DLG_STRAT_SPELL</c>
    /// вместо панели команд, карта без хода.</summary>
    private void StartAim(string spellId, string? stackId = null, string? itemId = null)
    {
        if (Windows is not { } context || _panelView is null || _aimSpell is not null || CapitalOpen || _dropStack is not null) return;
        _aimSpell = spellId;
        _aimItem = stackId is not null && itemId is not null ? (stackId, itemId) : null;
        _aimPanel = ReplacePanel(StratSpellLogic.Screen, new StratSpellLogic(context, spellId, SpellTexts), StopAim);
        _aimArea = new Node2D { Name = "SpellArea", ZIndex = 100 };
        _map.AddChild(_aimArea);
        _aimInput = new SpellAim { Name = "SpellAim", MouseCell = MouseCell, Active = () => Aiming && _dialogViews.Count == 0 };
        _aimInput.Clicked += Target;
        _aimInput.Cancelled += () => Callable.From(StopAim).CallDeferred();
        _aimInput.Hovered += ShowArea;
        AddChild(_aimInput);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        GD.Print($"aim start {spellId}");
    }

    private void StopAim()
    {
        if (_aimPanel is not { } panel || _panelView is null) return;
        _aimSpell = null;
        _aimItem = null;
        _aimPanel = null;
        _aimInput?.QueueFree();
        _aimInput = null;
        RestorePanel(panel);
        _aimArea?.QueueFree();
        _aimArea = null;
        _map.ShowLayered(AimChannel, []);
        _aimView = null;
        Cursor.Show(null);
        ShowMap(_dialogViews.Count == 0, keepVisible: true);
        SyncPanels();
    }

    private bool Aiming => _aimSpell is not null;

    /// <summary>Панель режима (прицел, выбор клетки мешка) на месте панели команд; «Закрыть» — <paramref name="close"/>.</summary>
    private UiScreenView ReplacePanel(string screen, IScreenLogic logic, Action close)
    {
        var view = new UiScreenView { Hosted = screen, Name = screen, SlotAt = _panelView!.SlotAt, SlotHost = _host, Logic = logic };
        view.Fired += events =>
        {
            foreach (var e in events)
                if (e is UiClicked { Element: "BTN_CLOSE" }) Callable.From(close).CallDeferred();
        };
        RemoveChild(_panelView);
        _screens.Remove(_panelView);
        AddChild(view);
        _screens.Add(view);
        CoverPalette();   // панель режима — палитра гаснет (0x490061, 0x490553; Зс57)
        return view;
    }

    /// <summary>Вернуть панель команд на место панели режима.</summary>
    private void RestorePanel(UiScreenView view)
    {
        if (_panelView is null) return;
        RemoveChild(view);
        view.QueueFree();
        _screens.Remove(view);
        AddChild(_panelView);
        _screens.Add(_panelView);
        CoverPalette();   // панель команд снова — палитра зажигается (0x48fc5e)
    }

    /// <summary>Щелчок прицелом по клетке: можно — литьё, прицел кончается; нельзя — сообщение (или молча), прицел остаётся.</summary>
    private void Target((int X, int Y) cell)
    {
        if (_aimSpell is not { } spell || _map.Viewer is not { } viewer) return;
        string? refused = _aimItem is { } used
            ? _map.Session.ItemCastRefusal(used.Stack, used.Item, cell.X, cell.Y) ?? _map.Session.Run<string?>(new D2.Data.Play.Commands.CastItemCommand(used.Stack, used.Item, cell.X, cell.Y))
            : _map.Session.SpellRefusal(viewer, spell, cell.X, cell.Y, useSpellBank: false)
              ?? _map.Session.Run<string?>(new D2.Data.Play.Commands.CastSpellCommand(viewer, spell, cell.X, cell.Y, UseSpellBank: false));
        GD.Print($"aim target {cell.X},{cell.Y} {(refused ?? "брошено").Replace(' ', '_')}");
        if (refused is null)
        {
            StopAim();
            _map.RefreshGround();
            _map.Reload();
            SyncPanels();
            return;
        }
        if (CastSpellLogic.TargetMessage(refused) is { } message)
            OpenWindow(MessageBoxLogic.Screen, new MessageBoxLogic(Text(message), null));
    }

    /// <summary>Клетка карты под мышью; мышь не над картой — null.</summary>
    private (int X, int Y)? MouseCell()
    {
        var local = _mapBox.GetLocalMousePosition();
        if (local.X < 0 || local.Y < 0 || local.X >= _mapBox.Size.X || local.Y >= _mapBox.Size.Y) return null;
        var world = _map.GetGlobalMousePosition();
        var cell = GroundGeometry.CellAt(world.X, world.Y);
        return _map.Map.Contains(cell.X, cell.Y) ? cell : null;
    }

    /// <summary>Прицел над клеткой: картинки области и отметка цели (<c>IsoCmon</c>) в серединах клеток, курсор (<c>IsoCursr</c>).
    /// Мышь не над картой — всё убрано, курсор обычный.</summary>
    private void ShowArea((int X, int Y)? cell)
    {
        if (_aimArea is null) return;
        foreach (var child in _aimArea.GetChildren()) child.QueueFree();
        _aimView = cell is { } c && _aimSpell is { } spell && _map.Viewer is { } viewer
            ? SpellAimView.At(_map.Session, viewer, spell, c.X, c.Y,
                              _aimItem is { } used ? (x, y) => _map.Session.ItemCastRefusal(used.Stack, used.Item, x, y) is null : null) : null;
        if (_aimView is not { } view)
        {
            _map.ShowLayered(AimChannel, []);
            Cursor.Show(null);
            return;
        }
        // Квадрат области — в общем порядке отрисовки карты (сессия Г): SPLAREA — слой SYM_SPELL_AREA 895, под туманом (1052);
        // клетки тумана SPLAREAFOG — SYM_SPELL_FOG 1590, над ним (docs/map-render-stacks.md). Отметка цели — поверх всего.
        int layer = view.AreaOverFog ? SpellFogLayer : SpellAreaLayer;
        _map.ShowLayered(AimChannel, [.. view.Area.Select(m => Texture("IsoCmon", m.Frame) is { } tex
            ? MapView.CellSprite("IsoCmon", m.Frame, m.X, m.Y, tex.GetWidth(), tex.GetHeight(), layer) : null).OfType<MapSprite>()]);
        foreach (var t in view.Targets)
            if (Texture("IsoCmon", t.Frame) is { } texture)
                _aimArea.AddChild(new Sprite2D { Texture = texture, Centered = true, Position = CellCenter(t.X, t.Y) });
        Cursor.Show(view.Cursor);
    }

    /// <summary>Отметки целей (<see cref="AimMark"/>, <c>IsoCmon</c>) в серединах клеток узла <paramref name="into"/>; прежние снимаются.</summary>
    private void ShowMarks(Node2D? into, IReadOnlyList<AimMark> marks)
    {
        if (into is null) return;
        foreach (var child in into.GetChildren()) child.QueueFree();
        foreach (var mark in marks)
            if (Texture("IsoCmon", mark.Frame) is { } texture)
                into.AddChild(new Sprite2D { Texture = texture, Centered = true, Position = CellCenter(mark.X, mark.Y) });
    }

    /// <summary>Отметки строкой проверки: «x,y:кадр» через «+»; нет — «-».</summary>
    private static string MarksLine(IReadOnlyList<AimMark> marks) =>
        marks.Count == 0 ? "-" : string.Join("+", marks.Select(m => $"{m.X},{m.Y}:{m.Frame}"));

    private const string AimChannel = "aim";
    private const int SpellAreaLayer = 895, SpellFogLayer = 1590;

    private static Vector2 CellCenter(int x, int y)
    {
        var (ox, oy) = GroundGeometry.CellOrigin(x, y);
        return new Vector2(ox + (GroundGeometry.CellWidth / 2f), oy + (GroundGeometry.CellHeight / 2f));
    }

    /// <summary>Кадр графики игры (картинка или первый кадр анимации); нет файла — null.</summary>
    private Texture2D? Texture(string archive, string name)
    {
        string key = $"{archive}:{name}";
        if (_aimTextures.TryGetValue(key, out var cached)) return cached;
        var gfx = GameData.Content.Gfx;
        string? path = gfx.Image(archive, name) is { } image ? GfxAssets.PathOf(image, GameData.GfxRoot)
            : gfx.Anim(archive, name) is { } anim && GfxAssets.FramePaths(anim, GameData.GfxRoot) is { Count: > 0 } frames ? frames[0]
            : null;
        var texture = path is not null && File.Exists(path) ? ImageTexture.CreateFromImage(GfxTextures.Canvas(path)) : null;
        _aimTextures[key] = texture;
        return texture;
    }

    /// <summary>Шаг проверки «hover x,y»: прицел над клеткой без мыши.</summary>
    private void AimAt((int X, int Y) cell) => ShowArea(cell);

    /// <summary>Строка проверки прицела.</summary>
    private string AimLine() => _aimView is { } v
        ? $"прицел={_aimSpell ?? "-"} курсор={v.Cursor}:{Cursor.Frames} область={v.Area.Count}{(v.AreaOverFog ? "_над_туманом" : "")} " +
          $"цель={MarksLine(v.Targets)}"
        : $"прицел={_aimSpell ?? "-"} область=0";
}
