namespace D2.Rules.Units;

/// <summary>
/// Строение группы (отряд, гарнизон, сторона боя): шесть клеток, три колонки по две — клетки 0/2/4 передний ряд, 1/3/5 задний, пара колонки
/// — <c>cell ^ 1</c>, колонка — <c>cell / 2</c>; большой юнит стоит в обеих клетках колонки. Одно место вместо литералов 6, <c>% 2</c> и
/// <c>^ 1</c> по коду боя, ИИ и окон (этап Э2, задача E): размер и ряды группы станут данными отсюда. На клетках группы (0…5) формулы
/// равны прежним; клетка вне группы — ни переднего, ни заднего ряда (прежнее <c>% 2</c> там давало ряд, а индекс клетки — сбой).
/// </summary>
public static class Formation
{
    /// <summary>Клеток в группе.</summary>
    public const int Cells = 6;

    /// <summary>Передний ряд (<c>[0x6e8928]</c>).</summary>
    public static readonly IReadOnlyList<int> Front = [0, 2, 4];

    /// <summary>Задний ряд (<c>[0x6e8938]</c>).</summary>
    public static readonly IReadOnlyList<int> Back = [1, 3, 5];

    /// <summary>Клетка переднего ряда (<c>0x6114c6</c>); вне группы — нет.</summary>
    public static bool IsFront(int cell) => (uint)cell < Cells && cell % 2 == 0;

    /// <summary>Клетка заднего ряда; вне группы — нет.</summary>
    public static bool IsBack(int cell) => (uint)cell < Cells && cell % 2 == 1;

    /// <summary>Вторая клетка колонки (<c>0x502d15</c>): позиция пары большого юнита.</summary>
    public static int Pair(int cell) => cell ^ 1;

    /// <summary>Пустые клетки группы: клетка → индекс юнита, −1 — пусто (<c>MapStack.Slots</c>, гарнизон).</summary>
    public static int[] EmptySlots() => [.. Enumerable.Repeat(-1, Cells)];

    /// <summary>Колонка клетки (строка расстояния целей, <c>BattleTargets</c>).</summary>
    public static int Column(int cell) => cell / 2;

    /// <summary>Передняя клетка колонки: позиция большого юнита и любая позиция в обмене с ним (<c>0x6116ba</c>).</summary>
    public static int ColumnFront(int cell) => IsFront(cell) ? cell : cell - 1;
}
