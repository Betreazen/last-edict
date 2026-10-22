using System.Text;

namespace LastEdict.Data.Ui;

/// <summary>Перевод разметки старого движка (<c>Interf/Interf.dlg</c>, <c>Interf/CustomLobby.dlg</c>) в экраны
/// порта (<see cref="UiScreen"/>). Формат исходника: текст cp1251, строки
/// <c>DIALOG имя,x1,y1,x2,y2,фон,курсор,…</c>, затем <c>BEGIN</c>, элементы и <c>END</c>; вид отделён табуляцией,
/// поля — запятыми, строки в кавычках. У <c>RADIO</c> прямоугольника нет вовсе: это группа переключателей,
/// перечисленных по именам.
/// <para><b>Что перевод добавляет сверх исходника.</b> Экраны порта — своя система, а не копия старой разметки,
/// поэтому перевод не просто перекладывает поля:</para>
/// <list type="bullet">
/// <item><description><b>Id экрана</b> — имя из <c>DIALOG</c>. Имена в обоих файлах различны (132 из 132), поэтому
/// имя и есть устойчивый ключ: по нему мод накладывает свой экран (<see cref="UiScreens.Merge"/>). Переименовать
/// экран — значит порвать все моды, так что имена не трогаются даже там, где они некрасивы.</description></item>
/// <item><description><b><see cref="UiElement.Anchor"/></b> — считается из места элемента в экране, см.
/// <see cref="Anchor"/>. В исходнике привязки нет: игра рисовала всё в жёсткой сетке и окно не меняла.</description></item>
/// <item><description><b>Поля по видам элементов</b> разобраны так, как их читает движок, а не по общей схеме:
/// у переключателя четыре кадра выключенного состояния и три включённого, подсказка идёт после них; у списков
/// подсказки нет вовсе. См. таблицу в <see cref="Element"/>.</description></item>
/// <item><description><b><see cref="UiElement.Legacy"/></b> остаётся только там, где назначение поля неизвестно:
/// флаг кнопки, первые поля текстового списка, хвосты нулей. Всё, что разобрано, в <c>Legacy</c> не
/// дублируется; пустые поля в конце отбрасываются.</description></item>
/// </list>
/// <para><b>Насколько это надёжно.</b> Прямоугольники, кадры, строки локализации и состав групп — прочитаны прямо
/// из файла, сомнений нет. Разбивка кадров переключателя 4+3 и смысл кодов клавиш выведены из образцов
/// (<c>BTN_OK</c> = 13 = Enter, <c>BTN_BACK</c> = 27 = Esc, у <c>TOG_*</c> включённые кадры кончаются на
/// <c>_P</c>) — правдоподобно, но дизассемблированием не проверено; так же выведена раскладка полей списков
/// (см. <see cref="Element"/>). Флаг кнопки не разбирался: лежит в <c>Legacy</c>.
/// <c>ponytail:</c> разбирать <c>Legacy</c> по мере переноса экранов.</para></summary>
public static class DlgImport
{
    private static readonly Dictionary<string, UiKind> Kinds = new(StringComparer.Ordinal)
    {
        ["IMAGE"] = UiKind.Image, ["TEXT"] = UiKind.Text, ["BUTTON"] = UiKind.Button, ["TOGGLE"] = UiKind.Toggle,
        ["LBOX"] = UiKind.List, ["TLBOX"] = UiKind.TextList, ["EDIT"] = UiKind.Edit, ["SPIN"] = UiKind.Spin,
        ["RADIO"] = UiKind.RadioGroup,
    };

