using LastEdict.Data.Battle;
using LastEdict.Data.Dbf;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Таблицы игры в данных порта (<c>data/rules/*.json</c>, импорт <c>tools/RulesImport</c>): порт в рантайме
/// читает их, а не <c>.dbf</c> копии игры. Проверяется, что перевод ничего не потерял — значения совпадают клетка в
/// клетку, правила боя из JSON собираются те же, а повторный импорт даёт тот же файл.</summary>
public class RulesDataTests(ITestOutputHelper output)
{
    private static string Rules => ImportedData.PathOf("rules");

    [SkippableFact]
    public void Rules_AreImported()
    {
        Assert.True(Directory.Exists(Rules), $"нет {Rules}");
        Assert.True(GameTables.Has(Rules, "Gunits.dbf"));
        var units = GameTables.Read(Rules, "Gunits.dbf");
        Assert.Equal(356, units.Rows.Count);
        var knight = units.Rows.Single(r => r.GetString("UNIT_ID").Equals("g000uu0019", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(150, knight.GetInt("HIT_POINT"));
        Assert.Equal(20, knight.GetInt("MOVE"));
        Assert.False(knight.GetBool("WATER_ONLY"));
    }

    /// <summary>Каждая клетка каждой таблицы совпадает с исходной <c>.dbf</c>.</summary>
    [SkippableFact]
    public void Tables_MatchGameFiles()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        int tables = 0, cells = 0;
        var problems = new List<string>();
        foreach (string file in Directory.GetFiles(Rules, "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            var ours = GameTables.FromJson(File.ReadAllText(file));
            var game = GameTables.Read(GamePaths.Globals, name + ".dbf");
            tables++;
            if (ours.Rows.Count != game.Rows.Count)
            {
                problems.Add($"{name}: строк {ours.Rows.Count}, в игре {game.Rows.Count}");
                continue;
            }
            foreach (var column in game.Columns)
                for (int i = 0; i < game.Rows.Count; i++)
                {
                    string a = ours.Rows[i].GetString(column.Name), b = game.Rows[i].GetString(column.Name);
                    cells++;
                    if (a != b) problems.Add($"{name}.{column.Name} строка {i}: «{a}» вместо «{b}»");
                    if (problems.Count > 20) return;
                }
        }
        output.WriteLine($"таблиц {tables}, сверено клеток {cells}");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Правила боя, собранные из данных порта, — те же, что из таблиц игры.</summary>
    [SkippableFact]
    public void GameRules_FromPortData_MatchGameFiles()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        var ours = RulesLoader.Load(Rules);
        var game = RulesLoader.Load(GamePaths.Globals);

        Assert.Equal(game.Units.Count, ours.Units.Count);
        Assert.Equal(game.Attacks.Count, ours.Attacks.Count);
        // Записи со списками сравниваются по ссылке на список, поэтому константы — по полям.
        var (a, b) = (game.Constants, ours.Constants);
        Assert.Equal((a.BatInit, a.BatDamage, a.MaxDamage, a.MaxArmor, a.LeaderBonusMaxDamage, a.DefendBonus,
                      a.BatBreak, a.BatBmodif, a.BatLowerIni),
                     (b.BatInit, b.BatDamage, b.MaxDamage, b.MaxArmor, b.LeaderBonusMaxDamage, b.DefendBonus,
                      b.BatBreak, b.BatBmodif, b.BatLowerIni));
        Assert.Equal(a.BatBoostDamage, b.BatBoostDamage);
        Assert.Equal(a.BatLowerDamage, b.BatLowerDamage);
        // Те же списки внутри записей: сверяем поля и содержимое списков, а не ссылки.
        foreach (var (id, unit) in game.Units)
        {
            var mine = ours.Units[id];
            Assert.Equal((unit.Id, unit.Level, unit.HitPoints, unit.Armor, unit.AttackId, unit.Attack2Id,
                          unit.AttackTwice, unit.SizeSmall, unit.XpKilled, unit.XpNext, unit.PrevId,
                          unit.Category, unit.IsLeader),
                         (mine.Id, mine.Level, mine.HitPoints, mine.Armor, mine.AttackId, mine.Attack2Id,
                          mine.AttackTwice, mine.SizeSmall, mine.XpKilled, mine.XpNext, mine.PrevId,
                          mine.Category, mine.IsLeader));
            Assert.Equal(unit.ImmunitiesBySource, mine.ImmunitiesBySource);
            Assert.Equal(unit.ImmunitiesByClass, mine.ImmunitiesByClass);
        }
        foreach (var (id, attack) in game.Attacks)
        {
            var mine = ours.Attacks[id];
            Assert.Equal((attack.Id, attack.Initiative, attack.Class, attack.Power, attack.Reach, attack.QtyDamage,
                          attack.QtyHeal, attack.CritHit, attack.Level, attack.Infinite, attack.Source, attack.AltAttackId),
                         (mine.Id, mine.Initiative, mine.Class, mine.Power, mine.Reach, mine.QtyDamage,
                          mine.QtyHeal, mine.CritHit, mine.Level, mine.Infinite, mine.Source, mine.AltAttackId));
            Assert.Equal(attack.Wards ?? [], mine.Wards ?? []);
        }
        output.WriteLine($"юнитов {ours.Units.Count}, атак {ours.Attacks.Count}");
    }

    /// <summary>Импорт повторяем: те же таблицы дают тот же файл.</summary>
    [SkippableFact]
    public void Import_IsRepeatable()
    {
        Skip.IfNot(GamePaths.IsAvailable);
        foreach (string file in Directory.GetFiles(Rules, "*.json").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string again = GameTables.ToJson(GameTables.Read(GamePaths.Globals, name + ".dbf"));
            // Концы строк не в счёт: рабочая копия git на Windows выписывает файл с CRLF (i/lf w/crlf), импорт пишет LF.
            Assert.True(again.ReplaceLineEndings() == File.ReadAllText(file).ReplaceLineEndings(),
                        $"{name}.json устарел — перезапустите tools/RulesImport");
        }
    }
}
