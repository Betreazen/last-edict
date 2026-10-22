// Загрузчики правил ДО схемы содержимого (main 639961ee): правила читались прямо из строк таблиц оригинала. Эталон сверки
// «старая модель = новая» (ContentRulesParityTests) — не править; удаляется вместе с ней, когда таблицы уйдут из проверок.
using D2.Data.Dbf;
using D2.Rules.Units;

namespace D2.Data.Tests.LegacyLoaders;

/// <summary>Строка <c>GmodifL.dbf</c>, чей <c>BELONGS_TO</c> не находится в <c>Gmodif.dbf</c>:
/// хозяин удалён (запись <c>0x2A</c>), а строка эффекта осталась. Не выбрасывается молча — таких
/// строк семь, они перечислены в тесте <c>F6DataTests</c>.</summary>
public sealed record OrphanElement(string BelongsTo, ModifierElement Element);

/// <summary>Строка <c>Glord.dbf</c> — лорд (класс игрока): 18 живых записей из 42 в заголовке,
/// id <c>g000LR0001…g000LR0018</c>, по три категории (0, 1, 2) на каждую из шести рас
/// <c>Grace.dbf</c>. Заведён ради того, чтобы связь <c>GleaUpg.BELONGS_TO → Glord</c> проверялась
/// тестом, а не подтверждалась вручную (долг задачи 1).</summary>
public sealed record Lord(string Id, string RaceId, int Category, string NameTxt, string DescTxt);

/// <summary>Четыре таблицы одним куском.</summary>
public sealed record ModifierTables(IReadOnlyDictionary<string, Modifier> Modifiers,
                                    IReadOnlyList<OrphanElement> Orphans,
                                    IReadOnlyList<LeaderUpgrade> LeaderUpgrades,
                                    IReadOnlyDictionary<string, Lord> Lords);

/// <summary><c>Gmodif</c> / <c>GmodifL</c> / <c>GleaUpg</c> / <c>Glord</c> -> <see cref="ModifierTables"/>.
/// Удалённые записи <c>.dbf</c> пропускает сам <see cref="DbfTable"/> (семантика
/// <c>tools/dbf_dump.py:16</c>). Ключи регистронезависимы: в <c>.dbf</c> нижний регистр,
/// в корпусе и <c>.sg</c> — верхний.
/// <para>Записи <see cref="Modifier"/> и <see cref="ModifierElement"/> живут в <c>D2.Rules.Units</c>
/// и строятся прямо здесь. Копия с <c>Type</c> числом, заведённая задачей 1 в <c>D2.Data.Battle</c>,
/// снята задачей 3 — пока копий было две, а не четыре. Вместе с ней сняты <c>Gmodif.SOURCE</c> и
/// <c>COMMENTS</c>: <c>SOURCE</c> дублирует то, что задаёт сам <see cref="ModifierElementType"/>,
/// а <c>COMMENTS</c> прямо врут (<c>g000um2503</c> подписан «+25% Move» при <c>PERCENT</c> 20 —
/// находка задачи 2). Хранить то, чем нельзя пользоваться, незачем.</para></summary>
public static class LegacyModifierLoader
{
    public static ModifierTables Load(string globalsDir)
    {
        var heads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Gmodif.dbf").Rows)
        {
            var id = r.GetString("MODIF_ID");
            if (!heads.Add(id))
                throw new InvalidDataException($"Gmodif.dbf: MODIF_ID \"{id}\" встречается дважды");
        }

        var elements = new Dictionary<string, List<ModifierElement>>(StringComparer.OrdinalIgnoreCase);
        var orphans = new List<OrphanElement>();
        foreach (var r in GameTables.Read(globalsDir, "GmodifL.dbf").Rows)
        {
            var owner = r.GetString("BELONGS_TO");
            var element = ReadElement(r);
            if (!heads.Contains(owner)) { orphans.Add(new OrphanElement(owner, element)); continue; }
            if (!elements.TryGetValue(owner, out var list)) elements[owner] = list = [];
            list.Add(element);
        }

        var modifiers = new Dictionary<string, Modifier>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in heads)
            modifiers[id] = new Modifier(id, elements.TryGetValue(id, out var list) ? list : []);

