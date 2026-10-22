using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Полоса очереди ходов над полем боя (своя для Last Edict, <c>docs/own-version-roadmap.md</c> §3.3): портреты <c>Faces</c> тех, кто ходит
/// сейчас и дальше в текущем раунде (<see cref="BattleSession.Upcoming"/>), слева направо. Место — полоса фона между боковыми панелями
/// (<c>IMG_LUNITGROUPBG</c> кончается на x 174, <c>IMG_RUNITGROUPBG</c> начинается на 627), y 0–85 холста 800×600; портрет 70×85 уменьшен
/// до 43 px в ширину, их до 10; отражён, как в боковой панели (<see cref="Mirrored"/>). Ходящий — в золотой рамке, наведённый — в белой; «пропустит ход» — серым и надписью «Пас»
/// (<c>X008TA0010</c>), «ждёт» — синеватым и надписью «Ждать» (<c>X008TA0020</c>); полоска под портретом — сторона (свои зелёные, чужие
/// красные). Пересобирается только по <see cref="Fill"/> (экран зовёт его по событию сессии <see cref="BattleSession.QueueChanged"/>), не
/// каждый кадр.
/// </summary>
public partial class BattleQueueStrip : Node2D
{
    /// <summary>Свободная полоса холста 800×600 над полем: x 174–627, y 0–85.</summary>
    public static readonly Rect2 Area = new(174, 0, 627 - 174, 85);

    /// <summary>Ширина портрета, просвет между портретами, верх портрета, высота полоски стороны, портретов не больше.</summary>
    public const int CellWidth = 43, Gap = 2, FaceTop = 3, SideBar = 3, MaxCells = 10;

    /// <summary>Высота портрета: 85 × 44 / 70 (пропорция <c>Faces</c> 70×85), с округлением вниз.</summary>
    public const int FaceHeight = 85 * CellWidth / 70;

    /// <summary>Левый край первого портрета: на 2 px от края полосы — места рамке (10 портретов с просветами — 448 px из 453; просвет 2 px —
    /// рамка ходящего в 2 px не заходит на соседа).</summary>
    private const int FirstX = 176;

    /// <summary>Портрет юнита (кадр <c>{базовый}FACE</c>) — задаёт экран боя.</summary>
    public Func<BattleUnit, Texture2D?> Face { get; set; } = _ => null;

    /// <summary>Юнит на стороне игрока — цвет полоски под портретом.</summary>
    public Func<BattleUnit, bool> IsPlayers { get; set; } = _ => true;

    /// <summary>Отражать ли портрет — то же правило, что у боковой панели (<see cref="BattleView.PanelMirrored"/>); задаёт экран боя.</summary>
    public Func<BattleUnit, bool> Mirrored { get; set; } = _ => false;

    /// <summary>Отражён ли портрет места — в порядке <see cref="Shown"/>, для проверок.</summary>
    public IReadOnlyList<bool> Flipped => _flipped;

    private readonly List<bool> _flipped = [];

    /// <summary>Строки языка («Пас», «Ждать»).</summary>
    public Func<string, string> Text { get; set; } = _ => "";

    /// <summary>Показанные места очереди, по порядку (не больше <see cref="MaxCells"/>).</summary>
    public IReadOnlyList<QueueEntry> Shown => _shown;

    /// <summary>Прямоугольники портретов на холсте 800×600, в порядке <see cref="Shown"/>.</summary>
    public IReadOnlyList<Rect2> Cells => _cells;

    /// <summary>Юнит под мышью (портрет в белой рамке); null — никого.</summary>
    public BattleUnit? Hovered { get; private set; }

    /// <summary>Сколько раз полоса пересобрана — для проверки «по событию, а не каждый кадр».</summary>
    public int Rebuilds { get; private set; }

    private readonly List<QueueEntry> _shown = [];
    private readonly List<Rect2> _cells = [];
    private Node2D _faces = null!;
    private Node2D _frames = null!;

