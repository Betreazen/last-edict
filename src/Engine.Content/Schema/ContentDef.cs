using System.ComponentModel;
using System.Text.Json.Serialization;

namespace D2.Data.Content.Schema;

/// <summary>Сущность содержимого (юнит, атака, постройка…): устойчивый id с пространством имён мода (<c>base:unit/squire</c>,
/// <c>mymod:unit/halberdier</c>) и, у сущностей оригинала, его id (<c>g000uu0001</c>) — по нему их находят сохранения, карты
/// и журналы. Запись C# — единственный источник формата: JSON Schema для редакторов и Студии выводится из неё
/// (<see cref="ContentFiles.Schema"/>), импорт таблиц оригинала — из атрибутов <see cref="LegacyAttribute"/> её свойств.</summary>
public abstract record ContentDef
{
    [JsonPropertyOrder(-10)]
    [Description("Устойчивый id «мод:вид/имя» — по нему ссылаются другие сущности и моды.")]
    public required string Id { get; init; }

    [JsonPropertyOrder(-9)]
    [Description("Id оригинала (Gunits.UNIT_ID и т. п.): по нему сущность находят сохранения, карты и журналы. У новых сущностей модов — нет.")]
    public string? LegacyId { get; init; }

    /// <summary>Ключ сущности в правилах: id оригинала, а у новой сущности мода — её id. Правила и сохранения пока живут на id
    /// оригинала (<c>docs/content-schema.md</c>, §2).</summary>
    [JsonIgnore]
    public string Key => LegacyId ?? Id;
}

/// <summary>Таблица оригинала, из строк которой импортируется сущность, и столбец её id.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LegacyTableAttribute(string table, string key) : Attribute
{
    public string Table { get; } = table;
    public string Key { get; } = key;
}

/// <summary>Столбцы таблицы оригинала, из которых берётся свойство: один — значение, несколько — список значений по порядку
/// (пустые ссылки отбрасываются). Свойство-группа без атрибута, но с вложенной записью, читается из той же строки.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LegacyAttribute(params string[] columns) : Attribute
{
    public IReadOnlyList<string> Columns { get; } = columns;

    /// <summary>Столбца может не быть в таблице (его добавляют моды оригинала, как <c>GmodifL.IMMUNITYC</c>) — тогда «не задано». Прочие
    /// столбцы обязательны: прежние загрузчики на отсутствующем столбце падали, импорт — тоже, с именем таблицы и строки.</summary>
    public bool Optional { get; init; }
}

/// <summary>Свойство — ссылка (или список ссылок) на сущность вида <paramref name="kind"/>: в таблице — id оригинала, в содержимом —
/// id сущности.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RefAttribute(string kind) : Attribute
{
    public string Kind { get; } = kind;
}

/// <summary>Свойство — группа полей той же строки таблицы (вложенная запись с атрибутами <see cref="LegacyAttribute"/>).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class LegacyGroupAttribute : Attribute;
