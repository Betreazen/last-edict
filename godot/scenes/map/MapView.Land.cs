using System;
using System.Collections.Generic;
using System.Linq;
using D2.Data.Gfx;
using D2.Data.Play;
using D2.Rules.Map;
using Godot;

namespace D2.Godot;

/// <summary>Растекание земли по клеткам (<c>CMidAnim2Terrain</c>, сообщение 27; разбор 28.09, ранг 1 по листингу): клетки идут группами —
/// обход каждого источника (крепость, жезл) в порядке взятия. Раз в 5 тиков по 66 мс (первая работа — на 5-м тике) из каждой группы
/// снимается пачка — <c>n ≤ 3</c>: вся, <c>4…20</c>: по 4, больше — по <c>⌊n/5⌋</c> (<c>0x4f3a29</c>), — земля пачки перекрашивается, и один
/// раз звучит <c>OCCUPY</c> (<c>0x4f3b03</c>); конец — когда все группы пусты. Звуков <c>max(1, max ⌈n/шаг⌉)</c>, не больше шести.
/// Показ держит на ещё не пройденных клетках прежнюю землю подменой типа в сборщике (<see cref="_landHeld"/>), пока модель не запишет
/// новую. Квадраты жил (<c>CVisitorMapTerrain</c>, <c>0x60ba1c</c>/<c>0x60ba78</c>, ранг 1): кристалл на клетке пачки или рядом с ней
/// (8 соседей) — квадрат 3×3 вокруг него перекрашивается с этой же пачкой; прочие клетки вне групп — с последней.</summary>
public partial class MapView
{
    private const double LandTick = D2.Data.Map.AnimClock.SlowMs / 1000.0;   // CMidAnim2Terrain — медленная группа
    private const int LandEvery = 5;

    /// <summary>Подмена типа земли клеток на время растекания: пока клетка не пройдена — прежний, после — новый.</summary>
    private readonly Dictionary<(int X, int Y), GroundType> _landHeld = [];
    private Tile[,]? _landBase;   // клетки карты в начале показа: подмена снимается, когда модель записала новое состояние
    private bool _landDone;
    private int _landRun;         // номер показа: таймеры прежнего показа (новый начался раньше их конца) ничего не трогают

    /// <summary>Шаг пачки группы из <paramref name="n"/> клеток (<c>0x4f3a29</c>).</summary>
    public static int LandBatch(int n) => n <= 3 ? n : n <= 20 ? 4 : n / 5;

    /// <summary>Рабочих тиков (и звуков) у растекания: <c>max(1, max ⌈n/шаг⌉)</c> по группам.</summary>
    public static int LandTicks(IReadOnlyList<IReadOnlyList<(int X, int Y)>> groups) =>
        Math.Max(1, groups.Where(g => g.Count > 0).Select(g => (g.Count + LandBatch(g.Count) - 1) / LandBatch(g.Count)).DefaultIfEmpty(0).Max());

    /// <summary>Показать растекание (<see cref="TurnSightKind.LandSpread"/>): состояние партии ещё прежнее или уже новое — клетки держат
    /// прежнюю землю, пока до них не дошла пачка. Возвращает длительность при скорости 1, с (пауза показа хода ИИ).</summary>
    public double PlayLandSpread(TurnSight sight, float speed = 1f)
    {
        ArgumentNullException.ThrowIfNull(sight);
        var groups = sight.Groups.Count > 0 ? sight.Groups : [sight.Cells];
        int ticks = LandTicks(groups);
        var to = NewLand(sight.PlayerId);
        _landHeld.Clear();
        _landBase = Map.Tiles;
        _landDone = false;
        int run = ++_landRun;
        foreach (var c in sight.Cells) _landHeld[c] = GroundGeometry.TypeOf(Map.Tiles[c.X, c.Y]);
        var crystals = Looks.Crystals.Select(c => (c.X, c.Y)).ToList();
        var audio = GameAudio.Of(this);
        for (int k = 1; k <= ticks; k++)
        {
            int tick = k;
            double at = tick * LandEvery * LandTick / Math.Max(0.01f, speed);
            GetTree().CreateTimer(at).Timeout += () =>
            {
                if (!IsInstanceValid(this) || !IsInsideTree() || run != _landRun) return;
                var batch = new List<(int X, int Y)>();
                foreach (var g in groups)
                {
                    int step = LandBatch(g.Count);
                    for (int i = (tick - 1) * step; i < Math.Min(g.Count, tick * step); i++)
                    {
                        _landHeld[g[i]] = to;
                        batch.Add(g[i]);
                    }
                }
                foreach (var (cx, cy) in crystals.Where(c => batch.Any(b => Math.Max(Math.Abs(b.X - c.X), Math.Abs(b.Y - c.Y)) <= 1)))
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if (_landHeld.ContainsKey((cx + dx, cy + dy))) _landHeld[(cx + dx, cy + dy)] = to;
                if (tick == ticks)
                {
                    foreach (var c in sight.Cells) _landHeld[c] = to;
                    _landDone = true;
                }
                audio.PlayFx(Archive, "OCCUPY");
                RefreshGround(land: true);
                GameLog.Verbose($"landspread пачка {tick}/{ticks} новых={_landHeld.Values.Count(t => t == to)}/{sight.Cells.Count}");
            };
        }
        GameLog.Verbose($"landspread клеток={sight.Cells.Count} групп={groups.Count} пачки={string.Join("+", groups.Select(g => LandBatch(g.Count)))} " +
                 $"звуков={ticks} в=({sight.X},{sight.Y})");
        return ticks * LandEvery * LandTick;
    }

    /// <summary>Тип новой земли игрока — земля его расы.</summary>
    private GroundType NewLand(string playerId)
    {
        var book = Session.Book!;
        int terrain = book.Economy.Races[Map.Players[playerId].RaceId].Terrain;
        return GroundGeometry.TypeOf(new Tile((byte)terrain));
    }

    /// <summary>Подмена клеток сборщику: снимается целиком, когда показ кончился и модель записала новое состояние.</summary>
    private void HoldLand(GroundType[,] cells, MapState map)
    {
        if (_landDone && !ReferenceEquals(map.Tiles, _landBase)) _landHeld.Clear();
        foreach (var ((x, y), type) in _landHeld) cells[x, y] = type;
    }
}
