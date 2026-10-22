using LastEdict.Data.Game;
using LastEdict.Data.Leaders;
using LastEdict.Data.Map;
using LastEdict.Data.Menu;
using LastEdict.Rules.Map;

namespace LastEdict.Data.Play;

/// <summary>Начало партии из сценария и выбора игрока в меню (<see cref="NewGameSetup"/>). Что делает движок на
/// старте — <c>docs/new-game.md</c> (код <c>0x421667</c> и сохранения первого дня живых партий):
/// <list type="number">
/// <item>человек — игрок выбранной расы: лорд, имя, лицо; сложность партии — из меню; день 1;</item>
/// <item>каждому игроку расы, начиная с человека: герой в столичном отряде из одного юнита пересоздаётся типом по
/// категории лорда (<see cref="StartLeader"/>) и уровнем <c>SUGG_LVL</c>, постройки снимаются и даётся бесплатная
/// (маг — башня, гильдмастер — гильдия), туман собирается заново по обзору;</item>
/// <item>казна человека умножается на <c>GVars.BONUS_*</c>/100 целочисленно, затем доход и полный ход дня 1.</item>
/// </list>
/// Не перенесено: зелья в столице на лёгкой (<c>0x421d6a</c>), пасхалка «Calamity». «+200 золота и +20 маны» у Power to
/// Corrupt — <c>SPELL_BANK</c>, влитый в казну человека (<see cref="Retype"/>, <c>docs/new-game.md</c> §4).</summary>
public static class NewGame
{
    /// <exception cref="InvalidOperationException">В сценарии нет игрока выбранной расы или у расы нет лорда
    /// выбранного типа.</exception>
    /// <param name="book">Правила; без них — только выбор игрока (для проверок меню).</param>
    /// <param name="random">Случай для имени героя человека (движок берёт случайное мужское имя расы).</param>
    /// <summary>Партия из сценария порта (<see cref="ScenarioFile"/>): то же, что по карте, плюс события сценария — их
    /// состояние с начала, и проверка начала хода человека в день 1 (интро и прочее «с частотой», <c>0x41f4ca</c>; в корпусе
    /// попапы дня 1 уже в квест-логе автосохранения t01-001).</summary>
    public static GameState Start(ScenarioData scenario, NewGameSetup setup, GameCatalog catalog, GameBook? book = null,
                                  Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var state = WithEvents(Start(scenario.Looks, setup, catalog, book, random), scenario);
        if (book is null) return state;
        var session = new GameSession(state, MapArtData.Empty, book);
        session.RunEvents(session.ActiveHuman, "");
        return session.Snapshot();
    }

    /// <summary>То же, что <see cref="Start(ScenarioData, NewGameSetup, GameCatalog, GameBook?, Random?)"/>, но живая партия для
    /// сцены: события дня 1 идут уже с <see cref="GameSession.EventBattlesOnScreen"/> — бой эффекта с человеком в интро ждёт экрана
    /// (<see cref="GameSession.PendingEventBattle"/>), а не играется автобоем до сцены.</summary>
    /// <param name="reseed">Пересев генератора боя новой партии от времени (<see cref="RandomReseed"/>); null — прежнее начальное состояние.</param>
    public static GameSession Begin(ScenarioData scenario, NewGameSetup setup, GameCatalog catalog, GameBook book, MapArtData art,
                                    Random? random = null, uint? reseed = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var state = WithEvents(Start(scenario.Looks, setup, catalog, book, random), scenario);
        if (reseed is { } seed) state = state.Reseeded(seed, keepCrt: true);   // шаг CRT новой партии (0x42af9b) уже сделан
        var session = new GameSession(state, art, book) { EventBattlesOnScreen = true };
        session.RunEvents(session.ActiveHuman, "");
        return session;
    }

    private static GameState WithEvents(GameState state, ScenarioData scenario) =>
        state with { Events = scenario.Events, EventProgress = EventProgress.From(scenario.Events) };

