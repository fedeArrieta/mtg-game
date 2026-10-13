using MtgEngine.Roguelike;
using Xunit;
using static MtgEngine.Tests.TestHelpers;

namespace MtgEngine.Tests;

public class RoguelikeTests
{
    // ---------- Comandante ----------

    [Fact]
    public void CommanderIsCastFromTheCommandZoneAndReturnsWithTax()
    {
        var g = NewGame();
        var thornak = g.AddCommander(Me, "thornak");
        g.AddMana(Me, "bosque", "bosque", "bosque", "bosque", "bosque");
        g.Cast(thornak.Id);
        g.BothPass();
        Assert.Contains(thornak, g.Players[Me].Battlefield);
        Assert.Equal(2, g.Players[Me].Battlefield.Count(c => c.IsCreature)); // y el lobo que trae

        g.AddMana(Me, "pantano", "pantano", "pantano");
        g.Cast(g.AddToHand(Me, "fin-sombrio").Id, Target.OfCard(thornak.Id));
        g.BothPass();
        Assert.Contains(thornak, g.Players[Me].Command);
        Assert.DoesNotContain(g.Players[Me].Graveyard, c => c.IsCommander);
        Assert.Equal("{5}{G}{G}", Game.CostOf(thornak).ToString());
    }

    [Fact]
    public void BouncedCommanderAlsoGoesToTheCommandZone()
    {
        var g = NewGame();
        var kael = g.AddCommander(Opp, "kael");
        g.StartAt(Opp, Step.Main1);
        g.AddMana(Opp, "montana", "montana", "llanura", "llanura");
        g.Cast(kael.Id);
        g.BothPass();
        g.StartAt(Me, Step.Main1);
        g.AddMana(Me, "isla", "isla");
        g.Cast(g.AddToHand(Me, "retorno-bruma").Id, Target.OfCard(kael.Id));
        g.BothPass();
        Assert.Contains(kael, g.Players[Opp].Command);
        Assert.Equal(1, kael.CommanderCasts);
    }

    // ---------- IA ----------

    [Fact]
    public void AiPlaysALandAndCastsWhatItCan()
    {
        var g = new Game("Vos", "IA");
        for (int i = 0; i < 10; i++) { g.AddToLibrary(Me, "lobo-musgo"); g.AddToLibrary(Opp, "lobo-musgo"); }
        g.AddToBattlefield(Opp, "bosque");
        g.AddToHand(Opp, "bosque");
        g.AddToHand(Opp, "lobo-musgo");
        g.StartAt(Opp, Step.Main1);
        for (int i = 0; i < 10 && g.Players[Opp].Battlefield.Count(c => c.IsCreature) == 0; i++) DuelAI.Act(g);
        Assert.True(g.Players[Opp].LandPlayedThisTurn);
        Assert.Single(g.Players[Opp].Battlefield, c => c.Name == "Lobo de Musgo");
    }

    [Fact]
    public void AiKillsYourBestCreatureWithRemoval()
    {
        var g = NewGame();
        g.AddToBattlefield(Me, "escudero-aurora");
        var behemot = g.AddToBattlefield(Me, "behemot-valle");
        g.AddToBattlefield(Opp, "pantano");
        g.AddToBattlefield(Opp, "pantano");
        g.AddToBattlefield(Opp, "pantano");
        g.AddToHand(Opp, "fin-sombrio");
        g.StartAt(Opp, Step.Main1);
        DuelAI.Act(g);
        Assert.Single(g.Stack);
        Assert.Equal(behemot.Id, g.Stack[0].Target?.Card);
    }

    [Fact]
    public void AiAttacksWhenTheWayIsClearAndHoldsBackWhenItWouldDie()
    {
        var g = NewGame();
        g.AddToBattlefield(Opp, "lobo-musgo");
        g.StartAt(Opp, Step.DeclareAttackers);
        DuelAI.Act(g);
        Assert.Single(g.Combat);

        var h = NewGame();
        h.AddToBattlefield(Opp, "lobo-musgo");
        h.AddToBattlefield(Me, "behemot-valle");
        h.StartAt(Opp, Step.DeclareAttackers);
        DuelAI.Act(h);
        Assert.Empty(h.Combat);
    }

