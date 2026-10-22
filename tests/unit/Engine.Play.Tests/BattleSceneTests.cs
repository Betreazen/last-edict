using LastEdict.Data.Content;
using LastEdict.Data.Gfx;
using LastEdict.Data.Ui;
using LastEdict.Rules.Battle;
using Xunit;
using Xunit.Abstractions;

namespace LastEdict.Data.Tests;

/// <summary>Поле боя (<see cref="BattleScene"/>, <c>docs/battle-render.md</c>): места юнитов из разметки,
/// точка кадра по якорю и имена анимаций.</summary>
public class BattleSceneTests(ITestOutputHelper output)
{
    private static GameContent Content => _content ??= SharedContent.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static GameContent? _content;

    private static BattleScene Scene() =>
        new(Content.Screens.First(s => s.Id == "DLG_BATTLE_A"), Content.Gfx, type => type.ToUpperInvariant());

    /// <summary>Позиция в отряде <c>p</c> — это элемент <c>p+1</c>; числа — из разбора разметки.</summary>
    [SkippableTheory]
    [InlineData(true, 0, 345, 145)]
    [InlineData(true, 5, 20, 180)]
    [InlineData(false, 0, 442, 200)]
    [InlineData(false, 3, 446, 348)]
    public void Slot_ComesFromLayout(bool attacker, int position, int x, int y)
    {
        var slot = Scene().Slot(attacker, position);

        Assert.Equal((x, y, 80, 80), (slot.X, slot.Y, slot.Width, slot.Height));
    }

    /// <summary>Кадр 800×600 кладётся по якорю (400, 490): центр места по X, низ места по Y.</summary>
    [Fact]
    public void Place_UsesAnchorForFullCanvas()
    {
        var slot = new UiRect(345, 145, 80, 80);

        Assert.Equal((385 - 400, 225 - 490), BattleScene.Place(slot, 800, 600));
        Assert.Equal((385 - 16, 225 - 24), BattleScene.Place(slot, 32, 24));   // узкий кадр — по центру и низу
    }

    /// <summary>Большой юнит занимает пару мест: ширина одного, высота ×1.5, центр сдвинут на треть.</summary>
    [SkippableFact]
    public void BigSlot_SpansThePair()
    {
        var big = Scene().BigSlot(attacker: true, position: 0);
        var first = Scene().Slot(true, 0);

        Assert.Equal(first.Width, big.Width);
        Assert.Equal(3 * first.Height / 2, big.Height);
        Assert.NotEqual(first.Y, big.Y);
        output.WriteLine($"большой юнит: {big.X},{big.Y} {big.Width}×{big.Height}");
    }

    /// <summary>Места боковых панелей: портрет, полоса здоровья, крупный портрет, слоты предметов. У нападающих колонки переставлены
    /// (<c>m = p ^ 1</c>, <c>0x649ec2</c>): передняя позиция 0 — в колонке ближе к полю (<c>IMG_LSLOT2</c>, x = 87), позиция 1 — у края.</summary>
    [SkippableFact]
    public void PanelSlots_ComeFromLayout()
    {
        var scene = Scene();
        scene.SpotShown = true;   // основная панель справа — обороняющиеся, своя слева — нападающие (мышь над SPOT)

        Assert.Equal((87, 87, 70, 85), Rect(scene.PanelSlot(attacker: true, 0)));
        Assert.Equal((7, 87, 70, 85), Rect(scene.PanelSlot(attacker: true, 1)));
        Assert.Equal((7, 173, 70, 16), Rect(scene.PanelSlot(attacker: true, 1, meter: true)));
        Assert.Equal((643, 87, 70, 85), Rect(scene.PanelSlot(attacker: false, 0)));
        Assert.Equal((0, 453, 130, 142), Rect(scene.BigFaceSlot(attacker: true)));
        Assert.Equal((126, 412, 70, 85), Rect(scene.ItemSlot(attacker: true, 0)));
    }

