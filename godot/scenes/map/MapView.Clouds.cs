using System;
using System.Collections.Generic;
using System.Linq;
using LastEdict.Data.Gfx;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Облака над картой (<c>CMapClouds</c>, конструктор <c>0x5bd6d3</c>, ранг 1 по листингу): <c>K = ⌊(N/48)²·10⌋</c> облаков на карте N×N
/// (<c>0x540658</c>), картинка — случайная из <c>CLOUDI*</c> (IsoCmon 256×256, 17 кадров), слой <c>CLOUD_IMAGE</c> 890 в общем порядке — под
/// туманом. Якорные клетки — от угла (N−1, N−1) по антидиагоналям (наибольший ключ сортировки), к центру якоря прибавляется сдвиг
/// (dx, dy): dx ∈ [−(N+12)·32, (N+12)·32), dy ∈ [−(N−10)·16 − (N/2)·32, (N−10)·16 − (N/2)·32); скорость v = rand()%5 + 1 точек за тик
/// 66 мс, только вправо (<c>0x5bda76</c>); ушло за правый край — снова слева, с новым случайным dy. Тени <c>CLOUDS*</c> движок ставит на
/// слой <c>CLOUD_SHADOW</c> 49 с тем же номером — в ванили их нет в архивах. Настройка — <c>IsoBirds</c> ini (settings+0x190, по умолчанию 1),
/// кнопка <c>TOG_ISOCLOUDS</c>.
/// </summary>
public partial class MapView
{
    private const string CloudChannel = "clouds";
    private const double CloudTickMs = LastEdict.Data.Map.AnimClock.SlowMs;

    private sealed class Cloud
    {
        public required MapSprite Sprite { get; set; }
        public required int Dx { get; set; }
        public required int Dy { get; set; }
        public required int Speed { get; init; }
    }

    private readonly List<Cloud> _clouds = new();
    private readonly Random _cloudRandom = new();
    private double _cloudClock;
    private bool _cloudsOn = true;

    /// <summary>Облака над картой (настройка <c>IsoBirds</c>); смена — облака заново.</summary>
    public bool Clouds
    {
        get => _cloudsOn;
        set
        {
            if (_cloudsOn == value) return;
            _cloudsOn = value;
            BuildClouds();
        }
    }

    /// <summary>Сколько облаков над картой сейчас (проверка).</summary>
    public int CloudCount => _clouds.Count;

    /// <summary>Сдвиг первого облака по X (проверка движения).</summary>
    public int FirstCloudX => _clouds.Count > 0 ? _clouds[0].Sprite.X : 0;

    private (int Min, int Max) CloudX(int n) => (-(n + 12) * 32, (n + 12) * 32);

    private (int Min, int Max) CloudY(int n) => (-((n - 10) * 16) - (n / 2 * 32), ((n - 10) * 16) - (n / 2 * 32));

    /// <summary>Облака по размеру карты (при показе карты и при смене настройки).</summary>
    private void BuildClouds()
    {
        _clouds.Clear();
        if (_objects is null || Session is null) return;
        int n = Map.Size;
        var names = GameData.Content.Gfx.Archive("IsoCmon")?.Anims.Where(a => a.Name.StartsWith("CLOUDI", StringComparison.Ordinal))
                        .Select(a => a.Name).Order(StringComparer.Ordinal).ToList() ?? [];
        int count = _cloudsOn && names.Count > 0 && n > 10 ? (int)(Math.Pow(n / 48.0, 2) * 10) : 0;
        var (xMin, xMax) = CloudX(n);
        var (yMin, _) = CloudY(n);
        int layer = GameData.Content.MapArt.Layer("CLOUD_IMAGE");
        using var anchors = Anchors(n).GetEnumerator();
        for (int i = 0; i < count && anchors.MoveNext(); i++)
        {
            var (ax, ay) = anchors.Current;
            string name = names[_cloudRandom.Next(names.Count)];
            var anim = GameData.Content.Gfx.Anim("IsoCmon", name)!;
            int dx = xMin + _cloudRandom.Next(xMax - xMin), dy = yMin + _cloudRandom.Next(Math.Max(1, (n - 10) * 32));
            var cloud = new Cloud
            {
                Sprite = new MapSprite("IsoCmon", anim.Name, true, anim.Frames.Count, 0, 0, anim.W, anim.H, layer, ax, ay, "облако"),
                Dx = dx, Dy = dy, Speed = _cloudRandom.Next(5) + 1,
            };
            Place(cloud);
            _clouds.Add(cloud);
        }
        _objects.ShowLayered(CloudChannel, [.. _clouds.Select(c => c.Sprite)], GameData.Content.Gfx, GameData.GfxRoot);
    }

    /// <summary>Якорные клетки облаков: от (N−1, N−1) по антидиагоналям нижнего угла — (N−1,N−1), (N−2,N−1), (N−1,N−2), (N−3,N−1)…</summary>
    private static IEnumerable<(int X, int Y)> Anchors(int n)
    {
        for (int d = 0; d < 2 * n - 1; d++)
            for (int i = 0; i <= d; i++)
            {
                int x = n - 1 - d + i, y = n - 1 - i;
                if (x >= 0 && y >= 0) yield return (x, y);
            }
    }

    /// <summary>Место облака: центр якорной клетки − половина картинки + сдвиг (<c>0x5ad1ee</c>).</summary>
    private static void Place(Cloud cloud)
    {
        var s = cloud.Sprite;
        var (ox, oy) = GroundGeometry.CellOrigin(s.AnchorX, s.AnchorY);
        cloud.Sprite = s with
        {
            X = ox + (GroundGeometry.CellWidth / 2) - (s.Width / 2) + cloud.Dx,
            Y = oy + (GroundGeometry.CellHeight / 2) - (s.Height / 2) + cloud.Dy,
        };
    }

    /// <summary>Тик облаков — 66 мс, пропущенные не догоняются (<c>0x51e0d0</c>).</summary>
    private void StepClouds(double delta)
    {
        if (_clouds.Count == 0 || _objects is null) return;
        _cloudClock += delta * 1000;
        if (_cloudClock < CloudTickMs) return;
        _cloudClock %= CloudTickMs;
        int n = Map.Size;
        var (xMin, xMax) = CloudX(n);
        var (yMin, yMax) = CloudY(n);
        foreach (var cloud in _clouds)
        {
            cloud.Dx += cloud.Speed;
            if (cloud.Dx >= xMax)
            {
                cloud.Dx = xMin;
                cloud.Dy = yMin + _cloudRandom.Next(Math.Max(1, yMax - yMin));
            }
            Place(cloud);
        }
        _objects.MoveLayered(CloudChannel, [.. _clouds.Select(c => c.Sprite)]);
    }
}
