using System.Globalization;

namespace LastEdict.Data.Ui;

/// <summary>
/// Кадр графики игры в кадре интерфейса: <c>архив:картинка</c> (<c>Faces:G000UU0001FACE</c>), с необязательной подкраской
/// <c>#RRGGBB</c> — смешение каждой точки с цветом наполовину, как у движка (<c>0x5b6da4</c> → <c>0x678a0b</c>, альфа
/// 0x80: значки построек, <c>docs/ui-windows.md</c> §2), и <c>@N</c> — красить только строки ниже N % высоты (портрет
/// раненого: доля по потерянному здоровью, <c>0x5b1531</c>). Прозрачные точки не красятся. <c>~МАСКА</c> — картинка того
/// же архива, смешанная с кадром наполовину по всем точкам (павший: <c>MASKDEADS</c>/<c>MASKDEADL</c>, <c>0x5b1698</c>).
/// </summary>
public readonly record struct UiFrameRef(string Archive, string Name, UiColor? Tint, int FromPercent, string? Mask = null)
{
    /// <summary>Разобрать имя кадра; не <c>архив:картинка</c> — null (обычный кадр интерфейса).</summary>
    public static UiFrameRef? Parse(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int colon = frame.IndexOf(':');
        if (colon <= 0) return null;
        string rest = frame[(colon + 1)..];
        string? mask = null;
        int tilde = rest.IndexOf('~');
        if (tilde >= 0) (rest, mask) = (rest[..tilde], rest[(tilde + 1)..]);
        int hash = rest.IndexOf('#');
        if (hash < 0) return new UiFrameRef(frame[..colon], rest, null, 0, mask);
        string tint = rest[(hash + 1)..];
        int at = tint.IndexOf('@');
        int from = at >= 0 && int.TryParse(tint[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int p) ? Math.Clamp(p, 0, 100) : 0;
        string hex = at >= 0 ? tint[..at] : tint;
        UiColor? color = hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)
            ? new UiColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;
        return new UiFrameRef(frame[..colon], rest[..hash], color, from, mask);
    }

    /// <summary>Имя кадра с подкраской (<paramref name="fromPercent"/> — с какой доли высоты красить).</summary>
    public static string Tinted(string frame, UiColor color, int fromPercent = 0) =>
        $"{frame}#{color.R:X2}{color.G:X2}{color.B:X2}" + (fromPercent > 0 ? $"@{fromPercent.ToString(CultureInfo.InvariantCulture)}" : "");

    /// <summary>Знак зеркала в конце имени кадра: картинка отражается слева направо (правая сторона окна обмена, <c>0x492cee</c>).</summary>
    public const char Mirror = '|';

    /// <summary>Имя кадра, отражённого слева направо — любого: интерфейса, графики игры, с подкраской.</summary>
    public static string Mirrored(string frame) => frame + Mirror;

    /// <summary>Вырезка части кадра: <c>имя[x,y,w,h]</c> — прямоугольник в точках кадра (шкала отношений поля дипломатии: окно
    /// полосы <c>DLG_ISO_DIPLO_METER</c> по отношению, <c>0x48311f</c>). Часть за краем кадра — прозрачная.</summary>
    public static string Cropped(string frame, UiRect region) =>
        string.Create(CultureInfo.InvariantCulture, $"{frame}[{region.X},{region.Y},{region.Width},{region.Height}]");

    /// <summary>Разобрать вырезку: кадр и прямоугольник; имя без <c>[x,y,w,h]</c> в конце — null.</summary>
    public static (string Frame, UiRect Region)? Crop(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int open = frame.LastIndexOf('[');
        if (open <= 0 || frame[^1] != ']') return null;
        var parts = frame[(open + 1)..^1].Split(',');
        if (parts.Length != 4) return null;
        var n = new int[4];
        for (int i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n[i])) return null;
        return n[2] <= 0 || n[3] <= 0 ? null : (frame[..open], new UiRect(n[0], n[1], n[2], n[3]));
    }

    /// <summary>Знак сложения в начале имени кадра: кадр ложится на то, что под ним, сложением цветов (режим картинки 9 — кольцо
    /// выбранного лорда <c>DRAGDROP_BIGFACE_*</c> поля дипломатии, <c>0x484b53</c>; снимок 28.09 — разница 2,2 против 41 у обычного
    /// наложения).</summary>
    public const char Add = '+';

    /// <summary>Имя кадра, который ложится сложением цветов.</summary>
    public static string Added(string frame) => Add + frame;

    /// <summary>Имя кадра с маской поверх (<paramref name="mask"/> — картинка того же архива).</summary>
    public static string Masked(string frame, string mask) => $"{frame}~{mask}";

    /// <summary>Знак слоёв: <c>низ&amp;верх</c> — картинка из двух кадров, каждый по центру общего холста (размер — наибольший из двух),
    /// верхний ложится на нижний по альфе. Так движок собирает портрет в рамке карточек города и отряда (<c>0x57c45a</c>/<c>0x57c453</c> →
    /// <c>0x5b6870</c>/<c>0x5b680b</c>: составная картинка <c>0x5b6559</c>, первым слоем — рамка по таблице <c>0x7ae938</c>
    /// <c>BORDERUNITSMALL</c>/<c>BORDERUNITLARGE</c>, вторым — портрет, <c>0x5b66c8</c>; размер — наибольший из слоёв, <c>0x5b66f2</c>;
    /// слой — по центру, <c>0x5b6782</c>).</summary>
    public const char Layer = '&';

    /// <summary>Имя кадра из двух слоёв: <paramref name="over"/> по центру поверх <paramref name="under"/>.</summary>
    public static string Layered(string under, string over) => under + Layer + over;

    /// <summary>Разобрать кадр из слоёв: нижний и верхний; без <see cref="Layer"/> — null.</summary>
    public static (string Under, string Over)? Layers(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int at = frame.IndexOf(Layer);
        return at <= 0 || at == frame.Length - 1 ? null : (frame[..at], frame[(at + 1)..]);
    }

    /// <summary>Как кадр ложится на то, что под ним: знак сложения (<see cref="Add"/>) — сложение; кадр графики игры (<c>архив:картинка</c>) —
    /// по <c>opacity</c> описи (<see cref="LastEdict.Data.Gfx.GfxAssets.BlendOf(string, string, bool)"/>: отметка изученного заклинания
    /// <c>Icons:ICNSY001</c> — 300, сложение, у движка — та же отрисовка <c>CDisplayDDraw</c> <c>0x671fb0</c>); прочее — копия.</summary>
    public static LastEdict.Data.Gfx.GfxBlend BlendOf(string frame, LastEdict.Data.Gfx.GfxAssets gfx)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(gfx);
        if (frame.Length > 1 && frame[0] == Add) return LastEdict.Data.Gfx.GfxBlend.Add;
        if (Layers(frame) is not null) return LastEdict.Data.Gfx.GfxBlend.Opaque;   // слои уже смешаны при сборке
        if (frame.Length > 1 && frame[^1] == Mirror) frame = frame[..^1];
        return Parse(frame) is { } r && r.Tint is null && r.Mask is null ? gfx.BlendOf(r.Archive, r.Name, anim: false) : LastEdict.Data.Gfx.GfxBlend.Opaque;
    }

    /// <summary>Подкраска точки: половина своего цвета плюс половина цвета подкраски.</summary>
    public static byte Blend(byte own, byte tint) => (byte)((own + tint) / 2);
}
