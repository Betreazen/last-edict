namespace LastEdict.Rules.Map;

/// <summary>
/// Грунт клетки — старшие биты байта типа клетки.
/// Ранг 1: значения прочитаны из <c>Globals/Lground.DBF</c>
/// (<c>L_PLAIN 0</c>, <c>L_FOREST 1</c>, <c>L_WATER 3</c>, <c>L_MOUNTAIN 4</c>;
/// id 2 в таблице отсутствует).
/// </summary>
public enum Ground
{
    Plain = 0,
    Forest = 1,
    Water = 3,
    Mountain = 4,
}

/// <summary>
/// Клетка карты — один байт <c>Tile.Type</c> из <c>MidgardMapBlock</c>.
/// Ранг 1, два независимых источника: разбор питоновского читателя
/// <c>tools/sgfile.py:322-336</c> (биты 0-2 — террейн <c>Lterrain</c> 1..6, биты 3+ — грунт
/// <c>Lground</c>; проверено на 33 картах) и структура движка <c>CMidgardMapBlock</c>
/// (<c>D2ModdingToolset include/midgardmapblock.h:37-50</c>: декодеры <c>tileTerrain</c> биты 0-2,
/// <c>tileGround</c> биты 3-5).
/// </summary>
public readonly record struct Tile(byte Raw)
{
    /// <summary>Раса-владелец ландшафта (<c>Lterrain</c>, 1..6). На проходимость не влияет.</summary>
    public int Terrain => Raw & 0b111;

    /// <summary>Грунт — биты 3-5 (<c>tileGround</c>), старшие биты байта в декодер не входят.</summary>
    public Ground Ground => (Ground)((Raw >> 3) & 0b111);

    /// <summary>
    /// Ранг 3 (гипотеза): пешему отряду доступны равнина и лес, вода и горы — нет.
    /// Настоящая проходимость зависит от флагов отряда (водный отряд ходит по воде) и считается
    /// движковой <c>stackCanMoveToPosition</c>; здесь — только грубый признак грунта.
    /// Точная проверка — задача 5 (<c>Movement.Passable</c> с <c>MoveOptions</c>).
    /// </summary>
    public bool Passable => Ground is Ground.Plain or Ground.Forest;
}

/// <summary>
/// Казна или цена: золото и пять мана. Ранг 1: порядок валют — из полей Kaitai-записи
/// <c>bank_record</c> (<c>Gold, InfernalMana, LifeMana, DeathMana, RunicMana, GroveMana</c>),
/// буквы — из строкового вида <c>BankApi::toString</c> <c>"G%04d:R%04d:Y%04d:E%04d:W%04d:B%04d"</c>.
/// Сверено с <c>Globals/Grace.dbf</c>: у Империи (<c>g000rr0000</c>) доход
/// <c>g0100:r0000:y0025:e0000:w0000</c> — мана жизни, у Эльфов (<c>g000rr0005</c>)
/// <c>...:b0025</c> — мана рощи, у Легионов <c>r0025</c>, у Кланов <c>w0025</c>,
/// у Орд <c>e0025</c>. Шестая валюта (<c>:b</c>) есть только в файлах с сигнатурой
/// <c>D2EESFISIG</c> (Rise of the Elves).
/// <para>Валюты — строковые id (<see cref="Builtin"/>, этап Э2, задача E): шесть валют оригинала — поля (на них стоят правила, окна и
/// формат сохранений), любые другие (мода) — словарь <see cref="Extra"/>. Вся арифметика казны идёт по всем валютам: поэлементные
/// <see cref="Map(Func{long, long})"/> и <see cref="Zip"/> (обрезка, доли, умножение), сложение, вычитание, <see cref="Covers"/>; формулы
/// оригинала с разным правилом для золота и маны (доход ИИ) считают валюту мода маной. У партий оригинала <see cref="Extra"/> пуст.</para>
/// </summary>
public sealed record Bank(long Gold, long Infernal, long Life, long Death, long Runic, long Grove)
{
    public static readonly Bank Zero = new(0, 0, 0, 0, 0, 0);

    /// <summary>Валюты оригинала: золото и пять видов маны, в порядке букв казны <c>g r y e w b</c>.</summary>
    public static readonly IReadOnlyList<string> Builtin = ["gold", "infernal", "life", "death", "runic", "grove"];

    /// <summary>Валюты сверх шести оригинала (мода): id → количество, без нулей, id с учётом регистра, по порядку id; пусто — null (так у всех
    /// партий оригинала). Присвоенный словарь копируется в этот вид (чужой изменяемый словарь казна не держит).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, long>? Extra
    {
        get => _extra;
        init => _extra = Normalize(value);
    }