    [Fact]
    public void AiBurnsYouForTheWin()
    {
        var g = NewGame();
        g.SetLife(Me, 2);
        g.AddToBattlefield(Me, "lobo-musgo");
        g.AddToBattlefield(Opp, "montana");
        g.AddToHand(Opp, "chispazo");
        g.StartAt(Opp, Step.Main1);
        DuelAI.Act(g);
        Assert.Equal(Me, g.Stack.Single().Target?.Player);
    }

    // ---------- Duelos ----------

    static DuelSetup Setup(string enemyId, int seed)
    {
        var enemy = RunContent.Enemy(enemyId);
        var spells = RunContent.Enemy("patrulla-alba").Deck;
        return new DuelSetup
        {
            EnemyName = enemy.Name,
            EnemyLife = enemy.Life,
            HumanDeck = [.. spells, .. Run.Lands(spells)],
            EnemyDeck = [.. enemy.Deck, .. Run.Lands(enemy.Deck)],
            EnemyCommander = enemy.Commander,
            Seed = seed,
        };
    }

    public static TheoryData<string> EnemyIds => [.. RunContent.Enemies.Select(e => e.Id)];

    [Theory]
    [MemberData(nameof(EnemyIds))]
    public void EveryEnemyCanPlayAFullDuelAgainstTheAi(string enemyId)
    {
        var g = Duel.Simulate(Setup(enemyId, seed: 7));
        Assert.True(g.IsOver, $"El duelo contra {enemyId} no terminó en 60 turnos.");
    }

    [Fact]
    public void ADuelStopsOnlyWhenYouHaveSomethingToDecide()
    {
        var duel = new Duel(Setup("banda-trasgos", seed: 3));
        Assert.Equal(DuelStatus.Playing, duel.Status);
        Assert.Equal(Duel.Human, duel.Game.PriorityPlayer);
        Assert.Equal(7, duel.Game.Players[Duel.Human].Hand.Count);
        Assert.Equal(Step.Main1, duel.Game.Step);

        // Pasando todos los turnos sin jugar nada, el rival termina ganando.
        for (int i = 0; i < 100 && duel.Status == DuelStatus.Playing; i++)
        {
            if (duel.DeclaringBlockers) duel.DeclareBlockers(new Dictionary<int, int>());
            else duel.PassTurn();
        }
        Assert.Equal(DuelStatus.Lost, duel.Status);
    }

    [Fact]
    public void RelicsChangeHowTheDuelStarts()
    {
        var setup = Setup("banda-trasgos", seed: 1);
        var duel = new Duel(new DuelSetup
        {
            EnemyName = setup.EnemyName, EnemyLife = 9, HumanDeck = setup.HumanDeck, EnemyDeck = setup.EnemyDeck,
            HumanExtraCards = 1, HumanStartingLands = ["llanura"], HumanStartingSoldiers = 1, Seed = 1,
        });
        var me = duel.Game.Players[Duel.Human];
        Assert.Equal(8, me.Hand.Count);
        Assert.Contains(me.Battlefield, c => c.Name == "Llanura");
        Assert.Contains(me.Battlefield, c => c.Name == "Soldado");
        Assert.Equal(9, duel.Game.Players[Duel.Enemy].Life);
    }

    // ---------- Run ----------

    [Fact]
    public void ContentReferencesOnlyRealCards()
    {
        Assert.All(RunContent.Pool.Values.SelectMany(x => x), id => CardCatalog.Get(id));
        Assert.All(RunContent.Enemies.SelectMany(e => e.Deck), id => CardCatalog.Get(id));
        Assert.Equal(3, RunContent.Pool.Count);
        for (int act = 1; act <= Run.Acts; act++)
            Assert.Contains(RunContent.Enemies, e => e.Kind == EnemyKind.Boss && e.Act == act && e.Commander is not null);
    }

    [Fact]
    public void LandsFollowTheManaSymbols()
    {
        List<string> spells = [.. Enumerable.Repeat("lobo-musgo", 9), "chispazo", "chispazo", "chispazo"];
        var lands = Run.Lands(spells);
        Assert.Equal(9, lands.Count);
        Assert.Equal(7, lands.Count(l => l == "bosque"));
        Assert.Equal(2, lands.Count(l => l == "montana"));
    }