        return new ModifierTables(modifiers, orphans, LoadLeaderUpgrades(globalsDir), LoadLords(globalsDir));
    }

    public static IReadOnlyList<LeaderUpgrade> LoadLeaderUpgrades(string globalsDir) =>
        GameTables.Read(globalsDir, "GleaUpg.dbf").Rows
            .Select(r => new LeaderUpgrade(r.GetString("BELONGS_TO"), LegacyRulesLoader.Ref(r.GetString("MODIF_ID")),
                r.GetInt("MIN_LEVEL"), r.GetInt("PRIORITY")))
            .ToList();

    public static IReadOnlyDictionary<string, Lord> LoadLords(string globalsDir)
    {
        var lords = new Dictionary<string, Lord>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in GameTables.Read(globalsDir, "Glord.dbf").Rows)
        {
            var id = r.GetString("LORD_ID");
            if (!lords.TryAdd(id, new Lord(id, r.GetString("RACE_ID"), r.GetInt("CATEGORY"),
                                           r.GetString("NAME_TXT"), r.GetString("DESC_TXT"))))
                throw new InvalidDataException($"Glord.dbf: LORD_ID \"{id}\" встречается дважды");
        }
        return lords;
    }

    /// <summary><c>TYPE</c> разбирается здесь же, и неизвестное значение — <c>InvalidDataException</c>,
    /// а не тихий каст в перечисление с дыркой: ровно тот же отказ, что до задачи 3 держал тест
    /// <c>F6DataTests.ModifierTypes_AreKnown</c>. Список допустимых значений — <c>Globals/LmodifE.dbf</c>
    /// (значения 8 в нём нет).
    /// <para><b>Как у движка (16.10, ранг 1 по листингу).</b> Элементы читают три читателя по классу модификатора: юнита
    /// (<c>0x5a3d13</c>: Hp, Regeneration, Armor, Immunity, Immunityclass), атаки (<c>0x5a4a90</c>: Power, QtyDamage, Initiative,
    /// AttackDrain) и отряда (<c>0x5a31eb</c>…: ход, обзор, лидерство, способности, бегство, скидка). <c>TYPE 0 L_QTY_TARGET</c> не
    /// читает ни один (его константа <c>[0x838468]</c> в коде не встречается): движок отвечает «DB: Error loading modifier. Invalid type
    /// '%d' in table '%s'» (<c>0x5a918e</c>, строка <c>0x6eb518</c>), то есть мод с ним не загружается — так же здесь.
    /// <c>TYPE 14 Immunityclass</c> движок читает из своих колонок <c>IMMUNITYC</c> (класс атаки, <c>LattC</c>) и <c>IMMUNECATC</c>
    /// (уровень, <c>LImmune</c>) — <c>0x5a3e61</c>…<c>0x5a3e97</c>, а не из <c>IMMUNITY</c>/<c>IMMUNECAT</c> источника; в ванильной
    /// таблице этих колонок нет, их добавляют моды. В записи порта класс ложится в те же поля <see cref="ModifierElement.Immunity"/> и
    /// <see cref="ModifierElement.ImmuneCat"/> — смысл задаёт тип.</para></summary>
    private static ModifierElement ReadElement(DbfRow r)
    {
        int type = r.GetInt("TYPE");
        string owner = r.GetString("BELONGS_TO");
        if (!Enum.IsDefined(typeof(ModifierElementType), type))
            throw new InvalidDataException(
                $"GmodifL.dbf: строка \"{owner}\" несёт TYPE {type}, которого нет в Globals/LmodifE.dbf");
        if ((ModifierElementType)type == ModifierElementType.QtyTarget)
            throw new InvalidDataException(
                $"GmodifL.dbf: строка \"{owner}\" несёт TYPE 0 (L_QTY_TARGET) — его не читает ни один класс модификатора, " +
                "движок отказывает «DB: Error loading modifier. Invalid type '0' in table 'GmodifL'» (0x5a918e)");
        if ((ModifierElementType)type == ModifierElementType.Immunityclass)
        {
            int? cls = r.Has("IMMUNITYC") ? Optional(r, "IMMUNITYC") : null;
            int? level = r.Has("IMMUNECATC") ? Optional(r, "IMMUNECATC") : null;
            if (cls is null || level is null)
                throw new InvalidDataException(
                    $"GmodifL.dbf: строка \"{owner}\" TYPE 14 (Immunityclass) без IMMUNITYC/IMMUNECATC — класс и уровень движок " +
                    "читает только из них (0x5a3e61)");
            return new ModifierElement(ModifierElementType.Immunityclass, Immunity: cls, ImmuneCat: level);
        }
        return new ModifierElement((ModifierElementType)type, Optional(r, "PERCENT"), Optional(r, "NUMBER"),
            Optional(r, "ABILITY"), Optional(r, "IMMUNITY"), Optional(r, "IMMUNECAT"), Optional(r, "MOVE"));
    }

    /// <summary>Пустое числовое поле <c>.dbf</c> — «не задано», а не ноль (<c>ModifierValue.initialized</c>).
    /// <c>DbfRow.GetInt</c> обе формы сводит к нулю, поэтому «пусто» распознаётся до него.</summary>
    internal static int? Optional(DbfRow r, string column) => r[column] is null ? null : r.GetInt(column);
}
