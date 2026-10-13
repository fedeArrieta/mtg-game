namespace MtgEngine.Puzzles;

/// <summary>
/// Elige los bloqueos del jugador defensor probando todas las combinaciones y simulando el daño de combate.
/// Prioriza sobrevivir; después equilibra vidas y criaturas (una criatura vale el doble de su fuerza, resistencia y coste en vidas).
/// No anticipa los trucos que el atacante pueda lanzar después de los bloqueos: eso es lo que hace interesantes los puzzles.
/// </summary>
public static class BlockerAI
{
    const int MaxCombinations = 20_000;

    /// <param name="maxCombinations">Más allá de este número de combinaciones, elige de forma golosa.</param>
    public static IReadOnlyDictionary<int, int> Choose(Game game, int maxCombinations = MaxCombinations)
    {
        var defender = game.Defending;
        var attackers = game.Combat.Select(a => game.FindOnBattlefield(a.AttackerId)!).ToList();
        var blockers = defender.Battlefield.Where(c => c.IsCreature && !c.Tapped).ToList();

        // Para cada bloqueador: -1 (no bloquea) o el índice de un atacante que puede bloquear.
        var options = blockers.Select(b => new[] { -1 }
            .Concat(Enumerable.Range(0, attackers.Count).Where(i => CanBlock(b, attackers[i])))
            .ToArray()).ToList();

        long combinations = options.Aggregate(1L, (n, o) => n * o.Length);
        if (combinations > maxCombinations) return Greedy(game, blockers, attackers, options);

        Dictionary<int, int> best = [];
        (int, int) bestScore = (int.MinValue, int.MinValue);
        var choice = new int[blockers.Count];
        foreach (var _ in Enumerate(options, choice, 0))
        {
            var blocks = new Dictionary<int, int>();
            for (int i = 0; i < choice.Length; i++)
                if (choice[i] >= 0) blocks[blockers[i].Id] = attackers[choice[i]].Id;
            var score = Evaluate(game, blocks);
            if (score.CompareTo(bestScore) > 0) { bestScore = score; best = blocks; }
        }
        return best;
    }

    /// <summary>Con demasiadas combinaciones, cada bloqueador elige de a uno la mejor opción dadas las anteriores.</summary>
    static Dictionary<int, int> Greedy(Game game, List<CardInstance> blockers, List<CardInstance> attackers, List<int[]> options)
    {
        var blocks = new Dictionary<int, int>();
        foreach (var i in Enumerable.Range(0, blockers.Count).OrderByDescending(i => Value(blockers[i])))
        {
            int bestOption = -1;
            var bestScore = Evaluate(game, blocks);
            foreach (var o in options[i].Where(o => o >= 0))
            {
                var trial = new Dictionary<int, int>(blocks) { [blockers[i].Id] = attackers[o].Id };
                var score = Evaluate(game, trial);
                if (score.CompareTo(bestScore) > 0) { bestScore = score; bestOption = o; }
            }
            if (bestOption >= 0) blocks[blockers[i].Id] = attackers[bestOption].Id;
        }
        return blocks;
    }

    static bool CanBlock(CardInstance blocker, CardInstance attacker) =>
        !attacker.Has(Keyword.Flying) || blocker.Has(Keyword.Flying) || blocker.Has(Keyword.Reach);

    static IEnumerable<int> Enumerate(List<int[]> options, int[] choice, int index)
    {
        if (index == options.Count) { yield return 0; yield break; }
        foreach (var o in options[index])
        {
            choice[index] = o;
            foreach (var x in Enumerate(options, choice, index + 1)) yield return x;
        }
    }

    /// <summary>Simula el combate con esos bloqueos sin trucos de ningún lado.</summary>
    static (int Survives, int Value) Evaluate(Game game, Dictionary<int, int> blocks)
    {
        var sim = game.Clone();
        var me = sim.Defending.Index;
        sim.DeclareBlockers(blocks);
        while (!sim.IsOver && sim.Step != Step.EndCombat) sim.PassPriority();
        var defender = sim.Players[me];
        var attacker = sim.Players[1 - me];
        int material = defender.Battlefield.Where(c => c.IsCreature).Sum(Value)
                       - attacker.Battlefield.Where(c => c.IsCreature).Sum(Value);
        return (defender.HasLost ? 0 : 1, defender.Life + 2 * material);
    }

    static int Value(CardInstance c) => c.Power + c.Toughness + c.Definition.ManaCost.ManaValue;
}
