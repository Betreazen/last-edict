using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace D2.Data.Diagnostics;

/// <summary>
/// Журнал зонда порта: строки JSON (<c>.jsonl</c>) — что видел и делал игрок и что сломалось. Потокобезопасен: ошибки и сообщения
/// движка приходят с любого потока (ход ИИ идёт в фоне). Общие поля строки: <c>seq</c> (номер по порядку), <c>t</c> (мс от начала),
/// <c>th</c> (поток), <c>ev</c> (вид). Разбор — <c>tools/port_log_report.py</c>.
/// <para>Повторы одной ошибки (тот же текст и то же место в коде) пишутся целиком первые <see cref="FullRepeats"/> раз, дальше —
/// только счёт; итог по всем — строка <c>error_counts</c> при закрытии. У ошибки — последние <see cref="ContextLines"/> строк
/// сообщений игры перед ней (что происходило).</para>
/// </summary>
public sealed class PortJournal : IDisposable
{
    /// <summary>Сколько раз одна ошибка пишется целиком.</summary>
    public const int FullRepeats = 3;

    /// <summary>Сколько последних сообщений игры прикладывается к ошибке.</summary>
    public const int ContextLines = 12;

    private readonly object _lock = new();
    private readonly TextWriter _out;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Queue<string> _recent = new();
    private readonly Dictionary<string, int> _errorCounts = new(StringComparer.Ordinal);
    private long _seq;
    private bool _closed;
    private int _failures;

    /// <summary>Сколько сбоев записи подряд терпит журнал: дальше он выключается (полный диск, файл заблокирован) — зонд не должен
    /// ни ронять игру, ни тормозить её попытками писать.</summary>
    public const int MaxFailures = 5;

    public PortJournal(string path)
        : this(path, Open(path))
    {
    }

    /// <summary>Журнал в готовый поток <paramref name="writer"/> (проверки: поток, запись в который бросает).</summary>
    public PortJournal(string path, TextWriter writer)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(writer);
        Path = path;
        _out = writer;
    }

    private static StreamWriter Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        return new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
        {
            AutoFlush = true,   // строки нужны и при падении процесса
        };
    }

    /// <summary>Журнал выключен после <see cref="MaxFailures"/> сбоев записи подряд (или закрыт).</summary>
    public bool Disabled
    {
        get { lock (_lock) return _closed; }
    }

    /// <summary>Последний сбой записи (текст) или null.</summary>
    public string? LastFailure { get; private set; }

    /// <summary>Файл журнала.</summary>
    public string Path { get; }

    /// <summary>Строк записано.</summary>
    public long Count => Interlocked.Read(ref _seq);

    /// <summary>Записать строку вида <paramref name="ev"/>; поля — простые значения, списки или словари (сериализуются как есть).
    /// Не бросает никогда: сбой записи или разбора поля — счёт сбоев, после <see cref="MaxFailures"/> подряд журнал выключается (его зовут
    /// и из хода ИИ, и из конца боя — исключение отсюда порвало бы партию).</summary>
    public void Write(string ev, IReadOnlyDictionary<string, object?>? fields = null)
    {
        if (string.IsNullOrEmpty(ev)) return;
        lock (_lock)
        {
            if (_closed) return;
            var line = new Dictionary<string, object?>
            {
                ["seq"] = ++_seq,
                ["t"] = _clock.ElapsedMilliseconds,
                ["th"] = Environment.CurrentManagedThreadId,
                ["ev"] = ev,
            };
            try
            {
                if (fields is not null)
                    foreach (var (key, value) in fields) line[key] = value;
                _out.WriteLine(JsonSerializer.Serialize(line, Json));
                _failures = 0;
            }
            catch (Exception e)
            {
                LastFailure = $"{e.GetType().Name}: {e.Message}";
                if (++_failures >= MaxFailures) Shut();
            }
        }
    }

    /// <summary>Выключить журнал (под замком): поток закрывается, сбой закрытия глотается.</summary>
    private void Shut()
    {
        _closed = true;
        try
        {
            _out.Dispose();
        }
        catch (Exception e)
        {
            LastFailure = $"{e.GetType().Name}: {e.Message}";
        }
    }

    /// <summary>Сообщение игры (<c>GD.Print</c> и прочие строки движка). Вид — первое слово строки (<c>walked</c>, <c>battle</c>…).</summary>
    public void Message(string text, bool stderr = false)
    {
        if (text is null) return;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            lock (_lock)
            {
                _recent.Enqueue(line);
                while (_recent.Count > ContextLines) _recent.Dequeue();
            }
            Write(stderr ? "stderr" : "print", new Dictionary<string, object?> { ["tag"] = Tag(line), ["text"] = line });
        }
    }

    /// <summary>
    /// Ошибка или предупреждение (движок Godot, необработанное исключение C#, свой отказ). <paramref name="where"/> — место: файл и строка
    /// в коде порта, если есть. Повтор одного ключа (текст + место) после <see cref="FullRepeats"/> — только счёт.
    /// </summary>
    public void Error(string kind, string message, string? where = null, string? backtrace = null)
    {
        if (string.IsNullOrEmpty(kind) || message is null) return;
        string key = $"{kind}|{FirstLine(message)}|{where}";
        int n;
        string[] context;
        lock (_lock)
        {
            n = _errorCounts[key] = _errorCounts.GetValueOrDefault(key) + 1;
            context = [.. _recent];
        }
        if (n > FullRepeats)
        {
            if (IsPowerOfTwo(n)) Write("error_repeat", new Dictionary<string, object?> { ["kind"] = kind, ["key"] = key, ["n"] = n });
            return;
        }
        Write("error", new Dictionary<string, object?>
        {
            ["kind"] = kind, ["message"] = message, ["where"] = where, ["backtrace"] = backtrace, ["n"] = n, ["context"] = context,
        });
    }

    /// <summary>Итог повторов ошибок: ключ → сколько раз (строка <c>error_counts</c>).</summary>
    public IReadOnlyDictionary<string, int> ErrorCounts()
    {
        lock (_lock) return new Dictionary<string, int>(_errorCounts, StringComparer.Ordinal);
    }

    /// <summary>Закрыть: итог повторов ошибок и строка <c>end</c> (её нет — процесс упал).</summary>
    public void Close(string reason)
    {
        var counts = ErrorCounts();
        if (counts.Count > 0) Write("error_counts", new Dictionary<string, object?> { ["counts"] = counts });
        Write("end", new Dictionary<string, object?> { ["reason"] = reason });
        lock (_lock)
        {
            if (_closed) return;
            Shut();
        }
    }

    public void Dispose() => Close("dispose");

    /// <summary>Вид сообщения — его первое слово (латиница, цифры, подчёркивание), иначе <c>text</c>.</summary>
    public static string Tag(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        int n = 0;
        while (n < line.Length && (char.IsAsciiLetterOrDigit(line[n]) || line[n] == '_')) n++;
        return n == 0 ? "text" : line[..n];
    }

    private static string FirstLine(string text)
    {
        int cut = text.IndexOf('\n');
        return cut < 0 ? text : text[..cut];
    }

    private static bool IsPowerOfTwo(int n) => (n & (n - 1)) == 0;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
