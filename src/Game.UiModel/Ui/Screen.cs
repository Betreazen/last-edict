using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace D2.Data.Ui;

/// <summary>Вид элемента экрана. Свой перечень, а не коды `.dlg`: старые имена (<c>TLBOX</c>, <c>SPIN</c>)
/// ничего не говорят, а <c>RADIO</c> в игре вообще не элемент, а группа переключателей.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiKind { Image, Text, Button, Toggle, List, TextList, Edit, Spin, RadioGroup }

/// <summary>К какому краю экрана привязан элемент. Игра рисовала всё в жёсткой сетке 800×600 и за неё не выходила;
/// порт этого ограничения не наследует — экран задаёт свой <see cref="UiScreen.Design"/>, а элементы держатся
/// краёв при другом размере окна.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

/// <summary>Прямоугольник в точках: левый верхний угол и размер (в `.dlg` были две противоположные точки).</summary>
public readonly record struct UiRect(int X, int Y, int Width, int Height)
{
    public static UiRect FromCorners(int x1, int y1, int x2, int y2) => new(x1, y1, x2 - x1, y2 - y1);

    // Считаются из полей — в JSON им делать нечего: файл правят руками, и лишние поля пришлось бы держать
    // согласованными вручную.
    [JsonIgnore] public int Right => X + Width;
    [JsonIgnore] public int Bottom => Y + Height;
}

/// <summary>Кадры кнопки или переключателя по состоянию. Пустая строка — кадра нет.</summary>
public sealed record UiSprites(string Normal = "", string Hover = "", string Pressed = "", string Disabled = "");

/// <summary>Устройство списка: ячейки и кнопки, которыми он управляется (ссылки — <c>Id</c> кнопок того же экрана).
/// Столбцов и строк столько, сколько ячеек с зазорами помещается в прямоугольник элемента. У текстового списка
/// ячейки в разметке нет (<see cref="CellHeight"/> = 0): высота строки берётся от шрифта, ширина — во весь список.
/// <see cref="Up"/>/<see cref="Down"/> шагают на строку, <see cref="Left"/>/<see cref="Right"/> — на пункт,
/// <see cref="PageUp"/>/<see cref="PageDown"/> — на видимую страницу; <see cref="Confirm"/> подтверждает выбор.
/// <see cref="Selection"/> — кадр рамки выделения (списки картинок), <see cref="SelectedStyle"/> и
/// <see cref="Style"/> — оформление выделенной и обычной строки (текстовые списки).</summary>
public sealed record UiList
{
    public int CellWidth { get; init; }
    public int CellHeight { get; init; }
    public int GapX { get; init; }
    public int GapY { get; init; }
    public string? Up { get; init; }
    public string? Down { get; init; }
    public string? Left { get; init; }
    public string? Right { get; init; }
    public string? PageUp { get; init; }
    public string? PageDown { get; init; }
    public string? Confirm { get; init; }
    public string? Selection { get; init; }
    public string? SelectedStyle { get; init; }
    public string? Style { get; init; }
}

/// <summary>Кнопки счётчика: следующее и предыдущее значение (<c>Id</c> кнопок того же экрана).</summary>
public sealed record UiSpin(string? Increment, string? Decrement);

/// <summary>Элемент экрана. <see cref="Id"/> — имя, по которому на элемент ссылаются логика и мод;
/// <see cref="TextId"/> — строка локализации (<c>X005TA0020</c>), <see cref="Members"/> — состав группы
/// переключателей, <see cref="Legacy"/> — исходные поля `.dlg` (нужны, только пока экраны переносятся;
/// в собственных экранах их нет).</summary>
public sealed record UiElement
{
    public required string Id { get; init; }
    public required UiKind Kind { get; init; }
    public UiRect Rect { get; init; }
    public UiAnchor Anchor { get; init; } = UiAnchor.TopLeft;
    public UiSprites? Sprites { get; init; }

    /// <summary>Кадры переключателя во включённом состоянии. Отключённого кадра у включённого состояния в старой
    /// разметке нет, поэтому <see cref="UiSprites.Disabled"/> здесь всегда пуст.</summary>
    public UiSprites? Checked { get; init; }

    public string? TextId { get; init; }
    public string? Image { get; init; }

    /// <summary>Строка оформления старого движка как есть: <c>\hC;\vC;</c> — выравнивание по центру,
    /// <c>\fLarge;</c> — шрифт, <c>\c000;000;000;</c> — цвет. Не разобрана на части: порт разберёт её, когда
    /// у него появится своя типографика. <c>ponytail:</c> разобрать при переносе первого экрана.</summary>
    public string? Style { get; init; }

    /// <summary>Виртуальные коды клавиш Windows, которыми жмётся элемент (<c>13</c> — Enter, <c>27</c> — Esc,
    /// <c>38</c>/<c>40</c> — стрелки). Смысл кодов подтверждён по смыслу кнопок (<c>BTN_OK</c> = 13,
    /// <c>BTN_BACK</c> = 27), но не дизассемблированием.</summary>
    public IReadOnlyList<int> Hotkeys { get; init; } = [];

    /// <summary>Устройство списка — у <see cref="UiKind.List"/> и <see cref="UiKind.TextList"/>.</summary>
    public UiList? List { get; init; }

    /// <summary>Кнопки счётчика — у <see cref="UiKind.Spin"/>.</summary>
    public UiSpin? Spin { get; init; }

    public IReadOnlyList<string> Members { get; init; } = [];
    public IReadOnlyList<string> Legacy { get; init; } = [];

