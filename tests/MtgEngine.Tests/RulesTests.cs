using MtgEngine;
using Xunit;
using static MtgEngine.Tests.TestHelpers;

namespace MtgEngine.Tests;

public class CastingTests
{
    [Fact]
    public void CreatureSpellResolvesWithSummoningSickness()
    {
        var g = NewGame();
        g.AddMana(Me, "forest", "forest");
        var wolf = g.AddToHand(Me, "grizzly-bears");
        g.Cast(wolf.Id);
        Assert.Single(g.Stack);
        g.BothPass();
        Assert.Empty(g.Stack);
        Assert.Contains(wolf, g.Players[Me].Battlefield);
        Assert.True(wolf.SummoningSick);
        Assert.Equal(0, g.Players[Me].Pool.Total);
    }

    [Fact]
    public void CannotCastWithoutEnoughMana()
    {
        var g = NewGame();
        g.AddMana(Me, "forest");
        var behemoth = g.AddToHand(Me, "colossal-dreadmaw");
        var ex = Assert.Throws<RulesException>(() => g.Cast(behemoth.Id));
        Assert.Contains("maná", ex.Message);
        Assert.Contains(behemoth, g.Players[Me].Hand);
    }

    [Fact]
    public void OnlyOneLandPerTurn()
    {
        var g = NewGame();
        g.PlayLand(g.AddToHand(Me, "forest").Id);
        Assert.Throws<RulesException>(() => g.PlayLand(g.AddToHand(Me, "forest").Id));
    }

    [Fact]
    public void SorceriesNeedEmptyStackAndOwnMainPhase()
    {
        var g = NewGame();
        g.AddMana(Me, "mountain", "island", "island", "island");
        g.Cast(g.AddToHand(Me, "shock").Id, Target.OfPlayer(Opp));
        Assert.Throws<RulesException>(() => g.Cast(g.AddToHand(Me, "divination").Id));
    }

    [Fact]
    public void BurnSpellKillsCreatureAndGoesToGraveyard()
    {
        var g = NewGame();
        var bear = g.AddToBattlefield(Opp, "grizzly-bears");
        g.AddMana(Me, "mountain");
        var bolt = g.AddToHand(Me, "shock");
        g.Cast(bolt.Id, Target.OfCard(bear.Id));
        g.BothPass();
        Assert.Contains(bear, g.Players[Opp].Graveyard);
        Assert.Contains(bolt, g.Players[Me].Graveyard);
    }

    [Fact]
    public void StackResolvesLastInFirstOut()
    {
        var g = NewGame();
        var wolf = g.AddToBattlefield(Me, "grizzly-bears");
        g.AddMana(Me, "forest");
        var growth = g.AddToHand(Me, "giant-growth");
        var bolt = g.AddToHand(Opp, "shock");

        g.PassPriority(); // el rival recibe prioridad
        g.AddMana(Opp, "mountain");
        g.Cast(bolt.Id, Target.OfCard(wolf.Id));
        g.PassPriority(); // vuelve a mí, respondo con el pump
        g.Cast(growth.Id, Target.OfCard(wolf.Id));
        g.BothPass(); // resuelve el pump (5/5)
        g.BothPass(); // resuelve el chispazo: 2 de daño a un 5/5
        Assert.Contains(wolf, g.Players[Me].Battlefield);
        Assert.Equal(2, wolf.Damage);
    }

    [Fact]
    public void SpellFizzlesWhenTargetDisappears()
    {
        var g = NewGame();
        var wolf = g.AddToBattlefield(Opp, "grizzly-bears");
        g.AddMana(Me, "swamp", "swamp", "swamp", "island", "island");
        var kill = g.AddToHand(Me, "murder");
        var bounce = g.AddToHand(Me, "unsummon");
        g.Cast(kill.Id, Target.OfCard(wolf.Id));
        g.BothPass();
        Assert.Contains(wolf, g.Players[Opp].Graveyard);

        // Ahora con respuesta: regreso la criatura y el Fin Sombrío se neutraliza.
        var g2 = NewGame();
        var w2 = g2.AddToBattlefield(Opp, "grizzly-bears");
        g2.AddMana(Me, "swamp", "swamp", "swamp", "island", "island");
        var k2 = g2.AddToHand(Me, "murder");
        var b2 = g2.AddToHand(Me, "unsummon");
        g2.Cast(k2.Id, Target.OfCard(w2.Id));
        g2.Cast(b2.Id, Target.OfCard(w2.Id));
        g2.BothPass();
        g2.BothPass();
        Assert.Contains(w2, g2.Players[Opp].Hand);
        Assert.Contains(k2, g2.Players[Me].Graveyard);
        Assert.Contains(g2.History, l => l.Contains("se neutraliza"));
    }

