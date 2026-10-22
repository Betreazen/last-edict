using D2.Data.Sound;

namespace D2.Data.Tests;

/// <summary>Опись звуков (<see cref="SoundAssets"/>, <c>data/sound</c>): у каждого заклинания оригинала есть звук литья в
/// <c>Midgard</c> (имя — id заклинания, как у движка <c>0x5b0986</c>).</summary>
public class SoundAssetsTests
{
    [SkippableFact]
    public void Midgard_HasCastSoundOfEverySpell()
    {
        var sounds = SoundAssets.Load(ImportedData.PathOf("sound"));
        Assert.True(sounds.Count >= 844 + 1309 + 23, $"звуков {sounds.Count}");
        for (int i = 1; i <= 120; i++)
            Assert.NotNull(sounds.Find("Midgard", $"G000SS{i:0000}"));
        var first = sounds.Find("midgard", "g000ss0001")!;   // регистр не значим
        Assert.Equal(("mp3", 44100, 1), (first.Codec, first.Rate, first.Channels));
        Assert.Equal(3.318, first.Seconds, 3);
        Assert.Equal("pcm16", sounds.Find("Midgard", "G000SS0037")!.Codec);
        Assert.EndsWith(Path.Combine("Midgard", "G000SS0001.mp3"), SoundAssets.PathOf(first, "root"));
    }

    /// <summary>Звуки юнитов (<see cref="UnitSounds"/>): у мечника замах идёт из списка <c>SWORDSWOOSH</c> (кадр 6, удар 7), его
    /// варианты лежат в <c>Battle</c>; варианты шага <c>WALK SMALL SIZE</c> — в <c>Midgard</c>. Опись не спотыкается о
    /// <c>UnitSounds.json</c> в том же каталоге.</summary>
    [SkippableFact]
    public void UnitSounds_ListsResolveToManifest()
    {
        var sounds = SoundAssets.Load(ImportedData.PathOf("sound"));
        var units = UnitSounds.Load(ImportedData.PathOf("sound/UnitSounds.json"));
        Assert.Equal(345, units.Units.Count);
        var swordsman = units.Of("g000uu0001")!;
        Assert.Equal((6, 7), (swordsman.AttackFrame, swordsman.HitFrame));
        for (int i = 0; i < 4; i++)
        {
            Assert.NotNull(sounds.Find("Battle", units.Pick(swordsman.Attack, n => i % n)!));
            Assert.NotNull(sounds.Find("Battle", units.Pick(swordsman.Hit, n => i % n)!));
            Assert.NotNull(sounds.Find("Midgard", units.Pick(swordsman.Walk, n => i % n)!));
        }
        Assert.Null(units.Pick("", _ => 0));
        Assert.Equal("DEATH", units.Pick("death", _ => 0));
    }

    /// <summary>Звуки, которые ждут точек сессий Б и В (<c>docs/ai-screen.md</c> §11): имена по разбору вызовов проигрывания есть в
    /// своих архивах — зелья вне боя, союз, сделки, итог вора, рост города, захват земли ИИ, разграбление руины ИИ, смена лица
    /// владыки, подарок события, действие знатного; в бою — конец превращения, уход призванного, рост уровня эльфа. Имён, которых нет
    /// (движок их просит — тишина), нет и здесь: смена этого — новые данные, не забыть подключить.</summary>
    [SkippableFact]
    public void PendingSounds_NamesExistWhereEngineAsks()
    {
        var sounds = SoundAssets.Load(ImportedData.PathOf("sound"));
        foreach (string name in new[] { "PBOOST", "PHEAL", "PREVIVE", "USEITEM", "ALLIANCE", "REFALLIA", "BRKALLIA", "TRADITEM",
                                        "TRADSPEL", "REFTRITM", "REFTRSPE", "BKPOPUP", "CITYGROW", "OCCUPY", "LOOTRUIN", "SNOBLE",
                                        "SBATTLE", "CHNGFACE", "TAKEBAG" })
            Assert.True(sounds.Find("Midgard", name) is not null, $"Midgard/{name}");
        Assert.Equal(("TAKEBAG", null), (WindowSounds.OfGiven(first: true), WindowSounds.OfGiven(first: false)));   // раз на сообщение
        foreach (string name in new[] { "UNTRANSF", "UNSUMMON", "UPGRADEEL" })
            Assert.True(sounds.Find("Battle", name) is not null, $"Battle/{name}");
        foreach (string name in new[] { "UTRANSF", "UPARALYZE", "UPETRIFY", "UPGRADEHU", "UPGRADEHE", "UPGRADEDW", "UPGRADEUN", "UPGRADENE" })
            Assert.True(sounds.Find("Battle", name) is null, $"Battle/{name} появился");
        foreach (string name in new[] { "APPEAR", "SEEBAT", "CLOSINTR" })
            Assert.True(sounds.Find("Midgard", name) is null, $"Midgard/{name} появился");
    }

