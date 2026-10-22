namespace D2.Data.Ui;

/// <summary>
/// Ячейки группы юнитов в окнах (армия города, отряд): разметка даёт только первую ячейку группы
/// (<c>IMG_LSLOT1</c>, <c>IMG_RSLOT1</c>, <c>IMG_SLOT1</c>), остальные движок считает сам — <c>CGroupAreaManagement</c>
/// (<c>0x570258</c>), прямоугольники <c>0x570308</c>. Из прямоугольника разметки берётся только левый верхний угол; ячейка
/// 70×85 (<c>0x6ebedc</c>/<c>0x6ebee0</c>), зазоры 10 и 21 (<c>0x6ebef0</c>/<c>0x6ebef4</c>): шаг 80 по x и 106 по y.
/// Нечётная позиция (задний ряд) стоит в столбце разметки, чётная (передний ряд) — в соседнем, ближе к середине экрана:
/// у левой группы правее, у правой левее. Большой юнит занимает объединение ячеек своей колонки (<c>0x56e1f1</c>).
/// Ранг 1 — листинг конструктора окна <c>0x4b0bcf</c> и групп <c>0x4ff166</c>, <c>0x502056</c>, <c>0x505042</c>.
/// </summary>
public static class UnitGroupLayout
{
    public const int CellWidth = 70;
    public const int CellHeight = 85;
    public const int GapX = 10;
    public const int GapY = 21;
    public const int Positions = D2.Rules.Map.UnitCells.CellCount;

    /// <summary>Ячейка позиции <paramref name="position"/> (0…5) группы с первой ячейкой разметки <paramref name="first"/>.</summary>
    public static UiRect Cell(UiRect first, bool leftSide, int position)
    {
        int step = CellWidth + GapX;
        int front = leftSide ? first.X + step : first.X - step;
        int x = position % 2 == 0 ? front : first.X;
        return new UiRect(x, first.Y + ((CellHeight + GapY) * (position / 2)), CellWidth, CellHeight);
    }

    /// <summary>Колонка ряда <paramref name="row"/> (0…2) — место большого юнита: объединение двух ячеек.</summary>
    public static UiRect Column(UiRect first, bool leftSide, int row)
    {
        var a = Cell(first, leftSide, row * 2);
        var b = Cell(first, leftSide, (row * 2) + 1);
        int x = Math.Min(a.X, b.X);
        return new UiRect(x, a.Y, Math.Max(a.Right, b.Right) - x, CellHeight);
    }

    /// <summary>Вся группа — объединение шести ячеек (щелчок по пустой группе нанимает лидера).</summary>
    public static UiRect Area(UiRect first, bool leftSide)
    {
        var top = Column(first, leftSide, 0);
        var bottom = Column(first, leftSide, (Positions / 2) - 1);
        return new UiRect(top.X, top.Y, top.Width, bottom.Bottom - top.Y);
    }

    /// <summary>Позиция под точкой, как у движка (<c>CGroupAreaManagement</c>, vtable <c>0x6e5a14</c> +4 = <c>0x5704f8</c>): точка в зазоре
    /// между колонками сдвигается в ближнюю колонку на 2 точки (от середины зазора — в правую), затем ячейки по порядку позиций, каждая
    /// с половиной зазоров вокруг и ещё точкой (<c>0x57060b</c>: ±6 по x, ±11 по y). Поэтому зазоров нет: щелчок посередине большого
    /// юнита (между его двумя ячейками) и по подписи здоровья под ячейкой попадает в юнита. Дальше от ячеек — <c>null</c>.</summary>
    public static int? PositionAt(UiRect first, bool leftSide, int x, int y)
    {
        var front = Cell(first, leftSide, 0);
        var back = Cell(first, leftSide, 1);
        var (left, right) = leftSide ? (back, front) : (front, back);
        if (x >= left.Right && x <= right.X)
            x = x >= left.Right + ((right.X - left.Right) / 2) ? right.X + 2 : left.Right - 2;
        int dx = (GapX / 2) + 1, dy = (GapY / 2) + 1;
        for (int p = 0; p < Positions; p++)
        {
            var r = Cell(first, leftSide, p);
            if (x >= r.X - dx && x < r.Right + dx && y >= r.Y - dy && y < r.Bottom + dy) return p;
        }
        return null;
    }

    /// <summary>Имя картинки ячейки: <c>IMG_{группа}CELL{позиция}</c>.</summary>
    public static string CellImage(string group, int position) => $"IMG_{group}CELL{position}";

    /// <summary>Имя картинки большого юнита ряда: <c>IMG_{группа}BIG{ряд}</c>.</summary>
    public static string BigImage(string group, int row) => $"IMG_{group}BIG{row}";

    /// <summary>Подпись под ячейкой (в зазоре между рядами): <c>TXT_{группа}CELL{позиция}</c>.</summary>
    public static string CellText(string group, int position) => $"TXT_{group}CELL{position}";

    /// <summary>Подпись под большим юнитом: <c>TXT_{группа}BIG{ряд}</c>.</summary>
    public static string BigText(string group, int row) => $"TXT_{group}BIG{row}";

    /// <summary>Значок ячейки (<c>0x56e2e7</c>): <paramref name="key"/> — позиция малого (<c>0</c>…<c>5</c>) или колонка большого
    /// (<c>B0</c>…<c>B2</c>), <paramref name="kind"/> — один из <see cref="IconKinds"/>.</summary>
    public static string Icon(string group, string key, string kind) => $"IMG_{group}{kind}{key}";

    /// <summary>Рамка допустимости ячейки при перетаскивании (<c>DRAGDROP_SMALL_*</c>): <c>IMG_{группа}DROP{позиция}</c>.</summary>
    public static string DropImage(string group, int position) => $"IMG_{group}DROP{position}";