    private readonly IReadOnlyDictionary<string, long>? _extra;

    /// <summary>Каждая валюта — <paramref name="f"/>(количество): шесть полей и валюты мода (нули выбрасываются).</summary>
    public Bank Map(Func<long, long> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new Bank(f(Gold), f(Infernal), f(Life), f(Death), f(Runic), f(Grove))
        {
            Extra = Extra?.Select(p => new KeyValuePair<string, long>(p.Key, f(p.Value))).ToDictionary(StringComparer.Ordinal),
        };
    }

    /// <summary>Каждая валюта — <paramref name="f"/>(id, количество).</summary>
    public Bank Map(Func<string, long, long> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new Bank(f("gold", Gold), f("infernal", Infernal), f("life", Life), f("death", Death), f("runic", Runic), f("grove", Grove))
        {
            Extra = Extra?.Select(p => new KeyValuePair<string, long>(p.Key, f(p.Key, p.Value))).ToDictionary(StringComparer.Ordinal),
        };
    }

    /// <summary>Попарно по всем валютам двух казён (валюта, которой у одной нет, — 0): <paramref name="f"/>(эта, другая).</summary>
    public Bank Zip(Bank other, Func<long, long, long> f)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(f);
        var extra = (Extra?.Keys ?? []).Union(other.Extra?.Keys ?? [], StringComparer.Ordinal)
                                       .ToDictionary(c => c, c => f(this[c], other[c]), StringComparer.Ordinal);
        return new Bank(f(Gold, other.Gold), f(Infernal, other.Infernal), f(Life, other.Life), f(Death, other.Death), f(Runic, other.Runic),
                        f(Grove, other.Grove)) { Extra = extra };
    }

    /// <summary>Количество валюты по id; неизвестная — 0.</summary>
    public long this[string currency] => currency switch
    {
        "gold" => Gold, "infernal" => Infernal, "life" => Life, "death" => Death, "runic" => Runic, "grove" => Grove,
        _ => Extra?.GetValueOrDefault(currency) ?? 0,
    };

    /// <summary>Та же казна с другим количеством валюты.</summary>
    public Bank With(string currency, long amount)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return currency switch
        {
            "gold" => this with { Gold = amount }, "infernal" => this with { Infernal = amount }, "life" => this with { Life = amount },
            "death" => this with { Death = amount }, "runic" => this with { Runic = amount }, "grove" => this with { Grove = amount },
            _ => this with { Extra = (Extra ?? new Dictionary<string, long>()).Where(p => p.Key != currency).Append(new(currency, amount))
                                                                               .ToDictionary(StringComparer.Ordinal) },
        };
    }

    /// <summary>Ненулевые валюты по порядку: шесть оригинала, затем прочие по id.</summary>
    public IEnumerable<KeyValuePair<string, long>> Amounts() =>
        Builtin.Select(c => new KeyValuePair<string, long>(c, this[c]))
               .Concat(Extra?.OrderBy(p => p.Key, StringComparer.Ordinal) ?? Enumerable.Empty<KeyValuePair<string, long>>())
               .Where(p => p.Value != 0);

    /// <summary>Хватает ли казны на цену <paramref name="cost"/>: каждой валюты не меньше (не та мана не выручает).</summary>
    public bool Covers(Bank cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        return Gold >= cost.Gold && Infernal >= cost.Infernal && Life >= cost.Life && Death >= cost.Death && Runic >= cost.Runic
               && Grove >= cost.Grove && (cost.Extra is null || cost.Extra.All(p => this[p.Key] >= p.Value));
    }

    public static Bank operator +(Bank a, Bank b) => a.Extra is null && b.Extra is null
        ? new(a.Gold + b.Gold, a.Infernal + b.Infernal, a.Life + b.Life, a.Death + b.Death, a.Runic + b.Runic, a.Grove + b.Grove)
        : a.Zip(b, (x, y) => x + y);

    public static Bank operator -(Bank a, Bank b) => a.Extra is null && b.Extra is null
        ? new(a.Gold - b.Gold, a.Infernal - b.Infernal, a.Life - b.Life, a.Death - b.Death, a.Runic - b.Runic, a.Grove - b.Grove)
        : a.Zip(b, (x, y) => x - y);

    /// <summary>Словарь валют мода в вид казны: по порядку id (с учётом регистра), без нулей; пусто — null (одна и та же казна — одна и та же
    /// запись).</summary>
    private static SortedDictionary<string, long>? Normalize(IEnumerable<KeyValuePair<string, long>>? extra)
    {
        if (extra is null) return null;
        var sorted = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (currency, amount) in extra)
            if (amount != 0) sorted[currency] = amount;
        return sorted.Count == 0 ? null : sorted;
    }

    /// <summary>Печать как у записи из шести полей; валюты мода — только если есть (строки харнессов и отчётов прежние).</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"Gold = {Gold}, Infernal = {Infernal}, Life = {Life}, Death = {Death}, Runic = {Runic}, Grove = {Grove}");
        if (Extra is not null) builder.Append(", Extra = ").AppendJoin(", ", Extra.Select(p => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{p.Key}: {p.Value}")));
        return true;
    }

    public bool Equals(Bank? other) =>
        other is not null && Gold == other.Gold && Infernal == other.Infernal && Life == other.Life && Death == other.Death
        && Runic == other.Runic && Grove == other.Grove
        && (Extra?.Count ?? 0) == (other.Extra?.Count ?? 0) && (Extra is null || Extra.All(p => other[p.Key] == p.Value));

    public override int GetHashCode() =>
        HashCode.Combine(Gold, Infernal, Life, Death, Runic, Grove, Extra?.Count ?? 0);

    /// <summary>
    /// Разбирает строковую форму <c>g0100:r0000:y0025:e0000:w0000[:b0025]</c> (регистр не важен,
    /// пробелы по краям отбрасываются). Отсутствующая валюта — ноль, порядок частей не навязан:
    /// решает буква. Неизвестная буква или нечисловая величина — <see cref="FormatException"/>.
    /// </summary>
    public static Bank Parse(string s)
    {
        long gold = 0, infernal = 0, life = 0, death = 0, runic = 0, grove = 0;
        foreach (var part in s.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = part.Trim();
            if (token.Length < 2)
                throw new FormatException($"пустая часть казны в \"{s}\"");
            if (!long.TryParse(token.AsSpan(1), out var amount))
                throw new FormatException($"нечисловая величина в \"{token}\" (казна \"{s}\")");
            switch (char.ToLowerInvariant(token[0]))
            {
                case 'g': gold = amount; break;
                case 'r': infernal = amount; break;
                case 'y': life = amount; break;
                case 'e': death = amount; break;
                case 'w': runic = amount; break;
                case 'b': grove = amount; break;
                default: throw new FormatException($"неизвестная валюта '{token[0]}' в \"{s}\"");
            }
        }
        return new Bank(gold, infernal, life, death, runic, grove);
    }
}

