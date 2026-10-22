using System;
using System.Globalization;
using System.IO;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>Стенд земли: карта (<see cref="MapSource"/>) → земля всей карты <see cref="GroundComposer"/> из собранной
/// графики → PNG и строка чисел.
///
/// <c>--scenario "Имя"</c> или <c>--sg путь</c>, <c>--png out.png</c>, <c>--game</c>. Печатает размер карты, сид, число
/// клеток каждого типа, размер картинки, долю закрашенных точек (обязана быть 1 внутри ромба карты) и выходит с
/// кодом 0/1.</summary>
public partial class GroundTest : Node
{
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string? png = MapSource.Arg(args, "--png");
        try
        {
            var (map, path) = MapSource.Load(args);
            string name = Path.GetFileNameWithoutExtension(path);
            var cells = MapSource.GroundCells(map);
            int n = map.Size;

            var composer = new GroundComposer(cells, map.MapSeed, new GroundArt(GameData.Content.Gfx, GameData.GfxRoot));
            var (left, top, width, height) = GroundChunks.MapBounds(n);
            var t0 = DateTime.UtcNow;
            var image = composer.Render(left, top, width, height);
            double ms = (DateTime.UtcNow - t0).TotalMilliseconds;

            var godotImage = Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, image.Pixels);
            if (png is not null) godotImage.SavePng(GameData.FromProject(png));
            long painted = 0, magenta = 0;
            for (int i = 0; i < image.Pixels.Length; i += 4)
            {
                if (image.Pixels[i + 3] != 0) painted++;
                if (image.Pixels[i] == 255 && image.Pixels[i + 1] == 0 && image.Pixels[i + 2] == 255) magenta++;
            }
            string types = string.Join(",", Enum.GetValues<GroundType>()
                .Select(t => $"{t}:{cells.Cast<GroundType>().Count(c => c == t)}").Where(s => !s.EndsWith(":0", StringComparison.Ordinal)));
            // Ромб карты занимает ровно половину прямоугольника n·64 × n·32.
            double share = painted / (double)(width * height);
            GD.Print($"ground scenario={name.Replace(' ', '_')} size={n} seed={map.MapSeed} types={types} image={width}x{height} " +
                     $"painted={share.ToString("F4", CultureInfo.InvariantCulture)} magenta={magenta} ms={ms:F0} " +
                     $"sha={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.Pixels))[..16].ToLowerInvariant()}");
            GetTree().Quit(magenta == 0 && share > 0.49 ? 0 : 1);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or System.Collections.Generic.KeyNotFoundException)
        {
            GD.PrintErr($"GroundTest: {e.GetType().Name}: {e.Message}");
            GetTree().Quit(1);
        }
    }
}
