using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Слой картинок карты над землёй: <see cref="MapScene.Sprites"/> в порядке движка — порядок детей узла и
/// есть порядок отрисовки. Анимации — +1 кадр каждые 66 мс у всех сразу, первый кадр случайный
/// (<c>docs/map-render.md</c> §5). Тени рисуются полупрозрачными: как движок смешивает силуэт <c>(1,0,1)</c>, не
/// разобрано. Текстуры грузятся один раз на картинку — общим кэшем <see cref="GfxTextures"/>.</summary>
public partial class MapObjects : Node2D
{
    private const double FrameMs = LastEdict.Data.Map.AnimClock.SlowMs;   // медленная группа (FactoryImageAnim, 0x52def4)

    private readonly Dictionary<(string, string), FrameSet> _textures = new();
    private readonly List<(Sprite2D Sprite, FrameSet Frames, int Frame)> _animated = new();
    private double _clock;

    public int Shown { get; private set; }

    /// <summary>Узлов, переживших пересборку со своим кадром, с начала (проверка: пересборка не сбрасывает фазы нетронутых объектов).</summary>
    public int PhaseKept { get; private set; }

    /// <summary>Анимированных узлов, которым прежняя пересборка сменила бы кадр скачком (проверка мигания, до 11.10 — каждый такой узел).</summary>
    public int PhaseJumpsAvoided { get; private set; }

    /// <summary>Пересборок с начала (проверка).</summary>
    public int Builds { get; private set; }

    /// <summary>Знамёна отрядов (слой <c>BANNERS</c>) видны: у движка картинка знамени рисуется, только пока включён <c>TOG_BANNERS</c>
    /// или зажат Shift (<see cref="LastEdict.Data.Map.MapPalette"/>, <c>0x5b3369</c>). Узлы знамён остаются в порядке отрисовки, меняется
    /// только их видимость.</summary>
    public bool BannersShown
    {
        get => _bannersShown;
        set
        {
            if (_bannersShown == value) return;
            _bannersShown = value;
            foreach (var node in _banners) if (IsInstanceValid(node)) node.Visible = value;
        }
    }

    private bool _bannersShown = LastEdict.Data.Map.MapPalette.Banners;
    private readonly List<Sprite2D> _banners = new();
    private static int? _bannerLayer;

    /// <summary>Картинка — знамя отряда (слой <c>BANNERS</c>).</summary>
    private static bool IsBanner(MapSprite s) => s.Layer == (_bannerLayer ??= GameData.Content.MapArt.Layer("BANNERS"));

    /// <summary>Знамя прячется по <see cref="BannersShown"/>; прочие картинки видны всегда.</summary>
    private void Track(Sprite2D sprite, MapSprite s)
    {
        if (!IsBanner(s)) return;
        sprite.Visible = _bannersShown;
        _banners.Add(sprite);
    }

    /// <summary>Сколько узлов знамён и сколько из них видно — для строки проверки.</summary>
    public (int All, int Visible) BannerCount()
    {
        _banners.RemoveAll(n => !IsInstanceValid(n));
        return (_banners.Count, _banners.Count(n => n.Visible));
    }

    /// <summary>Картинки, у которых не нашлось файла PNG (опись есть, файла нет).</summary>
    public int MissingFiles { get; private set; }

    /// <summary>Метки (выделение, флажки пути) — в общем порядке отрисовки по ключу движка, как у движка (флажок под отрядом той же
    /// диагонали, <c>SYM_MOVE_PATH</c> 390 и <c>SYM_STACK_SEL</c> 25 — слои группы 0): <see cref="ShowLayered"/>, канал «метки».
    /// Флажки бегут по пути: начальный кадр — номер метки в списке (<c>0x4cce9c</c>: номер клетки пути % 11, ранг 3 — порядок списка).
    /// Журнал зонда (схема 7.6): эллипс и флажки — строки <c>map_sel</c>/<c>map_path</c> на смену видимого, как у зонда оригинала.</summary>
    public void ShowMarks(IReadOnlyList<MapSprite> marks, GfxAssets gfx, string root, string? pathStack = null)
    {
        // Флажки — по порядку пути (список меток идёт в порядке отрисовки; номер клетки — в источнике «путь N», MapScene.Marks).
        PortProbe.Av?.MapMarks(marks.OrderBy(m => m.Source.StartsWith("путь ", StringComparison.Ordinal) && int.TryParse(m.Source[5..], out int i) ? i : 0)
                                    .Select(m => (m.Name, m.AnchorX, m.AnchorY)), pathStack);
        ShowLayered("marks", marks, gfx, root);
    }

