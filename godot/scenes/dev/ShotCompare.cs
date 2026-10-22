using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Сверка карты со снимком игры (<c>docs/ai-screen.md</c> §14): кадр вида карты собирается без окна (<see cref="MapComposite"/>) —
/// земля, объекты, отряды и туман глазами игрока, со смешением кадров, — и сравнивается с областью вида на снимке
/// (<c>IMG_ISOAREA</c> <c>DLG_ISO</c>: 640×600 слева на экране 800×600). Камера на снимке известна с точностью до клетки: сдвиг
/// ищется на поле <c>--margin</c> точек.
///
/// <para><c>--sg путь --player id|human --center x,y --shot снимок.png</c> [<c>--margin 96</c>] [<c>--diff разница.png</c>]
/// [<c>--frame кадр.png</c>]. <c>--shot self</c> — самопроверка: снимок — свой же кадр со сдвигом (−7, 5), совпасть обязан весь.
/// Печатает <c>shotcompare …</c> и выходит с кодом 0 (1 — ошибка).</para>
/// </summary>
public partial class ShotCompare : Node
{
    private const int ScreenWidth = 800, ScreenHeight = 600, DefaultMargin = 96;

    /// <summary>Область вида карты на экране (<c>DLG_ISO</c>, <c>IMG_ISOAREA</c>).</summary>
    private static readonly (int X, int Y, int Width, int Height) Area = (0, 0, 640, 600);

    /// <summary>Интерфейс поверх карты — не считается (ранг 2 по снимкам 27.09): полоса ресурсов вверху справа (<c>DLG_ISO_RESPOPUP</c>,
    /// строки 0–24, с x 320) и строка сообщений внизу («Игра записана…», строки 565–589). Рамку края поля <c>ISO_OUTLINE</c> порт рисует
    /// сам (<see cref="LastEdict.Data.Map.IsoViewLogic"/>) — она сравнивается со снимком; при поиске сдвига её полоса не считается.</summary>
    private static readonly (int X, int Y, int Width, int Height)[] Interface = [(320, 0, 320, 25), (0, 565, 640, 25)];

    /// <summary>Полоса рамки <c>ISO_OUTLINE</c> в области вида (непрозрачные точки — x 594…639) — мимо поиска сдвига.</summary>
    private static readonly (int X, int Y, int Width, int Height) OutlineStrip = (594, 0, 46, 600);

    /// <summary>Хозяин точек рамки в <c>owner</c> (картинки сцены — с 0, земля — −1).</summary>
    private const int OutlineOwner = -2;

