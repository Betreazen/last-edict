using System;
using System.Linq;
using D2.Data.Map;
using D2.Data.Ui;
using Godot;

namespace D2.Godot;

/// <summary>Курсоры карты (<see cref="MapCursors"/>, <c>docs/ai-screen.md</c> §10): над клеткой — курсор задачи (выбор отряда, ход,
/// бой, вход в город…), вне карты и в ход ИИ — <c>DEFAULT</c>. Режимы прицела и мешка ставят свои курсоры сами (сессия Б), в бою курсор
/// не трогается, над окном — курсор окна из разметки (<see cref="UiCursor"/>, кинжал <c>_CUDEFAUL</c>). Показ — общий узел
/// <see cref="AnimatedCursor"/> (кадры, обрезка, горячая точка).</summary>
public partial class MapScreen
{
    private string _cursorName = "";

    /// <summary>От чего зависит курсор: клетка, выбор, ход ИИ, окно столицы — пересчёт (с путём A*) только при смене.</summary>
    private ((int X, int Y)? Cell, string? Selected, bool Ai, bool Capital)? _cursorKey;

    public override void _Process(double delta)
    {
        UpdateCursor();
        UpdateHighlight();
        UpdateBanners();
        CapitalMusic();
        ExpireChat();   // строки чата живут 10 с (MapScreen.QuickSave.cs)
        if (_map is not null)   // фон мест молчит под окнами, столицей и боем
            _map.RegionsPaused = CapitalOpen || _dialogViews.Count > 0 || _overlay is not null || _battle is not null;
    }

    private bool _capitalMusic;

    /// <summary>Окно столицы открылось — тема расы её хозяина (временная музыка), закрылось — музыка карты с того же места
    /// (<c>CCapitalInterf</c> <c>0x4af097</c>/<c>0x4aef07</c>).</summary>
    private void CapitalMusic()
    {
        if (CapitalOpen == _capitalMusic) return;
        _capitalMusic = CapitalOpen;
        var audio = GameAudio.Of(this);
        if (!CapitalOpen) { audio.EndTemporary(battle: false); return; }
        var session = _map.Session;
        int race = _map.Viewer is { } v && session.Map.Players.TryGetValue(v, out var p) && session.Book is { } book
                   && book.Economy.Races.TryGetValue(p.RaceId, out var r) ? r.RaceType : -1;
        audio.BeginCapital(race);
    }

    private void UpdateCursor()
    {
        if (_map is null) return;
        if (Aiming || _dropStack is not null || _dialogViews.Count > 0 || _overlay is not null || _battle is not null)
        {
            // Режим со своим курсором (прицел, мешок сессии Б), бой или окно: после него курсор карты ставится заново. Окно отвечает за
            // курсор над всем экраном — курсор окна из разметки (кинжал _CUDEFAUL, 0x5308d5); свой курсор окна (тащение, инструмент)
            // ставит UiScreenView.DragCursorChanged, его не трогаем.
            if (_battle is null && TopWindow() is { } top && top.DragCursor is null && top.DragImage is null) ShowWindowCursor(top);
            (_cursorName, _cursorKey) = ("", null);
            return;
        }
        var cell = MouseCell();
        var key = (cell, _map.Selected, _map.AiRunning, CapitalOpen);
        if (key == _cursorKey) return;
        _cursorKey = key;
        string name = _map.AiRunning || cell is null || CapitalOpen ? MapCursors.Default : CursorAt(cell.Value);
        if (name == _cursorName) return;
        _cursorName = name;
        Cursor.Show(name);
    }

    /// <summary>Верхнее окно над картой: попап или последнее окно (позже добавленный узел рисуется и ловит ввод поверх).</summary>
    private UiScreenView? TopWindow()
    {
        UiScreenView? window = _dialogViews.Count > 0 ? _dialogViews[^1] : null;
        if (_overlay is { } overlay && (window is null || overlay.GetIndex() > window.GetIndex())) window = overlay;
        return window;
    }

    /// <summary>Курсор окна из разметки (<see cref="UiCursor"/>); у окна без курсора (столица, панели) курсор не меняется
    /// (<c>0x5401c7</c>).</summary>
    private void ShowWindowCursor(UiScreenView window)
    {
        // Экран — по имени из данных, а не из состояния окна: у окна, чей экран не прочитался, состояния нет.
        if (UiCursor.Of(GameData.Content.Screens.FirstOrDefault(s => s.Id == window.Hosted)) is { } cursor) Cursor.ShowUi(cursor);
    }

    /// <summary>Подсветка клетки под курсором (<c>TILE_HIGHLIGHT</c>, настройка <c>IsoHighlight</c>): над любой клеткой карты, пока карта
    /// принимает ввод — не под окнами, не в бою и не в ход ИИ.</summary>
    private void UpdateHighlight()
    {
        if (_map is null) return;
        bool off = !_settings.IsoHighlight || _map.AiRunning || CoversMap || _dialogViews.Count > 0 || _overlay is not null
                   || _battle is not null || !_mapBox.Visible;
        _map.Highlight(off ? null : _highlightCell ?? MouseCell());
    }

    /// <summary>Проверка (шаг <c>highlight x,y</c>): клетка под «мышью» без мыши.</summary>
    private (int X, int Y)? _highlightCell;

    /// <summary>Курсор над клеткой глазами зрителя (выбор — отряд вида или открытое окно столицы).</summary>
    private string CursorAt((int X, int Y) cell)
    {
        var session = _map.Session;
        var context = new MapCursors.Context(session.Looks, session.Book, _map.Viewer, _map.Selected, null,
                                             (x, y) => _map.Scene.Hidden(x, y),
                                             (stack, x, y) => session.PathTo(stack, x, y).Points.Count > 1);
        return MapCursors.At(context, cell.X, cell.Y);
    }
}
