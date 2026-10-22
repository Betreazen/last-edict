using System.Collections.Concurrent;
using LastEdict.Data.Content;

namespace LastEdict.Data.Tests;

/// <summary>Содержимое игры без модов (<see cref="GameContent.Load"/>) — одно на каталог данных за процесс тестов. Тесты окон, ИИ и
/// харнессы брали его заново на каждый тест (сотни разборов описи графики, 16.10 — ~11 % процессорного времени LastEdict.Data.Tests), а
/// содержимое неизменяемое: таблицы — только для чтения, строки языка — под замком, прочие ленивые указатели (шрифты, картинки и
/// ролики по имени) в гонке строятся дважды одинаково. Проверки самой загрузки и слоёв модов
/// (<c>GameContentTests</c>, синтетические карты с модами) по-прежнему зовут <see cref="GameContent.Load"/>.</summary>
internal static class SharedContent
{
    private static readonly ConcurrentDictionary<string, Lazy<GameContent>> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Без данных игры (публичное дерево до импорта) тест пропускается до разбора — <see cref="ImportedData"/>.</summary>
    public static GameContent Load(string dataDir)
    {
        ImportedData.RequireIn(dataDir, ImportedData.ContentFiles);
        return Loaded.GetOrAdd(Path.GetFullPath(dataDir), dir => new Lazy<GameContent>(() => GameContent.Load(dir))).Value;
    }
}