    /// <summary>Исходники разметки в порядке, в котором они попадают в набор экранов: сперва основной файл, затем
    /// сетевое лобби. <c>CustomLobby.dlg</c> поставляется только с сетевой надстройкой, в эталонной копии игры его
    /// нет, поэтому имя ищется сначала в <paramref name="gameDir"/>, затем в соседней папке <c>game-dev</c>.
    /// Возвращаются только существующие файлы.
    /// <c>ponytail:</c> потолок — знание о соседней папке; уйдёт, когда у порта будет своя копия ресурсов.</summary>
    public static IReadOnlyList<string> Sources(string? gameDir = null)
    {
        string dir = gameDir ?? GamePaths.GameDir;
        string[] roots = [Path.Combine(dir, "Interf"), Path.Combine(dir, "..", "game-dev", "Interf")];
        return [.. new[] { "Interf.dlg", "CustomLobby.dlg" }
            .Select(name => roots.Select(r => Path.GetFullPath(Path.Combine(r, name))).FirstOrDefault(File.Exists))
            .Where(p => p is not null)
            .Select(p => p!)];
    }

    /// <summary>Перевести все исходники в один набор экранов: порядок файлов и порядок экранов внутри файла
    /// сохраняются, чтобы вывод не зависел от порядка обхода файловой системы.</summary>
    public static IReadOnlyList<UiScreen> LoadAll(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return [.. paths.SelectMany(Load)];
    }

    public static IReadOnlyList<UiScreen> Load(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Parse(File.ReadAllText(path, Encoding.GetEncoding(1251)));
    }

