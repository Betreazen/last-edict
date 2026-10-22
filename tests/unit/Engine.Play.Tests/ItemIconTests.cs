using D2.Data.Play;
using D2.Rules.Magic;

namespace D2.Data.Tests;

/// <summary>Значок предмета в <c>IconItem</c> (<see cref="GameBook.ItemIcon"/>, <c>0x5c5d89</c>): у свитков своих значков нет — значок расы
/// заклинания (раса первого лорда с заклинанием в <c>GspellR</c>, <c>0x5c5f4f</c>). Без этого ячейка свитка показывала одно число
/// (прогон 3 пользователя 11.10, снимок 39 — «призрак» в сумке после переноса предмета).</summary>
public class ItemIconTests
{
    private static readonly Lazy<GameBook> Book = new(() => GameBook.Load(ImportedData.PathOf("rules")));

    [SkippableTheory]
    [InlineData("G000IG5001", "SCROLLHU")]   // g000ss0001 — лорды Империи (RACE_TYPE 0, L_HUMAN)
    [InlineData("G000IG5021", "SCROLLDW")]   // g000ss0021 — g000rr0001 (RACE_TYPE 3, L_DWARF)
    [InlineData("G000IG5041", "SCROLLHE")]   // g000ss0041 — g000rr0002 (RACE_TYPE 2, L_HERETIC)
    [InlineData("G000IG5061", "SCROLLUN")]   // g000ss0061 — g000rr0003 (RACE_TYPE 1, L_UNDEAD)
    [InlineData("G000IG5097", "SCROLLEL")]   // g000ss0097 — g000rr0005 (RACE_TYPE 5, L_ELF)
    [InlineData("g000ig0006", "G000IG0006")] // не свиток — свой id
    public void Scroll_TakesRaceIconOfItsSpell(string item, string icon) => Assert.Equal(icon, Book.Value.ItemIcon(item));

    [SkippableFact]
    public void EveryScroll_HasOneOfFiveRaceIcons()
    {
        var scrolls = Book.Value.Items.Values.Where(i => i.Category == ItemCategory.Scroll).ToList();
        Assert.Equal(120, scrolls.Count);
        var icons = scrolls.Select(s => Book.Value.ItemIcon(s.Id)).ToHashSet();
        Assert.Equal(["SCROLLDW", "SCROLLEL", "SCROLLHE", "SCROLLHU", "SCROLLUN"], icons.Order());
    }
}