    /// <summary>Раскладка по таблице §11 (снимки 28.09): без зеркала нападающие слева, фон с −150, одна панель справа — группа целей,
    /// другая группа — только над SPOT; «Поменяться сторонами» при своей атаке (m = 1): нападающие справа на местах обороняющихся +173 с
    /// кадрами «D», фон с 0, панель слева, кнопка и SPOT отражены; столбцы панели — «нападающий XOR зеркало».</summary>
    [SkippableFact]
    public void Layout_FollowsFlipTable()
    {
        var plain = Scene();
        Assert.Equal(BattleLayout.BackgroundLeft, plain.Background("NEUTRAL")!.X);
        Assert.True(plain.PanelShown(attacker: false));
        Assert.True(plain.PanelShown(attacker: true));     // обе панели (ответ пользователя 11.10): своя группа — в левой
        Assert.Equal(true, plain.PanelLeft(attacker: true));
        plain.BothPanels = false;                           // как по коду: своя панель — только над SPOT
        Assert.False(plain.PanelShown(attacker: true));
        plain.MainShowsAttackers = true;   // TOG_RIGHTUNITS — та же правая панель, своя группа
        Assert.Equal(false, plain.PanelLeft(attacker: true));
        Assert.Equal((723, 87, 70, 85), Rect(plain.PanelSlot(attacker: true, 0)));   // своя в правой панели — правая колонка (снимок)

        var flipped = Scene();
        flipped.Layout = new BattleLayout(OwnAttacks: true, Flipped: true);
        Assert.True(flipped.Layout.Mirrored);
        var d = Scene().Slot(attacker: false, 0);
        Assert.Equal(d.X + BattleLayout.MirrorShift, flipped.Slot(attacker: true, 0).X);   // нападающий — на месте обороняющегося +173
        Assert.EndsWith("D00", flipped.NameOf("g000uu0019", BattleAction.Idle, attacker: true));
        Assert.Equal(0, flipped.Background("NEUTRAL")!.X);
        Assert.Equal(true, flipped.PanelLeft(attacker: false));
        Assert.Equal(new UiRect(138, 403, 32, 28), flipped.Layout.Mirror(new UiRect(630, 403, 32, 28)));
        Assert.Equal((87, 87, 70, 85), Rect(flipped.PanelSlot(attacker: false, 0)));   // обороняющиеся при зеркале — колонки переставлены (снимок f1)

        var defended = new BattleLayout(OwnAttacks: false, Flipped: false);   // на свой отряд нападают — m = 1
        Assert.True(defended.Mirrored);
        Assert.True(defended.AttackersLeft);
        Assert.Equal(BattleLayout.MirrorShift, defended.Shift);
    }

