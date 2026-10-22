using System.Text.Json;
using System.Text.Json.Nodes;
using LastEdict.Data.Content;
using LastEdict.Data.Map;

namespace LastEdict.Data.Play;

/// <summary>О партии, чего нет в карте: какой сценарий, кто за людей, выбор игрока в меню.</summary>
/// <param name="ScenarioId">Id сценария в каталоге (<c>ScenarioInfo.Id</c>); пусто — карта открыта из файла.</param>
/// <param name="Humans">Игроки-люди в порядке ходов (для «горячего стула» их несколько).</param>
/// <param name="Difficulty">Сложность игры (<c>Ldiff</c>: 0 — легко … 3 — очень сложно).</param>
/// <param name="SuggestedLevel">Рекомендуемый уровень сценария (<c>SUGG_LVL</c>): от него зависит бонус опыта ИИ.</param>
/// <param name="Winner">Игрок, выигравший партию (взята последняя чужая столица, событие <c>L_WIN</c>); <see cref="NoWinner"/> —
/// партия кончилась без победителя (<c>L_WIN</c> с <c>WIN_SCEN</c> 0: проиграли все); пусто — партия идёт.</param>
public sealed record GameInfo(string ScenarioId, string ScenarioName, IReadOnlyList<string> Humans, int Difficulty,
                              int SuggestedLevel = 0, string Winner = "")
{
    /// <summary>Партия кончилась без победителя (<c>L_WIN</c> с <c>WIN_SCEN</c> 0, <c>docs/scenario-events.md</c> §3).</summary>
    public const string NoWinner = "-";

    /// <summary>Глава кампании (сага или своя кампания); null — квест или «горячий стул».</summary>
    public CampaignRun? Campaign { get; init; }

    public static GameInfo FromMap(string name, MapLooks looks) =>
        new("", name, [.. looks.Map.Players.Values.Where(p => p.IsHuman).Select(p => p.Id)], looks.Map.Limits?.DifficultyGame ?? 1,
            looks.Map.Limits?.SuggestedLevel ?? 1);   // как NewGame: уровень сценария не ниже 1 (SUGG_LVL в .sg есть всегда)
}

/// <summary>Партия целиком: карта с объектами для отрисовки и сведения о партии. Неизменяемый снимок; ход
/// партии ведёт <see cref="GameSession"/>, сохраняет — <see cref="GameSave"/>.</summary>
public sealed record GameState(GameInfo Info, MapLooks Looks)
{
    /// <summary>Память стратегии ИИ по игрокам (<see cref="AiMemory"/>); пишется в сохранение.</summary>
    public IReadOnlyDictionary<string, AiMemory> Ai { get; init; } = new Dictionary<string, AiMemory>();

    /// <summary>Память тактики ИИ (<see cref="LastEdict.Rules.Map.AiTacticMemo"/>: статусы действий по дням) — от неё зависят
    /// «вчера продвинулся» ×1,25 и пропуск «сегодня не пошёл».</summary>
    public IReadOnlyList<LastEdict.Rules.Map.AiTacticRecord> Tactic { get; init; } = [];

    /// <summary>Потоки случайных чисел партии на момент снимка; пусто — партия только началась, потоки с начала.
    /// Заполняет <see cref="GameSession.Snapshot"/>.</summary>
    public RandomStreams? Random { get; init; }

    /// <summary>Предложения союза и сделок от ИИ людям, ждущие ответа (<see cref="AllianceOffer"/>). У движка ход
    /// ИИ стоит, пока человек не ответит (счётчик ожидания <c>+0x44</c>); порт копит предложение до хода человека.</summary>
    public IReadOnlyList<AllianceOffer> Offers { get; init; } = [];

    /// <summary>События сценария (определения и области) — из сценария порта; в сохранении целиком, чтобы партия не зависела
    /// от файла сценария.</summary>
    public LastEdict.Rules.Map.ScenarioEventSet Events { get; init; } = LastEdict.Rules.Map.ScenarioEventSet.Empty;

    /// <summary>Состояние событий: выключенные, переменные, квест-лог, цель (<see cref="LastEdict.Rules.Map.EventProgress"/>).</summary>
    public LastEdict.Rules.Map.EventProgress EventProgress { get; init; } = LastEdict.Rules.Map.EventProgress.Empty;