/// <summary>Отряд на карте (<c>CMidStack</c>). Всё — прямая проекция полей файла, ранг 1.</summary>
public sealed record MapStack
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public required string Subrace { get; init; }
    public required string LeaderId { get; init; }
    public bool LeaderAlive { get; init; }
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>Остаток очков хода на этот день (<c>MOVE</c>).</summary>
    public int Move { get; init; }

    /// <summary>Id объекта, внутри которого стоит отряд (город, руина); пусто — на карте.</summary>
    public required string Inside { get; init; }

    /// <summary>Id юнитов отряда в порядке полей <c>UNIT_0..UNIT_5</c>, пустые ссылки пропущены.</summary>
    public required IReadOnlyList<string> UnitIds { get; init; }

    /// <summary>
    /// Шесть КЛЕТОК построения (<c>POS_0..POS_5</c>): в клетке <c>k</c> лежит ИНДЕКС юнита
    /// в <see cref="UnitIds"/>, <c>-1</c> — клетка пуста. Не наоборот: это карта
    /// клетка → юнит, а не юнит → клетка.
    /// Чётные клетки 0/2/4 — передний ряд, нечётные 1/3/5 — задний
    /// (ранг 1: <c>disciples-mod tools/d2mod/core/sg.py:220-222</c>, которым отряды пишутся
    /// и которые игра принимает).
    /// Крупный юнит занимает две клетки одной колонки, и его индекс стоит в обеих: у отряда
    /// <c>S001KC0003</c> карты <c>'A Return To Simpler Times'.sg</c> один юнит и
    /// <c>POS = [-1,-1,0,0,-1,-1]</c>. Поэтому занятых клеток бывает больше, чем юнитов.
    /// В файле пустая клетка — <c>0xFFFFFFFF</c>, поэтому величина приводится к <see cref="int"/>.
    /// </summary>
    public required IReadOnlyList<int> Slots { get; init; }

    /// <summary>Инвентарь отряда (<c>MidStack.ITEM_ID</c>) — экземпляры предметов <c>IM</c>, пустые ссылки
    /// выброшены. Тип экземпляра — <see cref="MapState.ItemTypes"/>. Ранг 1.</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary>Шаблон, из которого создан отряд (<c>SRCTMPL_ID</c>, <c>CMidStack +0x24</c>): по нему события сценария узнают
    /// «экземпляры шаблона» (<c>L_STACK_EXISTS</c>, <c>L_KILL_STACK</c>, <c>L_CHANGE_ORDER</c>). Пусто — не из шаблона. Ранг 1 — поле.</summary>
    public string TemplateId { get; init; } = "";

    /// <summary>Семь слотов снаряжения в порядке полей записи: 0 <c>BANNER</c>, 1 <c>TOME</c>, 2 <c>BATTLE1</c>,
    /// 3 <c>BATTLE2</c>, 4 <c>ARTIFACT1</c>, 5 <c>ARTIFACT2</c>, 6 <c>BOOTS</c>; пустой слот — пустая строка.
    /// Ранг 1: порядок полей <c>sg.ksy</c> (mid_stack) совпадает с номерами слотов плана Ф7 §1.5 (0 знамя,
    /// 1 книга, 2–3 бой, 4–5 артефакты, 6 сапоги). У отряда из <c>.sg</c> всегда семь; у синтетического —
    /// пусто.</summary>
    public IReadOnlyList<string> Equipment { get; init; } = [];

    /// <summary>Приказ (<c>ORDER</c>, <c>LOrder</c>; 10 — <c>L_BEZERK</c> у отрядов погибшего игрока, план Ф7
    /// §1.9). Ранг 1 — поле.</summary>
    public int Order { get; init; }

    /// <summary>Цель приказа (<c>ORDER_TARG</c>), пусто — нет.</summary>
    public string OrderTarget { get; init; } = "";

    /// <summary>Приказ ИИ (<c>AIORDER</c>, <c>CMidStack +0xb8</c>): при <c>L_NORMAL</c> в <see cref="Order"/>
    /// движок берёт его (<see cref="AiTacticLists.EffectiveOrder"/>). Ранг 1 — поле.</summary>
    public int AiOrder { get; init; }

    /// <summary>Цель приказа ИИ (<c>AIORDERTAR</c>, <c>+0xbc</c>), пусто — нет. Ранг 1 — поле.</summary>
    public string AiOrderTarget { get; init; } = "";

    /// <summary>Отряд не в счёт ИИ хозяина (<c>AI_IGNORE</c>, <c>CMidStack +0x9c</c>): его не считают полевые лидеры
    /// (<c>0x446b59</c>, <c>0x466f57</c>) и счётчики ролей найма лидера (<c>0x44a405</c>). Ранг 1 — поле и три чтения в коде ИИ;
    /// в корпусе у всех 24 915 отрядов сохранений — ложь.</summary>
    public bool AiIgnore { get; init; }

    /// <summary>Отложенные выборы навыка лидера (<c>UPGCOUNT</c>, план Ф7 §1.5 «отложенный выбор копится»).
    /// Ранг 1 — поле; смысл — ранг 2 (levels §2.4).</summary>
    public int UpgradeCount { get; init; }

    /// <summary>Выигранные сражения отряда (<c>NBBATTLE</c>) — для карточки воина. Ранг 1 — поле.</summary>
    public int BattlesWon { get; init; }

    /// <summary>Уровень, на котором лидер «создан» (<c>CREAT_LVL</c>, 1…99): вывезти героя после победы можно только при 1
    /// (<c>0x4c9da0</c>, <c>X005TA0726</c>). Старт — уровень сценария, импорт — 1. Ранг 1 — поле.</summary>
    public int CreationLevel { get; init; } = 1;

    /// <summary>Приоритет редактора для ИИ (<c>AIPRIORITY</c>, <c>CMidStack</c> → <c>IAiPriority +4</c>, 0…6; 3 — без поправки):
    /// поправка счёта тактики к действию, чья цель — этот отряд (<see cref="AiTacticScore.EditorPriority"/>, <c>0x466d5c</c>).
    /// Отряд, созданный в партии, — 3. Ранг 1 — поле.</summary>
    public int AiPriority { get; init; } = AiTacticScore.DefaultPriority;
}

