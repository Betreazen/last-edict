using System;
using System.Linq;
using Godot;

namespace LastEdict.Godot;

/// <summary>
/// Болтливый вывод показа — строки о каждом событии карты, хода ИИ и звука (<c>aimove</c>, <c>aisight</c>, <c>walked</c>, <c>fx</c>,
/// <c>region</c>, <c>music</c>, <c>landspread</c>…). Их разбирают проверки (ui-render-check, харнессы по выводу сцен) и отчёт зонда
/// (<c>tools/port_log_report.py</c>: разложение хода ИИ по строкам), но в обычной игре <c>GD.Print</c> на каждое событие дорог: Debug-сборка
/// сбрасывает вывод на каждой строке (13,6 % профиля показа хода ИИ, <c>docs/perf-log.md</c> Г20).
///
/// <para>Куда идёт строка: в вывод (<c>GD.Print</c>, как прежде) — в проверках (<c>--check</c>, <c>--input</c>), с ключом <c>--verbose</c> или
/// переменной <c>D2_VERBOSE=1</c> (её ставит <c>tools/ui-render-check.py</c> всем своим сценам); иначе — только в журнал зонда порта, если
/// он включён (<see cref="PortProbe.Journal"/>: те же строки <c>print</c>, что дал бы перехватчик вывода), без зонда — никуда.
/// Строки, которые печатаются раз на запуск или по действию человека, идут обычным <c>GD.Print</c>.</para>
/// </summary>
public static class GameLog
{
    /// <summary>Болтливый вывод — в вывод движка (проверки, <c>--verbose</c>, <c>D2_VERBOSE=1</c>).</summary>
    public static bool ToOutput { get; } = Decide(OS.GetCmdlineUserArgs(), System.Environment.GetEnvironmentVariable("D2_VERBOSE"));

    /// <summary>Правило <see cref="ToOutput"/> по аргументам после <c>--</c> и переменной <c>D2_VERBOSE</c>.</summary>
    public static bool Decide(string[] args, string? env)
    {
        ArgumentNullException.ThrowIfNull(args);
        return env == "1" || args.Any(a => a is "--check" or "--input" or "--verbose");
    }

    /// <summary>Строка болтливого вывода (см. <see cref="GameLog"/>).</summary>
    public static void Verbose(string line)
    {
        if (ToOutput) GD.Print(line);
        else PortProbe.Journal?.Message(line);
    }
}