    /// <summary>Попапы событий, ждущие показа людям (<see cref="GameSession.TakeMessages"/> снимает их при показе).</summary>
    public IReadOnlyList<EventMessage> Messages { get; init; } = [];

    /// <summary>Человек, чей сейчас ход («горячий стул»: люди ходят по очереди в одном дне); пусто — первый из
    /// <see cref="GameInfo.Humans"/>.</summary>
    public string ActiveHuman { get; init; } = "";

    /// <summary>Доход последнего начала хода людей (<see cref="GameSession.TurnIncome"/>) — окну начала хода (<c>CCmdTurnInfoMsg</c>,
    /// <c>CEffectPlayerTurn</c> <c>0x43ab41</c>). В сохранение не пишется: после загрузки окна начала хода нет (сообщение «Повтор миссии»).</summary>
    public IReadOnlyDictionary<string, LastEdict.Rules.Map.Bank> TurnIncome { get; init; } = new Dictionary<string, LastEdict.Rules.Map.Bank>();

    /// <summary>Итоги хода людей, накопленные к записи и ещё не показанные (<see cref="PendingSummary"/>; ключ — человек). У движка они
    /// лежат в <c>.sg</c> (<c>CTurnSummary</c>) и приходят человеку в начале его следующего хода — и после загрузки: в «горячем стуле» у
    /// других людей, не у сохранившего. Порт собирает их сборщиком сцены (<c>TurnSummaryWatch</c>, сессия Б); снимок для записи берёт
    /// их у <see cref="GameSession.SummariesForSave"/>, загрузка отдаёт сцене (<see cref="GameSession.TakeSummaries"/>).</summary>
    public IReadOnlyDictionary<string, PendingSummary> Summaries { get; init; } = new Dictionary<string, PendingSummary>();

    /// <summary>Порождённые реализации юнитов (<c>G000UG…</c>), созданные в этом запуске до снимка сверх тех, что видны на карте
    /// (<see cref="GeneratedImpls"/>: от них зависит опыт лидера у порога). В сохранение не пишется — у движка коллекция живёт с входа в
    /// партию, и после загрузки в ней только юниты карты; задаёт её харнесс, начиная день с сохранения посреди партии.</summary>
    public IReadOnlySet<string> GeneratedUnits { get; init; } = new HashSet<string>();
}

/// <summary>Итоги хода человека, собранные с конца его хода (день <paramref name="Day"/> — день снимка конца хода: итоги отдаются, когда
/// день сменился) до записи партии: записи окна <c>DLG_TURNSUMMARY</c> как есть (вид, строка, объект).</summary>
public sealed record PendingSummary(int Day, IReadOnlyList<TurnSummaryEntry> Entries);

/// <summary>Запись итогов хода: вид (<c>0x4c4e11</c>, поле +0xC), строка и объект для карточки (окно — <c>TurnSummaryLogic</c>).</summary>
public sealed record TurnSummaryEntry(int Kind, string Text, string ObjectId);

/// <summary>Попап события (<c>L_POPUP</c>) для игрока: текст, картинки, звук — как в записи события. При <c>BOOLVALUE</c> — лидер
/// отряда-триггерера (<c>CEffectPopup</c> +0x64): имя (<paramref name="LeaderName"/>) и глобальный тип (<paramref name="LeaderType"/>)
/// для портрета; без флага или без отряда — пусто. Имя — <c>CMidUnit::getName</c> (<c>0x5e97dc</c>, ранг 1): у героя (категория «лидер»)
/// — собственное, даже пустое (<paramref name="OwnName"/>); у вора, жезлоносца, призванного и воина во главе отряда — имя типа.</summary>
public sealed record EventMessage(string PlayerId, int Day, string EventId, LastEdict.Rules.Map.PopupEffect Popup, string LeaderName = "",
                                  string LeaderType = "", bool OwnName = false);

