using System.Text;

namespace MtgEngine.Puzzles;

/// <summary>Una jugada posible, con su descripción para mostrarla como pista o solución.</summary>
public sealed record PuzzleMove(string Description, Action<PuzzleSession> Apply);

/// <summary>
/// Busca por fuerza bruta una secuencia de jugadas que gane el puzzle contra <see cref="BlockerAI"/>.
/// Sirve para comprobar que un puzzle tiene solución y para dar pistas. Las tierras se giran solas al lanzar.
/// </summary>
public static class PuzzleSolver
{
    public sealed record Result(bool Solved, IReadOnlyList<string> Moves, int StatesExplored);

    public static Result Solve(Puzzle puzzle, int maxStates = 200_000) => Solve(new PuzzleSession(puzzle), maxStates);

    public static Result Solve(PuzzleSession start, int maxStates = 200_000)
    {
        var visited = new HashSet<string>();
        var path = new List<string>();
        int explored = 0;

        bool Search(PuzzleSession s)
        {
            if (s.Status == PuzzleStatus.Won) return true;
            if (s.Status == PuzzleStatus.Failed || explored >= maxStates) return false;
            if (!visited.Add(Key(s.Game))) return false;
            explored++;
            foreach (var move in Moves(s.Game))
            {
                var next = s.Fork();
                try { move.Apply(next); }
                catch (RulesException) { continue; }
                path.Add(move.Description);
                if (Search(next)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        bool solved = Search(start);
        return new Result(solved, solved ? [.. path] : [], explored);
    }

    /// <summary>Las jugadas legales del jugador 0 en este momento.</summary>
    public static IEnumerable<PuzzleMove> Moves(Game g)
    {
        if (g.PriorityPlayer != Puzzle.You0) yield break;
        var me = g.Players[Puzzle.You0];

        if (g.Awaiting == Game.Pending.Attackers)
        {
            var able = me.Battlefield.Where(c => c.IsCreature && c.CanTapToAttack && !c.Has(Keyword.Defender)).ToList();
            for (int mask = (1 << able.Count) - 1; mask >= 0; mask--)
            {
                var chosen = able.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                var ids = chosen.Select(c => c.Id).ToList();
                var text = chosen.Count == 0 ? "No atacar" : $"Atacar con {string.Join(", ", chosen.Select(c => c.Name))}";
                yield return new PuzzleMove(text, s => s.DeclareAttackers(ids));
            }
            yield break;
        }

        foreach (var land in me.Hand.Where(c => c.IsLand).DistinctBy(c => c.Definition.Id).ToList())
            if (g.CanActAtSorcerySpeed && !me.LandPlayedThisTurn)
                yield return new PuzzleMove($"Jugar {land.Name}", s => s.PlayLand(land.Id));

        foreach (var card in me.Hand.Where(c => !c.IsLand).DistinctBy(c => c.Definition.Id).ToList())
        {
            var def = card.Definition;
            if (def.Type != CardType.Instant && !g.CanActAtSorcerySpeed) continue;
            if (!g.CanAfford(Puzzle.You0, def.ManaCost)) continue;
            if (def.Target == TargetKind.None)
            {
                yield return new PuzzleMove($"Lanzar {card.Name}", s => s.Cast(card.Id));
                continue;
            }
            foreach (var (target, name) in Targets(g))
                if (g.IsLegalTarget(def.Target, target))
                    yield return new PuzzleMove($"Lanzar {card.Name} sobre {name}", s => s.Cast(card.Id, target));
        }

        yield return new PuzzleMove(g.Stack.Count > 0 ? "Pasar (se resuelve la pila)" : $"Pasar ({StepName(g.Step)})", s => s.Pass());
    }

    static IEnumerable<(Target, string)> Targets(Game g)
    {
        yield return (Target.OfPlayer(Puzzle.Opp), "el rival");
        foreach (var c in g.Creatures().ToList())
            yield return (Target.OfCard(c.Id), c.Controller == Puzzle.You0 ? $"tu {c.Name}" : $"{c.Name} del rival");
        yield return (Target.OfPlayer(Puzzle.You0), "vos");
    }

    public static string StepName(Step step) => step switch
    {
        Step.Main1 => "fase principal",
        Step.BeginCombat => "inicio del combate",
        Step.DeclareAttackers => "declarar atacantes",
        Step.DeclareBlockers => "bloqueos",
        Step.CombatDamage => "daño de combate",
        Step.EndCombat => "fin del combate",
        Step.Main2 => "segunda fase principal",
        Step.End => "final del turno",
        _ => step.ToString(),
    };

    /// <summary>Clave del estado para no explorar dos veces la misma situación.</summary>
    static string Key(Game g)
    {
        var sb = new StringBuilder();
        sb.Append((int)g.Step).Append('|').Append(g.PriorityPlayer).Append('|').Append((int)g.Awaiting).Append('|');
        foreach (var p in g.Players)
        {
            sb.Append(p.Life).Append(p.LandPlayedThisTurn ? 'L' : 'l').Append(p.Pool).Append('[');
            foreach (var c in p.Hand.OrderBy(c => c.Definition.Id)) sb.Append(c.Definition.Id).Append(',');
            sb.Append("][");
            foreach (var c in p.Battlefield.OrderBy(c => c.Id))
                sb.Append(c.Id).Append(c.Tapped ? 'T' : 't').Append(c.Damage).Append('/').Append(c.PowerBonus).Append('/')
                  .Append(c.ToughnessBonus).Append(string.Concat(Enum.GetValues<Keyword>().Where(c.Has).Select(k => (char)('a' + (int)k)))).Append(',');
            sb.Append("][").Append(p.Graveyard.Count).Append(']');
        }
        foreach (var s in g.Stack) sb.Append('S').Append(s.Card.Definition.Id).Append(s.Target);
        foreach (var a in g.Combat) sb.Append('A').Append(a.AttackerId).Append(':').Append(string.Join('.', a.BlockerIds));
        return sb.ToString();
    }
}
