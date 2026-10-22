using D2.Rules.Map;

namespace D2.Rules.Tests;

/// <summary>Порядок изученных заклинаний после чтения (<see cref="KnownSpellOrder"/>, <c>0x5fe926</c> → <c>list::sort</c> <c>0x44b3df</c>):
/// по числу <c>CMidgardID</c>, как в сохранении движка t01 карты 19 при неупорядоченном сценарии.</summary>
public class KnownSpellOrderTests
{
    [Fact]
    public void Sort_Map19Undead_AsEngineSave() =>
        Assert.Equal(["G000SS0061", "G000SS0062", "G000SS0063", "G000SS0064", "G000SS0066", "G000SS0067"],
                     KnownSpellOrder.Sort(["G000SS0067", "G000SS0063", "G000SS0061", "G000SS0062", "G000SS0066", "G000SS0064"]));

    [Fact]
    public void Sort_CategoryBeforeLetters_HexNumber_UnreadableLast() =>
        // Категория — число (G 0, C 1, S 2, E 3), а не буква; номер — шестнадцатеричный; нечитаемое — в конце.
        Assert.Equal(["g000ss000a", "G000SS0010", "C000SS0001", "S143SS0001", "E000SS0001", "bad"],
                     KnownSpellOrder.Sort(["bad", "E000SS0001", "S143SS0001", "G000SS0010", "C000SS0001", "g000ss000a"]));
}
