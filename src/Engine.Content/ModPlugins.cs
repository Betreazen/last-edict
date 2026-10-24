using System.Reflection;
using System.Runtime.Loader;

namespace LastEdict.Data.Content;

/// <summary>Точка входа C#-плагина мода (Р21): игра создаёт каждый открытый неабстрактный тип сборки плагина с этим интерфейсом и зовёт
/// <see cref="Register"/> до чтения правил — плагин регистрирует свои поведения (<c>AttackEffects.Register</c>) и прочие точки расширения.</summary>
public interface IModPlugin
{
    void Register(ModInfo mod);
}

/// <summary>Строка отчёта загрузки плагинов; <see cref="Failed"/> — сборка не загружена.</summary>
public sealed record PluginLine(string Text, bool Failed)
{
    public override string ToString() => Text;
}

/// <summary>
/// Загрузка C#-плагинов модов, которые <b>исполняют код</b> (<see cref="ModInfo.ExecutesCode"/>). Сборка грузится в контекст сборок
/// самой игры (<see cref="AssemblyLoadContext.GetLoadContext(Assembly)"/>), чтобы плагин видел те же типы ядра, что игра (одна
/// <c>LastEdict.Rules</c>, один реестр эффектов). Песочницы нет — плагины только у доверенных модов (Р21); песочница скриптов — если моды
/// начнут раздавать чужим (roadmap §6).
/// </summary>
public static class ModPlugins
{
    /// <summary>Загрузить плагины модов по порядку; строки отчёта «мод: сборка → типов N» (<see cref="PluginLine.Failed"/> — сборка не
    /// грузится или её точка входа упала: остальные плагины грузятся дальше, запуск не обрывается). Сборка без точек входа (помощник
    /// плагина) — 0 типов; не сборка .NET (родная библиотека рядом) — пропуск со строкой.</summary>
    public static IReadOnlyList<PluginLine> Load(IEnumerable<ModInfo> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        var context = AssemblyLoadContext.GetLoadContext(typeof(ModPlugins).Assembly) ?? AssemblyLoadContext.Default;
        var lines = new List<PluginLine>();
        foreach (var mod in mods)
            foreach (string dll in mod.Plugins)
            {
                string name = Path.GetFileName(dll);
                try
                {
                    var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));
                    lines.Add(new($"{mod.Id}: {name} → типов {Register(assembly, mod)}", false));
                }
                catch (BadImageFormatException)
                {
                    lines.Add(new($"{mod.Id}: {name} — не сборка .NET, пропущена", false));
                }
#pragma warning disable CA1031 // плагин — чужой код: любое его исключение — строка отчёта, а не обрыв запуска игры
                catch (Exception e)
#pragma warning restore CA1031
                {
                    lines.Add(new($"{mod.Id}: {name} не загружен — {e.GetType().Name}: {e.Message}", true));
                }
            }
        return lines;
    }

    /// <summary>Создать точки входа сборки и зарегистрировать их; сколько типов.</summary>
    public static int Register(Assembly assembly, ModInfo mod)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        int count = 0;
        foreach (var type in assembly.GetExportedTypes().Where(t => typeof(IModPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (Activator.CreateInstance(type) is not IModPlugin plugin)
                throw new InvalidDataException($"мод {mod.Id}: {type.FullName} без открытого конструктора без параметров");
            plugin.Register(mod);
            count++;
        }
        return count;
    }
}