    [Fact]
    public void TheDraftBuildsADeckAndOpensTheMap()
    {
        var run = Run.Start(42, Color.Red, Color.Green);
        Assert.Equal(RunPhase.Draft, run.Phase);
        while (run.Phase == RunPhase.Draft)
        {
            Assert.Equal(3, run.Offer.Count);
            Assert.Equal(3, run.Offer.Distinct().Count());
            run.Pick(run.Offer[0]);
        }
        Assert.Equal(Run.DraftPicks, run.Spells.Count);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal(3, run.Available.Count());
        Assert.All(run.Available, n => Assert.Equal(NodeKind.Combat, n.Kind));
        Assert.True(run.Spells.Count(id => CardCatalog.Get(id).ManaCost.Colors.All(c => c is Color.Red or Color.Green)) >= 8);
    }

    [Fact]
    public void EveryMapRoomLeadsToTheBoss()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var run = DraftedRun(seed);
            var boss = Assert.Single(run.Map, n => n.Kind == NodeKind.Boss);
            Assert.All(run.Map.Where(n => n != boss), n => Assert.NotEmpty(n.Next));
            Assert.All(run.Map.Where(n => n.Floor > 0), n => Assert.Contains(run.Map, m => m.Next.Contains(n.Id)));
        }
    }

    static Run DraftedRun(int seed)
    {
        var run = Run.Start(seed, Color.White, Color.Black);
        while (run.Phase == RunPhase.Draft) run.Pick(run.Offer[0]);
        return run;
    }

    [Fact]
    public void WinningADuelKeepsYourLifeAndOffersACard()
    {
        var run = DraftedRun(5);
        run.Enter(run.Available.First().Id);
        Assert.Equal(RunPhase.Duel, run.Phase);
        var setup = run.CreateDuel();
        Assert.Equal(20, setup.HumanLife);
        Assert.Equal(run.Deck().Count, setup.HumanDeck.Count);

        run.FinishDuel(won: true, lifeLeft: 13);
        Assert.Equal(13, run.Hp);
        Assert.Equal(RunPhase.Reward, run.Phase);
        run.Pick(null);
        Assert.Equal(RunPhase.Map, run.Phase);
        Assert.Equal(Run.DraftPicks, run.Spells.Count);
    }

    [Fact]
    public void LosingADuelEndsTheRun()
    {
        var run = DraftedRun(6);
        run.Enter(run.Available.First().Id);
        run.FinishDuel(won: false, lifeLeft: 0);
        Assert.Equal(RunPhase.Defeat, run.Phase);
        Assert.Empty(run.Available);
    }

    [Fact]
    public void ARunCanBePlayedToTheEndAndSavedAnywhere()
    {
        var run = DraftedRun(11);
        for (int guard = 0; guard < 200 && run.Phase is not (RunPhase.Victory or RunPhase.Defeat); guard++)
        {
            run = Run.FromJson(run.ToJson()); // guardar y cargar en cualquier momento no cambia nada
            switch (run.Phase)
            {
                case RunPhase.Map: run.Enter(run.Available.First().Id); break;
                case RunPhase.Duel: run.FinishDuel(won: true, lifeLeft: run.Hp - 3); break;
                case RunPhase.Reward: run.Pick(run.Offer.FirstOrDefault()); break;
                case RunPhase.Rest: run.Rest(); break;
                case RunPhase.Treasure: run.Continue(); break;
                case RunPhase.Challenge: run.FinishChallenge(solved: true); break;
            }
        }
        Assert.Equal(RunPhase.Victory, run.Phase);
        Assert.Equal(Run.Acts, run.Act);
        Assert.NotEmpty(run.Relics);
    }

    [Fact]
    public void RestingHealsOrRemovesACard()
    {
        var run = DraftedRun(8);
        run.Hp = 5;
        run.Phase = RunPhase.Rest;
        run.Rest();
        Assert.Equal(5 + Run.RestHeal, run.Hp);

        run.Phase = RunPhase.Rest;
        var card = run.Spells[0];
        run.Rest(card);
        Assert.Equal(Run.DraftPicks - 1, run.Spells.Count);
    }

    [Fact]
    public void RelicsApplyToTheRun()
    {
        var run = DraftedRun(9);
        run.Relics.AddRange(["colmillo-sangriento", "piedra-raiz", "grimorio-gastado"]);
        run.Enter(run.Available.First().Id);
        var setup = run.CreateDuel();
        var enemy = RunContent.Enemy(run.Encounter!);
        Assert.Equal(enemy.Life - 3, setup.EnemyLife);
        Assert.Single(setup.HumanStartingLands);
        Assert.Equal(1, setup.HumanExtraCards);
    }
}
