using LastEdict.Authoring.Project;
using LastEdict.Authoring.Workspaces;
using LastEdict.Data.Content;
using LastEdict.Data.Content.Schema;
using LastEdict.Data.Play;
using LastEdict.Data.Ui.Windows;

namespace LastEdict.Data.Tests;

/// <summary>Юниты мода в Студии (Студия 2, задача 2) без окна, на test-pack: новый на основе существующего — своя атака, графика основы,
/// своё лицо поверх неё; правка формой (числа атаки — у своей копии, база не меняется), найм и повышение, удаление и переименование.</summary>
public sealed class StudioUnitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "le-studio-u-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StudioSession Session() =>
        new(ModProject.Create(Path.Combine(_root, "towers"), "towers"), TestPackTests.Dir, _ => null, language: "ru");

    private const string Footman = "test:unit/sun-footman", Halberdier = "towers:unit/alebardshchik";

    [Fact]
    public void CreateFrom_CopiesTheUnit_WithItsOwnAttack_AndTheBaseGraphics()
    {
        var session = Session();
        var units = new UnitWorkspace(session);
        Assert.Equal("alebardshchik", units.NameFor("Алебардщик"));
        Assert.True(units.CreateFrom(Footman, "alebardshchik", "Алебардщик"));
        var content = session.Report.Content!;
        var made = (UnitDef)content.Find(Halberdier)!;
        var source = (UnitDef)content.Find(Footman)!;
        Assert.Equal((source.HitPoints, source.Level, source.Race), (made.HitPoints, made.Level, made.Race));
        Assert.Equal(Footman, made.BaseUnit);                         // анимации и значок — основы
        Assert.Equal("towers:attack/alebardshchik", made.Attack);    // своя атака
        Assert.Equal(((AttackDef)content.Find(source.Attack!)!).Damage, ((AttackDef)content.Find(made.Attack!)!).Damage);
        Assert.Equal("Алебардщик", session.NameOf(Halberdier));
        Assert.Equal(Halberdier, units.Selected);
        Assert.Equal(0, session.Report.Errors);
        session.Undo();   // одна команда
        Assert.Empty(session.History.Current.Entities);
    }

    [Fact]
    public void Edit_ByForm_HiresAndUpgrades_AttackNumbersStayOwn()
    {
        var session = Session();
        var units = new UnitWorkspace(session);
        Assert.True(units.CreateFrom(Footman, "alebardshchik", "Алебардщик"));
        var form = units.FormOf(Halberdier);
        int baseDamage = ((AttackDef)session.Report.Content!.Find(((UnitDef)session.Report.Content.Find(Footman)!).Attack!)!).Damage ?? 0;
        Assert.True(units.Edit(Halberdier, form with
        {
            HitPoints = 150, Damage = baseDamage + 30, Initiative = 61, Hireable = true, HireBuilding = "test:building/sun-barracks",
            PreviousLevel = Footman, UpgradeBuilding = "test:building/sun-barracks", Title = "Алебардщик Солнца",
        }));
        var content = session.Report.Content!;
        var made = (UnitDef)content.Find(Halberdier)!;
        var attack = (AttackDef)content.Find(made.Attack!)!;
        Assert.Equal((150, baseDamage + 30, 61), (made.HitPoints, attack.Damage ?? 0, attack.Initiative));
        Assert.Equal(baseDamage, ((AttackDef)content.Find(((UnitDef)content.Find(Footman)!).Attack!)!).Damage);   // база — та же
        Assert.Contains(Halberdier, ((RaceDef)content.Find("test:race/sun")!).Soldiers);
        Assert.Equal("Алебардщик Солнца", session.NameOf(Halberdier));
        Assert.Equal(0, session.Report.Errors);
        // Правила видят найм и повышение.
        var book = GameBook.Load(content);
        Assert.Contains(Halberdier, book.Economy.Races["test:race/sun"].Soldiers);
        Assert.Equal("test:building/sun-barracks", book.UpgradeBuilding[Halberdier]);
        Assert.Contains(LastEdict.Rules.Units.UnitUpgrade.Successors(book.Rules.Units, Footman), u => u.Id == Halberdier);
        // Числа атаки юнита базы: своя копия, общая атака базы не меняется.
        var footman = units.FormOf(Footman);
        Assert.True(units.Edit(Footman, footman with { Damage = (footman.Damage ?? 0) + 1 }));
        var patched = (UnitDef)session.Report.Content!.Find(Footman)!;
        Assert.StartsWith("towers:attack/", patched.Attack, StringComparison.Ordinal);
        Assert.Equal(0, session.Report.Errors);
    }

    [Fact]
    public void ReviewFixes_RaceChangeMovesHire_DerivedKeepGraphics_LegacyNamesRefused()
    {
        var session = Session();
        var units = new UnitWorkspace(session);
        Assert.True(units.CreateFrom(Footman, "alebardshchik", "Алебардщик"));
        Assert.True(units.Edit(Halberdier, units.FormOf(Halberdier) with { Hireable = true }));
        // Смена расы: из солдат Солнца — в солдаты Луны; смена роли — в лидеры.
        Assert.True(units.Edit(Halberdier, units.FormOf(Halberdier) with { Race = "test:race/moon" }));
        var content = session.Report.Content!;
        Assert.DoesNotContain(Halberdier, ((RaceDef)content.Find("test:race/sun")!).Soldiers);
        Assert.Contains(Halberdier, ((RaceDef)content.Find("test:race/moon")!).Soldiers);
        Assert.True(units.Edit(Halberdier, units.FormOf(Halberdier) with { Role = UnitRole.Leader }));
        Assert.Contains(Halberdier, ((RaceDef)session.Report.Content!.Find("test:race/moon")!).Leaders);
        Assert.DoesNotContain(Halberdier, ((RaceDef)session.Report.Content.Find("test:race/moon")!).Soldiers);
        // Производный от своего юнита: удаление основы — графика производного остаётся графикой основы основы.
        Assert.True(units.CreateFrom(Halberdier, "sergeant", "Сержант"));
        Assert.Equal(Footman, ((UnitDef)session.Report.Content!.Find("towers:unit/sergeant")!).BaseUnit);
        Assert.True(units.Edit("towers:unit/sergeant", units.FormOf("towers:unit/sergeant") with { GraphicsFrom = Halberdier }));
        Assert.True(units.Remove(Halberdier));
        Assert.Equal(Footman, ((UnitDef)session.Report.Content!.Find("towers:unit/sergeant")!).BaseUnit);
        Assert.Equal(0, session.Report.Errors);
        // Числа атаки, общей с другим своим юнитом, — своей копией.
        Assert.True(units.CreateFrom(Footman, "twin", "Близнец"));
        Assert.True(units.Edit("towers:unit/twin", units.FormOf("towers:unit/twin") with { Attack = "towers:attack/sergeant" }));
        Assert.True(units.Edit("towers:unit/twin", units.FormOf("towers:unit/twin") with { Initiative = 77 }));
        var twin = (UnitDef)session.Report.Content!.Find("towers:unit/twin")!;
        Assert.NotEqual("towers:attack/sergeant", twin.Attack);
        Assert.NotEqual(77, ((AttackDef)session.Report.Content.Find("towers:attack/sergeant")!).Initiative);
        // Имя вида id оригинала — отказ: графика назвалась бы именем кадра оригинала.
        Assert.False(units.CreateFrom(Footman, "g000uu0154", "Двойник"));
        Assert.Contains("id оригинала", session.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnFace_WinsOverTheBaseGraphics_RemoveCleansTheRaceList()
    {
        var session = Session();
        var units = new UnitWorkspace(session);
        Assert.True(units.CreateFrom(Footman, "alebardshchik", "Алебардщик"));
        Assert.True(units.Edit(Halberdier, units.FormOf(Halberdier) with { Hireable = true }));
        Assert.True(units.SetPicture(Halberdier, "face.png", TestPng.Make(140, 170)));
        Assert.False(units.SetPicture(Halberdier, "icon.png", TestPng.Make(77, 74)));   // у юнита нет значка постройки
        session.Save();
        var art = GameContent.EntityIcons(ModProject.Open(session.Project.Dir).Info);
        var face = art.Image("Faces", "TOWERS:UNIT/ALEBARDSHCHIK" + "FACE");
        Assert.Equal((70, 85, (int?)2), (face!.W, face.H, face.Scale));
        var frames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Faces:TOWERS:UNIT/ALEBARDSHCHIKFACE" };
        var faces = UnitFaces.Load(session.Report.Content!, _ => false, frames.Contains);
        Assert.Equal("TOWERS:UNIT/ALEBARDSHCHIKFACE", faces.SmallName(Halberdier));           // своё лицо
        Assert.Equal($"Faces:{faces.Base(Halberdier)}FACEB", faces.Large(Halberdier));        // портрета нет — основы
        Assert.Equal(faces.Base(Footman) + "FACE", faces.SmallName(Footman));
        // Переименование и удаление: список найма расы — следом, своя атака — с юнитом.
        Assert.True(units.Rename(Halberdier, "halberdier"));
        Assert.Contains("towers:unit/halberdier", ((RaceDef)session.Report.Content!.Find("test:race/sun")!).Soldiers);
        Assert.True(units.Remove("towers:unit/halberdier"));
        Assert.DoesNotContain(session.History.Current.Entities, e => e.Kind is "race" or "attack" or "unit");
        Assert.Equal(0, session.Report.Errors);
    }
}