    private static readonly Color Backing = new(0, 0, 0, 0.45f), CurrentFrame = new(1f, 0.82f, 0.25f), HoverFrame = new(1f, 1f, 1f, 0.9f),
        Ours = new(0.35f, 0.75f, 0.3f), Theirs = new(0.85f, 0.25f, 0.2f), Skipping = new(0.45f, 0.45f, 0.45f), Waiting = new(0.72f, 0.78f, 1f);

    public override void _Ready()
    {
        // Портрет уменьшается в 0,63 раза — ближайший сосед рвал бы черты, поэтому линейный фильтр.
        TextureFilter = TextureFilterEnum.Linear;
        _faces = new Node2D { Name = "Faces" };
        AddChild(_faces);
        _frames = new Node2D { Name = "Frames" };
        _frames.Draw += DrawFrames;
        AddChild(_frames);
    }

    /// <summary>Пересобрать полосу по очереди сессии.</summary>
    public void Fill(IReadOnlyList<QueueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Rebuilds++;
        _shown.Clear();
        _shown.AddRange(entries.Take(MaxCells));
        _cells.Clear();
        _flipped.Clear();
        foreach (var child in _faces.GetChildren()) child.QueueFree();
        for (int i = 0; i < _shown.Count; i++)
        {
            var cell = new Rect2(FirstX + (i * (CellWidth + Gap)), FaceTop, CellWidth, FaceHeight);
            _cells.Add(cell);
            AddFace(_shown[i], cell);
        }
        if (Hovered is not null && !_shown.Any(e => ReferenceEquals(e.Unit, Hovered))) Hovered = null;
        QueueRedraw();
        _frames.QueueRedraw();
    }

    /// <summary>Портрет места и надпись пометки.</summary>
    private void AddFace(QueueEntry entry, Rect2 cell)
    {
        bool mirrored = Mirrored(entry.Unit);
        _flipped.Add(mirrored);
        if (Face(entry.Unit) is { } texture)
        {
            // Большой юнит — портрет 150×85: середина в пропорции малого.
            float w = texture.GetWidth(), h = texture.GetHeight(), regionWidth = Math.Min(w, h * 70f / 85f);
            _faces.AddChild(new Sprite2D
            {
                Texture = texture, Centered = false, RegionEnabled = true, RegionRect = new Rect2((w - regionWidth) / 2f, 0, regionWidth, h),
                Position = cell.Position, Scale = new Vector2(cell.Size.X / regionWidth, cell.Size.Y / h),
                FlipH = mirrored,
                Modulate = entry.Mark switch { QueueMark.Skips => Skipping, QueueMark.Waits => Waiting, _ => Colors.White },
            });
        }
        // Строки игры с разметкой выравнивания (\hC;\vC;) — для полосы только текст.
        string raw = entry.Mark switch { QueueMark.Skips => Text("X008TA0010"), QueueMark.Waits => Text("X008TA0020"), _ => "" };
        string note = LastEdict.Data.Ui.UiTextStyle.Parse(raw).Text;
        if (note.Length == 0) return;
        var label = new Label { Text = note, HorizontalAlignment = HorizontalAlignment.Center, ClipText = true };
        if (Small() is { } font) label.LabelSettings = UiArt.Outlined(font, Colors.White, Colors.Black);
        label.Position = new Vector2(cell.Position.X, cell.End.Y - 16);
        label.Size = new Vector2(cell.Size.X, 16);
        _faces.AddChild(label);
    }

    /// <summary>Подложка под портретами и полоски сторон — под портретами (рисование самого узла идёт раньше детей).</summary>
    public override void _Draw()
    {
        if (_cells.Count == 0) return;
        DrawRect(new Rect2(_cells[0].Position.X - 2, 0, _cells[^1].End.X - _cells[0].Position.X + 4, Framed(0).End.Y + FrameWidth + 2), Backing);
        for (int i = 0; i < _cells.Count; i++) DrawRect(Bar(i), IsPlayers(_shown[i].Unit) ? Ours : Theirs);
    }

    /// <summary>Толщина рамки ходящего; рамка наведённого — в точку.</summary>
    private const int FrameWidth = 2;

    /// <summary>Полоска стороны под портретом места.</summary>
    private Rect2 Bar(int i) => new(_cells[i].Position.X, _cells[i].End.Y + 1, CellWidth, SideBar);