/// <summary>Юнит (<c>CMidUnit</c>). Прямая проекция, ранг 1.</summary>
public sealed record MapUnit
{
    public required string Id { get; init; }

    /// <summary>Id типа из <c>Gunits.dbf</c> (<c>TYPE</c>), в файле в верхнем регистре.</summary>
    public required string TypeId { get; init; }

    public int Level { get; init; }
    public int Hp { get; init; }
    public int Xp { get; init; }

    /// <summary>День создания (<c>CMidUnit +8</c>, <c>CREATION</c>): по нему призванный уходит — <c>LIFE_TIME</c> призыва
    /// (<c>IUsSummon</c>) плюс этот день не позже текущего (<c>0x5d3dab</c>). Юниты карты — 0 (в файлы порта ноль не пишется). Ранг 1 —
    /// поле.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Creation { get; init; }

    /// <summary>Цепочка <c>MODIF_ID</c> в порядке хранения: способности уровня и <c>MOD_EQUIP</c> надетых
    /// предметов, знамя — у всех юнитов отряда (harness-saves §1, план Ф7 §1.5). Ранг 1.</summary>
    public IReadOnlyList<string> Modifiers { get; init; } = [];

    /// <summary>«Остановить продвижение» (<c>DYNLEVEL</c> = 1): тип заморожен, уровни растут по
    /// <c>GDynUpgr</c> (план Ф7 §1.3). В файлах без поля (не RotE и не версия 35) — false. Ранг 1.</summary>
    public bool DynLevel { get; init; }
}

