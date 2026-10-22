using System.Linq;
using System.Text;
using Kaitai;

namespace LastEdict.Data.Sg;

/// <summary>
/// Thin wrapper over the Kaitai-generated <see cref="Kaitai.Sg"/> parser for Disciples 2
/// scenario/save files (.sg). Reads the single <c>ScenarioInfo</c> object for scenario
/// metadata and counts top-level objects by their Kaitai type.
/// </summary>
public sealed class SgFile
{
    static SgFile()
    {
        // .sg strings are windows-1251 (cp1251); the provider must be registered
        // once before Kaitai.Sg.FromFile decodes any string_record/int_record name.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private SgFile(Kaitai.Sg raw)
    {
        Raw = raw;
        var info = raw.Objects.Select(o => o.Data).OfType<Kaitai.Sg.ScenarioInfo>().Single();
        ScenarioName = info.Name.String;
        MapSize = (int)info.MapSize.Value;
        PlayerCount = raw.Objects.Count(o => o.Data is Kaitai.Sg.MidPlayer);
        StackCount = raw.Objects.Count(o => o.Data is Kaitai.Sg.MidStack);
    }

    public Kaitai.Sg Raw { get; }
    public string ScenarioName { get; }
    public int MapSize { get; }
    public int PlayerCount { get; }
    public int StackCount { get; }

    /// <summary>Разбор <c>.sg</c> по пути. Кешируется на процесс, пока разобранное живо (только чтение: ни один вызывающий не меняет
    /// <see cref="Raw"/>, как и общий <c>TreasuryJournal</c> — см. его комментарий): один физический файл корпуса
    /// разбирают до полутора десятков харнессов и внутри одного харнесса — по несколько раз (карта, объекты,
    /// сложность). Разбор Kaitai — самая тяжёлая часть чтения сохранения; общий экземпляр убирает повтор.
    /// Не используется вживую движком (порт от <c>.sg</c> не зависит), поэтому рост кеша в долгоживущем процессе
    /// игре не грозит — только инструментам и тестам, где процесс короткий.</summary>
    /// <para>Ключ — полный путь, время записи и размер: файл, переписанный на диске, пока процесс жив (импорт, редактор), разбирается
    /// заново.</para>
    /// <para>Файл читается в память целиком: <c>Kaitai.Sg.FromFile</c> держал бы его открытым без права записи, пока жив
    /// разобранный объект, то есть всю жизнь процесса из-за кеша, и переписать файл было бы нельзя.</para>
    /// <para>Разобранное дерево (<see cref="Raw"/>) кеш держит слабой ссылкой: пока его кто-то держит или сборщик мусора не дошёл до его
    /// поколения, повторное открытие — тот же экземпляр (обёртка живёт, пока живо дерево: вызывающие часто отпускают её сразу, оставляя
    /// себе <see cref="Raw"/>); после — разбор заново (~5 мс на сохранение). Разбор занимает в памяти ~12× файла: на корпусе 20.10
    /// (1265 сохранений) сильный кеш держал до конца прогона 4,6 ГБ в процессе LastEdict.Data.Tests и 4,4 ГБ в LastEdict.Rules.Tests; сильный кеш
    /// последних 384 файлов поверх слабого — дольше и тяжелее слабого (живые деревья удорожают сборку старшего поколения) —
    /// <c>docs/perf-log.md</c>, «Скорость проверок, 20.10».</para>
    public static SgFile Open(string path)
    {
        string full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        string key = info.Exists ? $"{full}|{info.LastWriteTimeUtc.Ticks}|{info.Length}" : full;
        return Cache.GetOrAdd(key, _ => new Entry(full)).Get();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Entry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Обёртка разобранного дерева — жива, пока живо дерево.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Kaitai.Sg, SgFile> Wrappers = new();

    /// <summary>Разобранное дерево файла под слабой ссылкой; одновременные открытия одного файла ждут один разбор.</summary>
    private sealed class Entry(string full)
    {
        private readonly object _gate = new();
        private WeakReference<Kaitai.Sg>? _raw;

        public SgFile Get()
        {
            lock (_gate)
            {
                if (_raw is not null && _raw.TryGetTarget(out var raw) && Wrappers.TryGetValue(raw, out var known)) return known;
                var file = Load(File.ReadAllBytes(full));
                Wrappers.AddOrUpdate(file.Raw, file);
                _raw = new WeakReference<Kaitai.Sg>(file.Raw);
                return file;
            }
        }
    }

    /// <summary>Файл из байтов в памяти (сценарий саги, собранный <see cref="SagaSg"/>).</summary>
    public static SgFile Load(byte[] data) => new(new Kaitai.Sg(new KaitaiStream(data)));
}
