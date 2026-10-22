using LastEdict.Data.Ui;

namespace LastEdict.Data.Tests;

/// <summary>Перенос строк правилом движка (<see cref="UiTextWrap"/>, <c>0x529b94</c>/<c>0x52a1ba</c>): куски «слово + пробелы», строгое
/// «сумма &lt; ширины», пробелы за словом считаются, слово длиннее места не рвётся.</summary>
public class UiTextWrapTests
{
    /// <summary>Моноширинный замер: знак — 10 точек.</summary>
    private static float Mono(string s) => s.Length * 10;

    [Fact]
    public void Chunks_WordWithTrailingSpaces_LeadingSpacesApart()
    {
        Assert.Equal(["  ", "ab  ", "c ", "d"], UiTextWrap.Chunks("  ab  c d"));
        Assert.Equal(["word"], UiTextWrap.Chunks("word"));
        Assert.Empty(UiTextWrap.Chunks(""));
    }

    [Fact]
    public void LongWord_KeepsWholeOnItsOwnLine()
    {
        // Место 50 точек, слово 12 знаков (120): у Godot WordSmart — по буквам, у движка — целиком одной строкой (0x52a2f0).
        Assert.Equal(["ab ", "abcdefghijkl ", "cd"], UiTextWrap.Lines("ab abcdefghijkl cd", 50, Mono));
    }

    [Fact]
    public void TrailingSpaceCounts_AndSumMustBeStrictlyLess()
    {
        // «abcd » — 50 из 100, «efgh» — ещё 40: 90 < 100 — в строку. «abcd » + «efghi» = 100 — не меньше 100: перенос.
        Assert.Equal(["abcd efgh"], UiTextWrap.Lines("abcd efgh", 100, Mono));
        Assert.Equal(["abcd ", "efghi"], UiTextWrap.Lines("abcd efghi", 100, Mono));
        // Кусок ровно в ширину места — сам по себе на строке (сумма не меньше ширины, 0x52a2a5).
        Assert.Equal(["abcde", "f"], UiTextWrap.Lines("abcde\nf", 50, Mono));
    }

    [Fact]
    public void NewLines_AreMandatory_EmptyKept()
    {
        Assert.Equal(["ab", "", "cd"], UiTextWrap.Lines("ab\n\ncd", 100, Mono));
        Assert.Equal("ab\ncd", UiTextWrap.Wrap("ab\r\ncd", 100, Mono));
    }
}