/// <summary>Игрок (<c>CMidPlayer</c>). Прямая проекция, ранг 1.</summary>
public sealed record MapPlayer
{
    public required string Id { get; init; }
    public required string RaceId { get; init; }
    public required string LordId { get; init; }

    /// <summary>Имя лорда (<c>NAME_TXT</c>): в сценарии — из редактора, в партии — введённое игроком. Ранг 1 — поле.</summary>
    public string Name { get; init; } = "";

    /// <summary>Лицо лорда (<c>FACE</c>, 0…3, ролики <c>FACE_XX_N</c>). Ранг 1 — поле.</summary>
    public int Face { get; init; }

    public bool IsHuman { get; init; }

    /// <summary>Сторона дипломатии (этап Э2, задача E): ключ отношений <see cref="Relation"/>, союзов и дани. У движка отношения — пара
    /// родов рас (<c>CMidDiplomacy</c>), игрок рода один; null — сторона по роду расы (<see cref="Diplomacy.PartyOf(MapPlayer, IReadOnlyDictionary{string, RaceInfo})"/>),
    /// так у всех партий оригинала. Своя сторона даёт двух игроков одной расы и больше игроков, чем родов.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Party { get; init; }

    /// <summary>Команда (Р15, Э4): игроки одной команды — вечные союзники с общей победой (<see cref="Teams"/>); null — без команды.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Team { get; init; }

    public required Bank Bank { get; init; }

    /// <summary>Id объекта тумана этого игрока; ключ в <see cref="MapState.Fog"/>.</summary>
    public required string FogId { get; init; }

    /// <summary>Id объекта <c>CPlayerBuildings</c>; сам список — в <see cref="Buildings"/>.</summary>
    public required string BuildingsId { get; init; }

    /// <summary>Построенное в столице (<c>BUILD_ID</c> из <c>Gbuild.DBF</c>).</summary>
    public required IReadOnlyList<string> Buildings { get; init; }

    /// <summary>Второй кошелёк (<c>SPELL_BANK</c>, <c>CMidPlayer +0x7c</c>): им платит заклинание ИИ и в
    /// него уходит часть маны дохода ИИ (corpus-schema.md «Причины трат казны»). Ранг 1 — поле,
    /// назначение — ранг 2.</summary>
    public Bank SpellBank { get; init; } = Bank.Zero;

    /// <summary>Число разорванных союзов (<c>QTY_BREAKS</c>, +1 на разрыв — план Ф7 §1.8). Ранг 1.</summary>
    public int QtyBreaks { get; init; }

    /// <summary>Характер ИИ (<c>ATTITUDE</c>). Ранг 1 — поле; значения не разобраны.</summary>
    public int Attitude { get; init; }

    /// <summary>Кем захвачена столица (<c>CAPT_BY</c>, id игрока; пусто — не захвачена). Ранг 1:
    /// capture-treasury §2; <c>f7-m22-capture3</c> t01-058 — у Легионов <c>S143PL0001</c>.</summary>
    public string CapturedBy { get; init; } = "";

    /// <summary>День последней постройки (<c>CONSTR_T</c>, <c>CMidPlayer +0x50</c>): строить можно раз в день
    /// (<see cref="Economy.CanBuild"/>, <c>0x5dca8e</c>). Ранг 1 — поле.</summary>
    public int ConstructionTurn { get; init; }

