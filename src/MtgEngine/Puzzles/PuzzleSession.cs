namespace MtgEngine.Puzzles;

public enum PuzzleStatus { Playing, Won, Failed }

/// <summary>
/// Una partida de puzzle en curso. Vos sos el jugador 0; el rival pasa siempre la prioridad y bloquea
/// con <see cref="BlockerAI"/>. Cada jugada se puede deshacer.
/// </summary>
public sealed class PuzzleSession
{
    readonly Stack<Game> undo = new();

    public Puzzle Puzzle { get; }
    public Game Game { get; private set; }

    public PuzzleSession(Puzzle puzzle)
    {
        Puzzle = puzzle;
        Game = puzzle.CreateGame();
    }

    PuzzleSession(Puzzle puzzle, Game game)
    {
        Puzzle = puzzle;
        Game = game;
    }

    public PuzzleStatus Status =>
        Game.Winner == Puzzle.You0 ? PuzzleStatus.Won
        : Game.IsOver || Game.ActivePlayer != Puzzle.You0 || Game.Step >= Step.End ? PuzzleStatus.Failed
        : PuzzleStatus.Playing;

    public bool CanUndo => undo.Count > 0;

    public void PlayLand(int cardId) => Do(g => g.PlayLand(cardId));
    public void TapForMana(int landId) => Do(g => g.TapForMana(landId));
    public void Cast(int cardId, Target? target = null) => Do(g => g.CastWithAutoTap(cardId, target));
    public void DeclareAttackers(IEnumerable<int> ids) => Do(g => g.DeclareAttackers(ids));
    public void Pass() => Do(g => g.PassPriority());

    public void Undo()
    {
        if (undo.Count > 0) Game = undo.Pop();
    }

    public void Restart()
    {
        undo.Clear();
        Game = Puzzle.CreateGame();
    }

    void Do(Action<Game> action)
    {
        if (Status != PuzzleStatus.Playing) throw new RulesException("El puzzle ya terminó. Deshacé o reiniciá para seguir.");
        var before = Game.Clone();
        try { action(Game); }
        catch
        {
            Game = before;
            throw;
        }
        undo.Push(before);
        RunOpponent();
    }

    /// <summary>El rival actúa hasta que te toque a vos o termine el puzzle.</summary>
    void RunOpponent()
    {
        while (Status == PuzzleStatus.Playing)
        {
            if (Game.Awaiting == Game.Pending.Blockers) Game.DeclareBlockers(BlockerAI.Choose(Game));
            else if (Game.PriorityPlayer == Puzzle.Opp) Game.PassPriority();
            else break;
        }
    }

    /// <summary>Copia independiente, para que el solucionador pruebe líneas sin tocar esta sesión.</summary>
    internal PuzzleSession Fork() => new(Puzzle, Game.Clone());
}