    /// <summary>Рамка допустимости колонки большого юнита (<c>DRAGDROP_LARGE_*</c>): <c>IMG_{группа}DROPB{ряд}</c>.</summary>
    public static string BigDropImage(string group, int row) => $"IMG_{group}DROPB{row}";

    /// <summary>Подложка подписи большого юнита (<c>SPLITLRG</c>): <c>IMG_{группа}SPLIT{ряд}</c>.</summary>
    public static string BigSplit(string group, int row) => $"IMG_{group}SPLIT{row}";

    public const string UpgradeIcon = "UPG", HighLevelIcon = "HIGH", PotionIcon = "POT", BoostIcon = "BST", LowerIcon = "LOW";

    public static readonly string[] IconKinds = [UpgradeIcon, HighLevelIcon, PotionIcon, BoostIcon, LowerIcon];

    /// <summary>Подпись здоровья: на точку ниже ячейки, высота 20 (<c>0x6ebefc</c>/<c>0x6ebf00</c>/<c>0x6ebf04</c>).</summary>
    public const int TextGap = 1, TextHeight = 20;

    /// <summary>
    /// Места значков в ячейке (<c>0x56e2e7</c>, значки 23×28, высокий уровень 15×15 — вверху по центру, <c>W/2 − w/2</c>). Раскладка
    /// зеркальная (флаг <c>mirror</c> — группа слева, <c>0x56e181</c>; подтвердил пользователь 26.09): слева усиление вверху, зелье
    /// посередине, ослабление внизу у левого края, повышение — в правом верхнем углу; у правой группы — наоборот.
    /// </summary>
    private static IEnumerable<(string Kind, UiRect Rect)> Icons(UiRect cell, bool leftSide)
    {
        int near = cell.X, far = cell.Right - 23;
        int side = leftSide ? near : far, other = leftSide ? far : near;
        return
        [
            (UpgradeIcon, new UiRect(other, cell.Y, 23, 28)),
            (HighLevelIcon, new UiRect(cell.X + (cell.Width / 2) - (15 / 2), cell.Y, 15, 15)),
            (BoostIcon, new UiRect(side, cell.Y, 23, 28)),
            (PotionIcon, new UiRect(side, cell.Y + ((CellHeight - 28) / 2), 23, 28)),
            (LowerIcon, new UiRect(side, cell.Y + CellHeight - 28, 23, 28)),
        ];
    }

    /// <summary>
    /// Экран с элементами ячеек группы: сразу за первой ячейкой разметки (<paramref name="anchor"/>, её движок прячет)
    /// встают картинки шести ячеек и трёх колонок и подписи под ними. Логика окна рисует портреты через
    /// <see cref="UiScreenState.SetImage"/>, как у любой картинки экрана. Рамка допустимости (<see cref="DropImage"/>, <see cref="BigDropImage"/>)
    /// занимает место ячейки или колонки и стоит после портрета и подписи, до значков: <c>0x56e02d</c> первым проходом рисует портрет, подпись
    /// и рамку (<c>0x56e487</c>), вторым — значки (<c>0x56e2e7</c>). Картинка рамки — по середине места (<c>0x56e487</c>: середина прямоугольника
    /// 70×85 или 150×85 минус половина картинки: у 74×89 сдвиг −2,−2, у 152×85 — −1,0).
    /// </summary>
    /// <exception cref="KeyNotFoundException">На экране нет элемента <paramref name="anchor"/>.</exception>
    public static UiScreen Expand(UiScreen screen, string anchor, string group, bool leftSide)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var elements = screen.Elements.ToList();
        int at = elements.FindIndex(e => e.Id == anchor);
        if (at < 0) throw new KeyNotFoundException($"{screen.Id}: нет элемента {anchor}");
        var first = elements[at].Rect;
        var added = new List<UiElement>();
        for (int p = 0; p < Positions; p++)
        {
            var r = Cell(first, leftSide, p);
            added.Add(new UiElement { Id = CellImage(group, p), Kind = UiKind.Image, Rect = r });
            added.Add(new UiElement { Id = CellText(group, p), Kind = UiKind.Text, Rect = Below(r), Style = @"\hC;\vT;" });
            added.Add(new UiElement { Id = DropImage(group, p), Kind = UiKind.Image, Rect = r });
            added.AddRange(Icons(r, leftSide).Select(i => new UiElement
            {
                Id = Icon(group, p.ToString(System.Globalization.CultureInfo.InvariantCulture), i.Kind), Kind = UiKind.Image, Rect = i.Rect,
            }));
        }
        for (int row = 0; row < Positions / 2; row++)
        {
            var r = Column(first, leftSide, row);
            added.Add(new UiElement { Id = BigImage(group, row), Kind = UiKind.Image, Rect = r });
            var below = Below(r);
            added.Add(new UiElement { Id = BigSplit(group, row), Kind = UiKind.Image, Rect = new UiRect(below.X + ((below.Width - 13) / 2), below.Y - 1, 13, 17) });
            added.Add(new UiElement { Id = BigText(group, row), Kind = UiKind.Text, Rect = below, Style = @"\hC;\vT;" });
            added.Add(new UiElement { Id = BigDropImage(group, row), Kind = UiKind.Image, Rect = r });
            added.AddRange(Icons(r, leftSide).Select(i => new UiElement { Id = Icon(group, $"B{row}", i.Kind), Kind = UiKind.Image, Rect = i.Rect }));
        }
        elements.InsertRange(at + 1, added);
        return screen with { Elements = elements };
    }

    private static UiRect Below(UiRect cell) => new(cell.X, cell.Bottom + TextGap, cell.Width, TextHeight);
}
