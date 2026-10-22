using System.Text.Json;
using Xunit.Abstractions;

namespace LastEdict.Tests;

/// <summary>
/// Порядок запуска классов тестов (у xUnit класс — своя коллекция, классы идут параллельно, тесты класса — подряд): долгие по прошлым
/// прогонам — первыми, как в планировании «самая длинная работа вперёд». По умолчанию долгий класс мог начаться под конец прогона и
/// идти один, пока остальные ядра простаивают (<c>docs/perf-log.md</c>, «Скорость проверок, Э1»). Время классов — <c>tests/test-durations.json</c>
/// (большее из окна класса в полном прогоне и суммы его тестов, пишет <c>python tools/test_times.py &lt;trx&gt; --durations</c>); класса нет в файле — новый, его время
/// неизвестно: идёт первым. Порядок не меняет исходов — только когда класс стартует. Подключается в проект тестов ссылкой на файл и
/// атрибутом сборки <c>TestCollectionOrderer</c> (в .csproj).
/// </summary>
public sealed class LongestFirst : ITestCollectionOrderer
{
    private const string Prefix = "Test collection for ";

    private static readonly Lazy<IReadOnlyDictionary<string, double>> Seconds = new(Load);

    public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> testCollections) =>
        Environment.GetEnvironmentVariable("D2_TEST_ORDER") == "default" ? testCollections   // для замера «до/после»
        : testCollections.OrderByDescending(c => Seconds.Value.TryGetValue(ClassOf(c), out double s) ? s : double.MaxValue)
                       .ThenBy(c => c.DisplayName, StringComparer.Ordinal);

    /// <summary>Короткое имя класса коллекции «по классу» (<c>Test collection for LastEdict.Rules.Tests.FortHarnessTests</c>).</summary>
    private static string ClassOf(ITestCollection collection)
    {
        string name = collection.DisplayName.StartsWith(Prefix, StringComparison.Ordinal) ? collection.DisplayName[Prefix.Length..]
            : collection.DisplayName;
        return name[(name.LastIndexOf('.') + 1)..];
    }

    private static IReadOnlyDictionary<string, double> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "test-durations.json");
        if (!File.Exists(path)) return new Dictionary<string, double>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var own = doc.RootElement.TryGetProperty(typeof(LongestFirst).Assembly.GetName().Name!, out var classes) ? classes : default;
        return own.ValueKind == JsonValueKind.Object
            ? own.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.Ordinal)
            : new Dictionary<string, double>();
    }
}