    [Fact]
    public void RitualAddsManaThatCanBeSpent()
    {
        var g = NewGame();
        g.AddMana(Me, "mountain", "mountain", "mountain");
        g.Cast(g.AddToHand(Me, "pyretic-ritual").Id);
        g.BothPass();
        Assert.Equal(4, g.Players[Me].Pool[Color.Red]);
        g.Cast(g.AddToHand(Me, "lightning-strike").Id, Target.OfPlayer(Opp));
        g.BothPass();
        Assert.Equal(17, g.Players[Opp].Life);
        Assert.Equal(2, g.Players[Me].Pool[Color.Red]);
    }

    [Fact]
    public void TokensAndEnterEffects()
    {
        var g = NewGame();
        g.AddMana(Me, "plains", "plains", "plains", "plains", "plains");
        g.Cast(g.AddToHand(Me, "raise-the-alarm").Id);
        g.BothPass();
        g.Cast(g.AddToHand(Me, "inspiring-cleric").Id);
        g.BothPass();
        Assert.Equal(2, g.Players[Me].Battlefield.Count(c => c.IsToken && c.Name == "Soldado"));
        Assert.Equal(24, g.Players[Me].Life);
    }

    [Fact]
    public void ManaEmptiesBetweenSteps()
    {
        var g = NewGame();
        g.AddMana(Me, "forest");
        g.BothPass(); // pasa a inicio de combate
        Assert.Equal(Step.BeginCombat, g.Step);
        Assert.Equal(0, g.Players[Me].Pool.Total);
    }
}

public class CombatTests
{
    [Fact]
    public void UnblockedAttackerDealsDamage()
    {
        var g = NewGame();
        var wolf = g.AddToBattlefield(Me, "grizzly-bears");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([wolf.Id]);
        Assert.True(wolf.Tapped);
        g.PassUntil(Step.CombatDamage);
        Assert.Equal(18, g.Players[Opp].Life);
    }

    [Fact]
    public void SummoningSickCreaturesCannotAttackUnlessHaste()
    {
        var g = NewGame();
        var sick = g.AddToBattlefield(Me, "grizzly-bears", summoningSick: true);
        var hasty = g.AddToBattlefield(Me, "raging-cougar", summoningSick: true);
        g.PassUntil(Step.DeclareAttackers);
        Assert.Throws<RulesException>(() => g.DeclareAttackers([sick.Id]));
        g.DeclareAttackers([hasty.Id]);
        g.PassUntil(Step.CombatDamage);
        Assert.Equal(18, g.Players[Opp].Life);
    }

    [Fact]
    public void DefenderCannotAttackAndVigilanceDoesNotTap()
    {
        var g = NewGame();
        var wall = g.AddToBattlefield(Me, "wall-of-blossoms");
        var griffin = g.AddToBattlefield(Me, "griffin-sentinel");
        g.PassUntil(Step.DeclareAttackers);
        Assert.Throws<RulesException>(() => g.DeclareAttackers([wall.Id]));
        g.DeclareAttackers([griffin.Id]);
        Assert.False(griffin.Tapped);
    }

    [Fact]
    public void FlyingCanOnlyBeBlockedByFlyingOrReach()
    {
        var g = NewGame();
        var griffin = g.AddToBattlefield(Me, "griffin-sentinel");
        var wolf = g.AddToBattlefield(Opp, "grizzly-bears");
        var archer = g.AddToBattlefield(Opp, "canopy-spider");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([griffin.Id]);
        g.BothPass();
        Assert.Equal(Game.Pending.Blockers, g.Awaiting);
        Assert.Throws<RulesException>(() => g.DeclareBlockers(new Dictionary<int, int> { [wolf.Id] = griffin.Id }));
        g.DeclareBlockers(new Dictionary<int, int> { [archer.Id] = griffin.Id });
        g.PassUntil(Step.CombatDamage);
        Assert.Equal(20, g.Players[Opp].Life);
        Assert.Equal(1, archer.Damage);
        Assert.Equal(1, griffin.Damage);
    }

    [Fact]
    public void BlockedCreaturesTradeDamage()
    {
        var g = NewGame();
        var mine = g.AddToBattlefield(Me, "grizzly-bears");
        var theirs = g.AddToBattlefield(Opp, "grizzly-bears");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([mine.Id]);
        g.BothPass();
        g.DeclareBlockers(new Dictionary<int, int> { [theirs.Id] = mine.Id });
        g.PassUntil(Step.CombatDamage);
        Assert.Contains(mine, g.Players[Me].Graveyard);
        Assert.Contains(theirs, g.Players[Opp].Graveyard);
        Assert.Equal(20, g.Players[Opp].Life);
    }

