using LastEdict.Data.Gfx;

namespace LastEdict.Data.Tests;

/// <summary>Картинки литья заклинания на карте (<see cref="SpellMapAnim"/>): выбор по префиксу id, слой по знакам 10–11, ключевой
/// кадр по знакам 12–14, длина — наибольшее число кадров.</summary>
public class SpellMapAnimTests
{
    private static GfxAssets Gfx() => GfxAssets.Load(ImportedData.PathOf("gfx"));

    [SkippableFact]
    public void Parts_ByPrefix_LayerAndKeyFrame()
    {
        var gfx = Gfx();
        var one = SpellMapAnim.Parts(gfx, "G000SS0001");
        Assert.Equal("G000SS0001O1000", Assert.Single(one).Anim.Name);
        Assert.Equal((1031, true, 0), (one[0].Layer, one[0].Front, one[0].KeyFrame));

        var eight = SpellMapAnim.Parts(gfx, "g000ss0008");   // регистр id не значим
        Assert.Equal(["G000SS0008O1039", "G000SS0008U1039"], eight.Select(p => p.Anim.Name));
        Assert.Equal([(1031, true, 39), (338, false, 39)], eight.Select(p => (p.Layer, p.Front, p.KeyFrame)));
        Assert.Equal(46, SpellMapAnim.Length(eight));

        Assert.Empty(SpellMapAnim.Parts(gfx, "G000SS9999"));
        Assert.Empty(SpellMapAnim.Parts(gfx, ""));
    }

    /// <summary>У каждого заклинания оригинала (<c>G000SS0001</c>…<c>0120</c>) анимация есть и имён не больше четырёх.</summary>
    [SkippableFact]
    public void EverySpell_HasOneToFourParts()
    {
        var gfx = Gfx();
        var names = gfx.Archive(SpellMapAnim.Archive)!.Anims.Select(a => a.Name[..10].ToUpperInvariant()).Distinct().ToList();
        Assert.All(names, id => Assert.InRange(SpellMapAnim.Parts(gfx, id).Count, 1, 4));
    }
}
