using D2.Data.Ui;
using D2.Rules.Battle;
using D2.Rules.Units;

namespace D2.Data.Gfx;

/// <summary>Картинка боя: где лежит (архив и имя анимации), куда кладётся левым верхним углом в координатах
/// экрана 800×600 и на каком слое (<c>z</c>). <see cref="Shadow"/> — тень юнита.</summary>
public sealed record BattleSprite(string Archive, string Name, int X, int Y, int Layer, string Source, bool Shadow = false);

/// <summary>Действие юнита в бою — часть имени анимации (<c>docs/battle-render.md</c> §2).</summary>
public enum BattleAction
{
    /// <summary>Покой без анимации.</summary>
    Still,

    /// <summary>Покой с анимацией.</summary>
    Idle,

    /// <summary>Своя атака.</summary>
    Hit,

    /// <summary>Получает урон.</summary>
    Hurt,
}

/// <summary>
/// Раскладка экрана боя (разбор <c>0x62cc90</c>, <c>0x62df80</c>, <c>0x62df0e</c>, <c>0x62ddfa</c>, <c>0x646b08</c>; таблица
/// <c>docs/ui-windows.md</c> §11, сверена снимками 28.09 <c>DLG_BATTLE_A-f0/f1</c>): <paramref name="OwnAttacks"/> — нападает отряд
/// человека (a), <paramref name="Flipped"/> — «Поменяться сторонами» настроек (f), <see cref="Mirrored"/> m = !a ^ f. Нападающие стоят
/// слева (места <c>IMG_AUNIT</c>, кадры «A») при !f. При m места обеих групп сдвинуты на +173, фон — с x = 0 (иначе −150), основная
/// панель — слева (иначе справа), кнопка <c>TOG_RIGHTUNITS</c> и полоса <c>IMG_PUNITGROUP_SPOT</c> отражены (x' = 800 − x − ширина).
/// </summary>
public sealed record BattleLayout(bool OwnAttacks = true, bool Flipped = false)
{
    public const int ScreenWidth = 800, MirrorShift = 173, BackgroundLeft = -150;

    public static BattleLayout Default { get; } = new();

    /// <summary>m = !a ^ f.</summary>
    public bool Mirrored => !OwnAttacks ^ Flipped;

    /// <summary>Нападающие — слева (места <c>IMG_AUNIT</c>, кадры «A», левые слои).</summary>
    public bool AttackersLeft => !Flipped;

    /// <summary>Сдвиг мест юнитов по x.</summary>
    public int Shift => Mirrored ? MirrorShift : 0;

    /// <summary>x фона (холст 950 шире экрана).</summary>
    public int BackgroundX => Mirrored ? 0 : BackgroundLeft;

    /// <summary>Основная панель справа (<c>IMG_R*</c>) — иначе слева.</summary>
    public bool MainPanelRight => !Mirrored;

    /// <summary>Группа стоит слева.</summary>
    public bool LeftSide(bool attacker) => attacker == AttackersLeft;

    /// <summary>Место элемента, отражённого при m (кнопка панели, полоса SPOT).</summary>
    public UiRect Mirror(UiRect rect) => Mirrored ? rect with { X = ScreenWidth - rect.X - rect.Width } : rect;
}

/// <summary>
/// Экран боя: места юнитов и их картинки по разбору движка (<c>docs/battle-render.md</c>).
///
/// <para>Мест двенадцать, они берутся из разметки <c>DLG_BATTLE_A</c> (<c>IMG_AUNIT1..6</c> — нападающие,
/// <c>IMG_DUNIT1..6</c> — обороняющиеся): позиция в отряде <c>p</c> — это элемент <c>p+1</c>. Кадр в
/// <c>BatUnits.ff</c> — холст 800×600 с якорем <c>(400, 490)</c>, поэтому левый верхний угол кадра —
/// <c>(центрX места − 400, низ места − 490)</c>. Имя анимации — <c>{базовый юнит}{действие}{A|S}{слой}{A|D}00</c>;
/// зеркалирования нет, у обороняющихся свои кадры.</para>
/// </summary>
public sealed class BattleScene
{
    /// <summary>Якорь кадра боя (<c>[0x838b00]</c>, <c>[0x838b08]</c>).</summary>
    public const int AnchorX = 400, AnchorY = 490;

    /// <summary>Холст кадра юнита — весь экран боя.</summary>
    public const int CanvasWidth = 800, CanvasHeight = 600;

    /// <summary>Слои по позиции (<c>0x6469a3</c>): слева и справа свои базы, метки — ниже юнитов.</summary>
    private static readonly int[] LeftLayers = [300, 200, 500, 400, 700, 600];
    private static readonly int[] RightLayers = [800, 900, 1000, 1100, 1200, 1300];

    public const int BackgroundLayer = 10, ForegroundLayer = 2000, PanelLayer = 3000, CurrentMarkLayer = 190;

    /// <summary>Метка поля под ходящим (<c>MRKCUR{SMALL|LARGE}</c>, <c>0x6379a5</c>).</summary>
    public const string CurrentMark = "MRKCUR";

    private readonly UiScreen _screen;
    private readonly GfxAssets _gfx;
    private readonly Func<string, string> _baseUnit;
    private readonly Func<string, string>? _deathRace;
    private readonly List<string> _missing = [];

    /// <param name="screen">Разметка <c>DLG_BATTLE_A</c>.</param>
    /// <param name="baseUnit">Тип юнита → базовый тип картинок (<c>Gunits.BASE_UNIT</c>, пусто — сам тип).</param>
    /// <param name="deathRace">Тип юнита → раса смерти (<c>Gunits.DEATH_ANIM</c> → <c>LDthAnim</c>: <c>HUMAN</c>,
    /// <c>ELF</c>, …). Без неё трупы не рисуются.</param>
    public BattleScene(UiScreen screen, GfxAssets gfx, Func<string, string> baseUnit, Func<string, string>? deathRace = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(baseUnit);
        _screen = screen;
        _gfx = gfx;
        _baseUnit = baseUnit;
        _deathRace = deathRace;
    }

    /// <summary>Имена, которых нет в графике.</summary>
    public IReadOnlyList<string> Missing => _missing;

    /// <summary>«Дополнительная анимация в бою» (<c>TOG_BATTLEANIM</c>, ini <c>BattleAnim</c>, <c>GameSettings+0x178</c> → <c>data2+8</c> →
    /// загрузчик картинок боя <c>+0x100</c>, <c>0x647869</c>): включена — рамки, метки и курсоры берутся анимацией «…A» (загрузчики
    /// <c>0x5b3842</c>…<c>0x5b3e0d</c>; нет «…A» — картинка без неё), выключена — картинкой без «A».</summary>
    public bool Animated { get; set; } = true;

    /// <summary>Покой — неподвижный кадр <c>STIL</c> вместо <c>IDLE</c> (<c>0x5c6847</c>: флаг <c>BattleAnim</c> ложен; <c>0x651c34</c>: и при скорости
    /// «Сразу»).</summary>
    public bool StillIdle { get; set; }