    /// <summary>Партия по карте сценария. С правилами (<paramref name="book"/>) у состояния задан <see cref="GameState.Random"/>, и поток CRT
    /// уже сделал шаг новой партии (<c>0x42af9b</c>).</summary>
    public static GameState Start(MapLooks scenario, NewGameSetup setup, GameCatalog catalog, GameBook? book = null,
                                  Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(catalog);
        var map = scenario.Map;
        // Люди: одиночная игра — выбор меню, «горячий стул» — ячейки лобби (CMenusReqRaceMsg / CMenusReqLordMsg на каждого).
        IReadOnlyList<HotseatSeat> seats = setup.Hotseat.Count > 0 ? setup.Hotseat
            : [new HotseatSeat(setup.Race, setup.LordName ?? "", setup.Face, setup.LordCategory, setup.ImportedLeader)];
        var chosen = new Dictionary<string, (string Lord, HotseatSeat Seat)>(StringComparer.OrdinalIgnoreCase);
        foreach (var seat in seats)
        {
            var race = catalog.Race(seat.Race);
            // Место — своему игроку; без него — первому свободному игроку расы по id (двое одной расы — два места, Э4).
            var player = (seat.PlayerId is { } id ? map.Players.GetValueOrDefault(id)
                          : map.Players.Values.OrderBy(p => p.Id, StringComparer.Ordinal)
                                .FirstOrDefault(p => p.RaceId.Equals(race.Id, StringComparison.OrdinalIgnoreCase) && !chosen.ContainsKey(p.Id)))
                         ?? throw new InvalidOperationException($"в сценарии нет свободного игрока расы {race.Id}");
            var lord = race.Lords.FirstOrDefault(l => l.Category == seat.LordCategory)
                       ?? throw new InvalidOperationException($"у расы {race.Id} нет лорда типа {seat.LordCategory}");
            chosen[player.Id] = (lord.Id, seat);
        }
        // Стартовая казна лобби (0x42b343 — 0…2000, 0x42b74f): оба 0 — ничего; иначе каждому игроку карты, и нейтралу, к казне
        // сценария прибавляется золото G и по M каждого вида маны (Currency::Add 0x586642, поле — в пределах 0…9999).
        long gold = Math.Clamp(setup.StartGold, 0, LobbyMax), mana = Math.Clamp(setup.StartMana, 0, LobbyMax);
        Bank? cash = gold > 0 || mana > 0 ? new Bank(gold, mana, mana, mana, mana, mana) : null;
        var players = map.Players.ToDictionary(p => p.Key, p =>
        {
            var player = cash is null ? p.Value : GameSession.WithBank(p.Value, Economy.Clamp(p.Value.Bank + cash));
            return chosen.TryGetValue(player.Id, out var c)
                ? With(player, isHuman: true, lordId: c.Lord,
                       name: string.IsNullOrWhiteSpace(c.Seat.Name) ? player.Name : c.Seat.Name.Trim(), face: c.Seat.Face) with
                {
                    Team = c.Seat.Team ?? player.Team,
                }
                : With(player, isHuman: false);
        }, StringComparer.OrdinalIgnoreCase);
        // Люди ходят в порядке игроков (NetPlayerInfo; журнал хотсита 20260920-ui, S135: PL0001 → PL0002, ранг 2 — по id).
        List<string> humans = [.. players.Values.Where(p => p.IsHuman).Select(p => p.Id).Order(StringComparer.Ordinal)];
        int suggested = Math.Max(1, setup.Scenario?.SuggestedLevel ?? 1);
        var started = GameSession.With(map, players: players, turn: 1,
                                       limits: map.Limits is { } limits ? limits with { DifficultyGame = setup.Difficulty } : null);
        var looks = GameSession.Retold(scenario, started, scenario.Stacks);
        var info = new GameInfo(setup.Scenario?.Id ?? "", setup.Scenario?.Name ?? "", humans, setup.Difficulty, suggested)
        {
            Campaign = setup.Campaign,
        };
        if (book is null) return new GameState(info, looks);
        // Стороны и отношения (Э4): у игроков одной расы — свои стороны, у каждой пары сторон — запись отношений, команды — вечный союз.
        var sided = Sides(looks.Map, book);
        looks = GameSession.Retold(looks, GameSession.With(sided, relations: Teams.Bound(sided, book.Economy)), looks.Stacks);
        started = looks.Map;
        players = new Dictionary<string, MapPlayer>(started.Players, StringComparer.OrdinalIgnoreCase);

        // Цикл по NetPlayerInfo (0x421667: 0x42923b — число, 0x42927f — i-й; список сервера +4 → +0x30) — тот же список, что
        // переставлен при старте по ключу расы с хозяином первым (0x42b8ab → 0x42b8e8 → 0x432165 по +0x30), то есть очередь дня
        // (GameSession.DayOrder), а не порядок id. Журнал 20261018b-port-synth, карта 16: id_alloc нового лидера UN001d (человек),
        // UN001e — Легионы S143PL0002, UN001f — Кланы S143PL0001, UN0020 — Нежить S143PL0003, между ними build бесплатной постройки.
        var order = GameSession.DayOrder(started, humans, book).Select(id => players[id])
                               .Where(p => book.Economy.Races.TryGetValue(p.RaceId, out var r) && r.Playable).ToList();
        foreach (var player in order)
        {
            looks = StartLeader(looks, player, book, suggested, player.IsHuman ? random ?? new Random() : null);
            // Герой только своей расы (окно выбора других не берёт; выбор мог остаться от другой расы).
            if (chosen.TryGetValue(player.Id, out var c) && c.Seat.Leader is { } imported && imported.Race == c.Seat.Race)
                looks = ImportLeader(looks, player.Id, imported, book);
            if (player.IsHuman && setup.Carry is { } carry) looks = Carry(looks, player.Id, carry, book);
            looks = FreeBuilding(looks, player.Id, book);
        }
        // Множитель казны — только в одиночной игре (флаг 0x4292f9 «многопользовательская»: в хотсите строки bank нет).
        if (humans.Count == 1) looks = HumanBank(looks, humans[0], book, setup.Difficulty);

        var session = new GameSession(new GameState(info, looks) { ActiveHuman = humans[0] }, MapArtData.Empty, book);
        // Бросок CRT новой партии (0x42ae90 → 0x42af9b): пока номер дня 0, описание человека — rand() % n по описаниям его расы и
        // пола (Tplayer.dbf TEXT_M/TEXT_F, 0x581fa2 — список +0x5c/+0x60; n = 1, текст тот же, но шаг потока есть). Поток игры — свой
        // поток ОС со srand(1); журнал пишет этот шаг до game_phase new, и в корпусе у каждой новой партии он ровно один (98 из 98,
        // хотсит с двумя людьми — тоже один). Без него тип юнита найма ИИ (0x44c137) в партии порта шёл со сдвигом на шаг (карта 14,
        // день 1: Империя UU0001/UU0001/UU0006 вместо UU0008/UU0006/UU0011).
        session.Crt.Next();
        foreach (var player in order) session.ResetFog(player.Id);
        session.BeginFirstDay(humans[0]);
        return session.SnapshotWithoutSummaries();
    }

