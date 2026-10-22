using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LastEdict.Data.Battle;
using LastEdict.Data.Diagnostics;
using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using LastEdict.Rules.Battle;
using Godot;

namespace LastEdict.Godot;

/// <summary>Поле боя: фон, юниты на своих местах и рамка ходящего. Всё по разбору движка
/// (<c>docs/battle-render.md</c>): места — из разметки <c>DLG_BATTLE_A</c>, кадр кладётся по якорю
/// <c>(400, 490)</c>, порядок — сквозной номер слоя (в Godot это <c>z_index</c>). Анимация — 66 мс на кадр.
///
/// <para>Чего пока нет: эффекты <c>Battle.ff</c> (яд, мороз, воскрешение, призыв, превращение…) и предметов <c>BatItems.ff</c>.</para></summary>
public partial class BattleView : Node2D
{
    private const double FrameMs = LastEdict.Data.Map.AnimClock.SlowMs;   // CBatUnitAnim — медленная группа

    private readonly Dictionary<(string, string), Texture2D[]> _textures = new();
    private readonly List<(Sprite2D Sprite, Texture2D[] Frames, int Frame)> _animated = new();
    private readonly Dictionary<string, List<Node>> _byUnit = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Label Label, double Age)> _fading = new();
    private BattleScene _scene = null!;
    private Node2D _layer = null!;
    private double _clock;

    /// <summary>Сколько картинок нарисовано.</summary>
    public int Shown { get; private set; }

    /// <summary>Поле живёт во вьюпорте экрана боя, а у <see cref="SubViewport"/> фильтр по умолчанию — Linear: всё поле — точками.</summary>
    public override void _EnterTree() => TextureFilter = TextureFilterEnum.Nearest;

    /// <summary>Узел удаляется: подкрашенные портреты отпускаются сразу, а не сборкой мусора. Узлы, которые ещё держат эти текстуры (панели
    /// на экране боя), держат их ссылкой движка — им снятие обёртки C# не мешает. Кадры картинок — общий кэш партии (<see cref="GfxTextures"/>):
    /// их отпускает выход из партии, а не каждый бой.</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;
        _textures.Clear();
        foreach (var (_, texture) in _faces.Values) texture.Dispose();
        _faces.Clear();
    }

    /// <summary>Значков боевых предметов в панелях (последняя сборка панелей) — для проверок.</summary>
    public int ItemIcons { get; private set; }

    /// <summary>Имена, которых нет в графике.</summary>
    public IReadOnlyList<string> Missing => _scene?.Missing ?? [];

    /// <summary>Чей сейчас ход — на поле под ним <c>MRKCUR*</c> (<c>0x6379a5</c>), в панели — рамка <c>PLY*</c>.</summary>
    public BattleUnit? Active { get; set; }

    /// <summary>Ходящий — юнит человека: только тогда у ходящего лидера показываются боевые предметы (<see cref="BattleScene.ShowsItems"/>).</summary>
    public bool ActiveHuman { get; set; }

    /// <summary>Годные цели — рамки в панели вокруг их портретов (<c>0x64cb44</c>); на поле меток всех целей у обычной атаки нет.</summary>
    public IReadOnlyList<BattleUnit> Marked { get; set; } = [];

    /// <summary>Рамка целей в панели: <c>SEL</c> — обычная атака, <c>HEA</c> — поддерживающая; при тащении предмета — <c>ISEL</c>
    /// (у поддерживающих атак сферы и талисмана — <c>HEA</c>, <c>0x63460d</c>).</summary>
    public string MarkKind { get; set; } = "SEL";

    /// <summary>Атака бьёт всю группу — вместо рамок у каждой цели одна рамка <c>*ALL</c> на панель стороны целей (<c>0x64cbc9</c>).</summary>
    public bool TargetsAll { get; set; }

    /// <summary>Пустые позиции-цели призыва (сторона, позиция) — метка <c>EMPTYSELSMALL</c> (<c>0x64ce69</c>).</summary>
    public IReadOnlyList<(bool Attacker, int Slot)> EmptyMarked { get; set; } = [];

    /// <summary>Имя ходящего в подписи под крупным портретом: у героя — собственное (даже пустое), у прочих — имя типа (<c>0x5e97dc</c>).</summary>
    public string ActiveName { get; set; } = "";

    /// <summary>Юнит второго крупного портрета (<c>bigFace</c>, <c>getBigFace</c> <c>0x639e08</c>): под мышью или первая цель последнего
    /// действия; null — ещё никого, показывается ходящий (снимки 28.09: в начале боя оба портрета — ходящий).</summary>
    public BattleUnit? Focus { get; set; }

    /// <summary>Имя юнита второго крупного портрета (как <see cref="ActiveName"/>).</summary>
    public string FocusName { get; set; } = "";

    /// <summary>Чьи крупные портреты в последней сборке панелей: «L=id R=id» — для проверок.</summary>
    public string BigFaces { get; private set; } = "-";

    /// <summary>Вариант фона места (<see cref="BattleScene.BackgroundVariants"/>); задаёт экран боя.</summary>
    public int Variant { get; set; }

    public void Build(BattleScene scene, BattleState state, string place)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(state);
        _scene = scene;
        _layer ??= AddLayer();
        foreach (var child in _layer.GetChildren()) child.QueueFree();
        _once.Clear();
        _actions.Clear();
        // Надписи урона живут в панели (_notes) и снимаются по времени.
        _animated.Clear();
        _byUnit.Clear();
        Shown = 0;

        // Сначала весь список картинок по порядку, затем разбор их PNG разом в рабочих потоках (GfxTextures.Prefetch; скилл
        // gdscript-knowledge, performance.md «Сборка большого поддерева… в потоке», signals-async-threads.md «Потокобезопасные API»),
        // затем узлы — в том же порядке, что и прежде.
        var puts = new List<(BattleSprite Sprite, bool FromMiddle)>();
        void Put(BattleSprite sprite, bool fromMiddle = false) => puts.Add((sprite, fromMiddle));
        // Фон, тень и передний план места (BG/SHADOW/FG, 0x5b34f6 → 0x646a6f): без FG у UNDEAD_0 в фоне оставалась дыра (снимок 27 пользователя).
        foreach (var layer in scene.PlaceLayers(place, Variant)) Put(layer);
        // Павшие остаются на поле трупами (отступившие — нет).
        var corpses = new List<BattleSprite>();
        foreach (var unit in state.Units.Where(u => !u.Alive && !u.Retreated))
            if (scene.Corpse(unit) is { } corpse)
            {
                Put(corpse);
                corpses.Add(corpse);
            }
        // Пустые позиции призыва — MRKEMPTYSMALL на поле (0x646cb6) и EMPTYSELSMALL в панели.
        foreach (var (side, slot) in EmptyMarked)
            if (scene.MarkSlot(side, slot, "MRKEMPTYSMALL") is { } empty) Put(empty);
        _hoverNodes.Clear();
        _hovered = null;   // метки наведения сняты вместе со слоем — следующее движение мыши ставит их заново
        // Цели в обеих группах (двойник, превращение себя — отрицательные позиции набора целей, 0x6372e7): MRK под всеми целями сразу
        // (0x646cb6, слой юнита + 1); у обычной атаки меток всех целей на поле нет.
        bool bothSides = Marked.Any(m => m.Attacker) && Marked.Any(m => !m.Attacker);
        FieldMarks = 0;
        if (bothSides)
            foreach (var target in Marked.Where(m => m.OnField))
                if (scene.Mark(target, "MRK", target.Type.SizeSmall) is { } all)
                {
                    // Анимация меток всех целей — с середины (setImageIndex(count / 2), 0x637dcf), MRKEMPTY — с начала.
                    Put(all with { Layer = all.Layer + 2 }, fromMiddle: true);
                    FieldMarks++;
                }
        foreach (var unit in state.Units.Where(u => u.OnField))
        {
            if (ReferenceEquals(unit, Active) && scene.Mark(unit, BattleScene.CurrentMark, unit.Type.SizeSmall) is { } current)
                Put(current);
            // Значков состояний (FI*) на поле нет: движок грузит их только для панелей — CBatUnitGroup2 (0x649bc0 → 0x64a39e → 0x647dc9 →
            // 0x5b36e1, таблица имён 0x7ae6c0) и предзагрузчик 0x6474d0; других ссылок на загрузчик нет (ни вызовов, ни слотов vftable).
            // Прежде порт клал их над левым верхним углом места — флаг отступающего упыря D4 висел у шаманессы A4 (партия 15.10, пометка 10).
            foreach (var sprite in scene.UnitSprites(unit, BattleAction.Idle, unit.Type.SizeSmall)) Put(sprite);
        }
        Prefetch(puts.Select(p => p.Sprite));
        foreach (var (sprite, fromMiddle) in puts) this.Put(sprite, fromMiddle);
        CorpsesShown = corpses.Count(c => Frames(c).Length > 0);
        FieldIcons = puts.Count(p => p.Sprite.Name.StartsWith("FI", StringComparison.OrdinalIgnoreCase));
        EmptyMarks = puts.Count(p => p.Sprite.Name.StartsWith("MRKEMPTY", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Значков состояний (<c>FI*</c>) на поле последней сборкой — у движка их там нет (только в панелях); для проверок.</summary>
    public int FieldIcons { get; private set; }

    /// <summary>Меток пустых позиций (<c>MRKEMPTY*</c>, цели призыва) на поле последней сборкой — для проверок.</summary>
    public int EmptyMarks { get; private set; }

    /// <summary>Трупов (костей), нарисованных на поле последней сборкой, — для проверок.</summary>
    public int CorpsesShown { get; private set; }

    private readonly List<Node> _hoverNodes = new();

    /// <summary>Юнит, под которым стоят метки наведения (<c>data2+0xac</c> движка); null — меток нет.</summary>
    private BattleUnit? _hovered;

    /// <summary>Юнит под курсором — <c>MRK*</c> под любым живым (годным и нет, <c>markAttackTargets</c> <c>0x632b35</c>); если он годная
    /// цель атаки по всей группе — метки под всеми целями. null — метки снять. Тот же юнит, что при прошлом движении мыши, — метки не
    /// трогаются, их анимация идёт дальше (<c>0x633012</c>: id под мышью равен прежнему <c>data2+0xac</c> — выход до снятия меток
    /// <c>0x646c69</c>); другой юнит — новые картинки (<c>0x5b3a3e</c> → <c>0x5ae0bf</c>: новый объект, без случайного кадра) с первого кадра.</summary>
    public void Hover(BattleUnit? unit)
    {
        if (unit is not { Alive: true, OnField: true } || _scene is null) unit = null;
        if (ReferenceEquals(unit, _hovered)) return;
        _hovered = unit;
        // Метки снимаются вместе со своими кадрами анимации: иначе _Process пишет текстуру в удалённый спрайт каждый кадр
        // (ObjectDisposedException, лог игры пользователя 28.09 — 289 раз за бой).
        _animated.RemoveAll(a => _hoverNodes.Contains(a.Sprite));
        foreach (var node in _hoverNodes) node.QueueFree();
        _hoverNodes.Clear();
        HoverMarks = 0;
        if (unit is null || _scene is null) return;
        IEnumerable<BattleUnit> marked = TargetsAll && Marked.Any(m => ReferenceEquals(m, unit)) ? Marked.Where(m => m.Alive) : [unit];
        foreach (var target in marked)
            if (_scene.Mark(target, "MRK", target.Type.SizeSmall) is { } mark && Frames(mark) is { Length: > 0 } frames)
            {
                var sprite = new Sprite2D
                {
                    Texture = frames[0], Centered = false, Position = new Vector2(mark.X, mark.Y), ZIndex = mark.Layer,
                    TextureFilter = TextureFilterEnum.Nearest,
                };
                Blend(sprite, mark);
                _layer.AddChild(sprite);
                if (frames.Length > 1) _animated.Add((sprite, frames, 0));
                _hoverNodes.Add(sprite);
                HoverMarks++;
            }
    }

    /// <summary>Меток всех целей на поле (цели в обеих группах) последней сборки — для проверок.</summary>
    public int FieldMarks { get; private set; }

    /// <summary>Сколько меток под курсором — для проверок.</summary>
    public int HoverMarks { get; private set; }

    /// <summary>Кадр анимации первой метки под курсором (−1 — анимированной метки нет) — для проверок.</summary>
    public int HoverFrame => _animated.FindIndex(a => _hoverNodes.Contains(a.Sprite)) is var i and >= 0 ? _animated[i].Frame : -1;

    /// <summary>Рамки панели последней сборки (имя кадра) — для проверок.</summary>
    public List<string> PanelFrames { get; } = [];

    /// <summary>Рамки панели (<c>CBatUnitGroup2</c>, отрисовка <c>0x64a7be</c>: позже — выше): <c>PLY</c> у ходящего, у годных целей
    /// <c>SEL</c>/<c>HEA</c>/<c>ISEL</c> (у ходящего-цели — поверх <c>PLY</c>), <c>EMPTYSEL</c> у пустых позиций призыва, последней — одна
    /// <c>*ALL</c> на сторону вместо рамок целей (<c>0x64b0f9</c>; <c>PLY</c> остаётся). Все — выше портретов, значков и надписей.
    /// Анимированные («…A», 16 кадров, <see cref="BattleScene.Animated"/>) идут от общего счётчика экрана (<see cref="PanelFrame"/>):
    /// у движка одна картинка на вид рамки, кадр в 66 мс с открытия боя, при появлении рамки не сбрасывается. Наведения в панели нет.</summary>
    private void Frames(Node2D into, BattleScene scene, BattleState state)
    {
        PanelFrames.Clear();
        _panelFrames.Clear();
        var frames = new List<BattleSprite?>();
        // Рамки — только у групп, которые сейчас показаны в панели (основная — по TOG_RIGHTUNITS, другая — над SPOT).
        if (Active is { OnField: true } active && scene.PanelShown(active.Attacker))
            frames.Add(scene.PanelFrame(active.Attacker, active.Slot, "PLY", active.Type.SizeSmall));
        if (!TargetsAll || Marked.Count == 0)
            foreach (var target in Marked.Where(m => scene.PanelShown(m.Attacker)))
                frames.Add(scene.PanelFrame(target.Attacker, target.Slot, MarkKind, target.Type.SizeSmall));
        foreach (var (side, slot) in EmptyMarked.Where(e => scene.PanelShown(e.Attacker))) frames.Add(scene.PanelFrame(side, slot, "EMPTYSEL", small: true));
        // «Вся группа» — одна рамка на каждую сторону, где есть цели (цели бывают и в другой группе — 0x6372e7).
        if (TargetsAll && Marked.Count > 0)
            foreach (bool side in Marked.Select(m => m.Attacker).Distinct().Where(scene.PanelShown)) frames.Add(scene.PanelGroupFrame(side, MarkKind));
        foreach (var frame in frames)
            if (frame is not null && Frames(frame) is { Length: > 0 } textures)
            {
                var node = new Sprite2D
                {
                    Texture = textures[PanelFrame(textures.Length)], Centered = false, Position = new Vector2(frame.X, frame.Y),
                    ZIndex = frame.Layer, TextureFilter = TextureFilterEnum.Nearest,
                };
                Blend(node, frame);
                into.AddChild(node);
                PanelFrames.Add(frame.Name);
                if (textures.Length > 1) _panelFrames.Add((node, textures));
            }
    }

    /// <summary>Анимированные рамки панели последней сборки: кадр — от общего счётчика (<see cref="PanelFrame"/>).</summary>
    private readonly List<(Sprite2D Sprite, Texture2D[] Frames)> _panelFrames = new();

    /// <summary>Мс с открытия экрана боя — общий счётчик кадров рамок панели.</summary>
    private double _since;

    /// <summary>Кадр рамки панели сейчас: (время с открытия / 66 мс) по кругу.</summary>
    private int PanelFrame(int frames) => frames <= 1 ? 0 : (int)(_since / FrameMs) % frames;

    /// <summary>Портреты и полосы здоровья боковых панелей — поверх разметки экрана, поэтому рисуются в узел
    /// <paramref name="into"/>, который хозяин кладёт над панелью.</summary>
    public void BuildPanels(Node2D into, BattleScene scene, BattleState state)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(state);
        foreach (var child in into.GetChildren())
            if (child != _notes) child.QueueFree();
        DropKeptFlashes();
        _faceSprites.Clear();
        if (_notes is null || !IsInstanceValid(_notes))
        {
            _notes = new Node2D { Name = "Notes", ZIndex = BattleScene.PanelLayer + 5, ZAsRelative = false };
            into.AddChild(_notes);
        }
        RedrawSteadyNotes(scene, state);
        // Крупные портреты (BattleScene.ActiveFaceLeft): ходящий — слева у своего юнита, справа у чужого; на другой стороне — Focus (под мышью
        // или первая цель последнего действия), пока его нет — пусто, виден фон IMG_BIGFACESBG: CBatBigFace создаётся с пустым id
        // (0x64ec10, +0xfb0 = 0x8372f4), рисование при пустом выходит (0x64ee98 → 0x64f142), id ставит только setUnit 0x64f149 — из начала
        // хода (ходящий), показа действия (первая цель 0x630f1c) и наведения мыши (0x632ff9, 0x633857). Подпись — X100TA0608 (0x64f819): имя
        // юнита (CMidUnit::getName 0x5e97dc — BattleScreen.NameOf) и «ОЗ: %HP%/%HPMAX%».
        BigTextLines.Clear();
        BigFaces = "-";
        if (Active is { } active)
        {
            bool activeLeft = scene.ActiveFaceLeft(active.Attacker);
            var faces = new List<string>();
            foreach (bool left in (bool[])[true, false])
            {
                var (unit, name) = left == activeLeft ? (active, ActiveName) : (Focus, FocusName);
                faces.Add($"{(left ? 'L' : 'R')}={unit?.Id ?? "-"}");
                if (unit is null) continue;
                if (scene.BigFace(unit) is not { } bigFace || Frames(bigFace) is not { Length: > 0 } bigFrames) continue;
                var at = scene.BigFaceAt(left);
                into.AddChild(new Sprite2D
                {
                    Texture = bigFrames[0], Centered = false, Position = new Vector2(at.X, at.Y), TextureFilter = TextureFilterEnum.Nearest,
                    FlipH = !left,   // IMG_RBIGFACE — отражён всегда, левый — никогда (0x64f62a: !bigFace[+4]; ранг 1, снимки 28.09)
                });
                BigText(into, scene.BigFaceAt(left, text: true), Text("X100TA0608").Replace("%NAME%", name)
                    .Replace("%HP%", Math.Max(0, unit.Hp).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("%HPMAX%", BattlePanel.ShownHpMax(unit).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            BigFaces = string.Join(" ", faces);
        }
        // Боевые предметы — только у ходящего лидера человека (updateBattleItems 0x6383d0 → 0x649775, BattleScene.ShowsItems); у прочих
        // и у лидера ИИ на его ходу слоты пусты; применённый в этом бою предмет из слота ушёл (BattleScene.ShownItems, 0x622fee).
        ItemIcons = 0;
        foreach (var unit in state.Units.Where(u => u.OnField && ReferenceEquals(u, Active)))
            foreach (int i in BattleScene.ShownItems(unit, ActiveHuman))
                if (scene.ItemIcon(unit.Attacker, i, unit.BattleItems[i].Id) is { } item
                    && Frames(item) is { Length: > 0 } icon && ++ItemIcons > 0)
                    into.AddChild(new Sprite2D
                    {
                        Texture = icon[0], Centered = false, Position = new Vector2(item.X, item.Y),
                        TextureFilter = TextureFilterEnum.Nearest,
                    });
        PanelIcons = 0;
        _panelIconsShown.Clear();
        MeterLines.Clear();
        Frames(into, scene, state);
        // Скрытые и не вызванные в панели не рисуются (0x64b8a1); группа, которой нет в панели, — тоже. Павший остаётся на своём месте
        // панели — портрет чёрным наполовину без красной доли (0x64c269, BattlePanel.Tint), полоса «0/макс», значков нет (замечание 25);
        // отступивший из боя ушёл.
        PanelUnits.Clear();
        DeadMasks = 0;
        foreach (var unit in state.Units.Where(u => InPanel(u, state) && scene.PanelShown(u.Attacker)))
        {
            if (scene.Portrait(unit) is not { } portrait) continue;
            PanelUnits.Add(unit.Alive ? unit.Id : unit.Id + ":павший");
            if (BattlePanel.DeadMask(unit, !unit.Type.SizeSmall) is { } skull && Mask(skull) is not null) DeadMasks++;
            var faceRect = scene.PanelFace(unit, unit.Type.SizeSmall ? 70 : BattlePanel.BigWidth);
            var face = PanelFace(portrait, unit);
            bool big = face is not null && face.GetWidth() >= BattlePanel.BigWidth;
            if (face is not null)
            {
                var faceSprite = new Sprite2D
                {
                    Texture = face, Centered = false, Position = new Vector2(portrait.X, portrait.Y), ZIndex = portrait.Layer,
                    TextureFilter = TextureFilterEnum.Nearest,
                    FlipH = PanelMirrored(scene, unit),
                };
                into.AddChild(faceSprite);
                _faceSprites[unit.Id] = (faceSprite, portrait, unit);
            }
            int width = face?.GetWidth() ?? 70, height = face?.GetHeight() ?? 85;
            Meter(into, scene, unit, big, faceRect.X);
            var (upgrade, highLevel) = LevelMarks?.Invoke(unit) ?? (false, 0);
            var icons = BattlePanel.Icons(unit, big, Marks, upgrade, highLevel);
            foreach (var icon in BattlePanel.Place(icons, width, height, IconSize, rightRetreat: !scene.Layout.LeftSide(unit.Attacker)))
                if (Frames(new BattleSprite("Battle", icon.Name, 0, 0, 0, unit.Id)) is { Length: > 0 } frames)
                {
                    into.AddChild(new Sprite2D
                    {
                        Texture = frames[0], Centered = false, Position = new Vector2(portrait.X + icon.X, portrait.Y + icon.Y),
                        ZIndex = portrait.Layer + 1, TextureFilter = TextureFilterEnum.Nearest,
                    });
                    PanelIcons++;
                    _panelIconsShown.Add($"{unit.Id}:{icon.Name}@{icon.X},{icon.Y}");
                }
        }
    }

    /// <summary>Юнит в боковой панели: на поле — живой, не отступивший и не скрытый, а также павший (не отступивший, не скрытый), если его
    /// позицию не занял призванный: призыв ставит юнита в позицию группы (<c>addUnitToGroup</c> <c>0x5e78f7</c> из <c>0x663507</c>), и панель
    /// по позиции показывает его одного. Прежде в клетке были оба — портреты и надписи «ОЗ» павшего и призванного друг на друге (партия
    /// 15.10, пометка 5). Клетки сравниваются с размером (<see cref="BattleUnit.Covers"/>): большой призванный на клетке павшего малого и
    /// малый призванный во второй клетке павшего большого тоже закрывают павшего — призыв убирает юнита с позиций группы (<c>0x5e7a71</c>
    /// из <c>0x66343b</c>/<c>0x6634a2</c>, обе клетки большого).</summary>
    private static bool InPanel(BattleUnit u, BattleState state) =>
        !u.Retreated && !u.Hidden
        && (u.Alive || !state.Units.Any(o => o.Attacker == u.Attacker && o.OnField && (o.Covers(u.Slot) || u.Covers(o.Slot))));

    /// <summary>Портреты панелей последней сборки (id, у павших — с «:павший») — для проверок.</summary>
    public List<string> PanelUnits { get; } = [];

    /// <summary>Значки повышения и высокого уровня юнита (<c>FIUPGRADE</c>, <c>FIHIGHLEVEL1..3</c>; задаёт хозяин боя по юниту карты); null — нет.</summary>
    public Func<BattleUnit, (bool Upgrade, int HighLevel)>? LevelMarks { get; set; }

    /// <summary>Метки модификаторов для значков усиления, ослабления и зелья (задаёт экран боя по книге правил).</summary>
    public BattleMarkSets Marks { get; set; } = BattleMarkSets.None;

    /// <summary>Строки языка (текст полосы здоровья <see cref="BattlePanel.MeterText"/>); задаёт экран боя.</summary>
    public Func<string, string> Text { get; set; } = _ => "";

    /// <summary>Значков состояний в панелях (последняя сборка) — для проверок.</summary>
    public int PanelIcons { get; private set; }

    /// <summary>Значки панелей последней сборки: «юнит:кадр@x,y» (место в прямоугольнике портрета) — для проверок.</summary>
    public IReadOnlyList<string> PanelIconsShown => _panelIconsShown;

    private readonly List<string> _panelIconsShown = [];

    /// <summary>Тексты полос здоровья последней сборки панелей — для проверок.</summary>
    public List<string> MeterLines { get; } = [];

    private (int W, int H)? IconSize(string name) =>
        Frames(new BattleSprite("Battle", name, 0, 0, 0, "")) is { Length: > 0 } f ? (f[0].GetWidth(), f[0].GetHeight()) : null;

    /// <summary>Портрет панели с подкраской по состоянию портрета (<see cref="FaceOf"/>: вспышка, поставленное или база — <see cref="BattleFaces"/>):
    /// весь — цветом состояния, нижняя доля — красным по ранам, у превращения и призыва — цветом целиком (<see cref="BattleFaceLook.Alpha"/>).</summary>
    private Texture2D? PanelFace(BattleSprite portrait, BattleUnit unit)
    {
        var state = FaceOf(unit);
        var look = BattleFaces.Look(state);
        var (whole, wounds) = (look.Whole, look.Wounds);
        var gfx = GameData.Content.Gfx;
        if (gfx.Image(portrait.Archive, portrait.Name) is not { } info) return null;
        string path = GfxAssets.PathOf(info, GameData.GfxRoot);
        // Ключ — по высоте из описи, до разбора PNG: прежде каждая сборка панелей (каждый шаг показа боя) заново разбирала PNG портретов
        // всех юнитов, даже когда подкраска не менялась (замечание 35).
        int kept = wounds ? BattlePanel.KeptRows(unit, info.H) : info.H;
        // Маска-череп — только у состояния «павший» (0x5b14bc: картинка маски по состоянию +0x40): вспышка поверх павшего её снимает.
        string? maskName = state == BattleFace.Dead ? BattlePanel.DeadMask(unit, info.W >= BattlePanel.BigWidth) : null;
        string key = $"{portrait.Name}|{state}|{kept}|{maskName}";
        // Одна текстура на юнита — текущая: раны меняются каждый ход, старые подкраски не нужны.
        if (_faces.TryGetValue(unit.Id, out var cached) && cached.Key == key) return cached.Texture;
        if (Pixels(path) is not var (source, w, h)) return null;
        if (h != info.H) kept = wounds ? BattlePanel.KeptRows(unit, h) : h;   // HD-замена
        // Точки портрета разобраны один раз на файл (Pixels): вспышка перекрашивает портрет дважды (начало и срок), без разбора PNG.
        byte[] data = (byte[])source.Clone();
        var mask = maskName is null ? null : Mask(maskName);
        BattlePanel.Shade(data, w, whole, kept, mask?.Data, mask?.W ?? 0, mask?.H ?? 0, look.Alpha);
        var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        int center = (((h / 2) * w) + (w / 2)) * 4;
        if (center + 2 < data.Length) FaceCenters[unit.Id] = $"{data[center]};{data[center + 1]};{data[center + 2]}";
        var texture = ImageTexture.CreateFromImage(image);
        // Прежняя подкраска больше не нужна: спрайт панели, если он ещё жив, держит её ссылкой движка.
        if (_faces.TryGetValue(unit.Id, out var old)) old.Texture.Dispose();
        _faces[unit.Id] = (key, texture);
        return texture;
    }

    /// <summary>Точки RGBA8 портрета по файлу — разбираются один раз за бой; файла нет — null.</summary>
    private (byte[] Data, int W, int H)? Pixels(string path)
    {
        if (_pixels.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;
        var image = GfxTextures.Canvas(path);
        image.Convert(Image.Format.Rgba8);
        var pixels = (image.GetData(), image.GetWidth(), image.GetHeight());
        image.Dispose();
        return _pixels[path] = pixels;
    }

    private readonly Dictionary<string, (byte[] Data, int W, int H)> _pixels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Портретов павших с маской-черепом (<see cref="BattlePanel.DeadMask"/>) в последней сборке панелей — для проверок.</summary>
    public int DeadMasks { get; private set; }

    /// <summary>Точки маски павшего (<c>Faces</c>, <c>MASKDEADS</c>/<c>MASKDEADL</c>) — разбираются один раз на запуск; нет в графике — null
    /// (портрет павшего тогда — чёрным наполовину, как прежде).</summary>
    private static (byte[] Data, int W, int H)? Mask(string name)
    {
        if (_masks.TryGetValue(name, out var cached)) return cached;
        (byte[], int, int)? mask = null;
        if (GameData.Content.Gfx.Image("Faces", name) is { } info && GfxAssets.PathOf(info, GameData.GfxRoot) is var path && File.Exists(path)
            && GfxTextures.Canvas(path) is { } image)
        {
            image.Convert(Image.Format.Rgba8);
            mask = (image.GetData(), image.GetWidth(), image.GetHeight());
            image.Dispose();
        }
        return _masks[name] = mask;
    }

    private static readonly Dictionary<string, (byte[] Data, int W, int H)?> _masks = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string Key, Texture2D Texture)> _faces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Полоса здоровья под портретом — текст «сейчас/макс» (<see cref="BattlePanel.MeterText"/>, чёрный medbold по центру);
    /// у большого юнита — шириной 150 с подложкой <see cref="BattlePanel.BigMeterUnder"/> по центру.</summary>
    private void Meter(Node2D into, BattleScene scene, BattleUnit unit, bool big, int faceX)
    {
        var rect = scene.PanelSlot(unit.Attacker, unit.Slot, meter: true);
        if (big) rect = rect with { X = faceX };
        int width = big ? BattlePanel.BigWidth : rect.Width;
        if (big && UiAssetImage(BattlePanel.BigMeterUnder) is { } under)
            into.AddChild(new Sprite2D
            {
                Texture = under, Centered = false, TextureFilter = TextureFilterEnum.Nearest, ZIndex = BattleScene.PanelLayer + 1,
                Position = new Vector2(rect.X + ((width - under.GetWidth()) / 2), rect.Y),
            });
        var style = UiTextStyle.Parse(Text(BattlePanel.MeterText)
            .Replace("%NOW%", Math.Max(0, unit.Hp).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("%MAX%", BattlePanel.ShownHpMax(unit).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        MeterLines.Add(style.Text);
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, ZIndex = BattleScene.PanelLayer + 2 };
        // Оформление — до текста и размера: без него подпись растёт по шрифту темы (UiArt.Place).
        if (MeterFont() is { } font) label.LabelSettings = new LabelSettings { Font = font.Face, FontSize = font.Size, FontColor = Colors.Black };
        else label.AddThemeColorOverride("font_color", Colors.Black);
        label.Text = style.Text;
        into.AddChild(label);
        // По вертикали — правилом движка (0x52ad04): шаг medbold 17 выше полосы 16 — текст от верха полосы, а не по центру.
        UiArt.Place(label, new Rect2(rect.X, rect.Y, width, rect.Height), style.V, BlockStep("medbold", label));
    }

    /// <summary>Шаг строки начертания (<see cref="UiArt.LineStep"/>); набора шрифтов нет — высота строки подписи шрифтом темы.</summary>
    private static int BlockStep(string? key, Label label) =>
        SharedFont(key) is null ? (int)Mathf.Ceil(label.GetLineHeight()) : UiArt.LineStep(GameData.Fonts.Resolve(key));

    /// <summary>Строки подписи крупного портрета последней сборки (текст без разметки, по строкам через «|», верх блока и высоты строк) — для проверок.</summary>
    public List<string> BigTextLines { get; } = [];

    /// <summary>Подпись под крупным портретом (<c>IMG_?BIGTEXT</c>, <c>X100TA0608</c> = <c>\vC;\fMedBold;%NAME%\n\fSmall;  ОЗ: …</c>, <c>0x64f7fa</c>):
    /// каждая строка — своим начертанием (имя — MedBold, здоровье — Small), выравнивание — по разметке (без <c>\h</c> — влево, пробелы
    /// строки сохраняются), блок строк — по вертикали по <c>\v</c>; пустая строка имени место занимает (снимок оригинала 28.09 f0: «ОЗ»
    /// мелким шрифтом у левого края, ниже середины места). Раньше всё шло одной подписью шрифтом темы по центру и вылезало за рамку.</summary>
    private void BigText(Node2D into, UiRect rect, string markup)
    {
        var lines = new List<(UiTextStyle Style, UiFont Font, int Height)>();
        var basis = UiTextStyle.Default;
        foreach (string piece in markup.Split(@"\n"))
        {
            var style = UiTextStyle.Parse(piece, basis);
            basis = style;
            if (UiFace(style.Font) is not { } font) return;
            // Строка — шаг шрифта (ячейка + межстрочный MFT, UiFont.Line), как у многострочной подписи; без шага — высота шрифта.
            lines.Add((style, font, font.Line > 0 ? font.Line : (int)Mathf.Ceil(UiArt.Face(font).GetHeight(font.Size))));
        }
        int total = lines.Sum(l => l.Height);
        var v = lines.Count > 0 ? lines[^1].Style.V : UiVAlign.Top;
        // Правило движка (0x52ad04, UiTextBlock): блок выше места — от верха.
        int y = rect.Y + (total > rect.Height ? 0 : v switch { UiVAlign.Center => (rect.Height - total) / 2, UiVAlign.Bottom => rect.Height - total, _ => 0 });
        BigTextLines.Add(string.Join("|", lines.Select(l => l.Style.Text)) + $" y={y} высоты={string.Join("+", lines.Select(l => l.Height))}");
        foreach (var (style, font, height) in lines)
        {
            var colour = style.Color ?? GameData.Fonts.Color;
            // Оформление — первым: подпись без него выросла бы по шрифту темы (UiArt.Place).
            into.AddChild(new Label
            {
                LabelSettings = UiArt.Settings(font, Color.Color8(colour.R, colour.G, colour.B)),
                Text = style.Text, Position = new Vector2(rect.X, y), Size = new Vector2(rect.Width, height), ClipText = true,
                HorizontalAlignment = style.H switch { UiHAlign.Center => HorizontalAlignment.Center, UiHAlign.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Left },
                VerticalAlignment = VerticalAlignment.Top, ZIndex = BattleScene.PanelLayer + 2,
            });
            y += height;
        }
    }

    /// <summary>Начертание интерфейса по ключу (<see cref="GameData.Fonts"/>); набора нет — null, причина печатается один раз.</summary>
    private static UiFont? UiFace(string? key) => SharedFont(key) is null ? null : GameData.Fonts.Resolve(key);

    private static (FontFile Face, int Size)? _meterFont;

    /// <summary>Шрифт полосы — <c>medbold</c> из шрифтов интерфейса (<see cref="GameData.Fonts"/>: база и моды).</summary>
    private static (FontFile Face, int Size)? MeterFont() => _meterFont ??= SharedFont("medbold");

    private static bool _fontFailed;

    /// <summary>Начертание по ключу разметки — общий с экранами шрифт (<see cref="UiArt.Face"/>). Набора шрифтов или файла нет — null
    /// (полоса и надписи остаются шрифтом темы, как прежде), причина печатается один раз.</summary>
    private static (FontFile Face, int Size)? SharedFont(string? key)
    {
        if (_fontFailed) return null;
        try
        {
            var face = GameData.Fonts.Resolve(key);
            return (UiArt.Face(face), face.Size);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or System.Text.Json.JsonException)
        {
            _fontFailed = true;
            GD.PrintErr($"BattleView: шрифт {key ?? "по умолчанию"} — {e.Message}");
            return null;
        }
    }

    /// <summary>Картинка интерфейса по имени (подложка полосы большого юнита) — одна текстура на имя на весь запуск, а не новая на каждую
    /// перестройку панелей.</summary>
    private static Texture2D? UiAssetImage(string name)
    {
        if (_uiImages.TryGetValue(name, out var cached) && (cached is null || IsInstanceValid(cached))) return cached;
        var texture = GameData.Content.AssetsByName.TryGetValue(name, out var asset) && UiAssets.PathOf(asset, GameData.UiAssetsRoot) is var path
                      && File.Exists(path)
            ? ImageTexture.CreateFromImage(Image.LoadFromFile(path)) : null;
        return _uiImages[name] = texture;
    }

    private static readonly Dictionary<string, Texture2D?> _uiImages = new(StringComparer.OrdinalIgnoreCase);

    private Node2D? _notes;

    /// <summary>Сколько держится надпись на портрете: N + 1 тиков панели (счётчик <c>+0x3c</c> = N, рисуется, пока ≥ 0: <c>0x64d120</c>,
    /// <c>0x64d467</c>, <c>0x64ae85</c>), N по скорости боя (<c>0x64a4cd</c>: 1 → 15, 2 → 10, 3 и 4 → 5); тик — 66 мс (ранг 2: группа в
    /// общем аниматоре, <c>0x64a45e</c>). Затухания и сдвига нет. ponytail: настройки скорости боя у порта нет — скорость 1.</summary>
    public double NoteSeconds { get; set; } = (15 + 1) * FrameMs / 1000;

    /// <summary>Надписи последнего удара (текст без разметки) — для проверок.</summary>
    public List<string> NoteLines { get; } = [];

    /// <summary>Строки надписей последнего удара: «строк×шире_места» (сколько строк и сколько из них шире места портрета) — для проверок.</summary>
    public List<string> NoteWraps { get; } = [];

    /// <summary>Надпись на портрете юнита в панели (<c>0x64d160</c>): картинка текста 70×85 (у большого 150×85, <c>0x6ebedc</c>/<c>0x6ebed8</c>,
    /// <c>0x6ebee0</c>), по центру, белым с чёрной обводкой (<c>X008TA0028</c>) — урон «−N», лечение «+N», промах, опыт после боя; держится
    /// <see cref="NoteSeconds"/>. Обводка — восемь сдвигов на точку (<see cref="UiArt.Outlined"/>), как у движка. Строки — переносом движка по
    /// ширине картинки (<see cref="UiTextWrap"/>: сборка текста картинки <c>0x52ad04</c> → <c>0x52add8</c> → <c>0x52a1ba</c>): «Понижение уровня»
    /// у малого портрета — две строки; слово шире места не рвётся, а срезается краем картинки. <paramref name="anyPanel"/> — и в панели SPOT
    /// (опыт в конце боя кладётся на все четыре группы). <paramref name="src"/> и <paramref name="persist"/> — для строки зонда <c>bat_text</c> 7.6
    /// (источник надписи, как у движка: <c>target</c>, <c>actor</c>, <c>heal</c>, <c>xp</c>; стойкая — без срока).</summary>
    public void Note(BattleScene scene, BattleUnit unit, string markup, bool anyPanel = false, string src = "target", bool persist = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(unit);
        // Только основная панель (группы 0x1384/0x1388): в панели SPOT надписей удара нет (0x63ace6, 0x63c2a5).
        if (persist && _steadyNotes.Remove(unit.Id, out var old) && IsInstanceValid(old.Label)) old.Label!.QueueFree();
        if (_notes is null || (!anyPanel && unit.Attacker != scene.MainShowsAttackers) || !scene.PanelShown(unit.Attacker))
        {
            // Стойкая надпись помнится и у скрытой группы — встанет, когда её панель покажут (опыт кладётся на все четыре группы).
            if (persist) _steadyNotes[unit.Id] = (markup, null);
            PortProbe.Av?.BattleText(unit.Id, markup, new(src, persist, On: false, !unit.Type.SizeSmall, 0, 0, -1, -1, 0));
            return;
        }
        var (label, style, rect) = DrawNote(scene, unit, markup, record: true);
        if (persist) _steadyNotes[unit.Id] = (markup, label);   // опыт конца боя — без срока, до закрытия боя (0x64d23e)
        else _fading.Add((label, 0));
        if (PortProbe.Av is { } av)
        {
            try
            {
                TraceNote(av, unit, markup, style.Text, src, persist, rect, label);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                GD.PushWarning($"probe bat_text: {e.Message}");   // журнал не роняет бой
            }
        }
    }

    /// <summary>Стойкие надписи (<see cref="Note"/> с <c>persist</c>): опыт конца боя «Опыт: +N» — у движка без срока (<c>0x64d23e</c>, в отличие
    /// от <c>0x64d09c</c>) и на всех четырёх группах панели (<c>0x636f2d…0x636fb1</c>): держится до закрытия боя вместе со светло-жёлтым
    /// портретом (<see cref="FixFace"/>), и в панели, переключённой <c>TOG_RIGHTUNITS</c>, — тоже. Партия 20.10 (<c>corpus/20261020-faces</c>):
    /// у движка ни одной строки <c>bat_text_end</c> после «Опыт: +N»; у порта надпись гасла через 16 тиков, а жёлтый оставался (слова
    /// пользователя: «жёлтый фильтр не пропадает вместе с текстом опыта»; Зс53).</summary>
    private readonly Dictionary<string, (string Markup, Label? Label)> _steadyNotes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Стойких надписей на виду — для проверок.</summary>
    public int SteadyNotes => _steadyNotes.Values.Count(n => IsInstanceValid(n.Label));

    /// <summary>Сборка панелей: стойкие надписи — заново на портретах юнитов в показанных панелях; у скрытых — помнятся без картинки.</summary>
    private void RedrawSteadyNotes(BattleScene scene, BattleState state)
    {
        foreach (var (id, (markup, label)) in _steadyNotes.ToList())
        {
            if (IsInstanceValid(label)) label!.QueueFree();
            var unit = state.Unit(id);
            bool shown = unit is not null && _notes is not null && scene.PanelShown(unit.Attacker);
            _steadyNotes[id] = (markup, shown ? DrawNote(scene, unit!, markup, record: false).Label : null);
        }
    }

    /// <summary>Картинка надписи на портрете юнита (<see cref="Note"/>): метка в узле надписей, разметка и место. <paramref name="record"/> —
    /// в списки проверок (<see cref="NoteLines"/>, <see cref="NoteWraps"/>); перерисовка стойкой — нет.</summary>
    private (Label Label, UiTextStyle Style, UiRect Rect) DrawNote(BattleScene scene, BattleUnit unit, string markup, bool record)
    {
        var rect = scene.PanelFace(unit, unit.Type.SizeSmall ? 70 : BattlePanel.BigWidth);
        var style = UiTextStyle.Parse(Text("X008TA0028") + markup);
        if (record) NoteLines.Add(style.Text);
        var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true };
        var face = UiFace(style.Font);
        string text = style.Text;
        if (face is not null)
        {
            label.LabelSettings = UiArt.Outlined(face, Colors.White, Colors.Black);
            var font = UiArt.Face(face);
            float Width(string chunk) => font.GetStringSize(chunk, HorizontalAlignment.Left, -1, face.Size).X;
            // Пробелы за последним словом строки в ширину входят (0x52a1ba), а выводятся без них — как у подписей окон (UiScreenView.Wrapped).
            var lines = UiTextWrap.Lines(text, rect.Width, Width).Select(l => l.TrimEnd(' ')).ToList();
            text = string.Join('\n', lines);
            if (record) NoteWraps.Add(FormattableString.Invariant($"{lines.Count}x{lines.Count(l => Width(l) > rect.Width)}"));
        }
        else
        {
            label.AddThemeColorOverride("font_color", Colors.White);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 2);
            label.AutowrapMode = TextServer.AutowrapMode.Word;
        }
        label.Text = text;
        _notes!.AddChild(label);
        // По вертикали — правилом движка (0x52ad04, UiTextBlock): блок × шаг шрифта, по центру места портрета.
        UiArt.Place(label, new Rect2(rect.X, rect.Y, rect.Width, rect.Height), style.V,
                    face is null ? (int)Mathf.Ceil(label.GetLineHeight()) : UiArt.LineStep(face));
        return (label, style, rect);
    }

    /// <summary>Строка зонда <c>bat_text</c> 7.6 о показанной надписи: место, строк показано и строк по правилу переноса движка
    /// (<see cref="UiTextWrap"/>, шрифтом надписи) — расхождение «строк меньше, чем по правилу» и есть надпись, вылезшая за портрет.</summary>
    private void TraceNote(AvTrace av, BattleUnit unit, string markup, string plain, string src, bool persist, UiRect rect, Label label)
    {
        string[] shown = label.Text.Split('\n');
        int textWidth = 0, rule = shown.Length;
        if (label.LabelSettings is { Font: { } font } settings)
        {
            float Measure(string chunk) => font.GetStringSize(chunk, HorizontalAlignment.Left, -1, settings.FontSize).X;
            textWidth = (int)Mathf.Ceil(shown.Max(Measure));
            rule = UiTextWrap.Lines(plain, rect.Width, Measure).Count;
        }
        av.BattleText(unit.Id, markup, new(src, persist, On: true, !unit.Type.SizeSmall, (int)rect.Width, (int)rect.Height, shown.Length,
                                           rule, textWidth));
        _noteUnits[label] = (unit.Id, Time.GetTicksMsec());
    }

    /// <summary>Надписи на виду — для строки зонда <c>bat_text_end</c>: юнит и миг показа.</summary>
    private readonly Dictionary<Label, (string Unit, ulong Shown)> _noteUnits = new();

    /// <summary>Длина в кадрах — наибольшая из картинок (0 — картинок нет в графике).</summary>
    public int Length(IEnumerable<BattleSprite> sprites) => sprites.Select(s => Frames(s).Length).DefaultIfEmpty(0).Max();

    /// <summary>Проиграть эффект один раз и убрать его. Возвращает число кадров (0 — картинки нет). <paramref name="kind"/> — вид для
    /// журнала зонда (схема 7.3, как у зонда оригинала): <c>HEFF</c>, <c>TUCH</c>, <c>TICK</c>, <c>HEAL</c> — строка <c>bat_fx</c> (юнит —
    /// ходящий шага), <c>EFFECT</c> и <c>ITEM</c> — <c>bat_anim</c> юнита <paramref name="unit"/> (эффект <c>Battle.ff</c>, смерть; анимация применения
    /// предмета у носителя — у зонда оригинала <c>ITEM</c> с типом носителя, <c>0x652006</c>).</summary>
    public int PlayOnce(BattleSprite sprite, string? kind = null, BattleUnit? unit = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        var frames = Frames(sprite);
        if (kind is not null && unit is not null && PortProbe.Av is { } av)
        {
            if (kind is "EFFECT" or "ITEM") av.BattleAnim(unit.Id, unit.Type.GlobalId, kind, frames.Length);
            else av.BattleFx(kind, unit.Id, sprite.Name, frames.Length);
        }
        if (frames.Length == 0) return 0;
        var node = new Sprite2D
        {
            Texture = frames[0], Centered = false, Position = new Vector2(sprite.X, sprite.Y), ZIndex = sprite.Layer,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        Blend(node, sprite);
        _layer.AddChild(node);
        _once.Add((node, frames, 0));
        return frames.Length;
    }

    private readonly List<(Sprite2D Sprite, Texture2D[] Frames, int Frame)> _once = new();

    /// <summary>Перерисовать юнита в новом действии (ударил, получил урон, стоит). Возвращает длину действия в кадрах — наибольшую
    /// из его картинок (0 — юнита на поле нет).</summary>
    public int Show(BattleUnit unit, BattleAction action)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_byUnit.TryGetValue(unit.Id, out var old))
        {
            foreach (var sprite in old)
            {
                _animated.RemoveAll(a => a.Sprite == sprite);
                sprite.QueueFree();
            }
            _byUnit.Remove(unit.Id);
            Shown -= old.Count;
        }
        _actions.Remove(unit.Id);
        if (!unit.OnField) return 0;
        int longest = 0;
        foreach (var sprite in _scene.UnitSprites(unit, action, unit.Type.SizeSmall))
        {
            Put(sprite);
            longest = Math.Max(longest, Frames(sprite).Length);
        }
        // Замах и удар — один проход, затем покой (автомат CBatUnitAnim: HMOV/HHIT → IDLE, docs/ai-screen.md «Автомат»). Прежде действие
        // крутилось по кругу до пересборки поля в конце шага, а шаг ждёт ещё и надписей (+0x14d8, NoteMs) — короткий замах успевал пройти
        // два-три раза (замечание 13).
        // Журнал зонда: только смена действия юнита, как у зонда оригинала (поле перерисовывается и без смены).
        if (PortProbe.Av is { } av && _traced.GetValueOrDefault(unit.Id) != (action, unit.Type.GlobalId))
        {
            _traced[unit.Id] = (action, unit.Type.GlobalId);
            av.BattleAnim(unit.Id, unit.Type.GlobalId, action switch
            {
                BattleAction.Still => "STIL",
                BattleAction.Idle => "IDLE",
                BattleAction.Hit => "HMOV",
                _ => "HHIT",
            }, longest);
        }
        if (action is BattleAction.Hit or BattleAction.Hurt && longest > 0)
        {
            _actions[unit.Id] = (unit, longest, longest);
            ActionsStarted++;
        }
        return longest;
    }

    /// <summary>Последнее действие и тип юнита, записанные в журнал зонда (строка <c>bat_anim</c> — только на смену).</summary>
    private readonly Dictionary<string, (BattleAction Action, string Type)> _traced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Юниты в действии одного прохода и сколько кадров ему осталось.</summary>
    private readonly Dictionary<string, (BattleUnit Unit, int Left, int Length)> _actions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Сколько действий одного прохода (замах, удар) начато и сколько из них дошли до покоя сами — для проверок.</summary>
    public int ActionsStarted { get; private set; }

    public int ActionsFinished { get; private set; }

    /// <summary>Сколько раз кадр действия одного прохода пошёл бы по второму кругу — для проверок (должно быть 0).</summary>
    public int ActionRepeats { get; private set; }

    /// <summary>Тик аниматора для действий одного прохода: у дошедших до конца — покой.</summary>
    private List<BattleUnit>? TickActions()
    {
        List<BattleUnit>? done = null;
        foreach (var (id, (unit, left, length)) in _actions.ToList())
        {
            if (left > 1)
            {
                _actions[id] = (unit, left - 1, length);
                continue;
            }
            _actions.Remove(id);
            (done ??= []).Add(unit);
        }
        return done;
    }

    public override void _Process(double delta)
    {
        _since += delta * 1000;
        _clock += delta * 1000;
        if (_clock < FrameMs) return;
        _clock %= FrameMs;
        TickFlashes();   // вспышки портретов — тот же тик и срок, что у надписей (0x64d356)
        // Надписи на портретах держатся NoteSeconds и снимаются (счётчик +0x3c, 0x64ae85).
        for (int i = _fading.Count - 1; i >= 0; i--)
        {
            var (label, age) = _fading[i];
            age += FrameMs / 1000;
            if (age >= NoteSeconds || !IsInstanceValid(label))
            {
                if (_noteUnits.Remove(label, out var note))
                    PortProbe.Av?.BattleTextEnd(note.Unit, IsInstanceValid(label) ? "end" : "gone", (long)(Time.GetTicksMsec() - note.Shown));
                if (IsInstanceValid(label)) label.QueueFree();
                _fading.RemoveAt(i);
            }
            else _fading[i] = (label, age);
        }
        for (int i = _once.Count - 1; i >= 0; i--)
        {
            var (sprite, frames, frame) = _once[i];
            if (!IsInstanceValid(sprite)) { _once.RemoveAt(i); continue; }
            frame++;
            if (frame >= frames.Length) { sprite.QueueFree(); _once.RemoveAt(i); continue; }
            sprite.Texture = frames[frame];
            _once[i] = (sprite, frames, frame);
        }
        for (int i = _panelFrames.Count - 1; i >= 0; i--)
        {
            var (sprite, frames) = _panelFrames[i];
            if (!IsInstanceValid(sprite)) { _panelFrames.RemoveAt(i); continue; }
            sprite.Texture = frames[PanelFrame(frames.Length)];
        }
        var finished = TickActions();
        for (int i = _animated.Count - 1; i >= 0; i--)
        {
            var (sprite, frames, frame) = _animated[i];
            if (!IsInstanceValid(sprite)) { _animated.RemoveAt(i); continue; }   // снят другой перестройкой
            if (frame + 1 == frames.Length && _actions.Count > 0 && RepeatsAction(sprite, frames.Length)) ActionRepeats++;
            frame = (frame + 1) % frames.Length;
            sprite.Texture = frames[frame];
            _animated[i] = (sprite, frames, frame);
        }
        if (finished is null) return;
        foreach (var unit in finished)
        {
            Show(unit, BattleAction.Idle);
            ActionsFinished++;
        }
    }

    /// <summary>Кадр картинки действия одного прохода (полной длины) уходит на второй круг.</summary>
    private bool RepeatsAction(Sprite2D sprite, int frames) =>
        _actions.Any(a => a.Value.Length == frames && _byUnit.TryGetValue(a.Key, out var list) && list.Contains(sprite));

    private Node2D AddLayer()
    {
        var node = new Node2D { Name = "Field" };
        AddChild(node);
        return node;
    }

    private void Put(BattleSprite s, bool fromMiddle = false)
    {
        var frames = Frames(s);
        if (frames.Length == 0) return;
        int first = fromMiddle ? frames.Length / 2 : 0;
        var sprite = new Sprite2D
        {
            Texture = frames[first], Centered = false, Position = new Vector2(s.X, s.Y), ZIndex = s.Layer,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        Blend(sprite, s);
        _layer.AddChild(sprite);
        Shown++;
        if (frames.Length > 1) _animated.Add((sprite, frames, first));
        if (!_byUnit.TryGetValue(s.Source, out var list)) _byUnit[s.Source] = list = [];
        list.Add(sprite);
    }

    /// <summary>Смешение картинки боя по <c>opacity</c> описи (<see cref="GfxBlending"/>, как у карты): 128 — половина (тени), 300 —
    /// сложение (эффекты атак, метки <c>MRK*</c>), 400 — вычитание (<c>CDisplayDDraw</c> blit <c>0x671fb0</c>).</summary>
    private static void Blend(Sprite2D sprite, BattleSprite s)
    {
        var gfx = GameData.Content.Gfx;
        GfxBlending.Apply(sprite, gfx.BlendOf(s.Archive, s.Name, gfx.Anim(s.Archive, s.Name) is not null));
    }

    /// <summary>Кадры картинки: свой словарь по (архив, имя), сами текстуры — общий кэш партии по пути (<see cref="GfxTextures"/>, гипотеза
    /// Г1 <c>docs/perf-log.md</c>).</summary>
    private Texture2D[] Frames(BattleSprite s)
    {
        var key = (s.Archive, s.Name);
        if (_textures.TryGetValue(key, out var cached)) return cached;
        return _textures[key] = GfxTextures.Of(Paths(s));
    }

    /// <summary>Файлы кадров картинки по порядку (как их берёт поле) — для проверок видимости.</summary>
    public static IReadOnlyList<string> FirstFramePaths(BattleSprite s) => Paths(s);

    private static List<string> Paths(BattleSprite s)
    {
        var paths = new List<string>();
        var gfx = GameData.Content.Gfx;
        if (gfx.Anim(s.Archive, s.Name) is { } anim) paths.AddRange(GfxAssets.FramePaths(anim, GameData.GfxRoot));
        else if (gfx.Image(s.Archive, s.Name) is { } image) paths.Add(GfxAssets.PathOf(image, GameData.GfxRoot));
        return paths;
    }

    /// <summary>Разобрать в рабочих потоках PNG картинок, которых ещё нет ни в своём словаре, ни в общем кэше (сборка поля; шаг показа
    /// боя — все картинки действия разом, а не по одной в главном потоке, замечание 35).</summary>
    public void Prefetch(IEnumerable<BattleSprite> sprites)
    {
        var seen = new HashSet<(string, string)>(_textures.Keys);
        GfxTextures.Prefetch(sprites.Where(s => seen.Add((s.Archive, s.Name))).SelectMany(Paths));
    }

    /// <summary>Начать в фоне разбор PNG картинок, которые скоро понадобятся (<see cref="GfxTextures.Warm(IEnumerable{string})"/>); кадр не ждёт.</summary>
    public void Warm(IEnumerable<BattleSprite> sprites)
    {
        var seen = new HashSet<(string, string)>(_textures.Keys);
        GfxTextures.Warm(sprites.Where(s => seen.Add((s.Archive, s.Name))).SelectMany(Paths));
    }
}