/// <summary>Предложение ИИ человеку: от кого, кому и в какой день. <paramref name="Trade"/> пусто — союз
/// (<c>ProposeAlliance 0x41ec75</c>, ответ — <see cref="GameSession.AnswerAllianceOffer"/>); иначе сделка-«подарок»
/// (<c>ProposeTrade 0x41eb74</c>, ответ — <see cref="GameSession.AnswerTradeOffer"/>). <paramref name="Exchange"/> — обмен картами
/// от человека человеку (<c>0x40740b</c>, ответ — <see cref="GameSession.AnswerExchangeOffer"/>).</summary>
public sealed record AllianceOffer(string From, string To, int Day, LastEdict.Rules.Map.AiTradeOffer? Trade = null, bool Exchange = false)
{
    /// <summary>Предложение союза: не сделка и не обмен картами.</summary>
    public bool IsAlliance => Trade is null && !Exchange;
}

/// <summary>Место потока R250 (<see cref="LastEdict.Rules.Rng.EngineRng"/>): индекс и таблица из 250 слов — таблица меняется с каждым
/// шагом, одного индекса мало.</summary>
public sealed record RngState(int Index, IReadOnlyList<ushort> Table);

/// <summary>Генераторы партии: потоки R250 по системам (<see cref="LastEdict.Rules.Rng.RngStream"/>: бой, ИИ, добыча, карта — у каждого
/// свой, от зерна партии, Э4) и CRT <c>rand()</c> процесса (<see cref="LastEdict.Rules.Rng.CrtRand"/>, одно слово состояния). Ключ потока
/// — его имя (<see cref="LastEdict.Rules.Rng.RngStreams.Name"/>).</summary>
public sealed record RandomStreams(IReadOnlyDictionary<string, RngState> Streams, uint Crt)
{
    /// <summary>Потоки партии с зерном <paramref name="seed"/> (<see cref="LastEdict.Rules.Rng.RngStreams.Derive"/>).</summary>
    public static RandomStreams FromSeed(ulong seed, uint crt = 1) => new(
        LastEdict.Rules.Rng.RngStreams.All.ToDictionary(LastEdict.Rules.Rng.RngStreams.Name, s =>
        {
            var rng = LastEdict.Rules.Rng.RngStreams.Derive(seed, s);
            return new RngState(rng.Index, rng.TableSnapshot());
        }, StringComparer.OrdinalIgnoreCase), crt);

    /// <summary>Место потока <paramref name="stream"/>; нет в записи — null.</summary>
    public RngState? Of(LastEdict.Rules.Rng.RngStream stream) => Streams.GetValueOrDefault(LastEdict.Rules.Rng.RngStreams.Name(stream));
}

/// <summary>Пересев генераторов партии от зерна: все потоки R250 сразу (<see cref="RandomStreams.FromSeed"/>, Э4) — одно зерно даёт те же
/// броски каждой системы, как бы ни менялись остальные. Новая партия и загруженная из интерфейса берут зерно от таймера (у оригинала —
/// <c>reseed(timeGetTime())</c>, <c>0x429dc8</c>), поэтому перезагрузка не повторяет бросков; проверки и харнессы подают своё зерно и
/// повторяемы.
/// <para>CRT <c>rand()</c> — поток процесса: партия (новая и загруженная) идёт новым потоком игры со <c>srand(1)</c> — состояние 1; у новой
/// партии <see cref="NewGame"/> уже сделал свой шаг, его <paramref name="keepCrt"/> сохраняет.</para></summary>
public static class RandomReseed
{
    public static GameState Reseeded(this GameState state, uint seed, bool keepCrt = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        uint crt = keepCrt && state.Random is { } kept ? kept.Crt : 1;
        return state with { Random = RandomStreams.FromSeed(seed, crt) };
    }
}

/// <summary>Сохранение партии своим форматом порта: JSON с именем формата и версией. <c>.sg</c> порт не пишет —
/// старые форматы только на входе (<c>docs/principles.md</c>).</summary>
public static class GameSave
{
    public const string Format = "d2-port-save";