    /// <summary>Первая сторона дипломатии, которую отдаёт <see cref="Sides"/>: выше родов рас оригинала (0…5), чтобы не совпасть с
    /// игроком без своей стороны.</summary>
    public const int FirstOwnParty = 10;

    /// <summary>Своя сторона второму и следующим играбельным игрокам одной стороны (обычно — одной расы без <see cref="MapPlayer.Party"/>):
    /// у движка сторона — род расы, и двое одной расы делили бы отношения. Первый (по id) остаётся на своей; новые — с
    /// <see cref="FirstOwnParty"/>, мимо занятых. Нечего делить — та же карта.</summary>
    public static MapState Sides(MapState map, GameBook book)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(book);
        var races = book.Economy.Races;
        var playable = map.Players.Values.Where(p => races.TryGetValue(p.RaceId, out var r) && r.Playable).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        var taken = playable.Select(p => Diplomacy.PartyOf(p, races)!.Value).ToHashSet();
        var used = new HashSet<int>();
        int next = FirstOwnParty;
        Dictionary<string, MapPlayer>? players = null;
        foreach (var p in playable)
        {
            int party = Diplomacy.PartyOf(p, races)!.Value;
            if (used.Add(party)) continue;
            while (taken.Contains(next) || used.Contains(next)) next++;
            used.Add(next);
            players ??= new Dictionary<string, MapPlayer>(map.Players, StringComparer.OrdinalIgnoreCase);
            players[p.Id] = p with { Party = next };
        }
        return players is null ? map : GameSession.With(map, players: players);
    }

    /// <summary>Замена стартового героя (<c>0x4220eb</c>): отряд в столице игрока, и только если в нём один юнит.
    /// Тип — первый из списка найма лидеров расы (<c>LEADER_1..4</c>, <c>NOBLE</c>), чья <c>LEADER_CAT</c> равна
    /// категории лорда: воитель — боец, гильдмастер — следопыт, маг — маг. Уровень <c>SUGG_LVL</c>, здоровье полное,
    /// опыта и модификаторов нет, id — следующий свободный; отложенных навыков <c>min(SUGG_LVL, LDRMAXABIL) − 1</c>.
    /// Имя у ИИ прежнее, у человека — случайное мужское имя расы (ранг 2: откуда бросок — не найдено).</summary>
    private static MapLooks StartLeader(MapLooks looks, MapPlayer player, GameBook book, int level, Random? random)
    {
        var map = looks.Map;
        var capital = map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital
                                                    && s.Owner.Equals(player.Id, StringComparison.OrdinalIgnoreCase));
        if (capital is null || capital.StackId.Length == 0 || !map.Stacks.TryGetValue(capital.StackId, out var stack)
            || stack.UnitIds.Count != 1 || !map.Units.TryGetValue(stack.UnitIds[0], out var old)) return looks;
        int leaderCategory = book.LordCategory(player.LordId) switch { 1 => 1, 2 => 2, _ => 3 };
        string? type = book.RaceLeaders.GetValueOrDefault(player.RaceId)?
            .FirstOrDefault(t => book.LeaderCategoryOf(t) == leaderCategory);
        if (type is null || book.Grown(type, level) is not { } grown) return looks;

        var ids = map.IdNext;   // счётчик карты (0x5ee8bd): номер старого лидера после его снятия не вернётся
        string id = GameSession.NewId(ref ids, map.Units.Keys, stack.Id, "UN");
        string name = random is not null && book.LeaderNames.GetValueOrDefault(player.RaceId) is { Count: > 0 } names
            ? FreshName(names, looks, random)
            : looks.Stacks.FirstOrDefault(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase))?.LeaderName ?? "";
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units.Remove(old.Id);
        units[id] = new MapUnit { Id = id, TypeId = type, Level = grown.Level, Hp = grown.Type.HitPoints };
        int pending = Math.Min(level, book.Vars.LeaderMaxAbilities) - 1;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        // Счётчик пишется только при n > 0 (0x5e8513 за проверкой): при SUGG_LVL 1 у отряда остаётся свой.
        stacks[stack.Id] = stack with
        {
            LeaderId = id, LeaderAlive = true, UnitIds = [id], UpgradeCount = pending > 0 ? pending : stack.UpgradeCount,
            CreationLevel = level,   // 0x422423: уровень сценария (ранг 2)
        };
        var stackLooks = looks.Stacks.Select(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)
            ? s with { LeaderType = type, LeaderAlive = true, AnyAlive = true, LeaderName = name } : s).ToList();
        return GameSession.Retold(looks, GameSession.With(map, stacks: stacks, units: units, idNext: ids), stackLooks);
    }

    /// <summary>
    /// Импорт героя (<c>CImportLeaderMsg</c> → <c>0x42f359</c>, в начале первого хода игрока — после замены стартового героя;
    /// <c>docs/day-cycle.md</c> §12–13). Отряд в столице подгоняется под лидерство героя (<see cref="FitToLeadership"/>), и его лидер
    /// становится героем файла — базовый тип и уровень (характеристики — заново из типа и уровня), имя, опыт, модификаторы; в пустой
    /// столице создаётся отряд из одного героя (<c>CVisitorCreateStackInside</c> <c>0x60e8da</c>): владелец и подраса столицы, приказ
    /// «обычный», лидер в клетке 2 (ближний бой или большой) или 3. Предметы — новыми экземплярами в свои слоты (заряды талисмана);
    /// отряду — победы (<c>NBBATTLE</c>), отложенные навыки (<c>UPGCOUNT</c>) и уровень создания 1 (<c>CREAT_LVL</c>, <c>0x42f5c5</c>:
    /// героя можно снова вывезти).
    /// </summary>
    internal static MapLooks ImportLeader(MapLooks looks, string playerId, ExportedLeader leader, GameBook book)
    {
        var map = looks.Map;
        var capital = map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (capital is null || book.Grown(leader.UnitType, Math.Max(1, leader.Level)) is not { } grown) return looks;
        string type = leader.UnitType;
        if (capital.StackId.Length == 0 || !map.Stacks.ContainsKey(capital.StackId)) looks = StackInside(looks, capital, type, grown.Type, book);
        else looks = FitToLeadership(looks, capital, leader.Leadership);
        map = looks.Map;
        var stack = map.Stacks[map.Sites.First(s => s.Id == capital.Id).StackId];
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[stack.LeaderId] = new MapUnit
        {
            Id = stack.LeaderId, TypeId = type, Level = grown.Level, Hp = grown.Type.HitPoints, Xp = leader.Xp,
            Modifiers = [.. leader.Modifiers],
        };
        var itemTypes = map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var charges = map.TalismanCharges.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var equipment = stack.Equipment.Count > 0 ? stack.Equipment.ToList() : [.. Enumerable.Repeat("", EquipmentSlots)];
        var ids = map.IdNext;
        // Предмет, которого нет в правилах (файл мода или другой версии), не берётся — как у боевых слотов (MapBattle.BattleSlots).
        foreach (var item in leader.Equipment.Where(i => i.ItemType.Length > 0 && i.Slot >= 0 && i.Slot < equipment.Count
                                                         && book.Items.ContainsKey(i.ItemType)))
        {
            string instance = GameSession.NewId(ref ids, itemTypes.Keys, stack.Id, "IM");
            itemTypes[instance] = item.ItemType;
            if (item.Charges > 0) charges[instance] = item.Charges;
            equipment[item.Slot] = instance;
        }
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with
        {
            LeaderAlive = true, Equipment = equipment, BattlesWon = leader.BattlesWon, UpgradeCount = leader.UpgradeCount, CreationLevel = 1,
        };
        var imported = GameSession.With(map, stacks: stacks, units: units, itemTypes: itemTypes, talismanCharges: charges, idNext: ids);
        var stackLooks = looks.Stacks.Select(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)
            ? s with { LeaderType = type, LeaderAlive = true, AnyAlive = true, LeaderName = leader.Name.Trim() } : s).ToList();
        return GameSession.Retold(looks, imported, stackLooks);
    }

    /// <summary>
    /// Перенос из прошлой главы (<c>0x42f036</c>): лидер отряда в столице становится перенесённым героем (<c>CVisitorOverlayUnit</c>:
    /// снимок юнита — тип, уровень, опыт, здоровье, модификаторы; id прежний) с его именем, ход отряда восстанавливается, отряду —
    /// отложенные навыки и победы; вещи создаются заново на складе столицы (<c>CVisitorCreateItem</c> с id крепости, ранг 2). Пустая
    /// столица — отряд из героя, как при импорте (у движка не разобрано, ранг 3). Отряд под лидерство не подгоняется.
    /// </summary>
    internal static MapLooks Carry(MapLooks looks, string playerId, CarryOver carry, GameBook book)
    {
        var map = looks.Map;
        var capital = map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Capital && s.Owner.Equals(playerId, StringComparison.OrdinalIgnoreCase));
        if (capital is null) return looks;
        var itemTypes = map.ItemTypes.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var charges = map.TalismanCharges.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var stored = capital.Items.ToList();
        var ids = map.IdNext;
        foreach (string type in carry.ItemTypes.Where(t => book.Items.ContainsKey(t)).Take(CarryOver.MaxItems))
        {
            string instance = GameSession.NewId(ref ids, itemTypes.Keys, capital.Id, "IM");
            itemTypes[instance] = type;
            // Новый талисман — с записью зарядов TALIS_CHRG, как при любом создании (GameSession.ChargeNewTalismans).
            if (book.Items[type].Category == LastEdict.Rules.Magic.ItemCategory.Talisman) charges[instance] = book.Vars.TalismanCharges;
            stored.Add(instance);
        }
        var sites = map.Sites.Select(s => s.Id == capital.Id ? s with { Items = stored } : s).ToList();
        looks = GameSession.Retold(looks, GameSession.With(map, sites: sites, itemTypes: itemTypes, talismanCharges: charges, idNext: ids),
                                  looks.Stacks);
        if (carry.Hero is not { } hero || book.Grown(hero.TypeId, Math.Max(1, hero.Level)) is not { } grown) return looks;
        map = looks.Map;
        capital = map.Sites.First(s => s.Id == capital.Id);
        if (capital.StackId.Length == 0 || !map.Stacks.ContainsKey(capital.StackId))
        {
            looks = StackInside(looks, capital, hero.TypeId, grown.Type, book);
            map = looks.Map;
        }
        var stack = map.Stacks[map.Sites.First(s => s.Id == capital.Id).StackId];
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[stack.LeaderId] = hero with { Id = stack.LeaderId };
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with { LeaderAlive = true, UpgradeCount = carry.UpgradeCount, BattlesWon = carry.BattlesWon };
        var carried = GameSession.With(map, stacks: stacks, units: units);
        var stackLooks = looks.Stacks.Select(s => s.Id.Equals(stack.Id, StringComparison.OrdinalIgnoreCase)
            ? s with { LeaderType = hero.TypeId, LeaderAlive = true, AnyAlive = true, LeaderName = carry.HeroName.Trim() } : s).ToList();
        return GameSession.Retold(looks, carried, stackLooks);
    }

    /// <summary>
    /// Имя нового героя человека (окно первого хода <c>CChangeFirstLeaderNameInterf</c>, по умолчанию — <c>0x4b02f3</c>): из имён расы
    /// берутся реже всего носимые юнитами карты, по порядку строк (<c>std::map</c>), и из них — <c>rand() % n</c>. Генератор движка —
    /// CRT потока интерфейса, засеянный <c>MAP_SEED</c> при построении вида карты (<c>0x5ae6cf</c>), а число вызовов до окна зависит от
    /// кадров ожидания — поэтому бросок порта свой (<paramref name="random"/>), а набор и порядок — движка. Ранг 1 — набор, ранг 2 — бросок.
    /// </summary>
    private static string FreshName(IReadOnlyList<string> names, MapLooks looks, Random random)
    {
        var worn = looks.Stacks.Where(s => s.LeaderName.Length > 0).GroupBy(s => s.LeaderName.Trim(), StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int least = names.Min(n => worn.GetValueOrDefault(n.Trim()));
        var pool = names.Select(n => n.Trim()).Where(n => worn.GetValueOrDefault(n) == least).Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToList();
        return pool[random.Next(pool.Count)];
    }

    private const int EquipmentSlots = LastEdict.Rules.Magic.Equipment.SlotCount, GroupCells = UnitCells.CellCount;

    /// <summary>Потолок золота и маны лобби «горячего стула» (<c>SPIN_GOLD</c>/<c>SPIN_MANA</c>, <c>0x42b343</c>).</summary>
    private const long LobbyMax = 2000;

    /// <summary>
    /// Подгонка отряда столицы под лидерство героя (<c>0x42f5dd</c>, ранг 1): лишних <c>E = N − L − 1</c> (N — воины отряда вместе с
    /// прежним лидером, большой — один; L — лидерство героя из файла). Свободных клеток гарнизона (большой занимает две) меньше E —
    /// воины отряда и гарнизона по возрастанию опыта (<c>std::list::sort</c> устойчив: при равенстве — порядок списков воинов, отряд,
    /// потом гарнизон) и первые <c>E − свободных</c> удаляются; затем лишние воины отряда по клеткам с первой (кроме лидера) уходят в
    /// первые пустые клетки гарнизона.
    /// Отличие от движка (решение пользователя 28.09): у движка прежний лидер тоже кандидат, его снятие запрещено (<c>0x614963</c>) —
    /// и тогда импорт отменяется целиком; порт лидера в кандидаты не берёт и импорт проводит.
    /// </summary>
    private static MapLooks FitToLeadership(MapLooks looks, MapSite capital, int leadership)
    {
        var map = looks.Map;
        var stack = map.Stacks[capital.StackId];
        int excess = stack.UnitIds.Count - leadership - 1;
        if (excess <= 0) return looks;
        var stackIds = stack.UnitIds.ToList();
        var stackSlots = stack.Slots.ToList();
        var guardIds = capital.Guards.ToList();
        var guardSlots = capital.GuardSlots.Count == GroupCells ? capital.GuardSlots.ToList() : [.. Enumerable.Repeat(-1, GroupCells)];
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> ByCells(List<string> ids, List<int> slots) =>
            slots.Where(i => i >= 0 && i < ids.Count).Distinct().Select(i => ids[i]);
        int free = guardSlots.Count(i => i < 0);
        if (free < excess)
        {
            var doomed = stackIds.Where(id => !id.Equals(stack.LeaderId, StringComparison.OrdinalIgnoreCase)).Concat(guardIds)
                .OrderBy(id => units.TryGetValue(id, out var u) ? u.Xp : 0)
                .Take(excess - free).ToList();
            foreach (string id in doomed)
            {
                if (!Remove(stackIds, stackSlots, id)) Remove(guardIds, guardSlots, id);
                units.Remove(id);
            }
        }
        foreach (string id in ByCells(stackIds, stackSlots).Where(id => !id.Equals(stack.LeaderId, StringComparison.OrdinalIgnoreCase))
                                                          .Take(excess).ToList())
        {
            int cell = guardSlots.IndexOf(-1);
            if (cell < 0) break;
            Remove(stackIds, stackSlots, id);
            guardIds.Add(id);
            guardSlots[cell] = guardIds.Count - 1;
        }
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stack.Id] = stack with { UnitIds = stackIds, Slots = stackSlots };
        var sites = map.Sites.Select(s => s.Id == capital.Id ? s with { Guards = guardIds, GuardSlots = guardSlots } : s).ToList();
        return GameSession.Retold(looks, GameSession.With(map, stacks: stacks, units: units, sites: sites), looks.Stacks);
    }

    /// <summary>Воин уходит из группы: клетки с его номером пустеют, номера после него сдвигаются. Нет в группе — ложь.</summary>
    private static bool Remove(List<string> ids, List<int> slots, string id)
    {
        int at = ids.FindIndex(u => u.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return false;
        ids.RemoveAt(at);
        for (int c = 0; c < slots.Count; c++) slots[c] = slots[c] == at ? -1 : slots[c] > at ? slots[c] - 1 : slots[c];
        return true;
    }

    /// <summary>Отряд в пустой столице (<c>CVisitorCreateStackInside</c> <c>0x60e7fe</c>/<c>0x60e8da</c>): один воин типа героя, владелец и
    /// подраса столицы, приказ «обычный», внутри столицы; клетка лидера — 2 при ближней атаке или большом воине, иначе 3.</summary>
    private static MapLooks StackInside(MapLooks looks, MapSite capital, string type, LastEdict.Rules.Battle.UnitType grown, GameBook book)
    {
        var map = looks.Map;
        var ids = map.IdNext;
        string unitId = GameSession.NewId(ref ids, map.Units.Keys, capital.Id, "UN");
        string stackId = GameSession.NewId(ref ids, map.Stacks.Keys, capital.Id, "KC");
        var slots = GameSession.LeaderSlots(grown, book.Rules);   // CMidStack 0x5ebe7a — как у найма лидера и призыва
        var stack = new MapStack
        {
            Id = stackId, Owner = capital.Owner, Subrace = capital.Subrace, LeaderId = unitId, LeaderAlive = true, X = capital.X, Y = capital.Y,
            Inside = capital.Id, UnitIds = [unitId], Slots = slots, Equipment = [.. Enumerable.Repeat("", EquipmentSlots)],
            Order = (int)LastEdict.Rules.Battle.StackOrder.Normal, CreationLevel = 1,
        };
        var units = map.Units.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        units[unitId] = new MapUnit { Id = unitId, TypeId = type, Level = grown.Level, Hp = grown.HitPoints };
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        stacks[stackId] = stack;
        var sites = map.Sites.Select(s => s.Id == capital.Id ? s with { StackId = stackId } : s).ToList();
        var plan = map.Plan.ToDictionary(p => p.Key, p => p.Value);
        plan[(capital.X, capital.Y)] = [.. plan.GetValueOrDefault((capital.X, capital.Y)) ?? [], stackId];
        var look = new StackLook(stackId, capital.X, capital.Y, capital.Owner, capital.Subrace, type, LeaderAlive: true, AnyAlive: true,
                                 Facing: 0, Inside: capital.Id, Invisible: false, LeaderName: "");
        return GameSession.Retold(looks, GameSession.With(map, stacks: stacks, units: units, sites: sites, plan: plan, idNext: ids),
                                  [.. looks.Stacks, look]);
    }

    /// <summary>Бесплатная постройка (<c>0x421f2f</c>): постройки игрока снимаются, затем маг получает первую по id
    /// башню магии из списка своего лорда, гильдмастер — гильдию, воитель — ничего.</summary>
    private static MapLooks FreeBuilding(MapLooks looks, string playerId, GameBook book)
    {
        var map = looks.Map;
        var player = map.Players[playerId];
        int? category = book.LordCategory(player.LordId) switch { 0 => MagicTower, 2 => Guild, _ => null };
        IReadOnlyList<string> buildings = [];
        if (category is { } wanted && book.LordBuildings.GetValueOrDefault(player.LordId)?
                .FirstOrDefault(b => book.Economy.Buildings.TryGetValue(b, out var t) && t.Category == wanted) is { } free)
            buildings = [free];
        if (buildings.SequenceEqual(player.Buildings, StringComparer.OrdinalIgnoreCase)) return looks;
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[playerId] = With(player, player.IsHuman, buildings: buildings);
        return GameSession.Retold(looks, GameSession.With(map, players: players), looks.Stacks);
    }

    /// <summary><c>Lbuild</c>: 0 — гильдия, 2 — башня магии.</summary>
    private const int Guild = 0, MagicTower = 2;

    /// <summary>Множитель стартовой казны человека (<c>0x421be0</c>): каждая валюта × (<c>BONUS_*</c> / 100) с целым
    /// делением — ×3 на лёгкой, ×1 на средней, ×0 на трудной и очень трудной (ранг 1 по коду; данными проверена
    /// только средняя).</summary>
    private static MapLooks HumanBank(MapLooks looks, string humanId, GameBook book, int difficulty)
    {
        var map = looks.Map;
        var player = map.Players[humanId];
        int k = book.StartBankPercent[Math.Clamp(difficulty, 0, book.StartBankPercent.Count - 1)] / 100;
        var b = player.Bank;
        var bank = new Bank(b.Gold * k, b.Infernal * k, b.Life * k, b.Death * k, b.Runic * k, b.Grove * k);
        var players = map.Players.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        players[humanId] = GameSession.WithBank(player, Economy.Clamp(bank));
        return GameSession.Retold(looks, GameSession.With(map, players: players), looks.Stacks);
    }

    private static MapPlayer With(MapPlayer p, bool isHuman, string? lordId = null, string? name = null, int? face = null,
                                  IReadOnlyList<string>? buildings = null) =>
        Retype(p, isHuman) with
        {
            LordId = lordId ?? p.LordId, Name = name ?? p.Name, Face = face ?? p.Face, Buildings = buildings ?? p.Buildings,
        };

    /// <summary>
    /// Смена вида игрока при занятии места (<c>CVisitorChangePlayerType</c> <c>0x60befe</c>, сервер <c>0x42ae90</c> — до дохода дня 1):
    /// игрок стал человеком — вся казна ИИ на заклинания (<c>SPELL_BANK</c>) вливается в его казну (<c>0x586642</c>, в пределах 0…9999) и
    /// обнуляется; стал ИИ — общее делится: золото и половина каждой маны — в казну, вторая половина маны — на заклинания
    /// (<c>0x5867f0</c>). Вид не сменился — ничего. Так объясняются «+200 золота и +20 маны» человека на Power to Corrupt: у всех игроков
    /// этого файла <c>SPELL_BANK</c> = 200/20 (<c>docs/new-game.md</c> §4). Ранг 1.
    /// </summary>
    internal static MapPlayer Retype(MapPlayer p, bool isHuman)
    {
        if (p.IsHuman == isHuman) return p;
        var total = Economy.Clamp(p.Bank + p.SpellBank);
        if (isHuman) return p with { IsHuman = true, Bank = total, SpellBank = Bank.Zero };
        var half = new Bank(0, total.Infernal / 2, total.Life / 2, total.Death / 2, total.Runic / 2, total.Grove / 2);
        return p with { IsHuman = false, Bank = total - half, SpellBank = half };
    }
}
