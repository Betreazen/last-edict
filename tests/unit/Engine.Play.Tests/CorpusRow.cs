using System.Reflection;
using Xunit.Sdk;

namespace D2.Data.Tests;

/// <summary>Строка харнесса по корпусу — как <c>InlineData</c>, первый аргумент — имя каталога корпуса. Класс-наследник с
/// <see cref="OnlyCorpusAttribute"/> получает только свою строку: xUnit гоняет параллельно классы, а не строки одной теории, и
/// три партии большого харнесса подряд держали весь прогон (28.09: <c>Candidates_MatchJournal</c> — 83 + 58 + 39 с). Числа
/// остаются в одном месте (<c>tools/repin_harness.py</c> их находит так же, как <c>InlineData</c>).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CorpusRowAttribute(params object?[] data) : DataAttribute
{
    public override IEnumerable<object?[]> GetData(MethodInfo testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        var only = testMethod.ReflectedType?.GetCustomAttribute<OnlyCorpusAttribute>();
        if (only is null) yield return data;
        else if (data.Length > 0 && Equals(data[0], only.Corpus) && (only.Stem is null || data.Contains(only.Stem))) yield return data;
    }
}

/// <summary>Класс харнесса гоняет только строки <see cref="CorpusRowAttribute"/> этого корпуса; с <see cref="Stem"/> — только строки, где
/// среди аргументов есть это значение (в одном журнале несколько карт — у каждой свой класс).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class OnlyCorpusAttribute(string corpus) : Attribute
{
    public string Corpus { get; } = corpus;

    public string? Stem { get; init; }
}