    /// <summary>Версия формата. Меняется, когда старое сохранение нельзя прочитать новой моделью как есть.
    /// <para>2 — события сценария (<see cref="GameState.Events"/>, <see cref="GameState.EventProgress"/>,
    /// <see cref="GameState.Messages"/>); сохранение версии 1 читается с пустыми событиями.</para>
    /// <para>3 — «горячий стул»: чей ход (<see cref="GameState.ActiveHuman"/>); старые версии читаются — ход первого человека.</para>
    /// <para>4 — выигранные сражения отряда (<c>MapStack.BattlesWon</c>, <c>NBBATTLE</c>); старые версии читаются с нулём.</para>
    /// <para>5 — могилы (<c>MapState.Tombs</c>, <c>CMidTomb</c>) и уровень создания отряда (<c>MapStack.CreationLevel</c>, <c>CREAT_LVL</c>); старые версии читаются без могил, уровень — 1.</para>
    /// <para>6 — глава кампании (<see cref="GameInfo.Campaign"/>); старые версии читаются без кампании.</para>
    /// <para>7 — лидер-триггерер у попапа с <c>BOOLVALUE</c> (<see cref="EventMessage.LeaderName"/>, <c>LeaderType</c>); старые — без
    /// него.</para>
    /// <para>8 — у лидера-триггерера признак собственного имени (<see cref="EventMessage.OwnName"/>); старые — без него (пустое имя —
    /// имя типа, как раньше).</para>
    /// <para>9 — итоги хода людей, ещё не показанные (<see cref="GameState.Summaries"/>, у движка — <c>CTurnSummary</c> в <c>.sg</c>);
    /// старые — без них.</para>
    /// <para>10 — память обмена картами у игрока (<c>MapPlayer.MapExchanges</c>, <c>EXMAPID/EXMAPTURN</c>) и приоритет редактора для ИИ
    /// (<c>MapStack.AiPriority</c>, <c>MapSite.AiPriority</c>, <c>AIPRIORITY</c>), прежние хозяева деревни (<c>MapSite.PreviousOwners</c>,
    /// <c>P_O_*</c>); старые — без памяти обмена и прежних хозяев, приоритет 3 (без поправки).</para>
    /// <para>11 — ссылки на содержимое — id содержимого (<c>base:unit/knight</c>), а не id оригинала (Р18: перенос между версиями Last
    /// Edict); старые переводятся при чтении (<see cref="LegacyRefs"/>).</para>
    /// <para>12 — генераторы по потокам (<see cref="RandomStreams.Streams"/>: бой, ИИ, добыча, карта); у старых единый поток становится
    /// потоком боя, прочие выводятся из его места (<see cref="SplitStreams"/>).</para></summary>
    public const int Version = 12;

    /// <summary>Первая версия с генераторами по потокам.</summary>
    private const int StreamsVersion = 12;

    /// <summary>Первая версия со ссылками по id содержимого.</summary>
    private const int ContentIdsVersion = 11;

    /// <summary>Файл сохранения: сначала то, что показывает список сохранений (имя, день, когда), потом партия.</summary>
    private sealed record Envelope(string Format, int Version, string Title, int Day, DateTime SavedAt, GameInfo Info,
                                   MapLooks Looks, IReadOnlyDictionary<string, AiMemory>? Ai = null,
                                   RandomStreams? Random = null,
                                   IReadOnlyList<LastEdict.Rules.Map.AiTacticRecord>? Tactic = null,
                                   IReadOnlyList<AllianceOffer>? Offers = null,
                                   LastEdict.Rules.Map.ScenarioEventSet? Events = null,
                                   LastEdict.Rules.Map.EventProgress? EventProgress = null,
                                   IReadOnlyList<EventMessage>? Messages = null,
                                   string? ActiveHuman = null,
                                   IReadOnlyDictionary<string, PendingSummary>? Summaries = null);

    public static string ToJson(GameState state, string title = "", DateTime? savedAt = null) =>
        JsonSerializer.Serialize(new Envelope(Format, Version, title, state.Looks.Map.Turn, savedAt ?? DateTime.UtcNow,
                                              state.Info, state.Looks, state.Ai, state.Random, state.Tactic,
                                              state.Offers, state.Events, state.EventProgress, state.Messages,
                                              state.ActiveHuman, state.Summaries), SaveJson.Options);

