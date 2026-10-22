namespace Kaitai;

/// <summary>Кодировка строк <c>.sg</c> (cp1251) для сгенерированного разборщика <c>Sg.g.cs</c>: один экземпляр на процесс.
/// Kaitai пишет <c>Encoding.GetEncoding("windows-1251")</c> на каждое поле, а поиск по имени у поставщика кодовых страниц идёт под
/// <c>ReaderWriterLockSlim</c>: при разборе сохранений в несколько потоков 97 % времени разбора уходило в ожидание этого замка
/// (профиль LastEdict.Rules.Tests 16.10). Подстановку делает <c>tools/ksy/compile.js</c> при каждой генерации.</summary>
internal static class Cp1251
{
    public static readonly System.Text.Encoding Encoding = Load();

    private static System.Text.Encoding Load()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding("windows-1251");
    }
}