    /// <summary>Раса типа юнита — эльфы (<c>Gunits.RACE_ID</c> → <c>Grace.RACE_TYPE</c> 5, категория <c>L_ELF</c>, GOG <c>0x8372a0</c>): у них
    /// вторая атака «лечение» идёт без картинки на цели (<see cref="Touch"/>). По умолчанию — никто.</summary>
    public Func<string, bool> IsElf { get; init; } = _ => false;

    /// <summary>Раскладка (<see cref="BattleLayout"/>); по умолчанию — человек нападает, без зеркала.</summary>
    public BattleLayout Layout { get; set; } = BattleLayout.Default;

    /// <summary>Какую группу показывает основная панель, когда она одна: нападающих или обороняющихся (<c>TOG_RIGHTUNITS</c>; в начале
    /// хода человека — группа целей, <c>data+0xfa1</c> «цели — нападающие», <c>0x6304dc</c>; после действия — группа первой цели).
    /// Пока видны обе панели, стороны групп постоянны (<see cref="PanelLeft"/>).</summary>
    public bool MainShowsAttackers { get; set; } = false;

    /// <summary>Мышь над полосой <c>IMG_PUNITGROUP_SPOT</c>: видны обе панели — своя со своим отрядом, основная с отрядом врага.</summary>
    public bool SpotShown { get; set; }

    /// <summary>Обе панели отрядов видны сразу: свой отряд — в панели своей стороны всегда, а не только пока мышь над SPOT.
    /// Ответ пользователя 11.10 (видел в оригинале обе). По коду GOG 3.01 своя панель включается только наведением на SPOT:
    /// вход в зону <c>data+0x14e4</c> (<c>0x6329e8</c> → <c>0x632b14</c> <c>0x638298(1)</c> и <c>0x632b1c</c> <c>0x638035</c>, флаг
    /// <c>+0x14e1</c>), выход — <c>0x632a22</c> <c>0x6380dc</c>; группы панелей создаются выключенными (<c>0x649ebe</c>), других мест,
    /// где их включают, нет (вызовы <c>0x64c355</c> — только из <c>0x638035</c> и <c>0x638298</c>), условия по размеру экрана или
    /// настройке там нет. Ложь — как по коду (своя панель по наведению).</summary>
    public bool BothPanels { get; set; } = true;

    /// <summary>Сторона панели группы; null — группа сейчас не показана. Основная панель (<c>groupAreas1</c>) — на стороне врага, своя
    /// (<c>groupAreas2</c>) — на своей. Своя панель показывает только свой отряд (<c>0x638035</c>: группа по <c>data+0x14f6</c> m и
    /// <c>+0x14f7</c> f — нападающие тогда и только тогда, когда нападает человек). Пока видны обе (<see cref="BothPanels"/> или мышь над SPOT),
    /// основная показывает отряд врага: вход в SPOT зовёт <c>0x638298(1)</c>, и переключатель читается как m (<c>0x639975</c> при
    /// <c>+0x14e1</c>) — включается группа врага при любых a и f. Одна основная панель — группа по <c>TOG_RIGHTUNITS</c>
    /// (<see cref="MainShowsAttackers"/>: в ходе человека — группа целей, у лекаря — свой отряд). Раньше вторая панель показывала «другую
    /// группу», и в ход лекаря свой отряд уезжал в правую панель, а враг — в левую (пометка 1 партии 14.10).</summary>
    public bool? PanelLeft(bool attacker)
    {
        bool mainLeft = !Layout.MainPanelRight;
        if (BothPanels || SpotShown) return attacker == Layout.OwnAttacks ? !mainLeft : mainLeft;
        return attacker == MainShowsAttackers ? mainLeft : null;
    }

    /// <summary>Крупный портрет ходящего — слева, если ходит свой юнит, иначе справа; на другой стороне — портрет «под курсором»
    /// (юнит под мышью или первая цель последнего действия). Разбор: <c>getBigFace2</c> <c>0x639d89</c> даёт ходящему (<c>data+0xf9c</c>,
    /// его ставят <c>update</c> <c>0x630558</c> и показ действия <c>0x630b51</c>/<c>0x630d72</c>) портрет <c>+0x1394</c>, если он нападающий
    /// (<c>0x63e370</c>), иначе <c>+0x1380</c>; <c>getBigFace</c> <c>0x639e08</c> — наоборот. Места: <c>+0x1380</c> — в <c>groupAreas1</c>
    /// при !f, в <c>groupAreas2</c> при f, <c>+0x1394</c> — в другом (<c>0x62e514…0x62e5a3</c>); при m наборы мест переставлены
    /// (<c>0x62d7b1</c>). Итог для всех a и f: <c>+0x1394</c> слева тогда и только тогда, когда нападает человек, — портрет ходящего слева
    /// ровно у своих юнитов.</summary>
    public bool ActiveFaceLeft(bool attacker) => attacker == Layout.OwnAttacks;

    /// <summary>Группа показана в какой-то панели.</summary>
    public bool PanelShown(bool attacker) => PanelLeft(attacker) is not null;


    /// <summary>Место юнита: прямоугольник разметки для позиции <paramref name="position"/> (0..5).</summary>
    public UiRect Slot(bool attacker, int position)
    {
        string id = $"IMG_{(Layout.LeftSide(attacker) ? 'A' : 'D')}UNIT{Math.Clamp(position, 0, 5) + 1}";
        var rect = _screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect
                   ?? throw new KeyNotFoundException($"{_screen.Id}: нет места {id}");
        return rect with { X = rect.X + Layout.Shift };
    }

    /// <summary>Прямоугольник большого юнита: пара мест <c>(2k, 2k+1)</c>, ширина одного места, высота ×1.5,
    /// центр-низ сдвинут на треть расстояния между местами (<c>0x62f594</c>).</summary>
    public UiRect BigSlot(bool attacker, int position, bool retreating = false)
    {
        int even = (Math.Clamp(position, 0, 5) / 2) * 2;
        var a = Slot(attacker, even);
        var b = Slot(attacker, even + 1);
        bool forward = a.X < b.X;
        if (retreating)
        {
            forward = !forward;
            (a, b) = (b, a);
        }
        int width = a.Width, height = 3 * a.Height / 2;
        int dx = Math.Abs(Center(a) - Center(b)), dy = Math.Abs(Bottom(a) - Bottom(b));
        int cx = forward ? Center(a) + (dx / 3) : Center(a) - (dx / 3);
        int cy = forward ? Bottom(a) + (dy / 3) : Bottom(a) - (dy / 3);
        return new UiRect(cx - (width / 2), cy - height, width, height);
    }

    /// <summary>На сколько зона мыши места ниже самого места (<c>0x6326b9</c>: низ прямоугольника <c>getUnitRect</c> + 0x20).</summary>
    public const int HitBelow = 32;

