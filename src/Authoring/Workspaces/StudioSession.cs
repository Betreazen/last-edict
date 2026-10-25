using LastEdict.Authoring.Commands;
using LastEdict.Authoring.Documents;
using LastEdict.Authoring.Project;
using LastEdict.Authoring.Validation;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;

namespace LastEdict.Authoring.Workspaces;

/// <summary>
/// Открытый в Студии мод: документ с историей правок (<see cref="EditHistory"/>), проверка поверх базы (<see cref="ModCheck"/>) и имена
/// сущностей. Общая для рабочих пространств («Дерево построек», «Содержимое», «Баланс»): все правят один документ, отмена — одна;
/// после каждой перемены пространства пересчитываются (<see cref="Refreshed"/>).
/// </summary>
public sealed class StudioSession
{
    private readonly Func<string, string?> _strings;
    private ContentSet? _base;

    /// <param name="strings">Строка по ключу на языке окна (строки игры и модов на диске).</param>
    /// <param name="others">Моды под этим (его зависимости) — их слои накладываются раньше.</param>
    public StudioSession(ModProject project, string baseDir, Func<string, string?> strings, IReadOnlyList<ModInfo>? others = null,
                         string language = "ru")
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        BaseDir = baseDir ?? throw new ArgumentNullException(nameof(baseDir));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        Others = others ?? [];
        Language = language;
        History = new EditHistory(ModDocument.Load(project));
        Report = ModCheck.Run(History.Current, Project, BaseDir, Others);
    }

    public ModProject Project { get; }

    public string BaseDir { get; }

    public IReadOnlyList<ModInfo> Others { get; }

    public string Language { get; }

    public EditHistory History { get; }

    /// <summary>Итог проверки текущего документа и содержимое мода поверх базы.</summary>
    public CheckReport Report { get; private set; }

    /// <summary>Содержимое без этого мода (база и моды под ним) — «было» для сравнения.</summary>
    public ContentSet BaseContent => _base ??= Others.Count == 0 ? ContentSource.Open(BaseDir) : ContentLayers.Load(BaseDir, Others);

    /// <summary>Номер перемены: растёт с каждой правкой, отменой, перечитыванием, отказом.</summary>
    public int Revision { get; private set; }

    /// <summary>Последний отказ (круг требований, неверное значение); нет — null.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Документ поменялся — пространства пересчитываются.</summary>
    public event Action? Refreshed;

    /// <summary>Проверить заново и известить пространства.</summary>
    public void Refresh()
    {
        Report = ModCheck.Run(History.Current, Project, BaseDir, Others);
        Revision++;
        Refreshed?.Invoke();
    }

    /// <summary>Правка командой; отказ команды — <see cref="Refusal"/>, документ прежний.</summary>
    public bool Do(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            History.Do(command);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or InvalidDataException or FormatException)
        {
            return Refuse(e.Message);
        }
        Refusal = null;
        Refresh();
        return true;
    }

    public bool Refuse(string text)
    {
        Refusal = text;
        Revision++;
        Refreshed?.Invoke();
        return false;
    }

    public bool Undo() => Changed(History.Undo());

    public bool Redo() => Changed(History.Redo());

    private bool Changed(bool done)
    {
        if (done) Refresh();
        return done;
    }

    /// <summary>Записать мод (игра с <c>--watch</c> перечитает его сама). Сколько файлов записано и удалено.</summary>
    public int Save()
    {
        var (written, deleted) = History.Current.Save(Project);
        History.MarkSaved();
        _savedPrint = History.Current.Fingerprint();
        Revision++;
        Refreshed?.Invoke();
        return written + deleted;
    }

    /// <summary>Отпечаток документа последней записи Студией: наблюдатель видит и её — свою запись не перечитываем.</summary>
    private string? _savedPrint;

    /// <summary>Мод поменяли снаружи (<c>d2mod</c>, редактор): перечитать, если на диске не то, что в окне. Свою запись (<see cref="Save"/>)
    /// не перечитывает. Есть несохранённые правки — не перечитывает (иначе они пропали бы), а отказывает с текстом: автор решает сам
    /// (<paramref name="force"/> — перечитать всё равно).</summary>
    public bool ReloadFromDisk(bool force = false)
    {
        ModDocument disk;
        try
        {
            disk = ModDocument.Load(Project);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            return Refuse($"мод на диске не читается — {e.Message}");
        }
        string print = disk.Fingerprint();
        if (print == History.Current.Fingerprint() || (!force && print == _savedPrint)) return false;
        if (History.Dirty && !force)
            return Refuse("мод изменён снаружи, а в окне есть несохранённые правки — сохраните их (перезапишут диск) или перечитайте мод");
        History.Reset(disk);
        _savedPrint = print;
        Refresh();
        return true;
    }

    /// <summary>Имя сущности: строка несохранённого документа, строка игры, иначе хвост id.</summary>
    public string NameOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (History.Current.Find(id) is { Source: EntitySource.Folder } folder
            && (folder.Strings.GetValueOrDefault(Language) ?? folder.Strings.GetValueOrDefault("en")) is { } map && map.TryGetValue("name", out var own))
            return own;
        string? key = (Report.Content?.Find(id) ?? BaseContent.Find(id)) switch
        {
            BuildingDef b => b.NameText,
            UnitDef u => u.NameText,
            LordDef l => l.NameText,
            SpellDef s => s.NameText,
            ItemDef i => i.NameText,
            BuildingBranchDef br => br.NameText,
            RaceDef race => race.NameText,
            AttackDef attack => attack.NameText,
            EffectDef effect => effect.Looks.NameText,
            _ => null,
        };
        return key is not null && Text(key) is { Length: > 0 } text ? text : id[(id.LastIndexOf('/') + 1)..];
    }

    /// <summary>Картинка своей сущности-каталога по кадру графики игры (<c>IconBld:TOWERS:BUILDING/SPIRE</c>; оттенок <c>#…</c> не важен) —
    /// байты из документа: окно Студии показывает и несохранённую картинку. Не своя или картинки нет — null.</summary>
    public (System.Collections.Immutable.ImmutableArray<byte> Png, EntityArtSlot Slot)? Picture(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        int colon = frame.IndexOf(':'), tint = frame.LastIndexOf('#');
        if (colon <= 0) return null;
        string archive = frame[..colon], name = tint > colon ? frame[(colon + 1)..tint] : frame[(colon + 1)..];
        foreach (var e in History.Current.Entities.Where(e => e.Files.Count > 0))
            foreach (var slot in EntityArt.Of(e.Kind).Where(s => s.ArchiveName.Equals(archive, StringComparison.OrdinalIgnoreCase)))
                if (e.Files.TryGetValue(slot.File, out var png) && EntityArt.FrameName(e.Id, slot).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (png, slot);
        return null;
    }

    /// <summary>Строка по ключу; нет — null.</summary>
    public string? Text(string key) => _strings(key);

    /// <summary>Сущность мода (своя: id в пространстве имён мода).</summary>
    public bool IsMine(string id) => id.StartsWith(Project.Id + ":", StringComparison.Ordinal);
}