    private readonly Dictionary<(string, string), RgbaImage?> _images = new();

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        try
        {
            GetTree().Quit(Run(args));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or ArgumentException
                                      or KeyNotFoundException)
        {
            GD.PrintErr($"ShotCompare: {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }

    private int Run(string[] args)
    {
        var (looks, path) = MapSource.LoadLooks(args);
        // --player human — первый игрок-человек сохранения (снимок F12 делается в его ход, туман — его).
        string? viewer = MapSource.Arg(args, "--player") is "human"
            ? looks.Map.Players.Values.FirstOrDefault(p => p.IsHuman)?.Id
            : MapSource.Arg(args, "--player");
        (int X, int Y) center = MapSource.Arg(args, "--center") is { } c ? Cell(c) : (looks.Map.Size / 2, looks.Map.Size / 2);
        int margin = MapSource.Arg(args, "--margin") is { } m ? int.Parse(m, CultureInfo.InvariantCulture) : DefaultMargin;
        string shotArg = MapSource.Arg(args, "--shot") ?? throw new ArgumentException("нужен --shot снимок.png или --shot self");

        var scene = MapScene.Build(looks, GameData.Content.MapArt, GameData.Content.Gfx, animated: false, viewer: viewer,
                                   objectives: Objectives(looks, path, viewer));
        var (ox, oy) = GroundGeometry.CellOrigin(center.X, center.Y);
        int left = ox + (GroundGeometry.CellWidth / 2) - (Area.Width / 2) - margin;
        int top = oy + (GroundGeometry.CellHeight / 2) - (Area.Height / 2) - margin;
        int width = Area.Width + (2 * margin), height = Area.Height + (2 * margin);
        // Варианты масок и берега — бросками R250 движка по состоянию из заголовка сохранения (GroundVariantDraws); --hash — хэшем.
        var state = args.Contains("--hash") ? null : GroundVariantDraws.ReadSaveState(path);
        var draws = args.Contains("--hash") ? null : GroundVariantDraws.ForSave(state?.Table, state?.Index ?? 0, looks.Map.Size);
        var ground = new GroundComposer(MapSource.GroundCells(looks.Map), looks.Map.MapSeed,
                                        new GroundArt(GameData.Content.Gfx, GameData.GfxRoot)) { Hidden = scene.Hidden, Draws = draws, Rgb565 = !args.Contains("--rgb888") }
            .Render(left, top, width, height);
        // Экран движка — 16 бит (DirectDraw 565): кадр собирается в 565 и расширяется, как снимок обёртки; --rgb888 — без этого.
        bool rgb565 = !args.Contains("--rgb888");
        var owner = new int[width * height];
        var sprites = scene.Sprites;
        var frame = MapComposite.Render(left, top, ground, sprites,
                                        s => (Picture(s), GfxBlending.Of(s, GameData.Content.Gfx)), rgb565, owner);

        var shot = shotArg == "self" ? SelfShot(frame, margin) : LoadShot(shotArg);
        bool self = shotArg == "self";
        var mask = args.Contains("--nomask") || self ? null : Interface;
        // --shift dx,dy — сдвиг камеры из строки screenshot зонда (выходы 0x540b6c); без него — поиск (без полосы рамки: она не едет с картой).
        int dx, dy;
        if (MapSource.Arg(args, "--shift") is { } shift) (dx, dy) = Cell(shift);
        else
        {
            (int X, int Y, int Width, int Height)[]? search = self ? null : [.. mask ?? [], OutlineStrip];
            var found = MapComposite.Match(frame, shot, Area, margin, search);
            (dx, dy) = (found.Dx, found.Dy);
        }
        // Рамка края поля — поверх карты на месте экрана, как у движка (самопроверке не нужна: её «снимок» — сам кадр без рамки).
        // Экран (0, 0) при сдвиге (dx, dy) — точка кадра (margin + dx − Area.X, margin + dy − Area.Y).
        if (!self && Outline(rgb565) is { } outline)
            MapComposite.Draw(frame, outline, margin + dx - Area.X, margin + dy - Area.Y, GfxBlend.Opaque, rgb565, (owner, OutlineOwner));
        var match = MapComposite.Score(frame, shot, Area, margin, dx, dy, 1, mask);
        var (outlineEqual, outlinePoints) = OutlineMatch(frame, shot, margin, match, owner, mask);
        if (MapSource.Arg(args, "--diff") is { } diffPath) Save(MapComposite.Difference(frame, shot, Area, margin, match), diffPath);
        if (MapSource.Arg(args, "--frame") is { } framePath) Save(frame, framePath);
        string map = Path.GetFileNameWithoutExtension(path).Replace(' ', '_');
        GD.Print(FormattableString.Invariant(
            $"shotcompare карта={map} зритель={viewer ?? "-"} центр={center.X},{center.Y} сдвиг={match.Dx},{match.Dy} равно={match.Equal:F4} близко={match.Near:F4} разница={match.MeanDiff:F2} точек={match.Points} картинок={scene.Sprites.Count} цвет={(rgb565 ? "565" : "888")} варианты={(draws is null ? "хэш" : draws.Origin.Replace(' ', '_'))} рамка={outlineEqual}/{outlinePoints}"));
        // --blame N — кто виноват в несовпавших точках: N картинок с наибольшим числом (земля — «земля»).
        if (MapSource.Arg(args, "--blame") is { } blame)
            foreach (var (who, count) in MapComposite.Blame(frame, shot, Area, margin, match, owner, mask)
                         .OrderByDescending(p => p.Value).Take(int.Parse(blame, CultureInfo.InvariantCulture)))
                GD.Print(FormattableString.Invariant(
                    $"blame {(who == OutlineOwner ? "рамка" : who < 0 ? "земля" : $"{sprites[who].Archive}/{sprites[who].Name} {sprites[who].Source}")} точек={count}"));
        // --blame-ground N — несовпавшие точки земли по клеткам (кучно — вариант маски или берега, россыпью — цвет).
        if (MapSource.Arg(args, "--blame-ground") is { } cells)
        {
            var byCell = new Dictionary<(int, int), int>();
            for (int y = 0; y < Area.Height; y++)
                for (int x = 0; x < Area.Width; x++)
                {
                    int fx = margin + match.Dx + x, fy = margin + match.Dy + y;
                    int f = frame.Offset(fx, fy), s = shot.Offset(Area.X + x, Area.Y + y);
                    if (owner[f / 4] >= 0 || (mask is not null && mask.Any(r => x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height))
                        || shot.Pixels.AsSpan(s, 3).SequenceEqual(frame.Pixels.AsSpan(f, 3))) continue;
                    var cell = GroundGeometry.CellAt(left + fx, top + fy);
                    byCell[cell] = byCell.GetValueOrDefault(cell) + 1;
                }
            foreach (var (cell, count) in byCell.OrderByDescending(p => p.Value).Take(int.Parse(cells, CultureInfo.InvariantCulture)))
                GD.Print(FormattableString.Invariant($"ground {cell.Item1},{cell.Item2} точек={count} тип={looks.Map.Tiles[Math.Clamp(cell.Item1, 0, looks.Map.Size - 1), Math.Clamp(cell.Item2, 0, looks.Map.Size - 1)].Ground}"));
            GD.Print(FormattableString.Invariant($"ground клеток={byCell.Count} точек={byCell.Values.Sum()}"));
        }
        return 0;
    }

    /// <summary>Первый кадр картинки сцены как RGBA (анимация на снимке может стоять на любом кадре — разница ожидаема).</summary>
    private RgbaImage? Picture(MapSprite s)
    {
        if (_images.TryGetValue((s.Archive, s.Name), out var cached)) return cached;
        var gfx = GameData.Content.Gfx;
        string? file = s.Anim && gfx.Anim(s.Archive, s.Name) is { } anim ? GfxAssets.FramePaths(anim, GameData.GfxRoot)[0]
            : gfx.Image(s.Archive, s.Name) is { } image ? GfxAssets.PathOf(image, GameData.GfxRoot) : null;
        RgbaImage? result = null;
        if (file is not null && File.Exists(file))
        {
            var loaded = GfxTextures.Canvas(file);
            loaded.Convert(Image.Format.Rgba8);
            result = new RgbaImage(loaded.GetWidth(), loaded.GetHeight(), loaded.GetData());
        }
        return _images[(s.Archive, s.Name)] = result;
    }

    /// <summary>Круги целей сценария глазами зрителя: события — из сохранения (<see cref="LastEdict.Data.Map.EventLoader"/>), набор —
    /// <see cref="LastEdict.Data.Play.GameSession.Objectives"/>. Не сохранение или нет зрителя — без кругов.</summary>
    private static MapObjectives? Objectives(LastEdict.Data.Map.MapLooks looks, string path, string? viewer)
    {
        if (viewer is null || !path.EndsWith(".sg", StringComparison.OrdinalIgnoreCase)) return null;
        var events = LastEdict.Data.Map.EventLoader.Load(path);
        var state = new LastEdict.Data.Play.GameState(LastEdict.Data.Play.GameInfo.FromMap(Path.GetFileNameWithoutExtension(path), looks), looks)
        {
            Events = events, EventProgress = LastEdict.Rules.Map.EventProgress.From(events),
        };
        var session = new LastEdict.Data.Play.GameSession(state, GameData.Content.MapArt, GameData.Book);
        var ids = session.Objectives(viewer);
        GD.Print($"objectives {viewer} {string.Join(",", ids)}");
        return new MapObjectives(ids, events.Zones);
    }

    /// <summary>Рамка края поля <c>ISO_OUTLINE</c> из графики интерфейса (в 565, если кадр в 565); нет в описи или на диске — null.</summary>
    private static RgbaImage? Outline(bool rgb565)
    {
        var asset = GameData.Content.Assets.FirstOrDefault(a => a.Name == LastEdict.Data.Map.IsoViewLogic.Outline);
        string? file = asset is null ? null : LastEdict.Data.Ui.UiAssets.PathOf(asset, GameData.UiAssetsRoot);
        if (file is null || !File.Exists(file)) return null;
        var loaded = Image.LoadFromFile(file);
        loaded.Convert(Image.Format.Rgba8);
        var image = new RgbaImage(loaded.GetWidth(), loaded.GetHeight(), loaded.GetData());
        if (rgb565) MapComposite.To565(image);
        return image;
    }

    /// <summary>Точки рамки (хозяин <see cref="OutlineOwner"/>) в области вида вне маски интерфейса: сколько совпало со снимком точно и
    /// сколько их всего.</summary>
    private static (int Equal, int Points) OutlineMatch(RgbaImage frame, RgbaImage shot, int margin, ShotMatch match, int[] owner,
                                                        IReadOnlyList<(int X, int Y, int Width, int Height)>? mask)
    {
        int equal = 0, points = 0;
        for (int y = 0; y < Area.Height; y++)
            for (int x = 0; x < Area.Width; x++)
            {
                int f = frame.Offset(margin + match.Dx + x, margin + match.Dy + y), s = shot.Offset(Area.X + x, Area.Y + y);
                if (owner[f / 4] != OutlineOwner || (mask is not null && mask.Any(r => x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height)))
                    continue;
                points++;
                if (shot.Pixels.AsSpan(s, 3).SequenceEqual(frame.Pixels.AsSpan(f, 3))) equal++;
            }
        return (equal, points);
    }

    /// <summary>Снимок игры: 800×600 (другой размер 4:3 — окно обёртки — сводится к 800×600 ближайшей точкой).</summary>
    private static RgbaImage LoadShot(string file)
    {
        var image = Image.LoadFromFile(file);
        if (image.GetWidth() != ScreenWidth || image.GetHeight() != ScreenHeight)
        {
            if (image.GetWidth() * 3 != image.GetHeight() * 4) throw new FormatException($"снимок {image.GetWidth()}×{image.GetHeight()} — не 4:3");
            image.Resize(ScreenWidth, ScreenHeight, Image.Interpolation.Nearest);
        }
        image.Convert(Image.Format.Rgba8);
        return new RgbaImage(image.GetWidth(), image.GetHeight(), image.GetData());
    }

    /// <summary>Самопроверка: экран 800×600, в области вида — кадр со сдвигом (−7, 5).</summary>
    private static RgbaImage SelfShot(RgbaImage frame, int margin)
    {
        var shot = new RgbaImage(ScreenWidth, ScreenHeight);
        for (int y = 0; y < Area.Height; y++)
            frame.Pixels.AsSpan(frame.Offset(margin - 7, margin + 5 + y), Area.Width * 4)
                 .CopyTo(shot.Pixels.AsSpan(shot.Offset(Area.X, Area.Y + y)));
        return shot;
    }

    private static void Save(RgbaImage image, string file) =>
        Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels)
             .SavePng(GameData.FromProject(file));

    private static (int X, int Y) Cell(string text)
    {
        var parts = text.Split(',');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}