    /// <summary>День последнего исследования заклинания (<c>RESEAR_T</c>). Ранг 1 — поле.</summary>
    public int ResearchTurn { get; init; }

    /// <summary>Известные заклинания (<c>CPlayerKnownSpells</c> по <c>KNOWN_ID</c>). Ранг 1.</summary>
    public IReadOnlyList<string> KnownSpells { get; init; } = [];

    /// <summary>Разведанные вором игроки (<c>SPY_N</c>, множество <c>CMidPlayer +0x54</c>; поиск — <c>0x5e46c2</c>): против
    /// их отрядов и крепостей шпионажа больше нет (<see cref="Thieves.Available"/>), а знатный ИИ их не трогает
    /// (<c>docs/ai-tactic-candidates.md</c> §12). Пополняет успешный шпионаж. Ранг 1 — поле и поиск, смысл — ранг 2 (журнал:
    /// <c>noble_action</c> шпионажа пишет в <c>id</c> игрока цели).</summary>
    public IReadOnlyList<string> Spied { get; init; } = [];

    /// <summary>Последний обмен картами, который предлагал этот игрок: id игрока-цели → день (<c>EXMAPID1..3</c>/<c>EXMAPTURN1..3</c>,
    /// словарь <c>CMidPlayer +0x94</c>). Пишет итог обмена — <c>CEffectExchangeMap</c> (<c>0x438010</c>) до проверки согласия, то есть и
    /// при отказе: <c>CVisitorPlayerSetLastExchangeMapTurn</c> (<c>0x60cbad</c> → <c>0x5e4fc6</c>) кладёт текущий день предлагавшему
    /// (<c>+0x4</c> эффекта) по ключу цели (<c>+0x8</c>). Читает только окно дипломатии (<c>0x48445e</c> → <c>0x5e4fe5</c>): запись есть и
    /// день ≥ текущего — <c>X160TA0023</c>, второго предложения той же цели в этот день нет. Записи не стираются, новая затирает старую.
    /// Ранг 1 по листингу; корпус: 59 снимков игрока с записью, все у человека (ИИ обмен не предлагает), legions — день 9, затем 21.
    /// <para>Обменов не было — null: в файлы порта (сценарии, сохранения) пустое поле не пишется.</para></summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, int>? MapExchanges { get; init; }

    /// <summary>День последнего обмена картами, предложенного игроку <paramref name="targetId"/> (<see cref="MapExchanges"/>); не было — null.</summary>
    public int? LastMapExchange(string targetId) =>
        MapExchanges?.FirstOrDefault(p => p.Key.Equals(targetId, StringComparison.OrdinalIgnoreCase)) is { Key: not null } hit ? hit.Value : null;
}

/// <summary>
/// Объект карты с координатой. <see cref="Kind"/> — две буквы класса из id объекта
/// (<c>S001<b>KC</b>0007</c>). Ранг 1, измерено перечислением объектов карты
/// <c>Exports/Last Stand.sg</c>: <c>FT</c> город и столица, <c>RU</c> руина, <c>SI</c> лавка,
/// наставник или маг, <c>CR</c> кристалл, <c>BG</c> мешок, <c>RA</c> дорога, <c>MM</c> ландшафт,
/// <c>KC</c> отряд, <c>LO</c> область.
/// </summary>
public sealed class MapObject
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public int X { get; init; }
    public int Y { get; init; }

    /// <summary>
    /// Тип ресурса жилы (<c>CR</c>) по <c>Lres.dbf</c>: 0 <c>L_GOLD</c>, 1 <c>L_RED</c>,
    /// 2 <c>L_YELLOW</c>, 3 <c>L_ORANGE</c>, 4 <c>L_WHITE</c>, 5 <c>L_BLUE</c> — тот же порядок,
    /// что у полей <see cref="Bank"/>. У прочих объектов 0 и смысла не имеет. Нужен дневному
    /// доходу: жилы — единственный его источник кроме столицы (<see cref="Economy.DailyIncome"/>).
    /// </summary>
    public int Resource { get; init; }

    /// <summary>Тип ландмарка (<c>MM</c>) — <c>MidLandmark.TYPE</c>, строка <c>GLmark.LMARK_ID</c>; у прочих
    /// объектов пусто. Нужен росту земли: клетки ландмарка с <c>GLmark.MOUNTAIN = T</c> обход отвергает
    /// (<see cref="LandModel.BlockedCells"/>). Ранг 1 — поле файла.</summary>
    public string TypeId { get; init; } = "";
}