    /// <summary>Звуки окон (<see cref="WindowSounds"/>): у окон движка — <c>OPENINTR</c>, у книг — <c>OPENBOOK</c>/<c>CLOSBOOK</c>; звук
    /// закрытия <c>CLOSINTR</c> движок просит, но в <c>Midgard.wdb</c> его нет — закрытие окна беззвучно, как в игре.</summary>
    [SkippableFact]
    public void WindowSounds_NamesAndManifest()
    {
        var sounds = SoundAssets.Load(ImportedData.PathOf("sound"));
        Assert.Equal(("OPENINTR", "CLOSINTR"), (WindowSounds.Open("DLG_EXCHANGE"), WindowSounds.Close("dlg_exchange")));
        Assert.Equal(("OPENBOOK", "CLOSBOOK"), (WindowSounds.Open("DLG_CAST_SPELL"), WindowSounds.Close("DLG_RESEARCH_SPELL")));
        Assert.Equal(("BOTREPRT", null), (WindowSounds.Open("DLG_BEGIN_TURN"), WindowSounds.Close("DLG_BEGIN_TURN")));
        Assert.Null(WindowSounds.Open("DLG_CAPITAL"));
        foreach (var name in new[] { "OPENINTR", "OPENBOOK", "CLOSBOOK", "BOTREPRT", "AICHAT" })
            Assert.NotNull(sounds.Find(WindowSounds.Archive, name));
        Assert.Null(sounds.Find(WindowSounds.Archive, "CLOSINTR"));
        // Вход в лавку, попапы, действия в окнах и захват — звуки есть в описи Midgard.
        Assert.Equal(("ENTRSITE", "BKPOPUP"), (WindowSounds.Open("DLG_MAGE"), WindowSounds.Open("DLG_OFFER")));
        foreach (var kind in Enum.GetValues<D2.Data.Play.TurnSightKind>())
            if (WindowSounds.OfAction(kind) is { } name) Assert.NotNull(sounds.Find(WindowSounds.Archive, name));
        Assert.Equal("OCCUPY", WindowSounds.OfAction(D2.Data.Play.TurnSightKind.Capture));
        Assert.False(WindowSounds.IsWindowAction(D2.Data.Play.TurnSightKind.Capture));
        foreach (string name in new[] { "ENTRSITE", "BKPOPUP", "ENTRCITY", "LOOTRUIN", "ENTRRUIN", "SBATTLE" })
            Assert.NotNull(sounds.Find(WindowSounds.Archive, name));
    }

    /// <summary>Звуки уведомлений с объектом (<see cref="WindowSounds.OfSight"/>, разбор 27.09): зелье — по категории предмета (4 PBOOST,
    /// 5 PHEAL, 6 PREVIVE, 7 USEITEM), сделка — предмет или заклинание, союз, вор, рост деревни, растекание земли, руина, знатный; в бою
    /// (<see cref="BattleNoteSounds"/>) — откат превращения, уход призванного, рост — «UPGRADE» + раса владельца (есть только у эльфов).</summary>
    [SkippableFact]
    public void SightSounds_ByObject()
    {
        var sounds = SoundAssets.Load(ImportedData.PathOf("sound"));
        var book = D2.Data.Play.GameBook.Load(ImportedData.PathOf("rules"));
        static D2.Data.Play.TurnSight Sight(D2.Data.Play.TurnSightKind kind, string id) => new(kind, "P", 0, 0, id);
        var potions = new (D2.Rules.Magic.ItemCategory Category, string Sound)[]
        {
            (D2.Rules.Magic.ItemCategory.PotionBoost, "PBOOST"), (D2.Rules.Magic.ItemCategory.PotionHeal, "PHEAL"),
            (D2.Rules.Magic.ItemCategory.PotionRevive, "PREVIVE"), (D2.Rules.Magic.ItemCategory.PotionPermanent, "USEITEM"),
        };
        foreach (var (category, sound) in potions)
        {
            var item = book.Items.Values.First(i => i.Category == category);
            Assert.Equal(sound, WindowSounds.OfSight(Sight(D2.Data.Play.TurnSightKind.PotionDrunk, item.Id), book));
        }
        string anyItem = book.Items.Keys.First(), anySpell = book.Spells.Keys.First();
        Assert.Equal(("TRADITEM", "TRADSPEL"), (WindowSounds.OfSight(Sight(D2.Data.Play.TurnSightKind.Traded, anyItem), book),
                                                WindowSounds.OfSight(Sight(D2.Data.Play.TurnSightKind.Traded, anySpell), book)));
        Assert.Equal(("REFTRITM", "REFTRSPE"), (WindowSounds.OfSight(Sight(D2.Data.Play.TurnSightKind.TradeRefused, anyItem), book),
                                                WindowSounds.OfSight(Sight(D2.Data.Play.TurnSightKind.TradeRefused, anySpell), book)));
        Assert.Equal("BUILDING", WindowSounds.OfSight(Sight(D2.Data.Play.TurnSightKind.Built, "x"), book));   // прежние — как OfAction
        foreach (var kind in Enum.GetValues<D2.Data.Play.TurnSightKind>())
            if (WindowSounds.OfSight(Sight(kind, anyItem), book) is { } name) Assert.NotNull(sounds.Find(WindowSounds.Archive, name));

        Assert.Equal(("CHNGFACE", null), (WindowSounds.OfClick("DLG_CHOOSE_LORD", "BTN_FACE"), WindowSounds.OfClick("DLG_HOTSEAT_LOBBY", "BTN_FACE")));
        Assert.NotNull(sounds.Find(WindowSounds.Archive, "CHNGFACE"));

        Assert.Equal("UNTRANSF", BattleNoteSounds.Of(D2.Rules.Battle.BattleNoteKind.Untransformed, -1));
        Assert.Equal("UNSUMMON", BattleNoteSounds.Of(D2.Rules.Battle.BattleNoteKind.Unsummoned, 0));
        Assert.Equal(("UPGRADEEL", "UPGRADEHU", "UPGRADENE"), (BattleNoteSounds.Of(D2.Rules.Battle.BattleNoteKind.LevelUp, 5),
                                                               BattleNoteSounds.Of(D2.Rules.Battle.BattleNoteKind.LevelUp, 0),
                                                               BattleNoteSounds.Of(D2.Rules.Battle.BattleNoteKind.LevelUp, -1)));
        foreach (string name in new[] { "UNTRANSF", "UNSUMMON", "UPGRADEEL" })
            Assert.NotNull(sounds.Find(BattleNoteSounds.Archive, name));
    }
}