    /// <summary>Картинка рисуется поверх роликов экрана (рамка ролика вступления <c>_CINEMATIC_OVER</c>: <c>CAnimInterf</c> рисует её после
    /// кадра, <c>0x4c6618</c>); прочие элементы — под роликами. Не задано — под роликами (в файле не пишется).</summary>
    public bool? OverVideo { get; init; }

    /// <summary>Кнопка с автоповтором: поле `.dlg` сразу за подсказкой (<c>"X100TA0027",1,…</c>, разбор строки <c>0x512153</c>,
    /// создание кнопки <c>0x5126c5</c> → <c>setAutorepeatTimeout(250)</c>) — у 20 кнопок: стрелки списков, листание книги, стрелки
    /// передачи ресурса и предмета. Лежит первым полем <see cref="Legacy"/>.</summary>
    [JsonIgnore] public bool Autorepeat => Kind == UiKind.Button && Legacy is ["1", ..];

    /// <summary>Строка подсказки при наведении (<c>CTooltipImpl</c>): у кнопок, переключателей, картинок и счётчиков <see cref="TextId"/> —
    /// это она (поле `.dlg` после картинок, <c>0x50dd24</c>); у подписей <see cref="TextId"/> — сам текст, подсказки нет.
    /// Строка, которая не разбирается как id строки (<see cref="IsTextId"/>), подсказки не даёт: движок переводит её в id
    /// (<c>0x5c8670</c> → <c>CMidgardID::fromString</c> <c>0x57ef4f</c>), неразобранная становится пустым id (<c>0x3f0000</c>), и
    /// <c>0x5c868e</c> подсказку не ставит вовсе. Так у <c>IMG_MAIN</c> вида карты (<c>"255"</c>) и у имён кадров в поле подсказки боевого
    /// экрана (<c>DLG_BATTLE_A_*</c>).</summary>
    [JsonIgnore]
    public string? Tooltip => Kind is UiKind.Button or UiKind.Toggle or UiKind.Image or UiKind.Spin && TextId is { } id && IsTextId(id) ? id : null;

    /// <summary>Строка разбирается как id (<c>CMidgardID::fromString</c> <c>0x57ef4f</c>): десять знаков — категория из <c>"GCSX"</c>
    /// (таблица <c>0x6e7260</c>, без учёта регистра), три десятичные цифры (номер 0…255, <c>0x57f0d8</c>), два знака типа
    /// (<c>0x57f11f</c>) и четыре шестнадцатеричные (<c>0x57f1a7</c>). Тип движок сверяет со своей таблицей, здесь — что это буквы.</summary>
    public static bool IsTextId(string id) =>
        id.Length == 10 && "GCSX".Contains(char.ToUpperInvariant(id[0]), StringComparison.Ordinal)
        && id[1..4].All(char.IsAsciiDigit) && id[4..6].All(char.IsAsciiLetter) && id[6..].All(char.IsAsciiHexDigit);
}

/// <summary>Экран порта: имя, размер, под который нарисованы элементы, фон, курсор и элементы.
/// Экраны живут данными (JSON), а не кодом: их можно править, добавлять свои и раскладывать по модам.</summary>
public sealed record UiScreen
{
    public required string Id { get; init; }
    public UiRect Design { get; init; } = new(0, 0, 800, 600);
    public string Background { get; init; } = "";
    public string Cursor { get; init; } = "";
    public IReadOnlyList<UiElement> Elements { get; init; } = [];
    public IReadOnlyList<string> Legacy { get; init; } = [];
}

/// <summary>Набор экранов: чтение и запись JSON. Моды кладут рядом свои файлы, <see cref="Merge"/> накладывает их
/// поверх — экран с тем же <c>Id</c> заменяет прежний целиком.</summary>
public static class UiScreens
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmptyLists } },
    };

    /// <summary>Пустые списки (<c>Hotkeys</c>, <c>Members</c>, <c>Legacy</c>, пустой <c>Elements</c>)
    /// не пишутся: у большинства элементов
    /// они пусты, и без них файл вдвое короче и читается глазами. При чтении отсутствующий список даёт пустой.
    /// Своё <c>ShouldSerialize</c> отменяет <see cref="JsonIgnoreCondition.WhenWritingNull"/>, поэтому пустое
    /// значение проверяется здесь же.</summary>
    private static void SkipEmptyLists(JsonTypeInfo type)
    {
        foreach (var p in type.Properties)
            p.ShouldSerialize = (_, value) =>
                value is not null && (value is not System.Collections.ICollection list || list.Count > 0);
    }

    public static string ToJson(IEnumerable<UiScreen> screens) => JsonSerializer.Serialize(screens, Json);

    public static IReadOnlyList<UiScreen> FromJson(string json) =>
        JsonSerializer.Deserialize<List<UiScreen>>(json, Json) ?? throw new FormatException("пустой список экранов");

    public static IReadOnlyList<UiScreen> Load(string path) => FromJson(File.ReadAllText(path));

    public static void Save(string path, IEnumerable<UiScreen> screens) => File.WriteAllText(path, ToJson(screens));

    /// <summary>Наложить набор поверх базового: одинаковый <c>Id</c> — берётся из накладываемого, новые добавляются
    /// в конец. Порядок базовых экранов сохраняется.</summary>
    public static IReadOnlyList<UiScreen> Merge(IEnumerable<UiScreen> baseSet, IEnumerable<UiScreen> overlay)
    {
        ArgumentNullException.ThrowIfNull(baseSet);
        ArgumentNullException.ThrowIfNull(overlay);
        var over = overlay.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var result = baseSet.Select(s => over.Remove(s.Id, out var replaced) ? replaced : s).ToList();
        result.AddRange(over.Values);
        return result;
    }
}