/// <summary>
/// Карта целиком в том виде, в каком она лежит в <c>.sg</c>: сценарий и сохранение — один формат.
/// Ничего не достраивается: чего в файле нет, того нет и здесь.
/// </summary>
public sealed class MapState
{
    /// <summary>Сторона карты в клетках (<c>CMidgardMap</c>).</summary>
    public required int Size { get; init; }

    /// <summary>Клетки, индекс <c>[x, y]</c>. Клетки вне блоков остаются нулевыми.</summary>
    public required Tile[,] Tiles { get; init; }

    public required IReadOnlyDictionary<string, MapStack> Stacks { get; init; }
    public required IReadOnlyDictionary<string, MapUnit> Units { get; init; }
    public required IReadOnlyDictionary<string, MapPlayer> Players { get; init; }
    public required IReadOnlyDictionary<string, MapObject> Objects { get; init; }

    /// <summary>Занятость клеток (<c>CMidgardPlan</c>): что за объекты стоят в клетке.</summary>
    public required IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> Plan { get; init; }

    /// <summary>
    /// Туман по игрокам: ключ — id объекта тумана (<see cref="MapPlayer.FogId"/>),
    /// значение — маска <c>[x, y]</c>, где <c>true</c> — клетка ещё скрыта.
    /// Ранг 1 (измерено на <c>SaveGame</c>): у человека бит его собственной столицы — 0,
    /// бит столицы противника — 1; в сценарии из <c>Exports</c> все биты нулевые
    /// (туман расставляется на старте партии, в файле сценария его нет).
    /// </summary>
    public required IReadOnlyDictionary<string, bool[,]> Fog { get; init; }

    /// <summary>Номер дня (<c>CScenarioInfo.CUR_TURN</c>).</summary>
    public required int Turn { get; init; }

    /// <summary>Жезлы (<c>CMidRod</c>) по id. Ранг 1.</summary>
    public IReadOnlyDictionary<string, MapRod> Rods { get; init; } = new Dictionary<string, MapRod>();

    /// <summary>Посещаемые объекты карты (<c>LastEdict.Data.Map.SiteLoader</c>): укрепления с владельцем и уровнем,
    /// руины, лавки и прочее. Нужны росту земли — деревня тоже источник (план Ф7 §3 п. 26), а чужое
    /// укрепление обход отвергает. У синтетической карты пусто.</summary>
    public IReadOnlyList<MapSite> Sites { get; init; } = [];

    /// <summary>Дипломатия (<c>CMidDiplomacy</c>): запись на пару рас; разбор битов — <see cref="Relation"/>.</summary>
    public IReadOnlyList<Relation> Relations { get; init; } = [];

    /// <summary>Остаток зарядов талисманов (<c>CMidTalismanCharges</c>): <c>ID_TALIS</c> → <c>CHARGES</c>
    /// (план Ф7 §1.5: 5 зарядов, −1 за бой с применением, на нуле талисман уничтожается). Ранг 1.</summary>
    public IReadOnlyDictionary<string, int> TalismanCharges { get; init; } = new Dictionary<string, int>();

    /// <summary>Первый список <c>CMidSpellCast</c> — наложенные заклинания (<c>ORIGIN</c> = <c>G000SS…</c>).</summary>
    public IReadOnlyList<CastRecord> SpellCasts { get; init; } = [];

    /// <summary>Второй список <c>CMidSpellCast</c> — применения предметов (<c>ORIGIN</c> = экземпляр
    /// <c>IM</c>); пуст в начале дня — суточный запрет посоха на экземпляр (план Ф7 §1.5).</summary>
    public IReadOnlyList<CastRecord> ItemCasts { get; init; } = [];

    /// <summary>Модификаторы заклинаний и зелий карты на юнитах со сроком (<c>CMidSpellEffects</c>, <see cref="SpellEffectRecord"/>).</summary>
    public IReadOnlyList<SpellEffectRecord> SpellEffects { get; init; } = [];

    /// <summary>Экземпляры предметов (<c>CMidItem</c>): <c>ITEM_ID</c> → <c>ITEM_TYPE</c>. Ранг 1.</summary>
    public IReadOnlyDictionary<string, string> ItemTypes { get; init; } = new Dictionary<string, string>();

    /// <summary>Потолки сценария (<c>CScenarioInfo</c>); у синтетической карты null.</summary>
    public ScenarioLimits? Limits { get; init; }

