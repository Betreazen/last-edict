using LastEdict.Rules.Map;

namespace LastEdict.Rules.Tests;

/// <summary>Порядок изученных заклинаний после чтения (<see cref="KnownSpellOrder"/>, <c>0x5fe926</c> → <c>list::sort</c> <c>0x44b3df</c>):
/// по числу <c>CMidgardID</c>, как в сохранении движка t01 карты 19 при неупорядоченном сценарии.</summary>
public class KnownSpellOrderTests
{
    [Fact]
    public void Sort_Map19Undead_AsEngineSave() =>
        Assert.Equal(["G000SS0061", "G000SS0062", "G000SS0063", "G000SS0064", "G000SS0066", "G000SS0067"],
                     KnownSpellOrder.Sort(["G000SS0067", "G000SS0063", "G000SS0061", "G000SS0062", "G000SS0066", "G000SS0064"], id => id));

    [Fact]
    public void Sort_CategoryBeforeLetters_HexNumber_UnreadableLast() =>
        // Категория — число (G 0, C 1, S 2, E 3), а не буква; номер — шестнадцатеричный; нечитаемое — в конце.
        Assert.Equal(["g000ss000a", "G000SS0010", "C000SS0001", "S143SS0001", "E000SS0001", "bad"],
                     KnownSpellOrder.Sort(["bad", "E000SS0001", "S143SS0001", "G000SS0010", "C000SS0001", "g000ss000a"], id => id));

    /// <summary>Ключи — id содержимого: порядок — по id оригинала заклинания, новое заклинание мода (без него) — после, по id.</summary>
    [Fact]
    public void Sort_ContentIds_ByLegacyId_ModSpellsLast() =>
        Assert.Equal(["base:spell/b", "base:spell/a", "mod:spell/c"],
                     KnownSpellOrder.Sort(["mod:spell/c", "base:spell/a", "base:spell/b"],
                                          id => id switch { "base:spell/a" => "G000SS0002", "base:spell/b" => "G000SS0001", _ => null }));
}
