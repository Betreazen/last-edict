using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace D2.Data.Leaders;

/// <summary>
/// Файл героя игры <c>.ldr</c> (<c>Exports\имя.ldr</c>) — только импорт в формат порта (<see cref="LeaderStore.Import"/>). Разбор —
/// <c>docs/day-cycle.md</c> §12 (запись <c>0x4091b7</c>, чтение <c>0x409b5b</c>): заголовок 14 байт — <c>"D2LDR\0"</c>, версия (int32,
/// не больше 6), сумма 4096 байт тела как они лежат в файле (uint32); тело — каждый байт <c>0xFF − b</c>, дальше поток little-endian
/// (строка — int32 длина вместе с нулём и байты cp1251; список — int32 число и элементы; id — сырой <c>CMidgardID</c>). Хвост тела —
/// шум <c>rand()</c>.
/// </summary>
public static class LdrFile
{
    public const string Extension = ".ldr";
    private const int HeaderSize = 14, BodySize = 0x1000, MaxVersion = 6, MaxString = 0x400;

    /// <exception cref="InvalidDataException">Не файл героя, версия новее 6, сумма не сходится или запись обрывается.</exception>
    public static ExportedLeader Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length < HeaderSize + BodySize || !file.AsSpan(0, 6).SequenceEqual("D2LDR\0"u8))
            throw new InvalidDataException("не файл героя D2LDR");
        int version = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(6));
        if (version > MaxVersion) throw new InvalidDataException($"файл героя версии {version}, известно до {MaxVersion}");
        uint sum = 0;
        foreach (byte b in file.AsSpan(HeaderSize, BodySize)) sum += b;
        if (sum != BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10))) throw new InvalidDataException("сумма файла героя не сходится");
        var body = new byte[BodySize];
        for (int i = 0; i < BodySize; i++) body[i] = (byte)(0xFF - file[HeaderSize + i]);
        try
        {
            return Parse(new Stream(body), version);
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException("запись героя обрывается", e);
        }
    }

    private static ExportedLeader Parse(Stream s, int version)
    {
        string name = s.Str();
        int race = s.I32();
        string unitType = s.Id();
        int level = s.I32(), leadership = s.I32(), xp = s.I32(), xpNext = s.I32(), battles = s.I32(), hp = s.I32(), armor = s.I32();
        string attack1 = s.Id(), attack2 = s.Id();
        int cls = s.I32();
        string attackName = s.Str();
        int power = s.I32(), attackLevel = s.I32(), damage = s.I32(), source = s.I32(), initiative = s.I32(), reach = s.I32();
        string attack2Name = s.Str();
        int attack2Power = s.I32();
        bool twice = s.U8() != 0;
        int move = s.I32(), scout = s.I32(), negotiate = s.I32();
        bool terrain = s.U8() != 0;
        return new ExportedLeader
        {
            Name = name, Race = race, UnitType = unitType, Level = level, Leadership = leadership, Xp = xp, XpNext = xpNext,
            BattlesWon = battles, HitPoints = hp, Armor = armor,
            Attack = new LeaderAttack(attack1, cls, attackName, power, attackLevel, damage, source, initiative, reach),
            Attack2 = attack2, Attack2Name = attack2Name, Attack2Power = attack2Power, AttacksTwice = twice,
            Move = move, Scout = scout, Negotiate = negotiate, TerrainBonus = terrain,
            ImmuneSources = s.List(s.I32), ImmuneClasses = s.List(s.I32), WardSources = s.List(s.I32), WardClasses = s.List(s.I32),
            Abilities = s.List(s.I32), Modifiers = s.List(s.Id),
            Equipment = s.List(() => new LeaderItem(s.I32(), s.Id(), s.I32())),
            UpgradeCount = version > 5 ? s.I32() : 0,
        };
    }

    /// <summary>Строка id по сырому <c>CMidgardID</c>: категория (<c>G</c> — глобальный), номер сценария, тип, номер (<c>G000UU0021</c>);
    /// 0 — пусто.</summary>
    public static string IdOf(int raw)
    {
        if (raw == 0) return "";
        uint v = (uint)raw;
        char category = "GCSX"[(int)(v >> 30)];
        int type = (int)((v >> 16) & 0x3f);
        string code = type switch { 6 => "UU", 8 => "UM", 9 => "AA", 0xc => "IG", _ => type.ToString("X2", CultureInfo.InvariantCulture) };
        return string.Create(CultureInfo.InvariantCulture, $"{category}{(v >> 22) & 0xff:D3}{code}{v & 0xffff:X4}");
    }

    private sealed class Stream(byte[] data)
    {
        private int _at;

        public int I32()
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_at, 4));
            _at += 4;
            return v;
        }

        public int U8() => _at < data.Length ? data[_at++] : throw new ArgumentOutOfRangeException(nameof(data), "байт за концом записи");

        public string Id() => IdOf(I32());

        public string Str()
        {
            int n = I32();
            if (n is < 0 or > MaxString) throw new ArgumentOutOfRangeException(nameof(data), $"строка длины {n}");
            var bytes = data.AsSpan(_at, n);
            _at += n;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes).TrimEnd('\0');
        }

        public IReadOnlyList<T> List<T>(Func<T> item)
        {
            int n = I32();
            if (n is < 0 or > 256) throw new InvalidDataException($"список длины {n}");
            var list = new List<T>(n);
            for (int i = 0; i < n; i++) list.Add(item());
            return list;
        }
    }
}
