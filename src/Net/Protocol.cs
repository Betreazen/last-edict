using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LastEdict.Data.Battle;
using LastEdict.Data.Content;
using LastEdict.Data.Play;
using LastEdict.Data.Play.Commands;

namespace LastEdict.Net;

/// <summary>
/// Сообщение хоста и клиента (<c>docs/network.md</c> §3; по контракту <c>docs/rust-web/03-contracts-and-multiplayer.md</c> §3–6). Пишется
/// JSON с полем <c>"$m"</c>; посредник его не читает — пересылает как есть. Порядковые номера вида — 64-битные, в JSON строкой.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$m")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(Refused), "refused")]
[JsonDerivedType(typeof(CommandRequest), "command")]
[JsonDerivedType(typeof(Receipt), "receipt")]
[JsonDerivedType(typeof(ViewDelta), "delta")]
[JsonDerivedType(typeof(ViewSnapshot), "snapshot")]
[JsonDerivedType(typeof(ResyncRequest), "resync")]
[JsonDerivedType(typeof(TurnInfo), "turn")]
[JsonDerivedType(typeof(BattleRequest), "battleRequest")]
[JsonDerivedType(typeof(BattleReply), "battleReply")]
[JsonDerivedType(typeof(BattleStep), "battleStep")]
[JsonDerivedType(typeof(Chat), "chat")]
public abstract record NetMessage;

/// <summary>Клиент входит или возвращается: версия протокола и сборки, набор правил (<see cref="Ruleset"/>), имя; <see cref="Token"/> —
/// выданный хостом при первом входе (по нему место, а не по полю запроса); <see cref="Epoch"/>/<see cref="Seq"/> — последний вид, который
/// у клиента есть (переподключение дошлёт недостающее, иначе — снимок).</summary>
public sealed record Hello(int Protocol, string Build, Ruleset Ruleset, string Name, string? Token = null, string? Seat = null,
                           string? Epoch = null, [property: JsonNumberHandling(NetJson.Counter)] long Seq = 0) : NetMessage;

/// <summary>Хост принял: чьё место (<see cref="Player"/>, игрок партии) и токен для возвращения.</summary>
public sealed record Welcome(string Player, string Token, string Room) : NetMessage;

/// <summary>Отказ входа: код (<see cref="NetErrors"/>) и понятный текст (какие моды разные, занято место и т.п.).</summary>
public sealed record Refused(string Code, string Text) : NetMessage;

/// <summary>Команда клиента: <see cref="CommandId"/> — для идемпотентности (повтор — прежний ответ), <see cref="ExpectedViewSeq"/> — вид, на
/// котором клиент её подал, <see cref="TurnToken"/> — право хода, выданное хостом (<see cref="TurnInfo"/>).</summary>
public sealed record CommandRequest(string CommandId, [property: JsonNumberHandling(NetJson.Counter)] long ExpectedViewSeq,
                                    string TurnToken, GameCommand Command) : NetMessage;

/// <summary>Ответ на команду: исполнена (<see cref="Error"/> пусто) или код отказа с текстом без скрытого; значение команды
/// (<see cref="ValueCodec"/>), строки отчёта и — для боя на экране клиента — места генератора боя и CRT до команды (<see cref="Seed"/>).</summary>
public sealed record Receipt(string CommandId, string? Error = null, string? Text = null, JsonNode? Value = null,
                             IReadOnlyList<string>? Report = null, RandomStreams? Seed = null) : NetMessage
{
    [JsonIgnore] public bool Ok => Error is null;
}

/// <summary>Дельта вида игрока: правки (<see cref="ViewPatch"/>) от <see cref="BaseSeq"/> к <see cref="Seq"/> внутри <see cref="Epoch"/> и хеш
/// вида после них.</summary>
public sealed record ViewDelta(string Epoch, [property: JsonNumberHandling(NetJson.Counter)] long BaseSeq,
                               [property: JsonNumberHandling(NetJson.Counter)] long Seq, IReadOnlyList<PatchOp> Ops, string Hash) : NetMessage;

/// <summary>Вид целиком (вход, resync): новая эпоха, номер, вид (<see cref="NetView"/>) и его хеш.</summary>
public sealed record ViewSnapshot(string Epoch, [property: JsonNumberHandling(NetJson.Counter)] long Seq, JsonNode View, string Hash) : NetMessage;

/// <summary>Клиент потерял нить вида (дельта не к его номеру, хеш не сошёлся, отказ «вид устарел») — просит снимок.</summary>
public sealed record ResyncRequest(string Reason) : NetMessage;

/// <summary>Чей ход: игрок, право хода (<see cref="Token"/> — только у того, чей ход), день и срок (мс UNIX; null — без таймера).</summary>
public sealed record TurnInfo(string Active, string? Token, int Day, long? Deadline = null, bool Over = false) : NetMessage;

/// <summary>Хост: в ходе ИИ на отряд клиента напали (или бой людей, где клиент — сторона): бой собрать у себя по виду (группы нападающего
/// и защитника; что собран тот же бой — по отпечатку <see cref="BattleFingerprint"/>) и сыграть на генераторе боя и CRT хоста (<see cref="Seed"/>); ответ — <see cref="BattleReply"/>. <see cref="Side"/> —
/// за какую сторону играет клиент: «attacker», «defender»; при <see cref="Lockstep"/> вторую сторону ведёт другой человек ходами
/// <see cref="BattleStep"/>.</summary>
public sealed record BattleRequest(string Battle, string AttackerGroup, string DefenderGroup, string Fingerprint, RandomStreams Seed,
                                   string Side, bool Lockstep = false) : NetMessage;

/// <summary>Бой сыгран: действия по шагам (<see cref="BattlePlay"/>) и журнал ходов — хост повторит бой и сверит журнал. <see cref="Play"/>
/// null — клиент бой не собрал (вид неполон): хост играет автобой.</summary>
public sealed record BattleReply(string Battle, BattlePlay? Play, IReadOnlyList<LastEdict.Rules.Battle.TurnRecord> Log) : NetMessage;

/// <summary>Действие боя двух людей (<see cref="BattleInput"/>): номер действия в бою и само действие — обе стороны играют бой одинаково.</summary>
public sealed record BattleStep(string Battle, int Index, BattleInput Input) : NetMessage;

/// <summary>Строка от игрока (лобби, партия); хост пересылает всем.</summary>
public sealed record Chat(string From, string Text) : NetMessage;

/// <summary>Коды отказов (контракт §3).</summary>
public static class NetErrors
{
    public const string WrongTurn = "WrongTurn", InvalidCommand = "InvalidCommand", StaleView = "StaleView",
                        UnsupportedVersion = "UnsupportedVersion", RulesetMismatch = "RulesetMismatch", RoomFull = "RoomFull",
                        UnknownToken = "UnknownToken", NotYours = "NotYours", Lost = "Lost";
}

/// <summary>Настройки JSON протокола: как у сохранения (двумерные массивы, ключи-клетки), плюс поля записей-кортежей.</summary>
public static class NetJson
{
    public const int ProtocolVersion = 1;

    internal const JsonNumberHandling Counter = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString;

    public static readonly JsonSerializerOptions Options = new(SaveJson.Options) { IncludeFields = true };

    public static string Write(NetMessage message) => JsonSerializer.Serialize(message, Options);

    /// <exception cref="InvalidDataException">Не сообщение протокола.</exception>
    public static NetMessage Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<NetMessage>(json, Options) ?? throw new InvalidDataException("пустое сообщение");
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw new InvalidDataException($"сообщение не читается: {e.Message}", e);
        }
    }
}
