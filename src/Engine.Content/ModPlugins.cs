using System.Reflection;
using System.Runtime.Loader;

namespace LastEdict.Data.Content;

/// <summary>Точка входа C#-плагина мода (Р21): игра создаёт каждый открытый неабстрактный тип сборки плагина с этим интерфейсом и зовёт
/// <see cref="Register"/> до чтения правил — плагин регистрирует свои поведения (<c>AttackEffects.Register</c>) и прочие точки расширения.</summary>
public interface IModPlugin
{
    void Register(ModInfo mod);
}

/// <summary>
/// Загрузка C#-плагинов модов, которые <b>исполняют код</b> (<see cref="ModInfo.ExecutesCode"/>). Сборка грузится в контекст сборок
/// самой игры (<see cref="AssemblyLoadContext.GetLoadContext(Assembly)"/>), чтобы плагин видел те же типы ядра, что игра (одна
/// <c>LastEdict.Rules</c>, один реестр эффектов). Песочницы нет — плагины только у доверенных модов (Р21); песочница скриптов — если моды
/// начнут раздавать чужим (roadmap §6).
/// </summary>
public static class ModPlugins
{
    /// <summary>Загрузить плагины модов по порядку; строки отчёта «мод: сборка → типов N».</summary>
    public static IReadOnlyList<string> Load(IEnumerable<ModInfo> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        var context = AssemblyLoadContext.GetLoadContext(typeof(ModPlugins).Assembly) ?? AssemblyLoadContext.Default;
        var lines = new List<string>();
        foreach (var mod in mods)
            foreach (string dll in mod.Plugins)
            {
                Assembly assembly;
                try
                {
                    assembly = context.LoadFromAssemblyPath(Path.GetFullPath(dll));
                }
                catch (Exception e) when (e is BadImageFormatException or FileLoadException or FileNotFoundException)
                {
                    throw new InvalidDataException($"мод {mod.Id}: плагин {dll} не грузится — {e.Message}", e);
                }
                lines.Add($"{mod.Id}: {Path.GetFileName(dll)} → типов {Register(assembly, mod)}");
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
