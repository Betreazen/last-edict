using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using D2.Data.Gfx;
using D2.Data.Map;
using Godot;

namespace D2.Godot;

/// <summary>
/// Плавная прокрутка вида к месту события (<c>CMidAnimSmoothScroll</c>, <see cref="SmoothScrollPath"/>): перед анимацией команды вид едет к
/// её клетке с разгоном и торможением, положение за тик быстрой группы (33 мс, <see cref="AnimClock.SmoothScroll"/>); пока едет, анимация
/// команды ждёт (у движка прокрутка — отдельная задача очереди перед ней, <c>0x4f4a0c</c> → <c>0x56aff7</c>). Флажок «Прокрутка между
/// действиями» (<see cref="SmoothScroll"/>, <c>TOG_SMOOTHSCROLL</c>) выключен — вид встаёт на клетку сразу. Ручная прокрутка (стрелки, край
/// окна, мини-карта) и «Следующий лидер» идут без плавности, как у движка (<c>0x48ed72</c> → <c>0x540c38</c>).
/// </summary>
public partial class MapView
{
    /// <summary>Флажок <c>SmoothScroll</c> настроек (<see cref="MapSettings.SmoothScroll"/>, в поставляемом ini — 1).</summary>
    public bool SmoothScroll { get; set; } = true;

    private IReadOnlyList<(int X, int Y)>? _scrollPath;
    private int _scrollAt;
    private Vector2 _scrollTarget;
    private Action? _afterScroll;
    private readonly AnimTicker _scrollTicker = new(AnimClock.SmoothScroll);
    private double _scrollClockMs, _scrollLastMs, _scrollMinMs;
    private readonly List<(int X, int Y)> _scrollShown = [];

    /// <summary>Вид едет к месту события.</summary>
    public bool Scrolling => _scrollPath is not null;

    /// <summary>Прокруток с начала и всего показанных положений (проверка).</summary>
    public (int Scrolls, int Positions) ScrollCount { get; private set; }

    /// <summary>
    /// Прокрутка к клетке перед анимацией (<c>0x4f4a0c</c>): при <paramref name="onlyIfFar"/> — только если центр вида дальше 2 клеток по одной из
    /// осей (<see cref="SmoothScrollPath.Wanted"/>). Флажок выключен — вид встаёт на клетку сразу. Пошла плавная прокрутка — true, и
    /// <paramref name="then"/> позовётся по её концу; иначе <paramref name="then"/> зовётся сразу и ответ — false.
    /// </summary>
    public bool ScrollThen((int X, int Y) cell, bool onlyIfFar, Action? then = null)
    {
        if (StartScroll(cell, onlyIfFar, then)) return true;
        then?.Invoke();
        return false;
    }

    /// <summary>Как <see cref="ScrollThen"/>, но без прокрутки <paramref name="then"/> не зовётся (вызывающий продолжает сам).</summary>
    private bool StartScroll((int X, int Y) cell, bool onlyIfFar, Action? then)
    {
        var centre = CentreCell;
        if (_camera is null || !SmoothScrollPath.Wanted(centre, cell, onlyIfFar)) return false;
        if (!SmoothScroll)
        {
            CenterOn(cell);   // 0x4f4e48: без флажка — сразу на клетку, без сдвига
            return false;
        }
        if (centre == cell) return false;   // 0x4f4b76: центр уже в клетке — списка нет
        // Прежняя прокрутка (показы ждут её конца, так что не бывает) — к концу; её показ мог начать новую — и ту.
        while (_scrollPath is not null) FinishScroll();
        var (x, y) = GroundGeometry.CellOrigin(cell.X, cell.Y);
        _scrollTarget = new Vector2(x + 32, y + 16);
        var from = _camera.Position - _scrollTarget;
        var path = SmoothScrollPath.Offsets((int)Math.Round(from.X), (int)Math.Round(from.Y));
        if (path.Count <= 1) return false;   // вид уже на месте (сдвиг меньше точки) — ехать некуда
        _scrollPath = path;
        (_scrollAt, _afterScroll, _scrollClockMs, _scrollLastMs, _scrollMinMs) = (0, then, 0, 0, double.MaxValue);
        _scrollShown.Clear();
        _scrollTicker.Reset();
        ShowScrollPosition();   // начало задачи — первое положение (0x4f4e19 → 0x4f4dac)
        return true;
    }

    /// <summary>Показ после прокрутки: пошла прокрутка или показ начался — true (показ ждёт <see cref="SpellPlaying"/>).</summary>
    public bool ScrolledPlay((int X, int Y) cell, bool onlyIfFar, Func<bool> play)
    {
        bool played = false;
        bool scrolling = ScrollThen(cell, onlyIfFar, () => played = play());
        return scrolling || played;
    }

    /// <summary>Тик быстрой группы — следующее положение; последнее — ровно клетка, и тогда — показ, ждавший прокрутки.</summary>
    private void StepSmoothScroll(double delta)
    {
        if (_scrollPath is null) return;
        _scrollClockMs += delta * 1000;
        if (!_scrollTicker.Advance(delta * 1000)) return;
        _scrollAt++;
        _scrollMinMs = Math.Min(_scrollMinMs, _scrollClockMs - _scrollLastMs);
        _scrollLastMs = _scrollClockMs;
        ShowScrollPosition();
        if (_scrollAt >= _scrollPath.Count - 1) FinishScroll();
    }

    private void ShowScrollPosition()
    {
        var (dx, dy) = _scrollPath![_scrollAt];
        _camera.Position = Clamp(_scrollTarget + new Vector2(dx, dy));
        var shown = _camera.Position - _scrollTarget;
        _scrollShown.Add(((int)Math.Round(shown.X), (int)Math.Round(shown.Y)));
    }

    /// <summary>Конец прокрутки: строка проверки (положения по тикам — сдвиги центра вида от середины клетки цели, время), затем показ.</summary>
    private void FinishScroll()
    {
        if (_scrollPath is not { } path) return;
        if (_scrollAt < path.Count - 1)
        {
            _scrollAt = path.Count - 1;
            ShowScrollPosition();
        }
        var then = _afterScroll;
        (_scrollPath, _afterScroll) = (null, null);
        ScrollCount = (ScrollCount.Scrolls + 1, ScrollCount.Positions + _scrollShown.Count);
        var cell = GroundGeometry.CellAt(_scrollTarget.X, _scrollTarget.Y);
        string min = _scrollShown.Count > 1 ? _scrollMinMs.ToString("0.#", CultureInfo.InvariantCulture) : "0";
        GameLog.Verbose($"smoothscroll в=({cell.X},{cell.Y}) положений={_scrollShown.Count} тиков={_scrollShown.Count - 1} " +
                 $"мс={_scrollClockMs.ToString("0", CultureInfo.InvariantCulture)} тик_мин_мс={min} " +
                 $"сдвиги={string.Join(";", _scrollShown.Select(p => $"{p.X},{p.Y}"))}");
        then?.Invoke();
    }
}
