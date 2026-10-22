namespace Kaitai;

/// <summary>Чтения сгенерированного разборщика <c>Sg.g.cs</c> без лишних выделений памяти. Рантайм Kaitai 0.11 на каждое <c>u4le</c>
/// выделяет массив <c>byte[4]</c> (<c>ReadBytesNormalisedLittleEndian</c>), имя каждого поля записи (<c>IntRecord("POS_Y")</c> и т. п.)
/// читает в новый массив и декодирует в новую строку, а маску тумана — по биту (<c>ReadBitsIntBe(1)</c>). Сохранение 96×96 — сотни
/// тысяч таких полей; сборка мусора от них — ~2/3 времени разбора (профиль <c>parse_sg_t48</c>, 21.10). Ответ тот же: те же байты,
/// те же исключения конца потока, имя поля — та же строка (ожидаемое имя совпало байт в байт — берётся она, иначе декодируется).
/// Подстановку делает <c>tools/ksy/compile.js</c> при каждой генерации; целые — прямо <see cref="System.IO.BinaryReader"/>
/// (<c>ReadUInt32</c> всегда little-endian).</summary>
internal static class SgRead
{
    /// <summary>Ёмкость списка под <paramref name="count"/> элементов из поля файла (сгенерированный разбор, <c>tools/ksy/compile.js</c>): не
    /// меньше 0 и не больше 2²⁰ — испорченное поле не должно занять память заранее (список дорастёт сам).</summary>
    public static int Capacity(long count) => (int)Math.Clamp(count, 0, 1 << 20);

    /// <summary>Имя поля записи длиной <paramref name="expected"/>: совпало байт в байт с ожидаемым (ASCII) — ожидаемая строка.</summary>
    public static string Name(KaitaiStream io, string expected)
    {
        int n = expected.Length;
        Span<byte> bytes = n <= 64 ? stackalloc byte[n] : new byte[n];
        io.BaseStream.ReadExactly(bytes);
        for (int i = 0; i < n; i++)
            if (bytes[i] != expected[i]) return Cp1251.Encoding.GetString(bytes);
        return expected;
    }

    /// <summary>Маска из <paramref name="count"/> битов, старший бит байта — первый (как <c>ReadBitsIntBe(1)</c> подряд с границы
    /// байта). Число битов кратно 8 (<c>fog.value * 8</c>), поэтому поток после маски снова на границе байта, как и у побитового
    /// чтения.</summary>
    public static List<bool> BitsBe(KaitaiStream io, long count)
    {
        if (count % 8 != 0) throw new ArgumentOutOfRangeException(nameof(count), count, "маска не кратна байту");
        io.AlignToByte();
        var bytes = io.ReadBytes(count / 8);
        var bits = new List<bool>((int)count);
        foreach (byte b in bytes)
            for (int bit = 7; bit >= 0; bit--) bits.Add(((b >> bit) & 1) != 0);
        return bits;
    }
}