    /// <summary>Картинки, которые досыпаются в общий порядок отрисовки без пересборки сцены (метки, квадрат области заклинания —
    /// <c>SYM_SPELL_AREA</c> 895 под туманом 1052): у каждой — ключ движка (<see cref="MapSprite.Key"/>), место — после всех картинок
    /// с ключом не больше. Канал заменяется целиком; пустой список — канал убран. Переживает пересборку сцены.
    /// <paramref name="carryPhase"/> — анимация продолжается с того кадра, на котором стояла прежняя картинка канала (подсветка клетки
    /// <c>TILE_HIGHLIGHT</c> при смене клетки, <c>0x4d4bc9</c>); иначе кадр — номер картинки в списке.</summary>
    public void ShowLayered(string channel, IReadOnlyList<MapSprite> sprites, GfxAssets gfx, string root, bool carryPhase = false)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        if (carryPhase) KeepPhase(channel);
        else _phases.Remove(channel);
        RemoveChannel(channel);
        _channels[channel] = (sprites, gfx, root);
        InsertChannel(channel);
    }

    /// <summary>Сдвинуть картинки канала (облака): те же картинки и якоря — ключи и порядок прежние, меняется только место.</summary>
    public void MoveLayered(string channel, IReadOnlyList<MapSprite> sprites)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        if (!_channels.TryGetValue(channel, out var entry) || !_channelNodes.TryGetValue(channel, out var nodes)) return;
        _channels[channel] = (sprites, entry.Gfx, entry.Root);
        int n = 0;
        for (int i = 0; i < sprites.Count && n < nodes.Count; i++)
            if (Frames(sprites[i], entry.Gfx, entry.Root).Length > 0) nodes[n++].Position = new Vector2(sprites[i].X, sprites[i].Y);
    }

    /// <summary>Кадр, с которого продолжают анимации канала после замены или пересборки (<see cref="ShowLayered"/>).</summary>
    private readonly Dictionary<string, int> _phases = new();

    private void KeepPhase(string channel)
    {
        if (!_channelNodes.TryGetValue(channel, out var nodes) || nodes.Count == 0) return;
        int at = _animated.FindIndex(a => a.Sprite == nodes[0]);
        if (at >= 0) _phases[channel] = _animated[at].Frame;
    }

    /// <summary>Кадр анимации картинки канала сейчас (проверка переноса фазы); нет — -1.</summary>
    public int PhaseOf(string channel) =>
        _channelNodes.TryGetValue(channel, out var nodes) && nodes.Count > 0 && _animated.FindIndex(a => a.Sprite == nodes[0]) is var at and >= 0
            ? _animated[at].Frame : -1;

    private readonly Dictionary<string, (IReadOnlyList<MapSprite> Sprites, GfxAssets Gfx, string Root)> _channels = new();
    private readonly Dictionary<string, List<Sprite2D>> _channelNodes = new();

    /// <summary>Картинки сцены и досыпанные, по порядку отрисовки; ключи — параллельно детям.</summary>
    private Node2D Ordered => _ordered ??= MakeOrdered();

    private Node2D? _ordered;
    private readonly List<long> _keys = new();

    private Node2D MakeOrdered()
    {
        var node = new Node2D { Name = "Ordered" };
        AddChild(node);
        MoveChild(node, 0);   // под идущим отрядом и числами дней
        return node;
    }

    /// <summary>Снять узлы канала. Места берутся все до первого снятия: <c>remove_child</c> помечает кэш порядка детей устаревшим, и
    /// следующий <c>get_index</c> пересобирает его целиком (сортировка всех детей) — чередование стоило пересборку на каждый узел
    /// (замер Godot 4.7.2: 200 узлов из 3700 — 12 мс против 0,6). Ключи снимаются с конца, анимации — одним проходом.</summary>
    private void RemoveChannel(string channel)
    {
        if (!_channelNodes.Remove(channel, out var nodes)) return;
        var at = new int[nodes.Count];
        for (int i = 0; i < nodes.Count; i++) at[i] = nodes[i].GetIndex();
        Array.Sort(at);
        for (int i = at.Length - 1; i >= 0; i--) _keys.RemoveAt(at[i]);
        foreach (var node in nodes)
        {
            Ordered.RemoveChild(node);
            node.QueueFree();
        }
        var gone = new HashSet<Sprite2D>(nodes, ReferenceEqualityComparer.Instance);
        _animated.RemoveAll(a => gone.Contains(a.Sprite));
    }

    private void InsertChannel(string channel)
    {
        if (!_channels.TryGetValue(channel, out var entry)) return;
        var nodes = new List<Sprite2D>();
        for (int i = 0; i < entry.Sprites.Count; i++)
        {
            var s = entry.Sprites[i];
            var frames = Frames(s, entry.Gfx, entry.Root);
            if (frames.Length == 0) continue;
            int frame = (_phases.TryGetValue(channel, out int phase) ? phase + i : i) % frames.Length;
            var sprite = new Sprite2D
            {
                Texture = frames[frame], Centered = false, Position = new Vector2(s.X, s.Y), TextureFilter = TextureFilterEnum.Nearest,
            };
            GfxBlending.Apply(sprite, GfxBlending.Of(s, entry.Gfx));
            int at = UpperBound(s.Key);
            Ordered.AddChild(sprite);
            Ordered.MoveChild(sprite, at);
            _keys.Insert(at, s.Key);
            nodes.Add(sprite);
            if (frames.Length > 1) _animated.Add((sprite, frames, frame));
        }
        _channelNodes[channel] = nodes;
    }

    /// <summary>Первое место с ключом больше данного.</summary>
    private int UpperBound(long key)
    {
        int lo = 0, hi = _keys.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_keys[mid] <= key) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Картинки идущего отряда: они живут отдельным узлом, который сцена сдвигает по пикселям
    /// (<c>docs/map-render-stacks.md</c> §6). Пустой список убирает узел.</summary>
    public void ShowMoving(IReadOnlyList<MapSprite> sprites, GfxAssets gfx, string root)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        _moving ??= Add(new Node2D { Name = "Moving" });
        foreach (var child in _moving.GetChildren()) child.QueueFree();
        // Новый шаг (другое направление) — та же анимация юнита дальше со своего кадра: «кадр анимации юнита идёт своим ходом»
        // (docs/map-render-stacks.md §6 п. 4), перерисовка берёт кадр старой картинки (0x5ace8a). Прежде каждый шаг начинал с кадра 0.
        int phase = sprites.Count > 0 && _movingFrames.Count > 0 ? _movingFrames[0].Frame : 0;
        _movingFrames.Clear();
        foreach (var s in sprites)
        {
            var frames = Frames(s, gfx, root);
            if (frames.Length == 0) continue;
            int frame = phase % frames.Length;
            var sprite = new Sprite2D
            {
                Texture = frames[frame], Centered = false, Position = new Vector2(s.X, s.Y),
                TextureFilter = TextureFilterEnum.Nearest,
            };
            GfxBlending.Apply(sprite, GfxBlending.Of(s, gfx));
            Track(sprite, s);
            _moving.AddChild(sprite);
            if (frames.Length > 1) _movingFrames.Add((sprite, frames, frame));
        }
        _moving.Position = Vector2.Zero;
    }

    /// <summary>Числа дней над флажками пути (<c>X005TA0935</c>: крупное белое с чёрной обводкой). Шрифт — тема
    /// Godot: разбор не нашёл, где движок берёт начертание для этого места.</summary>
    public void ShowDays(IReadOnlyList<(Vector2 At, int Day)> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        _days ??= Add(new Node2D { Name = "Days" });
        foreach (var child in _days.GetChildren()) child.QueueFree();
        foreach (var (at, day) in days)
        {
            var label = new Label
            {
                Text = day.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Position = at, Size = new Vector2(72, 24), HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            _days.AddChild(label);
        }
    }

    private Node2D? _days;

    /// <summary>Сдвиг картинок идущего отряда сейчас (проверка: вид следует за отрядом).</summary>
    public Vector2 MovingOffset => _moving?.Position ?? Vector2.Zero;

    /// <summary>Сдвиг картинок идущего отряда от его клетки.</summary>
    public void MoveOffset(Vector2 offset)
    {
        if (_moving is not null) _moving.Position = offset;
    }

    private readonly List<(Sprite2D Sprite, FrameSet Frames, int Frame)> _movingFrames = new();
    private Node2D? _moving;

    private Node2D Add(Node2D node)
    {
        AddChild(node);
        return node;
    }

    /// <summary>Сколько картинок сцены ложится половиной, сложением и вычитанием (<see cref="GfxBlending"/>) — для проверок.</summary>
    public (int Half, int Add, int Subtract) BlendCounts()
    {
        int half = 0, add = 0, sub = 0;
        foreach (var child in Ordered.GetChildren())
        {
            if (child is not CanvasItem item) continue;
            if (item.Material is CanvasItemMaterial m) _ = m.BlendMode == CanvasItemMaterial.BlendModeEnum.Add ? add++ : sub++;
            else if (item.Modulate.A < 1) half++;
        }
        return (half, add, sub);
    }

    /// <summary>Чем картинка сцены выглядит, кроме кадра анимации: кадры (архив, имя), смешение (анимация, тень), место и ключ порядка.
    /// Узел с тем же обликом при пересборке переиспользуется (<see cref="Build"/>).</summary>
    private readonly record struct Look(string Archive, string Name, bool Anim, bool Shadow, int X, int Y, long Key)
    {
        public static Look Of(MapSprite s) => new(s.Archive, s.Name, s.Anim, s.Shadow, s.X, s.Y, s.Key);
    }

    /// <summary>Узлы картинок сцены (без каналов) по порядку детей <see cref="Ordered"/> и их облик.</summary>
    private List<(Sprite2D Node, Look Look)> _scene = new();

    /// <summary>Пересобрать картинки сцены по изменениям (гипотеза Г2 <c>docs/perf-log.md</c>; источник — скилл gdscript-knowledge,
    /// performance.md «Узлы, дерево сцены и обработка кадров»: создать и удалить узел ~37 мкс против ~4 у готового). Узел с тем же
    /// обликом (<see cref="Look"/>) остаётся, новые создаются, исчезнувшие снимаются, порядок детей и <c>_keys</c> — как у полной
    /// пересборки. Кадр анимации: у переиспользованного узла — его текущий (фаза идёт дальше, как у движка), у нового — <c>new Random(1)</c> по
    /// порядку сцены.</summary>
    public void Build(MapScene scene, GfxAssets gfx, string root)
    {
        Builds++;
        foreach (string channel in _phases.Keys.ToList()) KeepPhase(channel);   // пересборка не сбрасывает перенесённую фазу
        // Каналы снять сразу: места вставки считаются по списку детей (InsertChannel); после этого дети Ordered — ровно _scene.
        foreach (string channel in _channelNodes.Keys.ToList()) RemoveChannel(channel);
        var frameOf = new Dictionary<Sprite2D, int>(_animated.Count, ReferenceEqualityComparer.Instance);
        foreach (var (sprite, _, frame) in _animated) frameOf[sprite] = frame;
        var spare = new Dictionary<Look, Queue<Sprite2D>>(_scene.Count);
        foreach (var (node, look) in _scene)
        {
            if (!spare.TryGetValue(look, out var queue)) spare[look] = queue = new Queue<Sprite2D>(1);
            queue.Enqueue(node);
        }
        _animated.Clear();
        _banners.RemoveAll(n => !IsInstanceValid(n) || n.IsQueuedForDeletion());
        Shown = 0;
        Prepare(scene.Sprites, gfx, root);
        var sets = new FrameSet[scene.Sprites.Count];
        var first = new int[sets.Length];
        var random = new Random(1);
        for (int i = 0; i < sets.Length; i++)
        {
            sets[i] = Frames(scene.Sprites[i], gfx, root);
            first[i] = sets[i].Length > 1 ? random.Next(sets[i].Length) : 0;
        }
        Load(sets, first);
        var next = new List<(Sprite2D Node, Look Look)>(scene.Sprites.Count);
        var kept = new HashSet<Sprite2D>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < sets.Length; i++)
        {
            var s = scene.Sprites[i];
            var frames = sets[i];
            if (frames.Length == 0)
            {
                MissingFiles++;
                continue;
            }
            int frame = first[i];
            var look = Look.Of(s);
            Sprite2D sprite;
            if (spare.TryGetValue(look, out var queue) && queue.Count > 0)
            {
                sprite = queue.Dequeue();
                kept.Add(sprite);
                // Нетронутый объект фазу не сбивает: движок перерисовывает только изменившиеся объекты, а перерисовка берёт кадр
                // старой картинки (0x5ace8a → 0x5bbecf, docs/map-render-stacks.md §1 «Анимация»); случайный кадр — только у новой
                // (0x5ae0bf). Прежде каждая пересборка (ход ИИ — на каждой заявке) возвращала все анимации карты к кадрам Random(1).
                if (frames.Length > 1 && frameOf.TryGetValue(sprite, out int was))
                {
                    if (was % frames.Length != frame) PhaseJumpsAvoided++;   // прежняя пересборка поставила бы сюда кадр Random(1)
                    frame = was % frames.Length;
                }
                else if (frames.Length > 1) sprite.Texture = frames[frame];
                PhaseKept++;
            }
            else
            {
                sprite = new Sprite2D
                {
                    Texture = frames[frame], Centered = false, Position = new Vector2(s.X, s.Y),
                    TextureFilter = TextureFilterEnum.Nearest,
                };
                GfxBlending.Apply(sprite, GfxBlending.Of(s, gfx));
            }
            if (!_banners.Contains(sprite)) Track(sprite, s);
            next.Add((sprite, look));
            Shown++;
            if (frames.Length > 1) _animated.Add((sprite, frames, frame));
        }
        Arrange(next, kept);
        _scene = next;
        _keys.Clear();
        foreach (var (_, look) in next) _keys.Add(look.Key);
        foreach (string channel in _channels.Keys) InsertChannel(channel);
    }

    /// <summary>Привести детей <see cref="Ordered"/> к порядку <paramref name="next"/>: снять узлы, которых там нет, добавить новые,
    /// сдвинуть стоящие не на своём месте. Сравнение мест — по своему списку, без обращений к движку.</summary>
    private void Arrange(List<(Sprite2D Node, Look Look)> next, HashSet<Sprite2D> kept)
    {
        var order = new List<Sprite2D>(next.Count);
        foreach (var (node, _) in _scene)
        {
            if (kept.Contains(node)) order.Add(node);
            else
            {
                Ordered.RemoveChild(node);
                node.QueueFree();
            }
        }
        for (int i = 0; i < next.Count; i++)
        {
            var node = next[i].Node;
            if (i < order.Count && ReferenceEquals(order[i], node)) continue;
            if (!kept.Contains(node))
            {
                Ordered.AddChild(node);
                if (i < order.Count) Ordered.MoveChild(node, i);
                order.Insert(i, node);
                continue;
            }
            int at = order.IndexOf(node, i + 1);
            order.RemoveAt(at);
            order.Insert(i, node);
            Ordered.MoveChild(node, i);
        }
    }

    /// <summary>Картинки канала, как их дали (<see cref="ShowLayered"/>); нет канала — пусто.</summary>
    public IReadOnlyList<MapSprite> Channel(string channel) =>
        _channels.TryGetValue(channel, out var entry) ? entry.Sprites : [];

    /// <summary>Место картинки канала в общем порядке (проверка): номер среди всех картинок и их число; нет — (-1, всего).</summary>
    public (int At, int Of) OrderOf(string channel, int index) =>
        _channelNodes.TryGetValue(channel, out var nodes) && index < nodes.Count ? (nodes[index].GetIndex(), _keys.Count) : (-1, _keys.Count);

    /// <summary>Сколько непрозрачных точек первой картинки канала видно (проверка): точка закрыта, если там непрозрачна картинка, что
    /// рисуется позже (следующие в общем порядке и идущий отряд). Нет картинки — (0, 0).</summary>
    public (int Seen, int All) Visibility(string channel)
    {
        if (!_channelNodes.TryGetValue(channel, out var nodes) || nodes.Count == 0 || Opaque(nodes[0], Vector2.Zero) is not { } own)
            return (0, 0);
        var covers = new List<OpaqueImage>();
        for (int i = nodes[0].GetIndex() + 1; i < Ordered.GetChildCount(); i++)
            if (Ordered.GetChild(i) is Sprite2D { Visible: true } s && Opaque(s, Vector2.Zero) is { } image) covers.Add(image);
        if (_moving is not null)
            foreach (var child in _moving.GetChildren())
                if (child is Sprite2D { Visible: true } s && Opaque(s, _moving.Position) is { } image) covers.Add(image);
        int all = 0, seen = 0;
        foreach (var p in own.Points())
        {
            all++;
            if (!covers.Any(c => c.Covers(p))) seen++;
        }
        return (seen, all);
    }

    /// <summary>Непрозрачные точки узла в координатах карты (место узла и сдвиг его родителя).</summary>
    private static OpaqueImage? Opaque(Sprite2D node, Vector2 shift)
    {
        // Обрезка (GfxTextures): занятая часть — атлас, её угол в полном кадре — поле Margin.
        var (texture, corner) = node.Texture is AtlasTexture atlas ? (atlas.Atlas, atlas.Margin.Position) : (node.Texture, Vector2.Zero);
        return texture?.GetImage() is { } image && !image.IsEmpty() ? new OpaqueImage(image, node.Position + shift + corner) : null;
    }

    private readonly record struct OpaqueImage(Image Image, Vector2 At)
    {
        public IEnumerable<Vector2I> Points()
        {
            for (int y = 0; y < Image.GetHeight(); y++)
                for (int x = 0; x < Image.GetWidth(); x++)
                    if (Image.GetPixel(x, y).A > 0) yield return new Vector2I(x + (int)At.X, y + (int)At.Y);
        }

        public bool Covers(Vector2I p)
        {
            int x = p.X - (int)At.X, y = p.Y - (int)At.Y;
            return x >= 0 && y >= 0 && x < Image.GetWidth() && y < Image.GetHeight() && Image.GetPixel(x, y).A > 0;
        }
    }

    public override void _Process(double delta)
    {
        _clock += delta * 1000;
        if (_clock < FrameMs) return;
        _clock %= FrameMs;   // пропущенные тики не догоняются, как у движка (0x51e0d0)
        Advance(_animated);
        Advance(_movingFrames);
    }

    private static void Advance(List<(Sprite2D Sprite, FrameSet Frames, int Frame)> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var (sprite, frames, frame) = list[i];
            frame = (frame + 1) % frames.Length;
            sprite.Texture = frames[frame];
            list[i] = (sprite, frames, frame);
        }
    }

    /// <summary>Кадры картинки: свой словарь по (архив, имя) — как прежде, сами текстуры — из общего кэша по пути (<see cref="GfxTextures"/>,
    /// гипотеза Г1 <c>docs/perf-log.md</c>). Картинка не из сборки сцены (каналы, идущий отряд) — все кадры сразу, как прежде.</summary>
    private FrameSet Frames(MapSprite s, GfxAssets gfx, string root)
    {
        var key = (s.Archive, s.Name);
        if (_textures.TryGetValue(key, out var cached)) return cached;
        var set = FrameSet.Existing(Paths(s, gfx, root));
        set.LoadAll();
        return _textures[key] = set;
    }

    /// <summary>Кадры картинки — файлы, которые есть, по порядку (длина анимации — как у прежнего <see cref="GfxTextures.Of(IEnumerable{string})"/>:
    /// отсутствующие пропущены); текстура кадра берётся из общего кэша при первом обращении. Так кадр, ещё не нужный к показу, разбирается в фоне,
    /// а не в открытии карты (гипотеза Г17 <c>docs/perf-log.md</c>); не готов к обращению — подождать его разбор или разобрать здесь же
    /// (<see cref="GfxTextures.Of(string)"/>). Какой кадр на экране — не меняется, меняется только когда он разобран.</summary>
    private sealed class FrameSet(string[] paths)
    {
        private readonly Texture2D?[] _textures = new Texture2D?[paths.Length];

        public int Length => paths.Length;

        /// <summary>Текстура кадра. Файл удалили между проверкой и разбором — null (узел без картинки).</summary>
        public Texture2D this[int i] => (_textures[i] ??= GfxTextures.Of(paths[i]))!;

        /// <summary>Путь кадра, текстура которого ещё не взята; взята — null.</summary>
        public string? Unloaded(int i) => _textures[i] is null ? paths[i] : null;

        /// <summary>Взять текстуры всех кадров сразу — как прежде, до Г17.</summary>
        public void LoadAll()
        {
            for (int i = 0; i < paths.Length; i++) _ = this[i];
        }

        /// <summary>Кадры из файлов, которые есть (<see cref="GfxTextures.Exist"/>).</summary>
        public static FrameSet Existing(IReadOnlyList<string> paths)
        {
            var exist = GfxTextures.Exist(paths);
            return new FrameSet([.. paths.Where((_, i) => exist[i])]);
        }
    }

    private static List<string> Paths(MapSprite s, GfxAssets gfx, string root)
    {
        var paths = new List<string>();
        if (s.Anim && gfx.Anim(s.Archive, s.Name) is { } anim) paths.AddRange(GfxAssets.FramePaths(anim, root));
        else if (gfx.Image(s.Archive, s.Name) is { } image) paths.Add(GfxAssets.PathOf(image, root));
        return paths;
    }

    /// <summary>Кадры новых картинок сцены без разбора: есть ли файлы — разом в рабочих потоках (<see cref="GfxTextures.Exist"/>; от числа
    /// кадров зависит случайный первый кадр). Кадры берутся по первой картинке с тем же (архив, имя), как в <see cref="Frames"/>.</summary>
    private void Prepare(IEnumerable<MapSprite> sprites, GfxAssets gfx, string root)
    {
        var seen = new HashSet<(string, string)>(_textures.Keys);
        var fresh = sprites.Where(s => seen.Add((s.Archive, s.Name))).Select(s => (Key: (s.Archive, s.Name), Paths: Paths(s, gfx, root))).ToList();
        var exist = GfxTextures.Exist([.. fresh.SelectMany(f => f.Paths)]);
        int at = 0;
        foreach (var (key, paths) in fresh)
        {
            var have = new List<string>(paths.Count);
            foreach (string path in paths)
                if (exist[at++]) have.Add(path);
            _textures[key] = new FrameSet([.. have]);
        }
    }

    /// <summary>Разобрать разом, в рабочих потоках, кадры, нужные к первому показу (<see cref="GfxTextures.Prefetch"/>; источник — скилл
    /// gdscript-knowledge, performance.md «Сборка большого поддерева… в потоке», signals-async-threads.md «Потокобезопасные API»), а остальные
    /// кадры анимаций — в фоне (<see cref="GfxTextures.Warm(IEnumerable{string}, int)"/>) в том порядке, в каком их попросят тики анимации:
    /// сначала следующий кадр каждой, потом через один и т. д. Замер Г17 (t48: к показу 430 файлов из 1358): фон на всех потоках вдвое
    /// замедлял главный поток (сборка узлов, остаток открытия), открытие выходило дольше прежнего; <see cref="WarmThreads"/> потоков успевают
    /// раньше тиков, тик забирает готовое (~1 мс на тик первые ~12 тиков).</summary>
    private static void Load(FrameSet[] sets, int[] first)
    {
        var shown = new List<string>();
        for (int i = 0; i < sets.Length; i++)
            if (sets[i].Length > 0 && sets[i].Unloaded(first[i]) is { } path) shown.Add(path);
        GfxTextures.Prefetch(shown);
        var seen = new HashSet<(FrameSet, int)>();
        var starts = new List<(FrameSet Set, int At)>();
        for (int i = 0; i < sets.Length; i++)
            if (sets[i].Length > 1 && seen.Add((sets[i], first[i]))) starts.Add((sets[i], first[i]));
        int longest = starts.Count == 0 ? 0 : starts.Max(p => p.Set.Length);
        var order = new List<string>();
        for (int step = 1; step < longest; step++)
            foreach (var (set, at) in starts)
                if (step < set.Length && set.Unloaded((at + step) % set.Length) is { } path) order.Add(path);
        GfxTextures.Warm(order, WarmThreads);
    }

    /// <summary>Потоков фонового разбора кадров карты: восьмая часть логических ядер, от 1 до 4 (замер на 32: 2, 4 и 8 потоков — открытие
    /// t48 175–181 мс против прежних ~205; все 32 — 350–800 мс).</summary>
    private static readonly int WarmThreads = Math.Clamp(System.Environment.ProcessorCount / 8, 1, 4);
}
