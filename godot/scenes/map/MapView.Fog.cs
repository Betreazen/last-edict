using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using D2.Data.Map;
using D2.Rules.Magic;
using D2.Rules.Map;
using Godot;

namespace D2.Godot;

/// <summary>
/// Смена тумана кольцами (<c>CMidAnim2ChangeFog</c>, <see cref="FogReveal"/>). Вид показывает свой туман зрителя (<see cref="_shownFog"/>,
/// <see cref="MapScene.Build"/>): обычно он сразу равен туману модели (<see cref="Reload"/>), а после эффекта <c>CEffectFog</c> — развеивание
/// и туман заклинанием, событие <c>L_UNFOG</c>, перемены тумана в ход ИИ и в начале хода — догоняет модель кольцами от центра эффекта, кольцо
/// за тик медленной группы (66 мс), после прокрутки вида к центру (только если далеко, без проверки видимости — <c>0x4f351d</c>). Клетки, что
/// модель сменила не таким эффектом (обзор по шагу хода, захват), встают сразу. Центр и радиус известны у заклинания и события на пути; в ход
/// ИИ и в начале хода — по рамке сменившихся клеток (у эффекта на закрытой области она и есть квадрат эффекта; ранг 3 — откуда эффект, вид
/// не знает).
/// </summary>
public partial class MapView
{
    /// <summary>Туман, показанный зрителю (копия; null — зрителя нет).</summary>
    private bool[,]? _shownFog;

    /// <summary>Известные эффекты тумана, ещё не показанные: центр и радиус (<c>CEffectFog</c>, поле клетки и <c>[+0x14]</c>).</summary>
    private readonly List<(int X, int Y, int Radius)> _fogHints = [];

    /// <summary>Туман держится: ждёт показа эффекта (<see cref="HoldFog"/>) или показ идёт — перерисовка не догоняет модель сама.</summary>
    private bool _fogHold;

    private readonly Queue<FogReveal> _fogQueue = new();
    private FogReveal? _fogNow;
    private int _fogStep;
    private readonly AnimTicker _fogTicker = new(AnimGroup.Slow);
    private double _fogClockMs, _fogLastMs, _fogMinMs;
    private readonly List<int> _fogChanged = [];
    private readonly List<string> _fogCells = [];
    private int _fogCellCount;

    /// <summary>Клеток эффекта, выше которого строка журнала не перечисляет их (развеивание всей карты — тысячи).</summary>
    private const int MaxLoggedFogCells = 400;

    /// <summary>Идёт смена тумана кольцами (или прокрутка к ней).</summary>
    public bool FogChanging => _fogNow is not null || _fogQueue.Count > 0;

    /// <summary>Туман модели глазами зрителя; зрителя нет — null.</summary>
    private bool[,]? ModelFog() =>
        Viewer is { } viewer && Map.Players.TryGetValue(viewer, out var player) && Map.Fog.TryGetValue(player.FogId, out var fog) ? fog : null;

    /// <summary>Показанный туман — снова туман модели (новый зритель, загрузка, обычная перерисовка).</summary>
    private void SyncFog()
    {
        var model = ModelFog();
        if (model is null) { _shownFog = null; return; }
        if (_shownFog is null || _shownFog.GetLength(0) != model.GetLength(0) || _shownFog.GetLength(1) != model.GetLength(1))
            _shownFog = (bool[,])model.Clone();
        else Array.Copy(model, _shownFog, model.Length);
    }

    /// <summary>Туман для сцены: показанный, если смена держится, иначе — модели (и показанный догоняет её).</summary>
    private bool[,]? SceneFog()
    {
        if (!_fogHold) SyncFog();
        return _shownFog;
    }

    /// <summary>Эффект тумана будет — показ держит прежний туман до <see cref="PlayFogChanges"/> (литьё человека: модель меняется сразу после
    /// <see cref="ITurnObserver.Happened"/>).</summary>
    public void HoldFog(int x, int y, int radius)
    {
        if (_shownFog is null) return;
        _fogHints.Add((x, y, radius));
        _fogHold = true;
    }

    /// <summary>Центр и радиус эффекта тумана заклинания (как у модели: <c>GameSession.UnfogSpell</c>/<c>FogSpell</c>, <c>0x41c723</c>,
    /// <c>0x41c7f8</c>): радиус <c>AREA / 2 + 1</c>, у развеивания всей карты — сторона + 1, у тумана на всю карту — от центра карты, половина
    /// стороны + 1. Не туман — null.</summary>
    public (int X, int Y, int Radius)? SpellFog(string spellId, int x, int y)
    {
        if (Session.Book?.Spells.TryGetValue(spellId, out var spell) != true || spell is null) return null;
        int size = Map.Size;
        return spell.Category switch
        {
            SpellCategory.Unfog => (x, y, (spell.Area == WholeMapArea ? size : spell.Area / 2) + 1),
            SpellCategory.Fog when spell.Area == WholeMapArea => (size / 2, size / 2, (size / 2) + 1),
            SpellCategory.Fog => (x, y, (spell.Area / 2) + 1),
            _ => null,
        };
    }

