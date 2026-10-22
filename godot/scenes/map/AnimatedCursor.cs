using System;
using System.Collections.Generic;
using System.IO;
using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Курсор мыши из графики игры (<c>IsoCursr</c>): картинка или анимация, кадр в 66 мс, как у «медленных» анимаций движка;
/// горячая точка — середина исходного кадра (<c>0x5b27ee</c>). Кадр обрезается по непрозрачной части: курсор Godot — не больше
/// 256×256, а у <c>DEFAULT</c>/<c>MOVE</c>/<c>BATTLE</c> холст 321×321. Godot сам курсор не анимирует — узел меняет кадр в
/// <c>_Process</c>. <see cref="Show"/> с null возвращает обычный курсор. Один узел на экран карты: курсоры задачи над клеткой
/// (<c>MapScreen.Cursor.cs</c>) и режимов прицела и мешка. <see cref="ShowWith"/> — курсор из другого архива с наложенной картинкой по
/// середине (тащение предмета в бою: <c>CUATTACK</c>/<c>CUATTACKN</c> из <c>Battle</c> и значок предмета, <c>0x532df9</c>). <see cref="ShowUi"/> —
/// курсор окна из разметки (<see cref="UiCursor"/>: кадр интерфейса, горячая точка от левого верхнего угла).
/// Узлов несколько (меню, карта, бой), курсор у Godot один: курсор держит тот, кто ставил последним (<see cref="_active"/>), — узел,
/// уходящий из дерева, сбрасывает курсор, только если он его и держит, а показ того же имени другим узлом после чужого ставится заново.
/// </summary>
public partial class AnimatedCursor : Node
{
    private const double FrameMs = LastEdict.Data.Map.AnimClock.SlowMs;   // медленная группа
    private static readonly Dictionary<string, IReadOnlyList<(Texture2D Texture, Vector2 Hotspot)>> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Узел, чей курсор сейчас стоит.</summary>
    private static AnimatedCursor? _active;

    private IReadOnlyList<(Texture2D Texture, Vector2 Hotspot)> _frames = [];
    private double _clock;
    private int _frame;

    /// <summary>Имя показанного курсора; обычный — null.</summary>
    public string? Current { get; private set; }

    /// <summary>Сколько кадров у показанного курсора.</summary>
    public int Frames => _frames.Count;

    /// <summary>Тот же курсор уже стоит и держит его этот узел.</summary>
    private bool Shown(string? name) => _active == this && string.Equals(name, Current, StringComparison.OrdinalIgnoreCase);

    public void Show(string? name)
    {
        if (Shown(name)) return;
        Current = name;
        _frames = name is null ? [] : Load("IsoCursr", name, null, "");
        _frame = 0;
        _clock = 0;
        Apply();
        PortProbe.Av?.Cursor(Current ?? "-", PortProbe.Top);   // журнал зонда (схема 7.3): смена формы
    }

