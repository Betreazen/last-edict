using System.Text;
using LastEdict.Authoring.Documents;
using LastEdict.Data.Content;

namespace LastEdict.Authoring.Workspaces;

/// <summary>
/// Имя новой сущности мода (каталог и часть id: строчная латиница, цифры, «-») из названия, которое автор пишет по-русски: транслитерация
/// («Башня мага» → <c>bashnya-maga</c>), прочие знаки — дефис; занятое имя — с номером. Автор может поправить имя в форме.
/// </summary>
public static class EntityNames
{
    private static readonly Dictionary<char, string> Cyrillic = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>Имя по названию; пустое название — <paramref name="fallback"/>.</summary>
    public static string FromTitle(string title, string fallback = "new")
    {
        ArgumentNullException.ThrowIfNull(title);
        var name = new StringBuilder();
        foreach (char c in title.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) name.Append(c);
            else if (Cyrillic.TryGetValue(c, out var latin)) name.Append(latin);
            else if (name.Length > 0 && name[^1] != '-') name.Append('-');
        }
        string result = name.ToString().Trim('-');
        return result.Length == 0 ? fallback : result.Length > 40 ? result[..40].TrimEnd('-') : result;
    }

    /// <summary>Свободное имя вида <paramref name="kind"/> в моде: занято (в документе или в содержимом) — с номером <c>-2</c>, <c>-3</c>…</summary>
    public static string Free(ModDocument doc, ContentSet? content, string kind, string name)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var contentKind = ContentSet.KindNamed(kind);
        bool Taken(string n)
        {
            string id = EntityFolders.IdOf(doc.ModId, contentKind, n);
            return doc.Find(id) is not null || content?.Find(id) is not null;
        }
        if (!Taken(name)) return name;
        for (int i = 2; ; i++)
            if (!Taken($"{name}-{i}")) return $"{name}-{i}";
    }
}