    /// <summary>Зона мыши места на поле (<c>0x6326b9</c>, ранг 1): прямоугольник места (<c>getUnitRect</c> <c>0x62f594</c>; у большого —
    /// пара мест с признаком «отступает», <c>0x63e4c0</c>/<c>0x63e4e0</c>) с низом на <see cref="HitBelow"/> ниже — от ног юнита вниз, по
    /// метке под ним. По ней движок ищет юнита под курсором при наведении (<c>markAttackTargets</c> <c>0x632f4b</c>/<c>0x632f99</c>), щелчке
    /// и курсоре (<c>0x63217f</c>: <c>0x632334</c>/<c>0x63237f</c>; из <c>handleMouse</c> <c>0x631a85</c> и <c>updateCursor</c>
    /// <c>0x633b9d</c>), у пустых мест-целей призыва (<c>0x637405</c>). Попадание — левый и верхний край включительно, правый и нижний —
    /// нет (<c>0x63e460</c>). Картинки и метки кладутся по самому месту, без запаса (<c>0x633562</c> → <c>0x62f594</c>).</summary>
    public UiRect HitSlot(bool attacker, int position, bool small = true, bool retreating = false)
    {
        var slot = small ? Slot(attacker, position) : BigSlot(attacker, position, retreating);
        return slot with { Height = slot.Height + HitBelow };
    }

    /// <summary>Порядок позиций в обходе юнитов под мышью (<c>0x63e3d0</c> с флагом 1: ключ позиции p — <c>[1, 0, 3, 2, 5, 4][p]</c>).</summary>
    private static readonly int[] HitOrderKey = [1, 0, 3, 2, 5, 4];

    /// <summary>Юнит под точкой поля: первый, в чью зону мыши (<see cref="HitSlot"/>) попала точка (<c>0x632dc9…0x632fd6</c>: не попал —
    /// следующий, попал — выход). Обход — не в порядке <paramref name="units"/>, а в порядке движка: список юнитов боя (<c>0x6220c1</c>)
    /// сортируется (<c>std::list::sort</c> <c>0x63eed0</c>, сравнение <c>0x63fec0</c> → <c>0x63d26a</c>) — сначала обороняющиеся, потом
    /// нападающие (бит 3 байта <c>+0x30</c>, без зеркала), в группе — позиции 1, 0, 3, 2, 5, 4 (<see cref="HitOrderKey"/>). Тот же список —
    /// у щелчка и курсора (<c>0x6321ce</c>, <c>0x6321d6</c>). Где зоны перекрываются (места 2 и 3 левой группы, полоса 10 px), ловится
    /// позиция 1 раньше позиции 2.</summary>
    public BattleUnit? FieldUnitAt(IEnumerable<BattleUnit> units, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(units);
        foreach (var unit in units.OrderBy(u => u.Attacker).ThenBy(u => HitOrderKey[Math.Clamp(u.Slot, 0, 5)]))
        {
            var hit = HitSlot(unit.Attacker, unit.Slot, unit.Type.SizeSmall, unit.Retreating);
            if (x >= hit.X && y >= hit.Y && x < hit.Right && y < hit.Bottom) return unit;
        }
        return null;
    }

    /// <summary>Левый верхний угол кадра <paramref name="width"/>×<paramref name="height"/> в месте
    /// <paramref name="slot"/>: холст во весь экран кладётся по якорю, узкий кадр — по центру и низу.</summary>
    public static (int X, int Y) Place(UiRect slot, int width, int height) => PlaceAt(Center(slot), Bottom(slot), width, height);

    /// <summary>То же правило по точке привязки (<paramref name="x"/>, <paramref name="y"/>) — <c>0x649656</c>.</summary>
    public static (int X, int Y) PlaceAt(int x, int y, int width, int height) =>
        width >= AnchorX ? (x - AnchorX, y - AnchorY) : (x - (width / 2), y - height);

    /// <summary>Точка группы для эффекта ходящего (<c>0x63a377</c>, ранг 1): у шести мест группы (<c>unitImageAreas</c>) — середина нижнего
    /// края (центр по X, низ), и берётся середина их охвата: ((minX + maxX) / 2, (minY + maxY) / 2).</summary>
    public (int X, int Y) GroupPoint(bool attacker)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        for (int position = 0; position < Formation.Cells; position++)
        {
            var slot = Slot(attacker, position);
            (minX, maxX) = (Math.Min(minX, Center(slot)), Math.Max(maxX, Center(slot)));
            (minY, maxY) = (Math.Min(minY, Bottom(slot)), Math.Max(maxY, Bottom(slot)));
        }
        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>Имя анимации: <c>{базовый}{действие}{A тело|S тень}{слой}{A нападающий|D обороняющийся}{вариант}</c>.</summary>
    public string NameOf(string unitType, BattleAction action, bool attacker, bool shadow = false, int layer = 1, int variant = 0)
    {
        string act = action switch
        {
            BattleAction.Still => "STIL",
            BattleAction.Idle => "IDLE",
            BattleAction.Hit => "HMOV",
            BattleAction.Hurt => "HHIT",
            _ => "IDLE",
        };
        return $"{_baseUnit(unitType)}{act}{(shadow ? 'S' : 'A')}{layer}{(Layout.LeftSide(attacker) ? 'A' : 'D')}{variant:00}";
    }