    /// <summary>Подрасы (<c>CMidSubRace</c>): <c>SUBRACE_ID</c> → <c>SUBRACE</c> (<c>LSubRace</c>: 1 люди … 5 нейтралы …). По
    /// категории ИИ узнаёт «нейтральную» подрасу (<c>0x5f7189</c>) — её отряды берут в цели и свою подрасу. Ранг 1 — поле.</summary>
    public IReadOnlyDictionary<string, int> Subraces { get; init; } = new Dictionary<string, int>();

    /// <summary>Уничтоженные отряды (<c>CMidStackDestroyed</c>): отряд, его шаблон и убийца — по ним условие
    /// <c>L_KILL_STACK</c> (<c>docs/scenario-events.md</c> §2). Ранг 1 — поля.</summary>
    public IReadOnlyList<DestroyedStack> Destroyed { get; init; } = [];

    /// <summary>Могилы (<c>CMidTomb</c>): клетка и записи о погибших там отрядах. Ранг 1 — поля.</summary>
    public IReadOnlyList<MapTomb> Tombs { get; init; } = [];

    /// <summary>Счётчики id по классу объекта (две буквы: <c>UN</c>, <c>KC</c>, <c>IM</c>, <c>RD</c>, <c>BG</c> …) — номер следующего id:
    /// массив <c>CMidgardScenarioMap +0x30</c> (слот 9 <c>0x5ee8bd</c> выдаёт номер и прибавляет 1). При загрузке — наибольший номер
    /// среди объектов карты + 1 (<c>0x5ee996</c> после чтения <c>0x5eea31</c>; в файле счётчиков нет), дальше только растёт: номера
    /// удалённых за сессию объектов не переиспользуются (<c>id_alloc</c> 7.2). Пусто — счётчики не заданы (синтетическая карта), новый
    /// id — наибольший из живых + 1. Выдача — <c>GameSession.NewId</c>. Ни в сохранение, ни в сценарий порта не пишется: чтение
    /// <c>.sg</c>, <c>ScenarioFile</c> и <c>GameSave</c> считает заново, как движок (<c>docs/new-game.md</c> §6).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, int> IdNext { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Сид карты (<c>CScenarioInfo.MAP_SEED</c>): от него зависят только варианты узора земли
    /// (<c>docs/ground-render.md</c>), на правила не влияет.</summary>
    public uint MapSeed { get; init; }

    /// <summary>Отношение пары рас в любом порядке; null — записи пары в файле нет.</summary>
    public Relation? RelationOf(int race1, int race2)
    {
        foreach (var r in Relations)
            if (r.Pairs(race1, race2)) return r;
        return null;
    }

    /// <summary>Клетка карты. Координата вне поля — <see cref="ArgumentOutOfRangeException"/>
    /// с обеими величинами: в задаче 5 сюда приходят кандидаты хода, и «клетка (-1,4)» должна
    /// читаться в тексте отказа, а не только в номере строки.</summary>
    public Tile At(int x, int y) =>
        Contains(x, y)
            ? Tiles[x, y]
            : throw new ArgumentOutOfRangeException(
                nameof(x), $"клетка ({x},{y}) вне карты {Size}x{Size}");

    /// <summary>Лежит ли координата на карте.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

    /// <summary>
    /// Есть ли в клетке дорога. Дорога — отдельный объект <c>CMidRoad</c>, а не флаг клетки,
    /// поэтому ответ берётся из <see cref="Objects"/> (класс id <c>RA</c>).
    /// </summary>
    public bool IsRoad(int x, int y) => Roads.Contains((x, y));

    private HashSet<(int, int)>? _roads;

    private HashSet<(int, int)> Roads => _roads ??= Objects.Values
        .Where(o => o.Kind.Equals("RA", StringComparison.OrdinalIgnoreCase))
        .Select(o => (o.X, o.Y))
        .ToHashSet();
}

/// <summary>Запись <c>CMidStackDestroyed</c>: уничтоженный отряд (<c>ID_STACK</c>), его шаблон (<c>SRCTMPL_ID</c>) и
/// игрок-убийца (<c>ID_KILLER</c>; пусто — нейтрал).</summary>
public sealed record DestroyedStack(string StackId, string KillerId, string TemplateId);

/// <summary>Могила (<c>CMidTomb</c>): id, клетка и записи (<c>MidTombEntry</c>) по порядку.</summary>
public sealed record MapTomb(string Id, int X, int Y, IReadOnlyList<TombEntry> Entries);

/// <summary>Запись могилы: владелец погибшего отряда, игрок-убийца (пусто — нейтрал), день и имя отряда (имя лидера).</summary>
public sealed record TombEntry(string StackOwner, string Killer, int Turn, string StackName);
