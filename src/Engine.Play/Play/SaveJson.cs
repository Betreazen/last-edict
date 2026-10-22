using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2.Rules.Map;

namespace D2.Data.Play;

/// <summary>Настройки JSON сохранения. Модель карты (<see cref="MapState"/>, <see cref="Map.MapLooks"/>) пишется как
/// есть, без отдельного слоя DTO; то, чего стандартный сериализатор не умеет, покрывают конвертеры ниже:
/// двумерные массивы (клетки, туман, деревья), план карты с ключом-клеткой и словари с id — они в модели
/// нечувствительны к регистру (id в файлах игры бывают в разном регистре), и после чтения должны остаться такими.</summary>
public static class SaveJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            IgnoreReadOnlyProperties = true,
            IncludeFields = false,
        };
        options.Converters.Add(new TileGridConverter());
        options.Converters.Add(new BoolGridConverter());
        options.Converters.Add(new ByteGridConverter());
        options.Converters.Add(new PlanConverter());
        options.Converters.Add(new IdDictionaryConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Двумерный массив <c>[x, y]</c> как <c>{"w":…,"h":…,"data":"base64"}</c>, байты по строкам y.</summary>
    private abstract class GridConverter<T> : JsonConverter<T[,]>
    {
        protected abstract byte Encode(T value);

        protected abstract T Decode(byte value);

        public override T[,] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            int w = -1, h = -1;
            byte[]? data = null;
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("сетка: ждали объект");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string name = reader.GetString() ?? "";
                reader.Read();
                switch (name)
                {
                    case "w": w = reader.GetInt32(); break;
                    case "h": h = reader.GetInt32(); break;
                    case "data": data = reader.GetBytesFromBase64(); break;
                    default: reader.Skip(); break;
                }
            }
            if (w < 0 || h < 0 || data is null || data.Length != w * h)
                throw new JsonException($"сетка {w}×{h}: данных {data?.Length ?? 0}");
            var grid = new T[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) grid[x, y] = Decode(data[(y * w) + x]);
            return grid;
        }

        public override void Write(Utf8JsonWriter writer, T[,] value, JsonSerializerOptions options)
        {
            int w = value.GetLength(0), h = value.GetLength(1);
            var data = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) data[(y * w) + x] = Encode(value[x, y]);
            writer.WriteStartObject();
            writer.WriteNumber("w", w);
            writer.WriteNumber("h", h);
            writer.WriteBase64String("data", data);
            writer.WriteEndObject();
        }
    }

    private sealed class TileGridConverter : GridConverter<Tile>
    {
        protected override byte Encode(Tile value) => value.Raw;

        protected override Tile Decode(byte value) => new(value);
    }

    private sealed class BoolGridConverter : GridConverter<bool>
    {
        protected override byte Encode(bool value) => value ? (byte)1 : (byte)0;

        protected override bool Decode(byte value) => value != 0;
    }

    private sealed class ByteGridConverter : GridConverter<byte>
    {
        protected override byte Encode(byte value) => value;

        protected override byte Decode(byte value) => value;
    }

    /// <summary>План карты (<see cref="MapState.Plan"/>): клетка → id объектов, как список <c>[x, y, [ids]]</c>.</summary>
    private sealed class PlanConverter : JsonConverter<IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>>>
    {
        public override IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var entries = JsonSerializer.Deserialize<List<PlanEntry>>(ref reader, options) ?? [];
            var plan = new Dictionary<(int X, int Y), IReadOnlyList<string>>();
            foreach (var e in entries) plan[(e.X, e.Y)] = e.Ids;
            return plan;
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<(int X, int Y), IReadOnlyList<string>> value,
                                   JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X)
                                                  .Select(p => new PlanEntry(p.Key.X, p.Key.Y, p.Value)).ToList(), options);
    }

    private sealed record PlanEntry(int X, int Y, IReadOnlyList<string> Ids);

    /// <summary>Словари по id (<c>IReadOnlyDictionary&lt;string, T&gt;</c>) читаются нечувствительными к регистру.</summary>
    private sealed class IdDictionaryConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
            && typeToConvert.GetGenericArguments()[0] == typeof(string);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(
                typeof(IdDictionaryConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[1]))!;
    }

    private sealed class IdDictionaryConverter<T> : JsonConverter<IReadOnlyDictionary<string, T>>
    {
        public override IReadOnlyDictionary<string, T> Read(ref Utf8JsonReader reader, Type typeToConvert,
                                                           JsonSerializerOptions options)
        {
            var plain = JsonSerializer.Deserialize<Dictionary<string, T>>(ref reader, options) ?? [];
            return new Dictionary<string, T>(plain, StringComparer.OrdinalIgnoreCase);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, T> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, item) in value.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, item, options);
            }
            writer.WriteEndObject();
        }
    }
}