    /// <summary>Картинки юнита на его месте (<c>Slot</c> — позиция в отряде 0..5): тень, тело и второй слой.</summary>
    public IReadOnlyList<BattleSprite> UnitSprites(BattleUnit unit, BattleAction action, bool small = true, bool retreating = false)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var slot = small ? Slot(unit.Attacker, unit.Slot) : BigSlot(unit.Attacker, unit.Slot, retreating);
        int layer = Layers(unit.Attacker)[Math.Clamp(unit.Slot, 0, 5)];
        var sprites = new List<BattleSprite>();
        Add(sprites, unit, action, slot, layer - 1, shadow: true, layerNo: 1);
        Add(sprites, unit, action, slot, layer, shadow: false, layerNo: 1);
        Add(sprites, unit, action, slot, layer + 1, shadow: false, layerNo: 2, quiet: true);
        return sprites;
    }

    /// <summary>Место в боковой панели: портрет (<c>IMG_?SLOT{m+1}</c>) и полоса здоровья под ним (<c>IMG_?METER{m+1}</c>). Сторона панели —
    /// где группа показана (<see cref="PanelLeft"/>; не показана — место основной панели). Номер места <c>m</c> — позиция, с переставленными
    /// колонками (<c>m = p ^ 1</c>), когда флаг группы — «нападающий» XOR f, настройка «Поменяться сторонами» (<c>data+0x14f7</c>, конструктор
    /// <c>CBatUnitGroup2</c> <c>0x649d80…0x649dc6</c>, <c>0x649f21</c>; ранг 1, снимок 28.09).</summary>
    public UiRect PanelSlot(bool attacker, int position, bool meter = false)
    {
        int index = PanelIndex(attacker ^ Layout.Flipped, Math.Clamp(position, 0, 5));
        bool left = PanelLeft(attacker) ?? !Layout.MainPanelRight;
        string id = $"IMG_{(left ? 'L' : 'R')}{(meter ? "METER" : "SLOT")}{index + 1}";
        return _screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect
               ?? throw new KeyNotFoundException($"{_screen.Id}: нет места {id}");
    }

    /// <summary>Номер места панели для позиции (см. <see cref="PanelSlot"/>): <paramref name="swapped"/> — «нападающий» XOR f.</summary>
    public static int PanelIndex(bool swapped, int position) => swapped ? position ^ 1 : position;

    /// <summary>Место портрета в панели: малого — его слот, большого (150×85) — по центру пары слотов (<c>0x64a60b</c>).</summary>
    public UiRect PanelFace(BattleUnit unit, int width = 70)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var slot = PanelSlot(unit.Attacker, unit.Slot);
        if (unit.Type.SizeSmall) return slot;
        var pair = PanelSlot(unit.Attacker, Formation.Pair(unit.Slot));
        int center = (Math.Min(slot.X, pair.X) + Math.Max(slot.Right, pair.Right)) / 2;
        return new UiRect(center - (width / 2), slot.Y, width, slot.Height);
    }

    /// <summary>Все шесть мест панели стороны одним прямоугольником — для рамки «вся группа» (<c>*ALL</c>).</summary>
    public UiRect PanelArea(bool attacker)
    {
        var slots = Enumerable.Range(0, 6).Select(p => PanelSlot(attacker, p)).ToList();
        int x = slots.Min(s => s.X), y = slots.Min(s => s.Y);
        return new UiRect(x, y, slots.Max(s => s.Right) - x, slots.Max(s => s.Bottom) - y);
    }

    /// <summary>Рамка панели вокруг портрета (<c>CBatUnitGroup2</c>, отрисовка <c>0x64af60</c>…<c>0x64b533</c>, по центру места):
    /// <c>PLY</c> — ходящий, <c>SEL</c>/<c>HEA</c> — годные цели, <c>ISEL</c> — цели тащимого предмета, <c>EMPTYSEL</c> — пустая позиция
    /// призыва; <paramref name="kind"/> без размера — к нему добавляется <c>SMALL</c>/<c>LARGE</c> (у <c>EMPTYSEL</c> — только SMALL).</summary>
    public BattleSprite? PanelFrame(bool attacker, int position, string kind, bool small)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        var rect = small ? PanelSlot(attacker, position) : BigPanelRect(attacker, position);
        return Centered(kind + (small ? "SMALL" : "LARGE"), rect, PanelLayer + 2, $"panel{position}");
    }

    /// <summary>Рамка «вся группа» (<c>SELALL</c>, <c>HEAALL</c>, <c>ISELALL</c>, <c>0x64cbc9</c>) — по центру мест панели стороны.</summary>
    public BattleSprite? PanelGroupFrame(bool attacker, string kind) =>
        Centered(kind + "ALL", PanelArea(attacker), PanelLayer + 2, attacker ? "panelA" : "panelD");

    private UiRect BigPanelRect(bool attacker, int position)
    {
        var slot = PanelSlot(attacker, position);
        var pair = PanelSlot(attacker, position ^ 1);
        int x = Math.Min(slot.X, pair.X);
        return new UiRect(x, slot.Y, Math.Max(slot.Right, pair.Right) - x, slot.Height);
    }

    private BattleSprite? Centered(string name, UiRect rect, int layer, string source)
    {
        var (archive, frame, w, h) = Animated && _gfx.Anim("Battle", name + "A") is { } anim ? ("Battle", anim.Name, anim.W, anim.H)
            : _gfx.Image("Battle", name) is { } image ? ("Battle", image.Name, image.W, image.H) : ("", "", 0, 0);
        if (frame.Length == 0)
        {
            _missing.Add(name);
            return null;
        }
        return new BattleSprite(archive, frame, rect.X + ((rect.Width - w) / 2), rect.Y + ((rect.Height - h) / 2), layer, source);
    }

    /// <summary>Портрет юнита в панели: <c>{базовый}FACE</c> из <c>Faces.ff</c>, 70×85 (большой — 150×85 по центру пары мест).</summary>
    public BattleSprite? Portrait(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        string name = _baseUnit(unit.Type.GlobalId) + "FACE";
        if (_gfx.Image("Faces", name) is { } image)
        {
            var slot = PanelFace(unit, image.W);
            return new BattleSprite("Faces", image.Name, slot.X, slot.Y, PanelLayer + 1, unit.Id);
        }
        _missing.Add(name);
        return null;
    }

    /// <summary>Место крупного портрета (<c>IMG_?BIGFACE</c>) или подписи под ним (<c>IMG_?BIGTEXT</c>) стороны группы.</summary>
    public UiRect BigFaceSlot(bool attacker, bool text = false) => BigFaceAt(Layout.LeftSide(attacker), text);

    /// <summary>Место крупного портрета или подписи нижней панели: левой или правой.</summary>
    public UiRect BigFaceAt(bool left, bool text = false)
    {
        string id = $"IMG_{(left ? 'L' : 'R')}BIG{(text ? "TEXT" : "FACE")}";
        return _screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect
               ?? throw new KeyNotFoundException($"{_screen.Id}: нет места {id}");
    }

    /// <summary>Крупный портрет юнита: <c>{базовый}FACEB</c> из <c>Faces.ff</c> (<c>docs/battle-render.md</c> §3).</summary>
    public BattleSprite? BigFace(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        string name = _baseUnit(unit.Type.GlobalId) + "FACEB";
        var slot = BigFaceSlot(unit.Attacker);
        if (_gfx.Image("Faces", name) is { } image)
            return new BattleSprite("Faces", image.Name, slot.X, slot.Y, PanelLayer + 1, unit.Id);
        _missing.Add(name);
        return null;
    }

    /// <summary>Место слота боевого предмета лидера (<c>IMG_?ITEM1|2</c>).</summary>
    public UiRect ItemSlot(bool attacker, int index)
    {
        string id = $"IMG_{(Layout.LeftSide(attacker) ? 'L' : 'R')}ITEM{Math.Clamp(index, 0, 1) + 1}";
        return _screen.Elements.FirstOrDefault(e => e.Id == id)?.Rect
               ?? throw new KeyNotFoundException($"{_screen.Id}: нет места {id}");
    }

    /// <summary>Значок предмета в слоте: иконка <c>IconItem.ff</c> по типу предмета.</summary>
    public BattleSprite? ItemIcon(bool attacker, int index, string itemType)
    {
        if (itemType.Length == 0) return null;
        var slot = ItemSlot(attacker, index);
        if (_gfx.Image("IconItem", itemType) is { } image)
            return new BattleSprite("IconItem", image.Name, slot.X, slot.Y, PanelLayer + 1, itemType);
        return null;
    }

    /// <summary>Рамка под юнитом на поле: <c>PLY</c> — свой юнит, <c>SEL</c> — выбранный, <c>MRK</c> — метка цели
    /// (<c>docs/battle-render.md</c> §3). Размер — по юниту: малый, большой или «вся группа».</summary>
    public BattleSprite? Mark(BattleUnit unit, string kind, bool small = true)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentException.ThrowIfNullOrEmpty(kind);
        // Место большого — с признаком «отступает» (0x63e4c0/0x63e4e0): метка отступающего сдвигается вместе с ним.
        var slot = small ? Slot(unit.Attacker, unit.Slot) : BigSlot(unit.Attacker, unit.Slot, unit.Retreating);
        string name = kind + (small ? "SMALL" : "LARGE");
        // MRKCUR под ходящим — слой 190, ниже всех юнитов (0x646d91); прочие метки — под телом юнита, на слое его тени (0x646c14).
        int layer = kind == CurrentMark ? CurrentMarkLayer : Layers(unit.Attacker)[Math.Clamp(unit.Slot, 0, 5)] - 1;
        // Имя с «A» — анимация; нет её — берётся имя без «A» (0x5b3842).
        if (Animated && _gfx.Anim("Battle", name + "A") is { } anim)
            return MarkSprite("Battle", anim.Name, anim.W, anim.H, slot, layer, unit.Id);
        if (_gfx.Image("Battle", name) is { } image)
            return MarkSprite("Battle", image.Name, image.W, image.H, slot, layer, unit.Id);
        _missing.Add(name);
        return null;
    }

    /// <summary>Метка на пустой позиции (<c>EMPTYSELSMALL</c> — цель призыва, <c>0x64ce69</c>); нет кадра — null.</summary>
    public BattleSprite? MarkSlot(bool attacker, int slot, string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        var rect = Slot(attacker, slot);
        int layer = Layers(attacker)[Math.Clamp(slot, 0, 5)] - 1;
        if (Animated && _gfx.Anim("Battle", kind + "A") is { } anim) return MarkSprite("Battle", anim.Name, anim.W, anim.H, rect, layer, $"slot{slot}");
        if (_gfx.Image("Battle", kind) is { } image) return MarkSprite("Battle", image.Name, image.W, image.H, rect, layer, $"slot{slot}");
        _missing.Add(kind);
        return null;
    }

    /// <summary>Метка поля — центр картинки на середине нижнего края места (<c>0x637b48…0x637bc2</c>, <c>0x637ed4</c>, <c>0x63337a</c>;
    /// центр <c>0x63e180</c>): (центр − w/2, низ − h/2), ранг 1. Холст 800×600 — (центр − 400, низ − 300), кольцо у ног (снимок 28.09 f0).</summary>
    private static BattleSprite MarkSprite(string archive, string name, int w, int h, UiRect slot, int layer, string source) =>
        new(archive, name, Center(slot) - (w / 2), Bottom(slot) - (h / 2), layer, source);

    private static BattleSprite Sprite(string archive, string name, int w, int h, UiRect slot, int layer, string source)
    {
        var (x, y) = Place(slot, w, h);
        return new BattleSprite(archive, name, x, y, layer, source);
    }

    /// <summary>Эффект удара на цели (<c>TUCH</c>) — анимация из <c>BatUnits.ff</c> по типу бьющего (<c>docs/battle-render.md</c> §2). Сторона
    /// в имени: сначала общая <c>B</c>, затем своя (<c>A</c>/<c>D</c>); нет ни одной — эффекта нет.</summary>
    public BattleSprite? Effect(string attackerType, BattleUnit target) => Anim(_baseUnit(attackerType), "TUCH", 1, target);

    /// <summary>Эффект ходящего (<c>HEFF</c>, <c>0x5c6a1c</c>): оба слоя (<c>A1</c>, <c>A2</c>) — не на месте ходящего, а над <b>группой целей</b>
    /// (<c>0x639f2b</c>, ранг 1): сторона — у первой цели записи атаки (<c>0x62c2dd</c> → <c>0x63e370</c>, с учётом зеркала <c>0x63d239</c>),
    /// точка — <see cref="GroupPoint"/> её шести мест, кадр по якорю (400, 490) (<c>0x649656</c>). У <c>G000UU0009</c> это молнии над отрядом
    /// противника (замечание 4 партии 14.10: прежде холст клался на место мага — «над самим юнитом была, на половине противника не было»).
    /// Слои — общие для группы целей (<c>0x646ec5</c>: 1600 → 799 у левой и 1399 у правой, 1500 → 79 и 139). Сторона в имени — тоже группы
    /// целей, без зеркала (<c>0x635790</c> передаёт байт <c>0x63e370</c>, а <c>TUCH</c> — его зеркальную копию): у <c>A</c>/<c>D</c>-пар движение
    /// в кадрах идёт от ходящего к целям (<c>G000UU0180HEFFA1D00</c> — слева направо). Нет картинки — пусто.</summary>
    /// <param name="attackerType">Тип ходящего.</param>
    /// <param name="targetsAttackers">Цели — из группы нападающих (<see cref="BattleUnit.Attacker"/> первой цели).</param>
    public IReadOnlyList<BattleSprite> CasterEffects(string attackerType, bool targetsAttackers)
    {
        bool left = Layout.LeftSide(targetsAttackers);
        var (px, py) = GroupPoint(targetsAttackers);
        var sprites = new List<BattleSprite>(2);
        foreach (int layer in new[] { 1, 2 })
        {
            foreach (char side in new[] { 'B', targetsAttackers ? 'A' : 'D' })
            {
                if (_gfx.Anim("BatUnits", $"{_baseUnit(attackerType)}HEFFA{layer}{side}00") is not { } anim) continue;
                var (x, y) = PlaceAt(px, py, anim.W, anim.H);
                sprites.Add(new BattleSprite("BatUnits", anim.Name, x, y, layer == 1 ? (left ? 799 : 1399) : (left ? 79 : 139),
                                             targetsAttackers ? "attackers" : "defenders"));
                break;
            }
        }
        return sprites;
    }

    /// <summary>Эффект на цели <c>TUCH</c> атаки номер 1 или 2 (<c>0x63513a</c>, ранг 1): у основной атаки (<c>attackNumber == 1</c>) — свой у
    /// юнита (<c>0x6483b1</c>), у второй — только по классу атаки (<c>0x64845d</c>), своего юнита нет. В ванили по классу есть лишь <c>HEAL</c> и
    /// <c>BLIS</c>, поэтому вторая атака (яд ассасина) идёт без картинки на цели (замечание 3 партии 14.10: прежде нож ассасина
    /// <c>G000UU0154TUCH</c> летел дважды — и на ядовитом ударе).
    /// <para>Исключение второй атаки (<c>0x635321…0x6353da</c>, ранг 1): атака не предмета, раса ходящего (<c>IUsSoldier::getRaceId</c>, слот
    /// <c>+0xc</c>, → <c>Grace</c> → категория) — эльфы (<c>0x8372a0</c>), класс — лечение (<c>0x837490</c>): картинки на цели нет. Так лечение
    /// второй атакой у Лесной Девы и её ветки идёт без красного <c>HEALTUCH</c> (партия 15.10, пометки 3 и 5: «у девы анимация вампира»).</para></summary>
    public BattleSprite? Touch(string attackerType, AttackClass? attackClass, bool main, BattleUnit target)
    {
        if (main) return Effect(attackerType, target);
        if (attackClass is not { } c || c == AttackClass.Heal && IsElf(attackerType)) return null;
        return ClassEffect(c, target);
    }

    /// <summary>Подслой движка над местом юнита — в слой порта: тело юнита у движка на подслое 6 (<c>0x6537f6</c>), у порта — на базе места.
    /// Эффект на цели <c>TUCH</c> и тик яда — 7, эффекты <c>Battle.ff</c> и смерть — 12 (<c>0x6527d1…0x65292d</c>, ранг 1).</summary>
    public const int TouchSublayer = 7 - 6, EffectSublayer = 12 - 6, CorpseSublayer = 5 - 6;

    /// <summary>Эффект юнита из <c>Battle.ff</c> (<c>CBatUnitAnim::playEffect</c> <c>0x65255b</c>): <c>REVIVEANIM</c>, <c>TRANSF</c>, <c>DOPPEL</c>,
    /// <c>DRAINLVL</c>, откаты <c>U*</c>, <c>USUMMONANIM</c>, <c>UPGRADE{раса}</c>, тики <c>POISONANIM</c>/<c>FROSTBITEANIM</c>/<c>BLISTERANIM</c> —
    /// имя с хвостом <c>L</c>/<c>S</c> по размеру юнита, холст 800×600 на месте юнита (центр − 400, низ − 490, как смерть), подслой
    /// <paramref name="sublayer"/> (<see cref="EffectSublayer"/>, у тиков — <see cref="TouchSublayer"/>). Нет картинки — null.</summary>
    public BattleSprite? UnitEffect(string name, BattleUnit unit, int sublayer = EffectSublayer)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(unit);
        string full = name + (unit.Type.SizeSmall ? "S" : "L");
        var slot = unit.Type.SizeSmall ? Slot(unit.Attacker, unit.Slot) : BigSlot(unit.Attacker, unit.Slot);
        int layer = Layers(unit.Attacker)[Math.Clamp(unit.Slot, 0, 5)] + sublayer;
        if (_gfx.Anim("Battle", full) is { } anim) return Sprite("Battle", anim.Name, anim.W, anim.H, slot, layer, unit.Id);
        if (_gfx.Image("Battle", full) is { } image) return Sprite("Battle", image.Name, image.W, image.H, slot, layer, unit.Id);
        _missing.Add(full);
        return null;
    }

    /// <summary>Коды классов атаки для <c>TUCH</c> по классу (таблица <c>0x7b03a4</c>…, <c>docs/battle-render.md</c> §2). В ванили картинки
    /// есть только у <c>HEAL</c> и <c>BLIS</c>; остальные — для модов.</summary>
    private static readonly Dictionary<AttackClass, string> ClassCodes = new()
    {
        [AttackClass.Paralyze] = "PARA", [AttackClass.Petrify] = "PETR", [AttackClass.Damage] = "DAMA", [AttackClass.Drain] = "DRAN",
        [AttackClass.Heal] = "HEAL", [AttackClass.Fear] = "FEAR", [AttackClass.BoostDamage] = "BDAM", [AttackClass.LowerDamage] = "LDAM",
        [AttackClass.LowerInitiative] = "LINI", [AttackClass.Poison] = "POIS", [AttackClass.Frostbite] = "FBIT", [AttackClass.Blister] = "BLIS",
        [AttackClass.BestowWards] = "BWRD", [AttackClass.Shatter] = "SHAT", [AttackClass.Revive] = "RVIV", [AttackClass.DrainOverflow] = "DOVR",
        [AttackClass.Cure] = "CURE", [AttackClass.Summon] = "SUMM", [AttackClass.DrainLevel] = "DLVL", [AttackClass.GiveAttack] = "GATK",
        [AttackClass.TransformSelf] = "TSLF", [AttackClass.TransformOther] = "TOTH", [AttackClass.Doppelganger] = "DPEL",
    };

    /// <summary>Эффект на цели по классу атаки (<c>0x5c6a92</c>): <c>{КОД}TUCHA1{B|A|D}00</c>; нет картинки — null.</summary>
    public BattleSprite? ClassEffect(AttackClass attackClass, BattleUnit target) =>
        ClassCodes.TryGetValue(attackClass, out var code) ? Anim(code, "TUCH", 1, target) : null;

    /// <summary>Анимация <c>{префикс}{действие}A{слой}{B|A|D}00</c> на месте юнита <paramref name="at"/>, над ним (подслой движка 7 —
    /// <see cref="TouchSublayer"/>, <c>0x63a464</c>; другой — <paramref name="above"/>) из архива <paramref name="archive"/>.</summary>
    private BattleSprite? Anim(string prefix, string act, int layer, BattleUnit at, string archive = "BatUnits", int? above = null)
    {
        ArgumentNullException.ThrowIfNull(at);
        var slot = at.Type.SizeSmall ? Slot(at.Attacker, at.Slot) : BigSlot(at.Attacker, at.Slot, archive == "BatItems" && at.Retreating);
        int z = Layers(at.Attacker)[Math.Clamp(at.Slot, 0, 5)] + (above ?? TouchSublayer);
        foreach (char side in new[] { 'B', Layout.LeftSide(at.Attacker) ? 'A' : 'D' })
        {
            string name = $"{prefix}{act}A{layer}{side}00";
            if (_gfx.Anim(archive, name) is { } anim)
                return Sprite(archive, anim.Name, anim.W, anim.H, slot, z, at.Id);
        }
        return null;
    }

    /// <summary>«Юнит» анимации применения боевого предмета по категории (<c>0x682eb0</c>): зелья (4–7) — <c>G000IG1111</c>, сфера —
    /// <c>G000IG2222</c>, талисман — <c>G000IG3333</c>; прочее — нет (не <c>GItem.UNIT_ID</c>, ранг 1).</summary>
    public static string? ItemAnimation(D2.Rules.Magic.ItemCategory category) => category switch
    {
        D2.Rules.Magic.ItemCategory.PotionBoost or D2.Rules.Magic.ItemCategory.PotionHeal or D2.Rules.Magic.ItemCategory.PotionRevive
            or D2.Rules.Magic.ItemCategory.PotionPermanent => "G000IG1111",
        D2.Rules.Magic.ItemCategory.Orb => "G000IG2222",
        D2.Rules.Magic.ItemCategory.Talisman => "G000IG3333",
        _ => null,
    };

    /// <summary>Анимация применения предмета (<c>BatItems.ff</c>, <c>{id}HMOVA1B00</c> — общая для сторон, <c>0x5c76dd</c>): на месте
    /// применившего, на слое его тела (<c>0x651fc6</c>), один проход; замаха носителя нет (<c>0x6365f2</c>). Нет картинки — null.</summary>
    public BattleSprite? ItemEffect(D2.Rules.Magic.ItemCategory category, BattleUnit bearer) =>
        ItemAnimation(category) is { } id ? Anim(id, "HMOV", 1, bearer, "BatItems", above: 0) : null;

    /// <summary>Анимация смерти: <c>DEATH_{РАСА}_{S|L}{кадров}</c> из <c>Battle.ff</c> — число в имени это число
    /// кадров, поэтому ищется по началу имени (<c>docs/battle-render.md</c> §2).</summary>
    public BattleSprite? Death(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_deathRace is null) return null;
        string race = _deathRace(unit.Type.GlobalId);
        if (race.Length == 0) return null;
        string prefix = $"DEATH_{race}_{(unit.Type.SizeSmall ? 'S' : 'L')}";
        var anim = _gfx.Archive("Battle")?.Anims
            .FirstOrDefault(a => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (anim is null) return null;
        var slot = unit.Type.SizeSmall ? Slot(unit.Attacker, unit.Slot) : BigSlot(unit.Attacker, unit.Slot);
        int layer = Layers(unit.Attacker)[Math.Clamp(unit.Slot, 0, 5)] + EffectSublayer;   // смерть — код 1 того же playEffect
        return Sprite("Battle", anim.Name, anim.W, anim.H, slot, layer, unit.Id);
    }

    /// <summary>Раса в имени трупа — всегда <c>HUMAN</c>: загрузчик <c>0x5c626d</c> берёт аргумент расы <c>[ebp+0x10]</c>, но не читает его, а
    /// зовёт перевод расы смерти в строку (<c>0x5c636c</c>) с постоянной <c>0x8382b8</c> — объектом <c>L_HUMAN</c> (его поле +8 — та самая
    /// <c>[0x8382c0]</c>, с которой перевод сравнивает «HUMAN»). Загрузчик смерти <c>0x5c6428</c> тот же перевод зовёт с аргументом
    /// (<c>push [ebp+0x10]</c>). Ранг 1, дизассемблирование 11.10.</summary>
    public const string CorpseRace = "HUMAN";

    /// <summary>Труп павшего: <c>DEAD_HUMAN_{S|L}A{вариант:00}</c> из <c>Battle.ff</c> — кости одни на все расы (<see cref="CorpseRace"/>;
    /// прежде раса бралась из <c>Gunits.DEATH_ANIM</c>, и у не-людей костей не было вовсе — пункт 4 прогона 2, снимок 35). Место — не по
    /// общему правилу кадра, а как у меток: центр картинки на середине нижнего края места, (центр − w/2, низ − h/2)
    /// (<c>0x653887…0x6539fc</c>, ранг 1); подслой 5 — под телом юнита своего места (тело — подслой 6 = база слоя места). Картинка 90×180,
    /// кости — в её середине (строки 73–108): по правилу «низ − h» (как узкий кадр юнита) кости поднимались на 90 точек — на чужую половину
    /// поля (замечание 12 прогона 11.10).</summary>
    public BattleSprite? Corpse(BattleUnit unit, int variant = 0)
    {
        ArgumentNullException.ThrowIfNull(unit);
        string name = $"DEAD_{CorpseRace}_{(unit.Type.SizeSmall ? 'S' : 'L')}A{variant:00}";
        var slot = unit.Type.SizeSmall ? Slot(unit.Attacker, unit.Slot) : BigSlot(unit.Attacker, unit.Slot);
        int layer = Layers(unit.Attacker)[Math.Clamp(unit.Slot, 0, 5)] + CorpseSublayer;
        if (_gfx.Image("Battle", name) is { } image)
            return MarkSprite("Battle", image.Name, image.W, image.H, slot, layer, unit.Id);
        if (_gfx.Anim("Battle", name) is { } anim)
            return MarkSprite("Battle", anim.Name, anim.W, anim.H, slot, layer, unit.Id);
        return null;   // у этой расы трупа в архиве нет
    }

    /// <summary>Сколько вариантов фона у места (<c>{МЕСТО}_{n}_BG</c> в графике): движок берёт <c>rand() % n</c> из MSVCRT
    /// (<c>0x5b35ab</c> → <c>0x548576</c>; у мест с тремя вариантами n = 3, у прочих 1 — <c>rand()</c> зовётся и тогда). Снимки 28.09:
    /// два боя одного места — <c>NEUTRAL_1_BG</c> и <c>NEUTRAL_2_BG</c>.</summary>
    public static int BackgroundVariants(GfxAssets gfx, string place)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        int n = 0;
        while (gfx.Anim("Battle", $"{place}_{n}_BG") is not null || gfx.Image("Battle", $"{place}_{n}_BG") is not null) n++;
        return Math.Max(1, n);
    }

    /// <summary>Фон боя: <c>{МЕСТО}_{вариант}_BG</c> из <c>Battle.ff</c>, холст 950×600 — шире экрана.</summary>
    public BattleSprite? Background(string place, int variant = 0) => PlaceLayer(place, variant, "BG", BackgroundLayer, required: true);

    /// <summary>Слой тени места (<c>{МЕСТО}_{вариант}_SHADOW</c>) — над фоном, под всеми юнитами (<c>0x646a6f</c>: слой 0x8a).</summary>
    public const int PlaceShadowLayer = 138;

    /// <summary>Все слои места боя (загрузчик <c>0x5b34f6</c>: <c>"%s_%d_%s"</c> с <c>BG</c>, <c>SHADOW</c>, <c>FG</c>; раскладка <c>0x646a6f</c>):
    /// фон на слое 10, тень на 138, передний план на 2000 — над юнитами (у <c>UNDEAD_0</c> это шпиль с черепами, которым закрыта дыра
    /// мадженты в фоне). Все три кладутся в одну точку x (<see cref="BattleLayout.BackgroundX"/>, <c>0x646b08</c>). Тени и переднего
    /// плана у большинства мест нет — это не пропуск.</summary>
    public IReadOnlyList<BattleSprite> PlaceLayers(string place, int variant = 0) =>
        [.. new[]
        {
            PlaceLayer(place, variant, "BG", BackgroundLayer, required: true),
            PlaceLayer(place, variant, "SHADOW", PlaceShadowLayer, required: false),
            PlaceLayer(place, variant, "FG", ForegroundLayer, required: false),
        }.OfType<BattleSprite>()];

    private BattleSprite? PlaceLayer(string place, int variant, string part, int layer, bool required)
    {
        string name = $"{place}_{variant}_{part}";
        if (_gfx.Anim("Battle", name) is { } anim)
            return new BattleSprite("Battle", anim.Name, Layout.BackgroundX, 0, layer, "фон");
        if (_gfx.Image("Battle", name) is { } image)
            return new BattleSprite("Battle", image.Name, Layout.BackgroundX, 0, layer, "фон");
        if (required) _missing.Add(name);
        return null;
    }

    /// <summary>Рамки целей «лечебного» вида <c>HEA*</c> (иначе <c>SEL</c>/<c>ISEL</c>, красные): <c>0x63086e</c> → <c>0x630890</c> сравнивает класс
    /// атаки с шестью — <c>L_HEAL</c> <c>0x837490</c>, <c>L_CURE</c> <c>0x8374d0</c>, <c>L_BOOST_DAMAGE</c> <c>0x8374c0</c>, <c>L_REVIVE</c>
    /// <c>0x837440</c>, <c>L_BESTOW_WARDS</c> <c>0x837470</c>, <c>L_GIVE_ATTACK</c> <c>0x837530</c> (имена — загрузчик классов <c>0x584c0f</c>).
    /// Двойник, превращение себя и призыв в этот список не входят (снимок оригинала 19.10: у Двойника рамки целей красные), хотя бьют и
    /// своих (<see cref="Attacks.TargetsAllies"/>).</summary>
    public static bool SupportFrame(AttackClass cls) =>
        cls is AttackClass.Heal or AttackClass.Cure or AttackClass.BoostDamage or AttackClass.Revive or AttackClass.BestowWards
            or AttackClass.GiveAttack;

    /// <summary>Показываются ли боевые предметы при ходе юнита (<c>updateBattleItems</c> <c>0x6383d0</c> → <c>0x649775</c>): только у
    /// ходящего, только на ходу человека (<paramref name="humanTurn"/>: <c>0x638425</c> — без набора целей от сервера,
    /// <c>data->targetData</c> <c>+0xfa0</c>, предметы не ставятся; набор приходит только хозяину ходящего — человеку; ответ пользователя
    /// 11.10: у лидера ИИ на его ходу предметов нет), и только если он лидер отряда (приведение к <c>IUsStackLeader</c>, <c>0x40a41f</c>) и
    /// не превращён (<c>CMidUnit::transformed</c>, <c>+0x28</c>, <c>0x62a120</c>). У прочих юнитов слоты предметов пусты.</summary>
    public static bool ShowsItems(BattleUnit? unit, bool humanTurn) =>
        humanTurn && unit is { BattleItems.Count: > 0 } && unit.Type.IsLeader && !unit.IsTransformed;

    /// <summary>Слоты боевых предметов ходящего, у которых виден значок (и которые ловят мышь): при <see cref="ShowsItems"/> — первые два,
    /// кроме применённых в этом бою. Движок (<c>0x649939</c> из <c>updateBattleItems</c> <c>0x6384c4</c>) кладёт в слот пустой id, если
    /// предмет уже в <c>BattleMsgData.usedItemIds</c> (<c>+0xf20</c>, проверка <c>0x622fee</c>), и пустой слот пропускает (<c>0x638513</c>):
    /// выпитое зелье или брошенная сфера из слота уходят сразу, а не в конце боя. Прогон 15.10 (пометка 4): зелье после питья оставалось
    /// в слоте до конца боя.</summary>
    public static IEnumerable<int> ShownItems(BattleUnit? unit, bool humanTurn) =>
        ShowsItems(unit, humanTurn) ? Enumerable.Range(0, Math.Min(2, unit!.BattleItems.Count)).Where(i => !unit.AppliedItems.Contains(i)) : [];

    /// <summary>Юнит под точкой боковой панели: портрет (70×85, у большого — по центру пары мест) групп, которые сейчас показаны
    /// (<c>CBatUnitGroup2::getSelectedUnitId</c> <c>0x64c393</c>: выключенная группа мышь не ловит, <c>0x64c9a4</c>). Щелчок по портрету — то же,
    /// что по юниту на поле (поиск цели <c>0x631318</c> идёт по группам панелей).</summary>
    public BattleUnit? PanelUnitAt(IEnumerable<BattleUnit> units, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(units);
        foreach (var unit in units)
        {
            if (!PanelShown(unit.Attacker)) continue;
            var face = PanelFace(unit, unit.Type.SizeSmall ? 70 : BattlePanel.BigWidth);
            if (x >= face.X && y >= face.Y && x < face.Right && y < face.Bottom) return unit;
        }
        return null;
    }

    /// <summary>Место панели под точкой: позиция группы стороны <paramref name="attacker"/>, чьё место (<c>IMG_?SLOT</c>, 70×85) содержит точку;
    /// панель стороны не показана — null. У движка <c>getSelectedUnitId</c> панели (<c>0x64c393</c>) сначала обходит позиции-цели группы
    /// (<c>+0x10</c>, при тащении предмета — <c>+0x20</c>): прямоугольник 70×85 (<c>0x6ebedc</c>/<c>0x6ebee0</c>) по центру места
    /// (<c>+0x54</c>/<c>+0x58</c>), попадание <c>0x63e460</c> — и отдаёт id позиции (<c>0x64416b</c>) с признаком «годится»: щелчок по месту
    /// в панели выбирает цель-позицию так же, как щелчок по клетке поля (призыв, сфера призыва).</summary>
    public int? PanelPositionAt(bool attacker, int x, int y)
    {
        if (!PanelShown(attacker)) return null;
        for (int position = 0; position < Formation.Cells; position++)
        {
            var slot = PanelSlot(attacker, position);
            if (x >= slot.X && y >= slot.Y && x < slot.Right && y < slot.Bottom) return position;
        }
        return null;
    }

    /// <summary>Юнит под точкой экрана: портрет в показанной панели (<see cref="PanelUnitAt"/> — панель лежит поверх поля) или зона мыши места
    /// на поле (<see cref="FieldUnitAt"/>); <paramref name="withFallen"/> — и павшие (цель зелья воскрешения: и на поле, <c>0x63885e</c>, и в
    /// панели, <c>0x63911f</c>), но живой на той же клетке — раньше: призванный на место павшего стоит в конце списка юнитов боя, и щелчок,
    /// бросок предмета и портрет брали бы кости вместо него.</summary>
    public BattleUnit? UnitAt(IReadOnlyCollection<BattleUnit> units, int x, int y, bool withFallen = false)
    {
        ArgumentNullException.ThrowIfNull(units);
        var living = units.Where(u => u.OnField).ToList();
        if (!withFallen) return PanelUnitAt(living, x, y) ?? FieldUnitAt(living, x, y);
        var fallen = units.Where(u => !u.OnField && !u.Retreated && !u.Hidden).ToList();
        return PanelUnitAt(living, x, y) ?? PanelUnitAt(fallen, x, y) ?? FieldUnitAt(living, x, y) ?? FieldUnitAt(fallen, x, y);
    }

    private void Add(List<BattleSprite> sprites, BattleUnit unit, BattleAction action, UiRect slot, int z,
                     bool shadow, int layerNo, bool quiet = false)
    {
        if (action == BattleAction.Idle && StillIdle) action = BattleAction.Still;
        string name = NameOf(unit.Type.GlobalId, action, unit.Attacker, shadow, layerNo);
        var art = _gfx.Anim("BatUnits", name);
        // Нет STIL — покой IDLE (запасной, ранг 3: у движка такой картинки нет — не проверено).
        if (art is null && action == BattleAction.Still) art = _gfx.Anim("BatUnits", name = NameOf(unit.Type.GlobalId, BattleAction.Idle, unit.Attacker, shadow, layerNo));
        if (art is null)
        {
            if (!quiet) _missing.Add(name);
            return;
        }
        var (x, y) = Place(slot, art.W, art.H);
        sprites.Add(new BattleSprite("BatUnits", art.Name, x, y, z, unit.Id, shadow));
    }

    /// <summary>Слои по стороне группы на поле (<c>0x6469a3</c>).</summary>
    private int[] Layers(bool attacker) => Layout.LeftSide(attacker) ? LeftLayers : RightLayers;

    private static int Center(UiRect rect) => rect.X + (rect.Width / 2);

    private static int Bottom(UiRect rect) => rect.Y + rect.Height;
}
