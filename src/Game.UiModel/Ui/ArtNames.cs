using LastEdict.Data.Content;

namespace LastEdict.Data.Ui;

/// <summary>Имя графики и звука импорта по ключу сущности содержимого. Архивы оригинала называют кадры и звуки id оригинала заглавными
/// (<c>G000UU0019FACE</c>, <c>IconSpel:G000SS0001</c>, звуки <c>UnitSounds</c>), а состояние партии и правила живут на id содержимого
/// (<c>base:unit/pegasus-knight</c>): вид берёт имя здесь. У новой сущности мода — её ключ заглавными (своей графики у неё в архивах нет).</summary>
public static class ArtNames
{
    public static string Of(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return LegacyIds.Base.ArtOf(key).ToUpperInvariant();
    }
}
