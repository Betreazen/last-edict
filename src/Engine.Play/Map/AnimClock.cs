namespace D2.Data.Map;

/// <summary>
/// Группа часов анимации — две скорости общего аниматора движка (<c>CTwoSpeedAnimator</c>, <c>MidAnim2System.cpp</c>): медленная — кадр раз в
/// 66 мс (<c>0x51d871</c>: <c>mov [esi+0x34], 0x42</c>), быстрая — раз в 33 мс (<c>0x51d929</c>: <c>0x21</c>). Тик (<c>0x51e0d0</c>): если
/// <c>now ≥ next</c>, каждый член группы делает один шаг, затем <c>next = now + период</c> — пропущенные тики не догоняются.
/// <para>Кто в какой группе (разбор 16.10). Картинки карты (<c>FactoryImageAnim</c>, <c>0x52def4</c> → слот 0), вода, облака, курсоры, бой
/// (<c>CBatUnitAnim</c>, <c>0x6471b6</c>…), окна интерфейса — медленная. Задачи системы анимаций карты (<c>CMidAnim2System::Impl</c>,
/// <c>0x40ab78</c>) держат два <c>CTwoSpeedAnimator</c> (<c>0x40ac86</c>: слот 1 аниматора — быстрая, 0 — медленная) и шагают текущую задачу
/// только на тике её группы (<c>0x40ae5f</c>: байт задачи <c>[[this+4]]</c> через <c>0x4f11dc</c> против флага тика). Байт ставит
/// конструктор <c>CMidAnim2Base</c> (<c>0x4f1137</c>): <c>CMidAnimSmoothScroll</c> — 1 (<c>0x4f4920</c>), <c>CMidAnim2Unit</c> (ход отряда) —
/// флажок <c>StackSmoothScroll</c> (<c>0x4f5e7c</c> ← <c>0x61a5fd</c>), остальные (<c>Appear</c>, <c>ChangeFog</c>, <c>Map</c>/<c>Terrain</c>,
/// <c>Shake</c>, <c>Sound</c>, <c>TakeBag</c>, <c>LootRuin</c>, <c>CityGrow</c> и все наследники <c>CMidAnim2Animation</c>: литьё, гибель,
/// создание, смена владельца, горы, начало боя) — 0. Вне карты быстрая группа — у чата (<c>CIsoChatDisplay</c>, <c>0x487093</c>), титров
/// (<c>CMenuCredits</c>, <c>0x4db0f7</c>) и прокрутки текста (<c>CScrollTextBoxInterf</c>, <c>0x533dbb</c>) — в порте их нет.</para>
/// </summary>
public enum AnimGroup
{
    /// <summary>66 мс на шаг.</summary>
    Slow,

    /// <summary>33 мс на шаг.</summary>
    Fast,
}

/// <summary>Периоды групп и выбор группы для задач карты (<see cref="AnimGroup"/>).</summary>
public static class AnimClock
{
    public const int SlowMs = 66, FastMs = 33;

    /// <summary>Период группы в мс.</summary>
    public static int PeriodMs(AnimGroup group) => group == AnimGroup.Fast ? FastMs : SlowMs;

    /// <summary>Группа хода отряда (<c>CMidAnim2Unit</c>, <c>0x4f5e7c</c>): быстрая при <c>StackSmoothScroll</c> — тогда и шаг кадра вдвое короче
    /// (<see cref="StackWalk.PixelsPerKeyframe"/>), так что скорость в точках за секунду та же, а движение плавнее.</summary>
    public static AnimGroup StackWalk(bool stackSmoothScroll) => stackSmoothScroll ? AnimGroup.Fast : AnimGroup.Slow;

    /// <summary>Группа плавной прокрутки вида (<c>CMidAnimSmoothScroll</c>, <c>0x4f4920</c>: <c>push 1</c>) — всегда быстрая.</summary>
    public const AnimGroup SmoothScroll = AnimGroup.Fast;
}

/// <summary>Часы одной группы (<see cref="AnimGroup"/>): <see cref="Advance"/> копит время кадров и отвечает, пришёл ли тик; на тике лишнее
/// время сбрасывается (<c>next = now + период</c>, <c>0x51e23b</c>) — после долгого кадра шаг один, а не несколько.</summary>
public sealed class AnimTicker(AnimGroup group)
{
    private double _sinceMs;

    public AnimGroup Group { get; set; } = group;

    /// <summary>Период в мс.</summary>
    public int PeriodMs => AnimClock.PeriodMs(Group);

    /// <summary>Прошло <paramref name="deltaMs"/> мс: пришёл ли тик группы.</summary>
    public bool Advance(double deltaMs)
    {
        _sinceMs += Math.Max(0, deltaMs);
        if (_sinceMs < PeriodMs) return false;
        _sinceMs = 0;
        return true;
    }

    /// <summary>Начать отсчёт заново (новая задача: следующий тик — через полный период).</summary>
    public void Reset() => _sinceMs = 0;
}