    /// <summary>Курсор <paramref name="name"/> архива <paramref name="archive"/> с картинкой <paramref name="overlay"/> по середине;
    /// <paramref name="overlayKey"/> — имя картинки для кэша. <paramref name="animated"/> — анимированный вариант «…A» (у курсоров боя — при
    /// «дополнительной анимации боя», таблица <c>0x6ec738</c> или <c>0x6ec743</c> по флагу, <c>0x5b343c</c>; нет «…A» — без неё, <c>0x5b340c</c>):
    /// оригинал с <c>BattleAnim</c> 1 тащит предмет под <c>CUATTACKA</c>/<c>CUATTACKNA</c> (журнал <c>20261017-port-synth</c>, BI2).</summary>
    public void ShowWith(string archive, string name, Image overlay, string overlayKey, bool animated = true)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var gfx = GameData.Content.Gfx;
        string file = animated && (gfx.Anim(archive, name + "A") is not null || gfx.Image(archive, name + "A") is not null) ? name + "A" : name;
        string current = $"{archive}:{file}+{overlayKey}";
        if (Shown(current)) return;
        Current = current;
        _frames = Load(archive, file, overlay, overlayKey);
        _frame = 0;
        _clock = 0;
        Apply();
        PortProbe.Av?.Cursor(Current ?? "-", PortProbe.Top);   // журнал зонда (схема 7.3): смена формы
    }

    /// <summary>Курсор <paramref name="name"/> архива <paramref name="archive"/> без наложения; <paramref name="animated"/> — анимация «…A»
    /// (курсоры боя: <c>0x5b343c</c> берёт имя из таблицы анимированных <c>0x6ec738</c> или обычных <c>0x6ec743</c>).</summary>
    public void ShowFrom(string archive, string name, bool animated)
    {
        string file = animated ? name + "A" : name, current = $"{archive}:{file}";
        if (Shown(current)) return;
        Current = current;
        _frames = Load(archive, file, null, "");
        if (_frames.Count == 0 && animated) _frames = Load(archive, name, null, "");   // нет «…A» — картинка без неё (0x5b340c)
        _frame = 0;
        _clock = 0;
        Apply();
        PortProbe.Av?.Cursor(Current ?? "-", PortProbe.Top);   // журнал зонда (схема 7.3): смена формы
    }

    /// <summary>Курсор окна из разметки (<see cref="UiCursor"/>): кадр интерфейса (<c>data/ui/assets.json</c>) с горячей точкой от левого
    /// верхнего угла картинки, без анимации. Имя показа — <c>ui:кадр</c>.</summary>
    public void ShowUi(UiCursor cursor)
    {
        string current = $"ui:{cursor.Frame}";
        if (Shown(current)) return;
        Current = current;
        _frames = LoadUi(cursor);
        _frame = 0;
        _clock = 0;
        Apply();
        PortProbe.Av?.Cursor(Current ?? "-", PortProbe.Top);   // журнал зонда (схема 7.3): смена формы
    }

    public override void _Process(double delta)
    {
        if (_frames.Count < 2 || _active != this) return;   // курсор держит другой узел (бой поверх карты) — кадры не перебивают его
        _clock += delta * 1000;
        if (_clock < FrameMs) return;
        _clock %= FrameMs;
        _frame = (_frame + 1) % _frames.Count;
        Apply();
    }

    public override void _ExitTree()
    {
        if (_active != this) return;
        _active = null;
        Input.SetCustomMouseCursor(null);
    }

    private void Apply()
    {
        _active = this;
        if (_frames.Count == 0)
        {
            Input.SetCustomMouseCursor(null);
            return;
        }
        var frame = _frames[_frame];
        float scale = UiScale.CursorScale;
        if (scale == 1f) Input.SetCustomMouseCursor(frame.Texture, Input.CursorShape.Arrow, frame.Hotspot);
        else
        {
            var large = Scaled(frame.Texture, scale);
            var k = large.GetSize() / frame.Texture.GetSize();   // у курсора больше 256 точек множитель меньше заданного
            Input.SetCustomMouseCursor(large, Input.CursorShape.Arrow, frame.Hotspot * k);
        }
    }

    /// <summary>Крупный интерфейс (<see cref="UiScale"/>) — показанный курсор заново, в новом размере.</summary>
    public static void Refresh() => _active?.Apply();

    /// <summary>Увеличенные кадры — по самому кадру (кадры курсоров живут в <see cref="Cache"/> до конца запуска).</summary>
    private static readonly Dictionary<(Texture2D, float), Texture2D> Large = [];

    /// <summary>Кадр курсора в крупном режиме: увеличен (не больше 256 — предел курсора Godot), без размытия краёв.</summary>
    private static Texture2D Scaled(Texture2D texture, float scale)
    {
        var key = (texture, scale);
        if (Large.TryGetValue(key, out var done)) return done;
        var image = texture.GetImage();
        int w = Math.Min(256, (int)Math.Round(image.GetWidth() * scale)), h = Math.Min(256, (int)Math.Round(image.GetHeight() * scale));
        image.Resize(w, h, Image.Interpolation.Nearest);
        return Large[key] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Первый кадр курсора (обрезанный) — для проверок; нет картинки — null.</summary>
    public static Texture2D? FirstFrame(string name) => Load("IsoCursr", name, null, "") is [var first, ..] ? first.Texture : null;

    /// <summary>Кадр курсора окна: картинка интерфейса, обрезанная по непрозрачной части (у <c>_CUDEFAUL</c> холст 321×321 — больше
    /// предела Godot), горячая точка сдвинута на обрезку. Нет кадра в описи или на диске — пусто (обычный курсор).</summary>
    private static IReadOnlyList<(Texture2D Texture, Vector2 Hotspot)> LoadUi(UiCursor cursor)
    {
        string key = $"ui:{cursor.Frame}+{cursor.HotspotX},{cursor.HotspotY}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        IReadOnlyList<(Texture2D, Vector2)> frames = [];
        if (GameData.Content.AssetsByName.TryGetValue(cursor.Frame, out var asset)
            && UiAssets.PathOf(asset, GameData.UiAssetsRoot) is var path && File.Exists(path))
        {
            // HD-кадр — в логических точках: курсор и горячая точка в размере кадра (крупнее — UiScale).
            var full = ImageScales.ToLogical(ImageScales.With(Image.LoadFromFile(path), asset.Factor));
            full.Convert(Image.Format.Rgba8);
            var used = full.GetUsedRect();
            var cropped = used.Size == Vector2I.Zero ? full : full.GetRegion(used);
            var hotspot = new Vector2(cursor.HotspotX, cursor.HotspotY) - used.Position;
            frames = [(ImageTexture.CreateFromImage(cropped), hotspot.Clamp(Vector2.Zero, cropped.GetSize() - Vector2.One))];
        }
        else GD.PrintErr($"AnimatedCursor: нет кадра курсора окна {cursor.Frame} (python tools/ui-assets/build_ui_assets.py)");
        Cache[key] = frames;
        return frames;
    }

    /// <summary>Горячая точка показанного кадра (для проверок); курсора нет — null.</summary>
    public Vector2? Hotspot => _frames.Count == 0 ? null : _frames[_frame].Hotspot;

    private static IReadOnlyList<(Texture2D Texture, Vector2 Hotspot)> Load(string archive, string name, Image? overlay, string overlayKey)
    {
        string key = $"{archive}:{name}+{overlayKey}";
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var gfx = GameData.Content.Gfx;
        IReadOnlyList<string> paths = gfx.Image(archive, name) is { } image ? [GfxAssets.PathOf(image, GameData.GfxRoot)]
            : gfx.Anim(archive, name) is { } anim ? GfxAssets.FramePaths(anim, GameData.GfxRoot) : [];
        var frames = new List<(Texture2D, Vector2)>();
        foreach (string path in paths)
        {
            if (!File.Exists(path)) continue;
            var full = GfxTextures.Canvas(path);
            if (overlay is not null)
            {
                var o = overlay.GetFormat() == full.GetFormat() ? overlay : (Image)overlay.Duplicate();
                if (o != overlay) o.Convert(full.GetFormat());
                var at = new Vector2I((full.GetWidth() - o.GetWidth()) / 2, (full.GetHeight() - o.GetHeight()) / 2);
                full.BlendRect(o, new Rect2I(Vector2I.Zero, o.GetSize()), at);
            }
            var used = full.GetUsedRect();
            var cropped = used.Size == Vector2I.Zero ? full : full.GetRegion(used);
            var hotspot = (new Vector2(full.GetWidth(), full.GetHeight()) / 2) - used.Position;
            frames.Add((ImageTexture.CreateFromImage(cropped), hotspot.Clamp(Vector2.Zero, cropped.GetSize() - Vector2.One)));
        }
        Cache[key] = frames;
        return frames;
    }
}
