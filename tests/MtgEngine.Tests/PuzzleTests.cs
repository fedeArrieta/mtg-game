using MtgEngine.Puzzles;
using Xunit;

namespace MtgEngine.Tests;

public class PuzzleTests
{
    public static TheoryData<string> PuzzleIds => [.. PuzzleLibrary.BuiltIn.Select(p => p.Id)];

    static Puzzle Get(string id) => PuzzleLibrary.BuiltIn.Single(p => p.Id == id);

    static Puzzle Copy(Puzzle p) => PuzzleLibrary.Deserialize(PuzzleLibrary.Serialize(p));

    [Fact]
    public void ThereAreTenValidPuzzlesSortedByDifficulty()
    {
        var all = PuzzleLibrary.BuiltIn;
        Assert.Equal(10, all.Count);
        Assert.All(all, p => Assert.Empty(p.Validate()));
        Assert.Equal(all.OrderBy(p => p.Difficulty).Select(p => p.Id), all.Select(p => p.Id));
        Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(PuzzleIds))]
    public void EveryPuzzleHasASolution(string id)
    {
        var result = PuzzleSolver.Solve(Get(id), maxStates: 400_000);
        Assert.True(result.Solved, $"{id} no tiene solución (estados explorados: {result.StatesExplored}).");
    }

    [Theory]
    [InlineData("primer-golpe")]
    [InlineData("pisoton")]
    [InlineData("ritual-de-fuego")]
    [InlineData("muro-de-contencion")]
    [InlineData("veneno-y-furia")]
    public void OneMoreLifeMakesThePuzzleImpossible(string id)
    {
        var harder = Copy(Get(id));
        harder.Opponent.Life++;
        Assert.False(PuzzleSolver.Solve(harder).Solved);
    }

    [Fact]
    public void ReplayingTheSolutionWinsTheSession()
    {
        var session = new PuzzleSession(Get("primer-golpe"));
        var g = session.Game;
        session.PlayLand(g.Players[0].Hand.First(c => c.IsLand).Id);
        session.Cast(session.Game.Players[0].Hand.First(c => c.Name == "Trasgo furioso").Id);
        session.Pass();
        session.Cast(session.Game.Players[0].Hand.First(c => c.Name == "Choque").Id, Target.OfPlayer(1));
        session.Pass();
        session.Cast(session.Game.Players[0].Hand.First(c => c.Name == "Relámpago").Id, Target.OfPlayer(1));
        session.Pass();
        Assert.Equal(1, session.Game.Players[1].Life);
        while (session.Game.Awaiting != Game.Pending.Attackers) session.Pass();
        session.DeclareAttackers(session.Game.Players[0].Battlefield.Where(c => c.IsCreature).Select(c => c.Id));
        while (session.Status == PuzzleStatus.Playing) session.Pass();
        Assert.Equal(PuzzleStatus.Won, session.Status);
    }

    [Fact]
    public void PassingTheWholeTurnFails()
    {
        var session = new PuzzleSession(Get("primer-golpe"));
        while (session.Status == PuzzleStatus.Playing) session.Pass();
        Assert.Equal(PuzzleStatus.Failed, session.Status);
        Assert.Throws<RulesException>(() => session.Pass());
    }

    [Fact]
    public void UndoRestoresThePreviousState()
    {
        var session = new PuzzleSession(Get("primer-golpe"));
        var land = session.Game.Players[0].Hand.First(c => c.IsLand);
        session.PlayLand(land.Id);
        Assert.Equal(3, session.Game.Players[0].Hand.Count);
        session.Undo();
        Assert.Equal(4, session.Game.Players[0].Hand.Count);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void IllegalMoveLeavesStateUntouched()
    {
        var session = new PuzzleSession(Get("pisoton"));
        var wall = session.Game.Players[1].Battlefield.Single();
        var growth = session.Game.Players[0].Hand.First(c => c.Name == "Crecimiento gigante");
        Assert.Throws<RulesException>(() => session.Cast(growth.Id, Target.OfPlayer(1)));
        Assert.All(session.Game.Players[0].Battlefield.Where(c => c.IsLand), l => Assert.False(l.Tapped));
        Assert.False(session.CanUndo);
        Assert.NotNull(wall);
    }

    [Fact]
    public void AutoTapUsesTheRightColors()
    {
        var g = new Game();
        g.StartAt(0, Step.Main1);
        var forest = g.AddToBattlefield(0, "forest");
        var mountain = g.AddToBattlefield(0, "mountain");
        var wolf = g.AddToHand(0, "grizzly-bears");
        g.CastWithAutoTap(wolf.Id);
        Assert.True(forest.Tapped);
        Assert.True(mountain.Tapped);
        var bolt = g.AddToHand(0, "shock");
        Assert.Throws<RulesException>(() => g.CastWithAutoTap(bolt.Id, Target.OfPlayer(1)));
    }

    [Fact]
    public void BlockerChumpsToSurvive()
    {
        var p = new Puzzle
        {
            Title = "test",
            You = new() { Battlefield = [new() { Card = "colossal-dreadmaw" }] },
            Opponent = new() { Life = 6, Battlefield = [new() { Card = "elite-vanguard" }] },
        };
        var session = new PuzzleSession(p);
        while (session.Game.Awaiting != Game.Pending.Attackers) session.Pass();
        session.DeclareAttackers([session.Game.Players[0].Battlefield.Single().Id]);
        session.Pass();
        Assert.Contains(session.Game.Combat, a => a.Blocked);
        while (session.Status == PuzzleStatus.Playing) session.Pass();
        // 1 de daño a la vanguardia, 5 arrollan.
        Assert.Equal(1, session.Game.Players[1].Life);
        Assert.False(session.Game.Players[1].HasLost);
    }

    [Fact]
    public void BlockerDoesNotThrowCreaturesAwayWhenSafe()
    {
        var p = new Puzzle
        {
            Title = "test",
            You = new() { Battlefield = [new() { Card = "colossal-dreadmaw" }] },
            Opponent = new() { Life = 20, Battlefield = [new() { Card = "elite-vanguard" }] },
        };
        var session = new PuzzleSession(p);
        while (session.Game.Awaiting != Game.Pending.Attackers) session.Pass();
        session.DeclareAttackers([session.Game.Players[0].Battlefield.Single().Id]);
        session.Pass();
        Assert.DoesNotContain(session.Game.Combat, a => a.Blocked);
    }
}