    public static IReadOnlyList<UiScreen> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var screens = new List<UiScreen>();
        UiScreen? current = null;
        List<UiElement> elements = [];

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line == "BEGIN") continue;
            if (line == "END")
            {
                screens.Add((current ?? throw new FormatException("END без DIALOG")) with { Elements = elements });
                (current, elements) = (null, []);
                continue;
            }

            var (kind, fields) = Split(line);
            if (kind == "DIALOG")
            {
                if (current is not null) throw new FormatException($"DIALOG {fields[0]} внутри {current.Id}");
                var rect = Rect(fields, fields[0]);
                current = new UiScreen
                {
                    Id = fields[0], Design = rect,
                    Background = At(fields, 5), Cursor = At(fields, 6), Legacy = Trim(fields.Skip(7)),
                };
                continue;
            }
            if (current is null) throw new FormatException($"{kind} вне DIALOG");
            if (!Kinds.TryGetValue(kind, out var uiKind)) throw new FormatException($"неизвестный вид {kind}");
            elements.Add(uiKind == UiKind.RadioGroup ? Group(fields) : Element(uiKind, fields, current.Design));
        }
        if (current is not null) throw new FormatException($"DIALOG {current.Id} без END");
        return screens;
    }

    /// <summary>Группа переключателей: <c>RADIO имя,TOG_A,TOG_B,…</c> — ни прямоугольника, ни кадров. Место группы
    /// задают сами переключатели, поэтому <see cref="UiElement.Rect"/> у неё пустой, а <see cref="UiElement.Anchor"/>
    /// смысла не имеет и остаётся значением по умолчанию.</summary>
    private static UiElement Group(List<string> fields) =>
        new() { Id = fields[0], Kind = UiKind.RadioGroup, Members = Trim(fields.Skip(1)) };

    /// <summary>Разбор полей после имени и прямоугольника (поля 0..4). Раскладка у каждого вида своя:
    /// <list type="table">
    /// <item><term>IMAGE</term><description>5 кадр, 6 подсказка</description></item>
    /// <item><term>TEXT</term><description>5 оформление, 6 строка</description></item>
    /// <item><term>BUTTON</term><description>5..8 кадры «обычный / наведение / нажатие / запрет», 9 подсказка,
    /// 10 флаг (назначение неизвестно), 11..12 коды клавиш</description></item>
    /// <item><term>TOGGLE</term><description>5..8 кадры выключенного состояния, 9..11 кадры включённого,
    /// 12 подсказка, 13 код клавиши</description></item>
    /// <item><term>EDIT</term><description>5 оформление</description></item>
    /// <item><term>SPIN</term><description>5..6 кнопки «больше»/«меньше», 7 оформление, 8 строка</description></item>
    /// <item><term>LBOX</term><description>5..8 ширина и высота ячейки, зазоры по X и Y; 9..15 кнопки «вверх /
    /// вниз / влево / вправо / страница вверх / страница вниз / подтвердить»; 16 кадр рамки выделения</description></item>
    /// <item><term>TLBOX</term><description>5..7 неизвестны (всегда <c>1,0,0</c>, в <c>Legacy</c>); 8..14 те же
    /// семь кнопок; 15..16 оформление выделенной и обычной строки</description></item>
    /// </list>
    /// Раскладка списков выведена из самих экранов: ячейки с зазорами сходятся с прямоугольником (3×85 + 2×6 = 267
    /// у <c>LBOX_ITEMS_KEEP</c>), кнопки названы по назначению (<c>BTN_LEFT</c>/<c>BTN_RIGHT</c> — в сетке
    /// 2×4 у <c>DLG_IMPORT_LEADER</c>), а в «подтвердить» стоят <c>BTN_LOAD</c>, <c>BTN_OK</c>, <c>BTN_CAST_SPELL</c>.
    /// Порядок кнопок счётчика совпадает с <c>increment</c>/<c>decrement</c> в <c>CSpinButtonInterfData</c> тулсета.
    /// Поля после этих (нули и пустые) остаются в <c>Legacy</c>.</summary>
    private static UiElement Element(UiKind kind, List<string> fields, UiRect design)
    {
        var rect = Rect(fields, fields[0]);
        var e = new UiElement { Id = fields[0], Kind = kind, Rect = rect, Anchor = Anchor(rect, design) };
        return kind switch
        {
            UiKind.Image => e with
            {
                Image = Empty(At(fields, 5)), TextId = Empty(At(fields, 6)), Legacy = Trim(fields.Skip(7)),
            },
            UiKind.Text => e with
            {
                Style = Empty(At(fields, 5)), TextId = Empty(At(fields, 6)), Legacy = Trim(fields.Skip(7)),
            },
            UiKind.Button => e with
            {
                Sprites = new UiSprites(At(fields, 5), At(fields, 6), At(fields, 7), At(fields, 8)),
                TextId = Empty(At(fields, 9)), Hotkeys = Keys(fields, 11, 12),
                Legacy = Trim(fields.Skip(10).Take(1)),
            },
            UiKind.Toggle => e with
            {
                Sprites = new UiSprites(At(fields, 5), At(fields, 6), At(fields, 7), At(fields, 8)),
                Checked = new UiSprites(At(fields, 9), At(fields, 10), At(fields, 11)),
                TextId = Empty(At(fields, 12)), Hotkeys = Keys(fields, 13),
            },
            UiKind.Edit => e with { Style = Empty(At(fields, 5)), Legacy = Trim(fields.Skip(6)) },
            UiKind.Spin => e with
            {
                Spin = new UiSpin(Empty(At(fields, 5)), Empty(At(fields, 6))),
                Style = Empty(At(fields, 7)), TextId = Empty(At(fields, 8)), Legacy = Trim(fields.Skip(9)),
            },
            UiKind.List => e with
            {
                List = Links(fields, 9) with
                {
                    CellWidth = Number(fields, 5), CellHeight = Number(fields, 6),
                    GapX = Number(fields, 7), GapY = Number(fields, 8), Selection = Empty(At(fields, 16)),
                },
                Legacy = Trim(fields.Skip(17)),
            },
            UiKind.TextList => e with
            {
                List = Links(fields, 8) with
                {
                    SelectedStyle = Empty(At(fields, 15)), Style = Empty(At(fields, 16)),
                },
                Legacy = Trim(fields.Skip(5).Take(3).Concat(fields.Skip(17))),
            },
            _ => e with { Legacy = Trim(fields.Skip(5)) },
        };
    }

    /// <summary>Привязка элемента к краям экрана по его центру: экран делится на три равные полосы по каждой оси,
    /// центр элемента попадает в одну из девяти клеток — она и даёт <see cref="UiAnchor"/>. Правило нарочно тупое
    /// и предсказуемое: элемент у левого края останется у левого края при другом размере окна, а нарисованное
    /// посередине (и фоны во весь экран тоже) останется посередине. Ручная правка в JSON всегда главнее.</summary>
    public static UiAnchor Anchor(UiRect rect, UiRect design)
    {
        int h = Third(2 * (rect.X - design.X) + rect.Width, design.Width);
        int v = Third(2 * (rect.Y - design.Y) + rect.Height, design.Height);
        return (h, v) switch
        {
            (0, 0) => UiAnchor.TopLeft, (1, 0) => UiAnchor.Top, (2, 0) => UiAnchor.TopRight,
            (0, 1) => UiAnchor.Left, (1, 1) => UiAnchor.Center, (2, 1) => UiAnchor.Right,
            (0, _) => UiAnchor.BottomLeft, (1, _) => UiAnchor.Bottom, _ => UiAnchor.BottomRight,
        };
    }

    /// <summary>Номер трети (0, 1, 2), в которую попал удвоенный центр <paramref name="twiceCenter"/> при длине
    /// стороны <paramref name="size"/>. Считается в целых, чтобы деление не съезжало на нечётных размерах.</summary>
    private static int Third(int twiceCenter, int size) =>
        size <= 0 ? 0 : 3 * twiceCenter < 2 * size ? 0 : 3 * twiceCenter > 4 * size ? 2 : 1;

    /// <summary>Семь кнопок списка подряд с поля <paramref name="first"/>: вверх, вниз, влево, вправо, страница
    /// вверх, страница вниз, подтвердить.</summary>
    private static UiList Links(List<string> fields, int first) => new()
    {
        Up = Empty(At(fields, first)), Down = Empty(At(fields, first + 1)),
        Left = Empty(At(fields, first + 2)), Right = Empty(At(fields, first + 3)),
        PageUp = Empty(At(fields, first + 4)), PageDown = Empty(At(fields, first + 5)),
        Confirm = Empty(At(fields, first + 6)),
    };

    /// <summary>Число из поля; пустое поле — ноль.</summary>
    private static int Number(List<string> fields, int i) => int.TryParse(At(fields, i), out int v) ? v : 0;

    /// <summary>Коды клавиш из перечисленных полей; ноль и пустое поле означают «клавиши нет».</summary>
    private static IReadOnlyList<int> Keys(List<string> fields, params int[] indexes) =>
        [.. indexes.Select(i => int.TryParse(At(fields, i), out int v) ? v : 0).Where(v => v != 0)];

    private static (string Kind, List<string> Fields) Split(string line)
    {
        int cut = line.IndexOfAny(['\t', ' ']);
        if (cut < 0) throw new FormatException($"строка без полей: {line}");
        var fields = line[(cut + 1)..].Split(',').Select(f => f.Trim().Trim('"')).ToList();
        return (line[..cut].Trim(), fields);
    }

    private static UiRect Rect(List<string> fields, string name)
    {
        if (fields.Count < 5) throw new FormatException($"{name}: меньше пяти полей");
        return UiRect.FromCorners(Int(fields[1], name), Int(fields[2], name), Int(fields[3], name), Int(fields[4], name));
    }

    private static string At(List<string> fields, int i) => i < fields.Count ? fields[i] : "";

    private static string? Empty(string s) => s.Length == 0 ? null : s;

    /// <summary>Остаток полей без пустых хвостов: в исходнике строки добиты запятыми до постоянной длины,
    /// и хранить эти пустоты незачем.</summary>
    private static IReadOnlyList<string> Trim(IEnumerable<string> tail)
    {
        var list = tail.ToList();
        while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
        return list;
    }

    private static int Int(string s, string name) =>
        int.TryParse(s, out int v) ? v : throw new FormatException($"{name}: не число «{s}»");
}