    private const int WholeMapArea = 999;

    /// <summary>Эффекты тумана событий, сработавших на пути (<c>L_UNFOG</c>: центр области, радиус <c>(NUMVALUE == −1 ? радиус области :
    /// NUMVALUE) + 1</c> — как у модели). Оборвавшие путь (<see cref="EventFiring.CutOnly"/>) эффектов не дают — их событие проверяется
    /// после хода заново.</summary>
    private void HintEventFog(IEnumerable<EventFiring> fired)
    {
        foreach (var firing in fired.Where(f => !f.CutOnly))
            foreach (var effect in firing.Event.Effects.OfType<UnfogEffect>())
                if (Session.State.Events.Zones.GetValueOrDefault(effect.LocationId) is { } zone)
                    _fogHints.Add((zone.X, zone.Y, (effect.Value == -1 ? zone.Radius : effect.Value) + 1));
    }

    /// <summary>
    /// Показать смену тумана: показанный туман догоняет модель. Клетки в квадратах известных эффектов (<see cref="_fogHints"/>) — кольцами;
    /// прочие — при <paramref name="derive"/> тоже кольцами (рамка сменившихся клеток — центр и радиус), иначе сразу. Пошла анимация — true.
    /// </summary>
    public bool PlayFogChanges(bool derive)
    {
        if (FogChanging) return true;   // подсказки ждут конца нынешнего показа (NextFogChange)
        var model = ModelFog();
        var hints = _fogHints.ToList();
        _fogHints.Clear();
        if (model is null || _shownFog is null)
        {
            _fogHold = false;
            SyncFog();
            return false;
        }
        int size = model.GetLength(0);
        var changed = new HashSet<(int X, int Y)>();
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                if (model[x, y] != _shownFog[x, y]) changed.Add((x, y));
        foreach (var (x, y, radius) in hints)
        {
            var reveal = new FogReveal(x, y, radius, size);
            var inside = changed.Where(c => Math.Abs(c.X - x) <= radius && Math.Abs(c.Y - y) <= radius).ToList();
            if (inside.Count == 0) continue;   // эффект не про зрителя (другой игрок списка) — у его клиента команды нет
            _fogQueue.Enqueue(reveal);
            changed.ExceptWith(inside);
        }
        if (derive)
            foreach (var group in Groups(changed).ToList())
            {
                int minX = group.Min(c => c.X), maxX = group.Max(c => c.X), minY = group.Min(c => c.Y), maxY = group.Max(c => c.Y);
                int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
                int radius = Math.Max(Math.Max(maxX - cx, cx - minX), Math.Max(maxY - cy, cy - minY));
                _fogQueue.Enqueue(new FogReveal(cx, cy, radius, size));
                changed.ExceptWith(group);
            }
        foreach (var (x, y) in changed) _shownFog[x, y] = model[x, y];   // не эффект тумана — сразу
        if (_fogQueue.Count == 0)
        {
            _fogHold = false;
            if (changed.Count > 0) Redraw();
            return false;
        }
        _fogHold = true;
        if (changed.Count > 0) Redraw();
        NextFogChange();
        return true;
    }

