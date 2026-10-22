using LastEdict.Data.Dbf;

namespace LastEdict.Data.Ui;

/// <summary>Однократный импорт строк игры в данные порта. В рантайме порт читает только
/// <c>data/ui/strings.&lt;язык&gt;.json</c>; dbf нужен здесь и больше нигде (правило проекта:
/// старые форматы — только на входе).
///
/// <para>Читается тем же <see cref="DbfTable"/>, что и остальные таблицы игры: он уже держит cp866
/// (<see cref="DbfTable.GameEncoding"/>), в которой записаны и английский, и русский текст.</para>
///
/// <para>Языки собираются слоями: русский — это английская игра плюс слой русификации
/// (<c>disciples-vanilla/l10n/russobit</c>), в котором те же 3284 идентификатора. Слои накладываются
/// по порядку, последний выигрывает, так что непереведённая строка останется английской, а не пустой.</para></summary>
public static class UiStringsImport
{
    /// <summary>Таблицы со строками интерфейса, в порядке наложения. <c>Tglobal</c> — тексты игры (расы,
    /// постройки, заклинания, подсказки), <c>TApp</c> — тексты приложения, на которые ссылаются экраны.
    /// Идентификаторы двух таблиц не пересекаются (проверено пересчётом: 2292 + 992 = 3284 без совпадений).
    ///
    /// <para><c>Interf/TAppEdit.dbf</c> сюда не входит: это строки редактора сценариев, отдельной программы,
    /// которую порт не переносит. Восемь идентификаторов экранов, которых нет в <c>TApp</c>, в нём тоже
    /// отсутствуют — так что ничего не теряется.</para></summary>
    public static readonly IReadOnlyList<string> Tables = ["Globals/Tglobal.dbf", "Interf/TApp.DBF"];

    /// <summary>Столбцы таблиц: идентификатор и текст. <c>VERIFIED</c> и <c>CONTEXT</c> — пометки
    /// переводчиков, в порт не переносятся.</summary>
    private const string IdColumn = "TXT_ID", TextColumn = "TEXT";

    /// <summary>Строки из одного корня игры. Корень — папка с <c>Globals</c> и <c>Interf</c> либо слой
    /// русификации такого же устройства; таблицы, которых в корне нет, пропускаются.</summary>
    public static IReadOnlyDictionary<string, string> Read(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string table in Tables)
        {
            string path = Path.Combine(root, table.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            foreach (var row in DbfTable.Read(path).Rows)
            {
                string id = row.GetString(IdColumn).Trim().ToUpperInvariant();
                if (id.Length > 0) result[id] = row.GetString(TextColumn);
            }
        }
        return result;
    }

    /// <summary>Слои по порядку: каждый следующий перекрывает предыдущий по идентификатору.</summary>
    public static IReadOnlyDictionary<string, string> Read(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        IReadOnlyDictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots) result = UiStrings.Merge(result, Read(root));
        return result;
    }

    /// <summary>Есть ли в корне хоть одна таблица строк.</summary>
    public static bool IsAvailable(string root) =>
        Tables.Any(t => File.Exists(Path.Combine(root, t.Replace('/', Path.DirectorySeparatorChar))));

    /// <summary>Откуда берётся язык по умолчанию. Английский лежит в самой игре
    /// (<see cref="GamePaths.GameDir"/>); русский — это игра плюс слой русификации Руссобит-М из эталонной
    /// копии (<c>disciples-vanilla/l10n/russobit</c>): в нём те же 3284 идентификатора, и он кладётся
    /// поверх, а не вместо. Для остальных языков корни задаются вручную.</summary>
    public static IReadOnlyList<string> DefaultRoots(string language)
    {
        var roots = new List<string> { GamePaths.GameDir };
        if (language.Equals("ru", StringComparison.OrdinalIgnoreCase))
            roots.Add(Path.GetFullPath(Path.Combine(GamePaths.GameDir, "..", "..",
                                                    "disciples-vanilla", "l10n", "russobit")));
        return roots;
    }
}