    /// <exception cref="InvalidDataException">Не сохранение порта или версия новее, чем умеет эта сборка.</exception>
    public static GameState FromJson(string json)
    {
        Envelope? e;
        try
        {
            var node = JsonNode.Parse(json);
            int version = node?["Version"]?.GetValue<int>() ?? 0;
            if (version < ContentIdsVersion) LegacyRefs.ToContentIds(node, LegacyIds.Base);
            if (version < StreamsVersion && node?["Random"] is JsonObject single) node["Random"] = SplitStreams(single);
            e = node.Deserialize<Envelope>(SaveJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"сохранение не читается: {ex.Message}", ex);
        }
        if (e is null || e.Format != Format) throw new InvalidDataException("не сохранение порта");
        if (e.Version > Version) throw new InvalidDataException($"сохранение версии {e.Version}, сборка знает до {Version}");
        if (e.Random?.Streams.Values.FirstOrDefault(s => s.Table.Count != LastEdict.Rules.Rng.EngineRng.Size) is { } bad)
            throw new InvalidDataException($"таблица генератора — {bad.Table.Count} слов, нужно {LastEdict.Rules.Rng.EngineRng.Size}");
        // Счётчики id после загрузки — наибольший номер + 1 (0x5ee996): номера удалённых до записи объектов снова свободны, как у движка.
        var map = GameSession.KnownSpellsSorted(Healed(e.Looks.Map));   // как после чтения движком (KnownSpellOrder, 0x5fe926)
        var looks = GameSession.Retold(e.Looks, GameSession.With(map, idNext: GameSession.IdNextOf(map)), e.Looks.Stacks) with
        {
            // Облик подобранного мешка сборки до fix/mapfix оставляли на карте (GameSession.Enter) — при чтении он снимается.
            Bags = [.. e.Looks.Bags.Where(b => !map.Sites.Any(s => s.Kind == LastEdict.Rules.Map.SiteKind.Bag && s.Gone
                                                                    && s.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase)))],
        };
        return new GameState(e.Info, looks)
        {
            Ai = e.Ai ?? new Dictionary<string, AiMemory>(), Random = e.Random, Tactic = e.Tactic ?? [],
            Offers = e.Offers ?? [],
            Events = e.Events ?? LastEdict.Rules.Map.ScenarioEventSet.Empty,
            EventProgress = e.EventProgress ?? LastEdict.Rules.Map.EventProgress.Empty,
            Messages = e.Messages ?? [],
            ActiveHuman = e.ActiveHuman ?? "",
            Summaries = e.Summaries is { } summaries
                ? new Dictionary<string, PendingSummary>(summaries, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, PendingSummary>(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Единый генератор сохранения до версии 12 (<c>{Index, Table, Crt}</c>) — потоки партии: его место продолжает поток боя (бои
    /// старой партии идут теми же бросками), прочие потоки выводятся из этого места (зерно — свёртка индекса и таблицы).</summary>
    private static JsonObject SplitStreams(JsonObject single)
    {
        int index = single["Index"]?.GetValue<int>() ?? LastEdict.Rules.Rng.EngineRng.DefaultIndex;
        var table = single["Table"]?.AsArray().Select(w => w!.GetValue<ushort>()).ToList() ?? [];
        ulong seed = table.Aggregate((ulong)(uint)index, (h, w) => (h ^ w) * 0x100000001B3UL);
        var streams = RandomStreams.FromSeed(seed).Streams.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (table.Count > 0) streams[LastEdict.Rules.Rng.RngStreams.Name(LastEdict.Rules.Rng.RngStream.Battle)] = new RngState(index, table);
        var split = JsonSerializer.SerializeToNode(new RandomStreams(streams, single["Crt"]?.GetValue<uint>() ?? 1), SaveJson.Options)!.AsObject();
        return split;
    }

    /// <summary>
    /// Сохранения прежних сборок порта согласуются при чтении: признак «лидер жив» отряда (<c>LEADR_ALIV</c>) — по здоровью лидера.
    /// До f782245 зелье воскрешения признак не возвращало, и отряд оставался «без лидера» навсегда («Power To Corrupt — день 20» от
    /// 14.10 00:15: <c>S073KC0001</c> с лидером на 225 HP, <c>S073KC0003</c> — на 150; нарушение <c>leader_alive</c> зонда при
    /// подключении). У движка правило держится всегда — признак пишет каждый, кто убивает или воскрешает лидера; на сохранениях
    /// оригинала из корпуса нарушений нет.
    /// </summary>
    private static LastEdict.Rules.Map.MapState Healed(LastEdict.Rules.Map.MapState map)
    {
        bool Stale(LastEdict.Rules.Map.MapStack s) => map.Units.TryGetValue(s.LeaderId, out var leader) && (leader.Hp > 0) != s.LeaderAlive;
        if (!map.Stacks.Values.Any(Stale)) return map;
        var stacks = map.Stacks.ToDictionary(p => p.Key, p => Stale(p.Value) ? p.Value with { LeaderAlive = !p.Value.LeaderAlive } : p.Value,
                                             StringComparer.OrdinalIgnoreCase);
        return GameSession.With(map, stacks: stacks);
    }

    public static void Write(string path, GameState state, string title = "")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Сначала во временный файл: оборванная запись не должна портить прежнее сохранение.
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(state, title));
        File.Move(temp, path, overwrite: true);
    }

    public static GameState Read(string path) => FromJson(File.ReadAllText(path));
}

/// <summary>Строка списка сохранений: файл, имя, сценарий, день и время записи.</summary>
/// <param name="Humans">Сколько людей в партии (<c>Info.Humans</c>): по нему меню загрузки отличает «горячий стул» от одиночной игры —
/// отдельного флага нет и у движка (ячейки людей заголовка, <c>0x4df9a2</c>).</param>
/// <param name="Kind">Квест (<see cref="SaveKind.Quest"/>), глава саги или своей кампании — меню загрузки показывает их в своих окнах
/// (<c>0x4df375</c>: сага — кампания заголовка не <c>C000CC0001</c>; своя кампания — отдельный <c>.csg</c> в <c>SaveGame</c>).</param>
public sealed record SaveSlot(string Path, string Title, string Scenario, int Day, DateTime SavedAt, int Humans = 1,
                              SaveKind Kind = SaveKind.Quest);

/// <summary>Какая партия в сохранении: квест (и «горячий стул»), сага, своя кампания.</summary>
public enum SaveKind
{
    Quest,
    Saga,
    Custom,
}

/// <summary>Каталог сохранений игрока (<c>user://saves</c>): файлы <c>*.d2save</c>, новые сверху.</summary>
public static class SaveSlots
{
    private static SaveKind KindOf(JsonElement info) =>
        !info.TryGetProperty("Campaign", out var c) || c.ValueKind != JsonValueKind.Object ? SaveKind.Quest
        : c.TryGetProperty("Custom", out var custom) && custom.ValueKind == JsonValueKind.True ? SaveKind.Custom
        : SaveKind.Saga;

    public const string Extension = ".d2save";

    /// <summary>Сохранения каталога; нечитаемый файл пропускается (в списке его не будет), а не роняет список.</summary>
    public static IReadOnlyList<SaveSlot> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var slots = new List<SaveSlot>();
        foreach (string path in Directory.GetFiles(dir, "*" + Extension))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.GetProperty("Format").GetString() != GameSave.Format) continue;
                var info = root.GetProperty("Info");
                string title = root.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                slots.Add(new SaveSlot(path, title.Length > 0 ? title : System.IO.Path.GetFileNameWithoutExtension(path),
                                       info.GetProperty("ScenarioName").GetString() ?? "",
                                       root.TryGetProperty("Day", out var d) ? d.GetInt32() : 0,
                                       root.TryGetProperty("SavedAt", out var at) ? at.GetDateTime() : File.GetLastWriteTimeUtc(path),
                                       info.TryGetProperty("Humans", out var h) && h.ValueKind == JsonValueKind.Array ? h.GetArrayLength() : 1,
                                       KindOf(info)));
            }
            catch (Exception e) when (e is JsonException or IOException or KeyNotFoundException or InvalidOperationException
                                          or FormatException)
            {
                // Чужой или битый файл в каталоге сохранений — не сохранение порта.
            }
        }
        return [.. slots.OrderByDescending(s => s.SavedAt)];
    }

    /// <summary>Файл для имени сохранения: запрещённые в именах файлов знаки заменены, пустое имя — «save».</summary>
    public static string PathFor(string dir, string title)
    {
        var name = new string([.. title.Trim().Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)]);
        return System.IO.Path.Combine(dir, (name.Length == 0 ? "save" : name) + Extension);
    }
}