    /// <summary>Пометка 1 партии 14.10: в ход лекаря (группа целей — свой отряд) панели не меняются местами — пока видны обе, своя панель
    /// показывает свой отряд (<c>0x638035</c>), основная — отряд врага (<c>0x638298(1)</c> при наведении на SPOT) при всех a и f. Крупный
    /// портрет ходящего — слева только у своих (<c>0x639d89</c>/<c>0x639e08</c>, места <c>0x62e514</c>).</summary>
    [SkippableTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Panels_KeepOwnGroupOnOwnSide(bool ownAttacks, bool flipped)
    {
        var scene = Scene();
        scene.Layout = new BattleLayout(ownAttacks, flipped);
        bool ownLeft = scene.Layout.MainPanelRight;   // своя панель — напротив основной (таблица §11: без зеркала основная справа)
        Assert.Equal(ownAttacks, scene.Layout.LeftSide(attacker: true) == ownLeft);   // и на той же стороне, что свой отряд на поле
        foreach (bool main in (bool[])[true, false])
        {
            scene.MainShowsAttackers = main;   // ход бойца (цели — враг) и ход лекаря (цели — свои)
            Assert.Equal(ownLeft, scene.PanelLeft(attacker: ownAttacks));
            Assert.Equal(!ownLeft, scene.PanelLeft(attacker: !ownAttacks));
        }
        Assert.True(scene.ActiveFaceLeft(attacker: ownAttacks));
        Assert.False(scene.ActiveFaceLeft(attacker: !ownAttacks));

        scene.BothPanels = false;   // одна основная панель — группа по переключателю, хоть свой отряд
        scene.MainShowsAttackers = ownAttacks;
        Assert.Equal(!ownLeft, scene.PanelLeft(attacker: ownAttacks));
        Assert.Null(scene.PanelLeft(attacker: !ownAttacks));
        scene.SpotShown = true;     // мышь над SPOT — обе панели, свой отряд на своей стороне
        Assert.Equal(ownLeft, scene.PanelLeft(attacker: ownAttacks));
    }

    /// <summary>Крупный портрет и анимация смерти находятся в графике: <c>FACEB</c> и <c>DEATH_{раса}_S*</c>.</summary>
    [SkippableFact]
    public void BigFaceAndDeath_AreFound()
    {
        Skip.If(Content.Gfx.Archive("Faces") is null, "нет описи графики");
        var scene = new BattleScene(Content.Screens.First(s => s.Id == "DLG_BATTLE_A"), Content.Gfx,
                                    type => type.ToUpperInvariant(), _ => "HUMAN");

        Assert.NotNull(scene.BigFace(Unit()));
        Assert.NotNull(scene.Death(Unit()));
        Assert.NotNull(scene.Corpse(Unit()));
    }

    /// <summary>Кости одни на все расы (<c>0x5c626d</c> берёт постоянную <c>L_HUMAN</c>, а не расу смерти юнита): у павшего с расой смерти
    /// <c>NEUTRAL</c>, <c>UNDEAD</c> или без неё — тот же <c>DEAD_HUMAN_*</c> (пункт 4 прогона 2, снимок 35 — костей не было).</summary>
    [SkippableTheory]
    [InlineData("NEUTRAL")]
    [InlineData("UNDEAD")]
    [InlineData("")]
    public void Corpse_IsHumanBonesForEveryRace(string race)
    {
        Skip.If(Content.Gfx.Archive("Battle") is null, "нет описи графики");
        var scene = new BattleScene(Content.Screens.First(s => s.Id == "DLG_BATTLE_A"), Content.Gfx,
                                    type => type.ToUpperInvariant(), _ => race);

        var corpse = scene.Corpse(Unit());

        Assert.NotNull(corpse);
        Assert.StartsWith("DEAD_HUMAN_", corpse.Name, StringComparison.Ordinal);
        Assert.NotNull(Content.Gfx.Image("Battle", corpse.Name));
    }

    /// <summary>Труп — на месте павшего, как метка (<c>0x653887…0x6539fc</c>): (центр − w/2, низ − h/2), подслой 5 — под телом юнита
    /// этого места. Кости в картинке 90×180 — посередине (строки 73–108), то есть у ног места, а не на 90 точек выше (замечание 12).</summary>
    [SkippableTheory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(false, 3)]
    public void Corpse_LiesAtFeetOfItsSlot(bool attacker, int position)
    {
        Skip.If(Content.Gfx.Archive("Battle") is null, "нет описи графики");
        var scene = new BattleScene(Content.Screens.First(s => s.Id == "DLG_BATTLE_A"), Content.Gfx,
                                    type => type.ToUpperInvariant(), _ => "HUMAN");
        var model = Unit();
        var unit = new BattleUnit
        {
            Id = model.Id, Attacker = attacker, Slot = position, HpMax = model.HpMax, Hp = 0, Type = model.Type, Attack = model.Attack,
        };

        var corpse = scene.Corpse(unit);
        var slot = scene.Slot(attacker, position);
        int body = scene.UnitSprites(unit, BattleAction.Idle).First(s => !s.Shadow).Layer;

        Assert.NotNull(corpse);
        var image = Content.Gfx.Image("Battle", corpse.Name) ?? throw new InvalidOperationException(corpse.Name);
        Assert.Equal((slot.X + (slot.Width / 2) - (image.W / 2), slot.Y + slot.Height - (image.H / 2)), (corpse.X, corpse.Y));
        Assert.Equal(body - 1, corpse.Layer);
    }

    /// <summary>Эффект на цели по классу атаки (<c>0x5c6a92</c>): в ванили есть только лечение и ожог — <c>HEALTUCHA1B00</c>,
    /// <c>BLISTUCHA1B00</c>; у урона своей картинки нет. Эффект ходящего <c>HEFF</c> — оба слоя, над группой целей.</summary>
    [SkippableFact]
    public void ClassTouchAndCasterEffect_AreFound()
    {
        Skip.If(Content.Gfx.Archive("BatUnits") is null, "нет описи графики");
        var scene = Scene();
        var unit = Unit();

        Assert.Equal("HEALTUCHA1B00", scene.ClassEffect(AttackClass.Heal, unit)?.Name);
        Assert.Equal("BLISTUCHA1B00", scene.ClassEffect(AttackClass.Blister, unit)?.Name);
        Assert.Null(scene.ClassEffect(AttackClass.Damage, unit));

        string[] layers = [.. scene.CasterEffects("G000UU0025", targetsAttackers: true).Select(e => e.Name)];
        Assert.Equal(["G000UU0025HEFFA1B00", "G000UU0025HEFFA2B00"], layers);
        Assert.Empty(scene.CasterEffects("G000UU0019", targetsAttackers: false));   // у мечника эффекта нет
    }

    /// <summary>Замечание 4 партии 14.10 (волшебник <c>G000UU0009</c>, атака по всем): эффект ходящего <c>HEFF</c> движок кладёт не на место
    /// ходящего, а над группой целей (<c>0x639f2b</c>): точка — середина охвата «центр по X, низ» шести мест группы (<c>0x63a377</c>), кадр 800×600
    /// по якорю (400, 490), слои группы целей 799/79 слева и 1399/139 справа (<c>0x646ec5</c>), сторона в имени — группы целей. Числа мест —
    /// из разметки <c>DLG_BATTLE_A</c>: у обороны центры X 482, 582, 372, 486, 242, 344 и низы 280, 345, 341, 428, 400, 486 → (412, 383);
    /// у нападения центры 385, 302, 285, 185, 156, 60 и низы 225, 173, 275, 206, 333, 260 → (222, 253).</summary>
    [SkippableFact]
    public void CasterEffect_IsOverTargetGroup()
    {
        Skip.If(Content.Gfx.Archive("BatUnits") is null, "нет описи графики");
        var scene = Scene();

        Assert.Equal((412, 383), scene.GroupPoint(attacker: false));
        Assert.Equal((222, 253), scene.GroupPoint(attacker: true));
        var bolt = Assert.Single(scene.CasterEffects("G000UU0009", targetsAttackers: false));
        Assert.Equal(("G000UU0009HEFFA1B00", 412 - 400, 383 - 490, 1399), (bolt.Name, bolt.X, bolt.Y, bolt.Layer));
        // Маг на месте 2 нападающих (как в партии): прежде холст клался по его месту — (285 − 400, 275 − 490).
        Assert.NotEqual(BattleScene.Place(scene.Slot(true, 2), 800, 600), (bolt.X, bolt.Y));
        var healer = scene.CasterEffects("G000UU0025", targetsAttackers: true);
        Assert.Equal([(222 - 400, 253 - 490, 799), (222 - 400, 253 - 490, 79)], healer.Select(e => (e.X, e.Y, e.Layer)));
        // Пара A/D: сторона — у группы целей (цели — оборона → D), движение в кадрах — от ходящего к целям.
        Assert.Equal(["G000UU0180HEFFA1D00", "G000UU0180HEFFA2D00"], scene.CasterEffects("G000UU0180", targetsAttackers: false).Select(e => e.Name));
        Assert.Equal(["G000UU0180HEFFA1A00", "G000UU0180HEFFA2A00"], scene.CasterEffects("G000UU0180", targetsAttackers: true).Select(e => e.Name));
    }

    /// <summary>Замечание 3 партии 14.10 (ассасин <c>G000UU5354</c> → база <c>G000UU0154</c>: удар и яд второй атакой): эффект на цели <c>TUCH</c>
    /// у основной атаки — свой у юнита, у второй — только по классу (<c>0x63513a</c>: <c>attackNumber != 1</c> → <c>0x64845d</c>). Своего ножа на
    /// ядовитом ударе нет (прежде он летел второй раз); у основной атаки без своего <c>TUCH</c> класс не подставляется.</summary>
    [SkippableFact]
    public void Touch_SecondAttackTakesOnlyClassEffect()
    {
        Skip.If(Content.Gfx.Archive("BatUnits") is null, "нет описи графики");
        var scene = Scene();
        var target = Unit(attacker: false);

        Assert.Equal("G000UU0154TUCHA1D00", scene.Touch("G000UU0154", AttackClass.Damage, main: true, target)?.Name);
        Assert.Null(scene.Touch("G000UU0154", AttackClass.Poison, main: false, target));
        Assert.Equal("HEALTUCHA1B00", scene.Touch("G000UU0154", AttackClass.Heal, main: false, target)?.Name);   // по классу — есть
        Assert.Null(scene.Touch("G000UU0019", AttackClass.Heal, main: true, target));
    }

    /// <summary>Пометки 3 и 5 партии 15.10 (Лесная Дева, <c>G000UU8033</c>: обереги, затем лечение второй атакой): у эльфов вторая атака
    /// «лечение» идёт без картинки на цели (<c>0x635321…0x6353da</c>: раса ходящего — эльфы <c>0x8372a0</c>, класс — лечение <c>0x837490</c>),
    /// у прочих — <c>HEALTUCH</c> по классу. Основная атака — свой <c>TUCH</c> у всех.</summary>
    [SkippableFact]
    public void Touch_ElfSecondHeal_HasNoEffect()
    {
        Skip.If(Content.Gfx.Archive("BatUnits") is null, "нет описи графики");
        var elves = new BattleScene(Content.Screens.First(s => s.Id == "DLG_BATTLE_A"), Content.Gfx, type => type.ToUpperInvariant())
        {
            IsElf = type => type.Equals("G000UU8033", StringComparison.OrdinalIgnoreCase),
        };
        var target = Unit(attacker: true);

        Assert.Null(elves.Touch("G000UU8033", AttackClass.Heal, main: false, target));
        Assert.Equal("G000UU8033TUCHA1B00", elves.Touch("G000UU8033", AttackClass.BestowWards, main: true, target)?.Name);
        Assert.Equal("HEALTUCHA1B00", elves.Touch("G000UU0017", AttackClass.Heal, main: false, target)?.Name);   // Аббатиса — Империя
        Assert.Null(elves.Touch("G000UU8033", AttackClass.Cure, main: false, target));                         // по классу картинки нет
    }

    /// <summary>Эффекты <c>Battle.ff</c> на юните (<c>CBatUnitAnim::playEffect</c> <c>0x65255b</c>): имя с хвостом по размеру, холст 800×600
    /// на месте юнита, подслой 12 (у тиков — 7); эффект ходящего <c>HEFF</c> — общие слои группы 799/79 (<c>0x639f2b</c>).</summary>
    [SkippableFact]
    public void UnitEffects_NameLayerAndPlace()
    {
        Skip.If(Content.Gfx.Archive("Battle") is null, "нет описи графики");
        var scene = Scene();
        var unit = Unit();
        int body = scene.UnitSprites(unit, BattleAction.Idle).First(s => !s.Shadow).Layer;
        foreach (string name in (string[])["POISONANIM", "FROSTBITEANIM", "BLISTERANIM", "REVIVEANIM", "TRANSF", "DOPPEL", "DRAINLVL",
                                           "UTRANSF", "UDOPPEL", "UDRAINLVL", "USUMMONANIM", "UPGRADEHU", "UPGRADEEL"])
        {
            var effect = scene.UnitEffect(name, unit);
            Assert.Equal(name + "S", effect?.Name);
            Assert.Equal(body + BattleScene.EffectSublayer, effect!.Layer);
            Assert.Equal(BattleScene.Place(scene.Slot(true, 0), 800, 600), (effect.X, effect.Y));
        }
        Assert.Equal(body + BattleScene.TouchSublayer, scene.UnitEffect("POISONANIM", unit, BattleScene.TouchSublayer)!.Layer);
        Assert.Null(scene.UnitEffect("SUMMONANIM", unit));   // движок его не запрашивает, в архиве нет
        Assert.Equal([799, 79], scene.CasterEffects("G000UU0025", targetsAttackers: true).Select(e => e.Layer));
    }

    /// <summary>Рост уровнем в конце боя (шаг 15 просмотра, <c>0x636dda</c> → <c>0x65255b</c> код 0 → <c>0x647a98</c> → <c>0x5b3ed3</c>):
    /// <c>UPGRADE</c> + раса по цепочке сравнений с категориями <c>Lrace</c> (<c>0x5b3f12…0x5b3f81</c>: люди <c>HU</c>, легионы <c>HE</c>,
    /// кланы <c>DW</c>, нежить <c>UN</c>, эльфы <c>EL</c>, прочее <c>NE</c>; таблица строк <c>0x7ae7c0</c> идёт в порядке HU HE DW UN NE EL, а
    /// не по типу расы) + <c>L</c>, если юнит большой (<c>0x649749</c> = не <c>SIZE_SMALL</c>), иначе <c>S</c> (<c>0x5b3f98</c>). Раса — владельца
    /// отряда (<c>0x652f1b</c>: игрок → <c>+0x2c</c>; без владельца — раса типа юнита). Партия 15.10, пометка 2: у нежити зелёный череп
    /// <c>UPGRADEUNS</c> — так и в оригинале.</summary>
    [SkippableFact]
    public void LevelUpEffect_ByOwnerRace()
    {
        Skip.If(Content.Gfx.Archive("Battle") is null, "нет описи графики");
        var scene = Scene();
        var small = Unit();
        var big = new BattleUnit
        {
            Id = "S001UN0002", Attacker = true, Slot = 0, HpMax = 150, Hp = 150, Type = small.Type with { SizeSmall = false }, Attack = small.Attack,
        };
        string[] expected = ["UPGRADEHU", "UPGRADEUN", "UPGRADEHE", "UPGRADEDW", "UPGRADENE", "UPGRADEEL"];   // по типу расы Lrace 0…5
        for (int race = -1; race < expected.Length; race++)
        {
            string name = LastEdict.Data.Sound.BattleNoteSounds.Of(BattleNoteKind.LevelUp, race);
            string? s = scene.UnitEffect(name, small)?.Name, l = scene.UnitEffect(name, big)?.Name;
            output.WriteLine($"раса {race}: малый {s}, большой {l}");
            string want = race < 0 ? "UPGRADENE" : expected[race];
            Assert.Equal((want + "S", want + "L"), (s, l));
        }
    }

    /// <summary>Слои места боя (<c>0x5b34f6</c> → <c>0x646a6f</c>): фон на 10, тень на 138, передний план на 2000 — над юнитами; все три — с одной x.
    /// У <c>UNDEAD_0</c> передний план закрывает дыру мадженты в фоне (снимок 27 прогона 11.10); тени и переднего плана у большинства мест нет — это
    /// не пропуск картинки.</summary>
    [SkippableFact]
    public void PlaceLayers_BackgroundShadowForeground()
    {
        Skip.If(Content.Gfx.Archive("Battle") is null, "нет описи графики");
        var scene = Scene();

        var undead = scene.PlaceLayers("UNDEAD", 0);
        var neutral = scene.PlaceLayers("NEUTRAL", 0);
        var human = scene.PlaceLayers("HUMAN", 1);

        Assert.Equal([("UNDEAD_0_BG", 10), ("UNDEAD_0_FG", BattleScene.ForegroundLayer)], undead.Select(s => (s.Name, s.Layer)));
        Assert.Equal([("NEUTRAL_0_BG", 10), ("NEUTRAL_0_SHADOW", BattleScene.PlaceShadowLayer)], neutral.Select(s => (s.Name, s.Layer)));
        Assert.Equal(["HUMAN_1_BG"], human.Select(s => s.Name));
        Assert.All(undead.Concat(neutral), s => Assert.Equal(BattleLayout.BackgroundLeft, s.X));
        Assert.Empty(scene.Missing);
        Assert.True(BattleScene.PlaceShadowLayer < 200 && BattleScene.ForegroundLayer > 1300);   // тень — под всеми юнитами, передний план — над
    }

    /// <summary>Боевые предметы показываются только у ходящего лидера, не превращённого (<c>0x649775</c>: приведение к лидеру отряда,
    /// <c>CMidUnit::transformed</c>).</summary>
    [Fact]
    public void ShowsItems_OnlyLeaderNotTransformed()
    {
        var potion = new LastEdict.Rules.Magic.Item("g000ig0001", LastEdict.Rules.Magic.ItemCategory.PotionHeal, new LastEdict.Rules.Map.Bank(0, 0, 0, 0, 0, 0),
                                             "", "", 50, "", new LastEdict.Rules.Map.Bank(0, 0, 0, 0, 0, 0), "", "");
        var plain = Unit();
        plain.BattleItems.Add(potion);
        var leader = Unit();
        leader.Type = leader.Type with { IsLeader = true };
        var transformed = Unit();
        transformed.Type = transformed.Type with { IsLeader = true };
        transformed.BattleItems.Add(potion);
        transformed.OrigType = transformed.Type;

        Assert.False(BattleScene.ShowsItems(plain, humanTurn: true));         // не лидер, хоть предмет у него и есть
        Assert.False(BattleScene.ShowsItems(leader, humanTurn: true));        // лидер без предметов — слоты пусты
        Assert.False(BattleScene.ShowsItems(transformed, humanTurn: true));   // превращённый лидер
        leader.BattleItems.Add(potion);
        Assert.True(BattleScene.ShowsItems(leader, humanTurn: true));
        Assert.False(BattleScene.ShowsItems(leader, humanTurn: false));       // лидер ИИ на своём ходу — без предметов (ответ 11.10)
        Assert.False(BattleScene.ShowsItems(null, humanTurn: true));
    }

    /// <summary>Портрет в панели — та же цель, что юнит на поле (поиск цели движка — по группам панелей, <c>0x631318</c>): основная панель
    /// справа ловит свою группу; другая группа — только пока она показана (мышь над SPOT), выключенная группа мышь не ловит (<c>0x64c9a4</c>).
    /// При обеих панелях (<see cref="BattleScene.BothPanels"/>, по умолчанию) ловятся портреты обеих групп.</summary>
    [SkippableFact]
    public void PanelUnitAt_FindsPortraitOfShownGroup()
    {
        var scene = Scene();   // основная панель справа — обороняющиеся (MainShowsAttackers = false)
        scene.BothPanels = false;
        var defender = new BattleUnit
        {
            Id = "S001UN0002", Attacker = false, Slot = 0, HpMax = 150, Hp = 150, Type = Unit().Type, Attack = Unit().Attack,
        };
        var attacker = Unit();
        BattleUnit[] units = [attacker, defender];
        var face = scene.PanelFace(defender);
        var own = scene.PanelSlot(attacker: true, 0);

        Assert.Same(defender, scene.PanelUnitAt(units, face.X + 5, face.Y + 5));
        Assert.Null(scene.PanelUnitAt(units, face.X - 1, face.Y + 5));
        Assert.Null(scene.PanelUnitAt(units, own.X + 5, own.Y + 5));   // своя группа не показана — портрет не ловится
        scene.SpotShown = true;
        own = scene.PanelSlot(attacker: true, 0);
        Assert.Same(attacker, scene.PanelUnitAt(units, own.X + 5, own.Y + 5));

        var both = Scene();   // обе панели — без наведения на SPOT
        Assert.Same(defender, both.PanelUnitAt(units, face.X + 5, face.Y + 5));
        var left = both.PanelSlot(attacker: true, 0);
        Assert.True(left.X < 400);   // своя группа — в левой панели
        Assert.Same(attacker, both.PanelUnitAt(units, left.X + 5, left.Y + 5));
    }

    /// <summary>Место панели под точкой (<c>getSelectedUnitId</c> <c>0x64c393</c>: позиции-цели — прямоугольник 70×85 места): позиция стороны,
    /// чья панель показана; мимо мест или панель стороны не показана — null. Партия 17.10: щелчок по месту в панели не призывал.</summary>
    [SkippableFact]
    public void PanelPositionAt_FindsPlaceOfShownSide()
    {
        var scene = Scene();
        scene.BothPanels = false;   // основная панель — обороняющиеся, своя (нападающие) не показана
        for (int position = 0; position < 6; position++)
        {
            var slot = scene.PanelSlot(attacker: false, position);
            Assert.Equal(position, scene.PanelPositionAt(attacker: false, slot.X, slot.Y));
            Assert.Equal(position, scene.PanelPositionAt(attacker: false, slot.Right - 1, slot.Bottom - 1));
        }
        var own = scene.PanelSlot(attacker: true, 4);
        Assert.Null(scene.PanelPositionAt(attacker: true, own.X + 5, own.Y + 5));
        scene.SpotShown = true;
        own = scene.PanelSlot(attacker: true, 4);
        Assert.Equal(4, scene.PanelPositionAt(attacker: true, own.X + 5, own.Y + 5));
        Assert.Null(scene.PanelPositionAt(attacker: true, 400, 300));   // середина поля — не панель
    }

    /// <summary>Зона мыши юнита на поле — место и 32 px под ним (<c>0x6326b9</c>: низ <c>getUnitRect</c> + 0x20; проверка <c>0x63e460</c> —
    /// левый и верхний край включительно, правый и нижний нет). Слова пользователя 15.10: метка появлялась только над телом юнита, у
    /// оригинала — и под ногами, по самой метке.</summary>
    [SkippableFact]
    public void FieldUnitAt_CatchesBelowTheFeet()
    {
        var scene = Scene();
        var unit = Unit();
        var slot = scene.Slot(attacker: true, 0);   // IMG_AUNIT1 345,145–425,225
        int cx = slot.X + (slot.Width / 2);
        Assert.Equal((slot.X, slot.Y, slot.Width, slot.Height + BattleScene.HitBelow), Rect(scene.HitSlot(attacker: true, 0)));

        Assert.Same(unit, scene.FieldUnitAt([unit], cx, slot.Bottom + 16));   // под ногами, на метке
        Assert.Same(unit, scene.FieldUnitAt([unit], cx, slot.Bottom + 31));
        Assert.Null(scene.FieldUnitAt([unit], cx, slot.Bottom + 32));
        Assert.Same(unit, scene.FieldUnitAt([unit], cx, slot.Y));
        Assert.Null(scene.FieldUnitAt([unit], cx, slot.Y - 1));
        Assert.Same(unit, scene.FieldUnitAt([unit], slot.X, slot.Bottom));
        Assert.Null(scene.FieldUnitAt([unit], slot.Right, slot.Bottom));

        // Большой — пара мест (getUnitRect с признаком большого), тоже с запасом снизу.
        unit.Type = unit.Type with { SizeSmall = false };
        var big = scene.BigSlot(attacker: true, 0);
        Assert.Equal((big.X, big.Y, big.Width, big.Height + BattleScene.HitBelow), Rect(scene.HitSlot(attacker: true, 0, small: false)));
        Assert.Same(unit, scene.FieldUnitAt([unit], big.X + (big.Width / 2), big.Bottom + 31));
        Assert.Null(scene.FieldUnitAt([unit], big.X + (big.Width / 2), big.Bottom + 32));
    }

    /// <summary>Перекрытие зон мыши: места 2 и 3 левой группы (позиции 1 и 2) сходятся полосой y 195–205. Движок обходит юнитов в
    /// отсортированном списке (<c>0x63eed0</c>, сравнение <c>0x63d26a</c>): обороняющиеся, затем нападающие, в группе позиции 1, 0, 3, 2, 5, 4
    /// (<c>0x63e3d0</c>), первый попавший — под мышью. В полосе ловится позиция 1, в каком бы порядке ни шли юниты боя; так же при
    /// зеркале, когда слева стоят обороняющиеся.</summary>
    [SkippableFact]
    public void FieldUnitAt_OverlapGoesToEngineOrder()
    {
        foreach (bool flipped in (bool[])[false, true])
        {
            var scene = Scene();
            scene.Layout = new BattleLayout(OwnAttacks: true, Flipped: flipped);
            bool left = scene.Layout.AttackersLeft;   // группа на местах IMG_AUNIT
            var template = Unit();
            BattleUnit At(string id, int slot) => new()
            {
                Id = id, Attacker = left, Slot = slot, HpMax = 150, Hp = 150, Type = template.Type, Attack = template.Attack,
            };
            var first = At("S001UN0001", 1);
            var second = At("S001UN0002", 2);
            var a = scene.HitSlot(left, 1);
            var b = scene.HitSlot(left, 2);
            int x = Math.Max(a.X, b.X) + 5, y = Math.Max(a.Y, b.Y) + 5;
            Assert.True(y < Math.Min(a.Bottom, b.Bottom));   // точка в полосе перекрытия

            Assert.Same(first, scene.FieldUnitAt([second, first], x, y));
            Assert.Same(first, scene.FieldUnitAt([first, second], x, y));
            Assert.Same(second, scene.FieldUnitAt([second, first], x, b.Bottom - 1));   // ниже полосы — только место 3
        }
    }

    /// <summary>Ревью <c>84e40cb</c>: щелчок, бросок предмета и портрет с павшими (<see cref="BattleScene.UnitAt"/>, <c>withFallen</c>) — живой на
    /// клетке павшего раньше костей: призванный на место павшего стоит в конце списка юнитов боя, и прежде поиск по списку отдавал труп. Нет
    /// живого — павший (цель зелья жизни, и в панели); ушедший — никогда.</summary>
    [SkippableFact]
    public void UnitAt_LivingBeforeFallenOnTheSameCell()
    {
        var scene = Scene();
        var fallen = Unit();
        fallen.Hp = 0;
        var template = Unit();
        var summoned = new BattleUnit
        {
            Id = "S001UN0009", Attacker = true, Slot = 0, HpMax = 150, Hp = 150, Type = template.Type, Attack = template.Attack, Summoned = true,
        };
        var slot = scene.Slot(attacker: true, 0);
        int fx = slot.X + (slot.Width / 2), fy = slot.Y + (slot.Height / 2);
        var face = scene.PanelFace(fallen);
        int px = face.X + 5, py = face.Y + 5;
        List<BattleUnit> units = [fallen, summoned];

        Assert.Same(summoned, scene.UnitAt(units, fx, fy, withFallen: true));
        Assert.Same(summoned, scene.UnitAt(units, px, py, withFallen: true));
        Assert.Same(summoned, scene.UnitAt(units, fx, fy));

        units.Remove(summoned);
        Assert.Same(fallen, scene.UnitAt(units, fx, fy, withFallen: true));
        Assert.Same(fallen, scene.UnitAt(units, px, py, withFallen: true));
        Assert.Null(scene.UnitAt(units, fx, fy));
        fallen.Retreated = true;
        Assert.Null(scene.UnitAt(units, px, py, withFallen: true));
    }

    private static BattleUnit Unit(bool attacker = true) => new()
    {
        Id = "S001UN0001", Attacker = attacker, Slot = 0, HpMax = 150, Hp = 150,
        Type = new UnitType("g000uu0019", 1, 150, 0, "g000aa0019", "g000000000", false, true, 0, 0),
        Attack = new AttackType("g000aa0019", 50, AttackClass.Damage, 80, 3, 50, 0, false, 1),
    };

    private static (int, int, int, int) Rect(UiRect r) => (r.X, r.Y, r.Width, r.Height);

    /// <summary>Выросший уровнем юнит в бою — порождённая реализация <c>G000UG…</c> (<see cref="UnitType.GlobalId"/> — база): кадры, портреты
    /// и смерть ищутся по базе. Прогон 3 пользователя 11.10, пункты 4 и 7: Титан-лидер нейтралов 2-го уровня (<c>G000UU5218</c> →
    /// <c>G000UG978c</c>) и выросший лидер игрока пропадали и с поля, и из панели.</summary>
    [SkippableFact]
    public void GeneratedUnit_UsesGlobalTypeArt()
    {
        Skip.If(Content.Gfx.Archive("Faces") is null || Content.Gfx.Archive("BatUnits") is null, "нет описи графики");
        var scene = new BattleScene(Content.Screens.First(s => s.Id == "DLG_BATTLE_A"), Content.Gfx,
                                    type => type.ToUpperInvariant(), type => type.StartsWith("g000uu", StringComparison.OrdinalIgnoreCase) ? "HUMAN" : "");
        var unit = Unit();
        unit.Type = unit.Type with { Id = "G000UG0ce6", GlobalId = "g000uu0019" };

        Assert.NotNull(scene.Portrait(unit));
        Assert.NotNull(scene.BigFace(unit));
        Assert.NotNull(scene.Death(unit));
        Assert.Contains(scene.UnitSprites(unit, BattleAction.Idle), s => s.Name == "G000UU0019IDLEA1A00");
        Assert.Empty(scene.Missing);
    }

    /// <summary>Имя анимации: базовый тип, действие, тело или тень, номер слоя, сторона, вариант.</summary>
    [SkippableTheory]
    [InlineData(BattleAction.Idle, false, true, "G000UU0019IDLEA1A00")]
    [InlineData(BattleAction.Idle, true, true, "G000UU0019IDLES1A00")]
    [InlineData(BattleAction.Hit, false, false, "G000UU0019HMOVA1D00")]
    [InlineData(BattleAction.Hurt, false, false, "G000UU0019HHITA1D00")]
    [InlineData(BattleAction.Still, false, true, "G000UU0019STILA1A00")]
    public void NameOf_MatchesArchive(BattleAction action, bool shadow, bool attacker, string expected)
    {
        var scene = Scene();

        string name = scene.NameOf("g000uu0019", action, attacker, shadow);

        Assert.Equal(expected, name);
        Skip.If(Content.Gfx.Archive("BatUnits") is null, "нет описи графики");
        Assert.NotNull(Content.Gfx.Anim("BatUnits", name));
    }
}