    [Fact]
    public void TrampleAssignsExcessToPlayer()
    {
        var g = NewGame();
        var behemoth = g.AddToBattlefield(Me, "colossal-dreadmaw");
        var wolf = g.AddToBattlefield(Opp, "grizzly-bears");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([behemoth.Id]);
        g.BothPass();
        g.DeclareBlockers(new Dictionary<int, int> { [wolf.Id] = behemoth.Id });
        g.PassUntil(Step.CombatDamage);
        Assert.Equal(16, g.Players[Opp].Life);
        Assert.Contains(wolf, g.Players[Opp].Graveyard);
    }

    [Fact]
    public void DeathtouchKillsAnyBlockerOrAttacker()
    {
        var g = NewGame();
        var behemoth = g.AddToBattlefield(Me, "colossal-dreadmaw");
        var stalker = g.AddToBattlefield(Opp, "typhoid-rats");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([behemoth.Id]);
        g.BothPass();
        g.DeclareBlockers(new Dictionary<int, int> { [stalker.Id] = behemoth.Id });
        g.PassUntil(Step.CombatDamage);
        Assert.Contains(behemoth, g.Players[Me].Graveyard);
        Assert.Contains(stalker, g.Players[Opp].Graveyard);
        Assert.Equal(15, g.Players[Opp].Life); // 1 letal a las ratas por arrollar, 5 al jugador
    }

    [Fact]
    public void LifelinkGainsLife()
    {
        var g = NewGame();
        var cleric = g.AddToBattlefield(Me, "healers-hawk");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([cleric.Id]);
        g.PassUntil(Step.CombatDamage);
        Assert.Equal(21, g.Players[Me].Life);
        Assert.Equal(19, g.Players[Opp].Life);
    }

    [Fact]
    public void LethalDamageWinsTheGame()
    {
        var g = NewGame();
        g.SetLife(Opp, 2);
        var wolf = g.AddToBattlefield(Me, "grizzly-bears");
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([wolf.Id]);
        g.PassUntil(Step.CombatDamage);
        Assert.True(g.IsOver);
        Assert.Equal(Me, g.Winner);
        Assert.Throws<RulesException>(() => g.PassPriority());
    }

    [Fact]
    public void NoAttackersSkipsToEndOfCombat()
    {
        var g = NewGame();
        g.PassUntil(Step.DeclareAttackers);
        g.DeclareAttackers([]);
        Assert.Equal(Step.EndCombat, g.Step);
    }
}

public class TurnTests
{
    [Fact]
    public void FullTurnCycleUntapsDrawsAndClearsDamage()
    {
        var g = NewGame();
        var land = g.AddToBattlefield(Opp, "mountain", tapped: true);
        var wolf = g.AddToBattlefield(Opp, "grizzly-bears", summoningSick: true);
        g.AddMana(Me, "mountain");
        g.Cast(g.AddToHand(Me, "shock").Id, Target.OfCard(wolf.Id));
        g.BothPass();
        Assert.Contains(wolf, g.Players[Opp].Graveyard);

        var oppWolf = g.AddToBattlefield(Opp, "grizzly-bears", summoningSick: true);
        while (g.ActivePlayer != Opp || g.Step != Step.Main1) g.PassPriority();

        Assert.Equal(2, g.Turn);
        Assert.False(land.Tapped);
        Assert.False(oppWolf.SummoningSick);
        Assert.Single(g.Players[Opp].Hand);
        Assert.False(g.Players[Opp].LandPlayedThisTurn);
    }

    [Fact]
    public void PumpEndsAtCleanup()
    {
        var g = NewGame();
        var wolf = g.AddToBattlefield(Me, "grizzly-bears");
        g.AddMana(Me, "forest");
        g.Cast(g.AddToHand(Me, "giant-growth").Id, Target.OfCard(wolf.Id));
        g.BothPass();
        Assert.Equal(5, wolf.Power);
        while (g.ActivePlayer == Me) g.PassPriority();
        Assert.Equal(2, wolf.Power);
    }

    [Fact]
    public void DrawingFromEmptyLibraryLoses()
    {
        var g = new Game();
        g.StartAt(Me, Step.Main2);
        while (!g.IsOver) g.PassPriority();
        Assert.Equal(Me, g.Winner);
    }

    [Fact]
    public void CloneIsIndependentForUndo()
    {
        var g = NewGame();
        var wolf = g.AddToBattlefield(Opp, "grizzly-bears");
        g.AddMana(Me, "mountain");
        var bolt = g.AddToHand(Me, "shock");
        var before = g.Clone();

        g.Cast(bolt.Id, Target.OfCard(wolf.Id));
        g.BothPass();

        Assert.Single(before.Players[Opp].Battlefield);
        Assert.Single(before.Players[Me].Hand);
        Assert.Equal(1, before.Players[Me].Pool[Color.Red]);
        before.Cast(bolt.Id, Target.OfPlayer(Opp));
        before.BothPass();
        Assert.Equal(18, before.Players[Opp].Life);
        Assert.Equal(20, g.Players[Opp].Life);
    }
}