    /// <summary>Что обводит рамка: портрет вместе с полоской стороны — рамка снаружи, полоска видна целиком (замечание пользователя к
    /// снимку 21.10: золотая рамка ходящего закрывала полоску).</summary>
    private Rect2 Framed(int i) => new(_cells[i].Position, new Vector2(CellWidth, FaceHeight + 1 + SideBar));

    /// <summary>Штрихи рамок (ходящий — золотом в <see cref="FrameWidth"/>, наведённый — белым в точку), каждый — прямоугольник снаружи
    /// <see cref="Framed"/>.</summary>
    private List<(Rect2 Stroke, Color Color)> FrameStrokes()
    {
        var strokes = new List<(Rect2, Color)>();
        void Around(Rect2 r, int w, Color color)
        {
            strokes.Add((new Rect2(r.Position.X - w, r.Position.Y - w, r.Size.X + (2 * w), w), color));
            strokes.Add((new Rect2(r.Position.X - w, r.End.Y, r.Size.X + (2 * w), w), color));
            strokes.Add((new Rect2(r.Position.X - w, r.Position.Y, w, r.Size.Y), color));
            strokes.Add((new Rect2(r.End.X, r.Position.Y, w, r.Size.Y), color));
        }
        for (int i = 0; i < _cells.Count; i++)
        {
            if (_shown[i].Mark == QueueMark.Current) Around(Framed(i), FrameWidth, CurrentFrame);
            if (ReferenceEquals(_shown[i].Unit, Hovered)) Around(Framed(i), 1, HoverFrame);
        }
        return strokes;
    }

    /// <summary>Рамки ходящего и наведённого — поверх портретов.</summary>
    private void DrawFrames()
    {
        foreach (var (stroke, color) in FrameStrokes()) _frames.DrawRect(stroke, color);
    }

    /// <summary>Полосок сторон, которые хоть точкой закрывает рамка, — для проверок (должно быть 0).</summary>
    public int BarsCovered()
    {
        var strokes = FrameStrokes();
        return Enumerable.Range(0, _cells.Count).Count(i => strokes.Any(s => s.Stroke.Intersects(Bar(i))));
    }

    /// <summary>Юнит портрета под точкой холста 800×600; мимо портретов — null.</summary>
    public BattleUnit? UnitAt(Vector2 at)
    {
        for (int i = 0; i < _cells.Count; i++)
            if (_cells[i].HasPoint(at)) return _shown[i].Unit;
        return null;
    }

    /// <summary>Подсветить портрет юнита (мышь над ним здесь или над юнитом на поле); null или юнита нет в полосе — снять.</summary>
    public void Hover(BattleUnit? unit)
    {
        if (unit is not null && !_shown.Any(e => ReferenceEquals(e.Unit, unit))) unit = null;
        if (ReferenceEquals(unit, Hovered)) return;
        Hovered = unit;
        _frames.QueueRedraw();
    }

    /// <summary>Рамка всего нарисованного «x0,y0,x1,y1» — портреты, полоски сторон и место рамок вокруг них (пусто — «-»); для проверок.</summary>
    public string Bounds() => _cells.Count == 0 ? "-"
        : FormattableString.Invariant($"{_cells.Min(c => c.Position.X) - FrameWidth},{_cells.Min(c => c.Position.Y) - FrameWidth},") +
          FormattableString.Invariant($"{_cells.Max(c => c.End.X) + FrameWidth},{Enumerable.Range(0, _cells.Count).Max(i => Framed(i).End.Y) + FrameWidth}");

    private static LastEdict.Data.Ui.UiFont? _small;
    private static bool _smallFailed;

    /// <summary>Шрифт надписей — <c>small</c> интерфейса; нет — шрифт темы, причина печатается один раз.</summary>
    private static LastEdict.Data.Ui.UiFont? Small()
    {
        if (_small is not null || _smallFailed) return _small;
        try
        {
            return _small = GameData.Fonts.Resolve("small");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException
                                      or KeyNotFoundException)
        {
            _smallFailed = true;
            GD.PrintErr($"BattleQueueStrip: шрифт small — {e.Message}");
            return null;
        }
    }
}