    /// <summary>Связные (по 8 соседям) куски сменившихся клеток.</summary>
    private static IEnumerable<List<(int X, int Y)>> Groups(HashSet<(int X, int Y)> cells)
    {
        var left = new HashSet<(int X, int Y)>(cells);
        while (left.Count > 0)
        {
            var start = left.First();
            left.Remove(start);
            var group = new List<(int X, int Y)> { start };
            var stack = new Stack<(int X, int Y)>([start]);
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (left.Remove((x + dx, y + dy)))
                        {
                            group.Add((x + dx, y + dy));
                            stack.Push((x + dx, y + dy));
                        }
            }
            yield return group;
        }
    }

    /// <summary>Следующий эффект очереди: прокрутка к центру (только если далеко), затем первое кольцо — сразу (старт задачи зовёт шаг,
    /// <c>0x4f1d34</c>).</summary>
    private void NextFogChange()
    {
        if (!_fogQueue.TryDequeue(out var next))
        {
            _fogNow = null;
            if (_fogHints.Count > 0 && PlayFogChanges(derive: false)) return;   // эффект, пришедший во время показа
            _fogHold = false;
            SyncFog();   // клетки, до которых кольца не дошли (квадрат зажат картой), — к модели
            Redraw();
            return;
        }
        _fogNow = next;
        _fogStep = 0;
        ScrollThen((next.X, next.Y), onlyIfFar: true, StartFogChange);
    }

    private void StartFogChange()
    {
        (_fogClockMs, _fogLastMs, _fogMinMs) = (0, 0, double.MaxValue);
        _fogChanged.Clear();
        _fogCells.Clear();
        _fogCellCount = 0;
        _fogTicker.Reset();
        FogRing();
    }

    /// <summary>Тик медленной группы — следующее кольцо; последнее — конец эффекта и следующий из очереди.</summary>
    private void StepFog(double delta)
    {
        if (_fogNow is null || Scrolling) return;
        _fogClockMs += delta * 1000;
        if (!_fogTicker.Advance(delta * 1000)) return;
        _fogMinMs = Math.Min(_fogMinMs, _fogClockMs - _fogLastMs);
        _fogLastMs = _fogClockMs;
        FogRing();
    }

    /// <summary>Кольцо шага: каждой его клетке — туман модели (<c>0x5f3bdc</c>), перерисовка сменившихся (<c>0x5ac56a</c>).</summary>
    private void FogRing()
    {
        if (_fogNow is not { } reveal) return;   // вид сброшен (загрузка, другой зритель) во время прокрутки к эффекту
        var model = ModelFog();
        int changed = 0;
        var cells = new List<string>();
        if (model is not null && _shownFog is not null)
            foreach (var (x, y) in reveal.Cells(_fogStep))
                if (_shownFog[x, y] != model[x, y])
                {
                    _shownFog[x, y] = model[x, y];
                    changed++;
                    cells.Add($"{x},{y}");
                }
        _fogChanged.Add(changed);
        _fogCells.Add(string.Join("/", cells));
        _fogCellCount += cells.Count;
        if (changed > 0) Redraw();
        if (++_fogStep < reveal.Steps) return;
        GameLog.Verbose($"fogchange в=({reveal.X},{reveal.Y}) радиус={reveal.Radius} шагов={reveal.Steps} " +
                 $"мс={_fogClockMs.ToString("0", CultureInfo.InvariantCulture)} " +
                 $"тик_мин_мс={(reveal.Steps > 1 ? _fogMinMs : 0).ToString("0.#", CultureInfo.InvariantCulture)} " +
                 $"сменилось={_fogChanged.Sum()} по_шагам={string.Join(",", _fogChanged)} " +
                 $"клетки={(_fogCellCount <= MaxLoggedFogCells ? string.Join(";", _fogCells) : "-")}");
        FogShown = (FogShown.Effects + 1, FogShown.Cells + _fogChanged.Sum());
        _fogNow = null;
        NextFogChange();
    }

    /// <summary>Проверка: открыть зрителю в модели квадрат <c>±r</c> без углов (форма <c>CEffectFog</c>, <c>0x5d4dfc</c>), как сделал бы эффект;
    /// сколько клеток открылось.</summary>
    public int OpenFogForCheck(int x, int y, int radius)
    {
        if (ModelFog() is not { } fog) return 0;
        int opened = 0;
        for (int fx = x - radius; fx <= x + radius; fx++)
            for (int fy = y - radius; fy <= y + radius; fy++)
                if (Map.Contains(fx, fy) && D2.Rules.Map.Fog.InSight(fx - x, fy - y, radius, D2.Rules.Map.Fog.DefaultShape) && fog[fx, fy])
                {
                    fog[fx, fy] = false;
                    opened++;
                }
        return opened;
    }

    /// <summary>Клеток, где показанный туман расходится с моделью (проверка: после смены — 0).</summary>
    public int FogMismatch()
    {
        if (ModelFog() is not { } model || _shownFog is null) return 0;
        int n = 0;
        for (int x = 0; x < model.GetLength(0); x++)
            for (int y = 0; y < model.GetLength(1); y++)
                if (model[x, y] != _shownFog[x, y]) n++;
        return n;
    }

    /// <summary>Сбросить очереди показа (загрузка, другой зритель, уход вида из хода ИИ): прокрутка без продолжения, смена тумана — сразу к
    /// модели, отложенная заявка хода ИИ.</summary>
    private void ResetAnimations()
    {
        (_scrollPath, _afterScroll, _aiAfterFog, _fogNow, _fogHold) = (null, null, null, null, false);
        _fogQueue.Clear();
        _fogHints.Clear();
        SyncFog();
    }

    /// <summary>Показано эффектов тумана кольцами и клеток в них (проверка).</summary>
    public (int Effects, int Cells) FogShown { get; private set; }

    /// <summary>Земля и картинки — по показанному туману.</summary>
    private void Redraw()
    {
        RefreshGround();
        Reload(_hiddenNow);
    }
}
