using MtgEngine.Puzzles;

namespace MtgEngine.Roguelike;

/// <summary>
/// La IA de los rivales en partidas completas. Decide de a una jugada por vez simulando qué pasa
/// después de cada opción (resolver la pila y terminar el combate) y eligiendo la que deja mejor el tablero.
/// No conoce tu mano y supone que no vas a responder: así se la puede sorprender con trucos.
/// </summary>
public static class DuelAI
{
    const int BlockerCombinationsInSims = 300;
    const double HandCardValue = 1.5;
    /// <summary>Lo mínimo que tiene que mejorar el tablero para que valga la pena gastar la carta.</summary>
    const double MinGain = 1.0;

    /// <summary>Hace una jugada por el jugador que tiene que decidir: declarar, jugar una tierra, lanzar algo o pasar.</summary>
    public static void Act(Game g)
    {
        if (g.Awaiting == Game.Pending.Blockers) { g.DeclareBlockers(BlockerAI.Choose(g, 4_000)); return; }
        if (g.Awaiting == Game.Pending.Attackers) { g.DeclareAttackers(ChooseAttackers(g)); return; }
        if (ChooseLand(g) is { } land) { g.PlayLand(land.Id); return; }
        if (BestSpell(g) is { } play) { g.CastWithAutoTap(play.CardId, play.Target); return; }
        g.PassPriority();
    }

    // ---------- Tierras ----------

    /// <summary>La tierra del color que más falta para lo que hay en la mano.</summary>
    static CardInstance? ChooseLand(Game g)
    {
        var me = g.Priority;
        if (!g.CanActAtSorcerySpeed || me.LandPlayedThisTurn) return null;
        var lands = me.Hand.Where(c => c.IsLand).ToList();
        if (lands.Count == 0) return null;
        int Need(Color color) =>
            me.Hand.Concat(me.Command).Where(c => !c.IsLand).Sum(c => c.Definition.ManaCost[color])
            - me.Battlefield.Count(c => c.Definition.Produces == color);
        return lands.OrderByDescending(l => Need(l.Definition.Produces!.Value)).First();
    }

    // ---------- Hechizos ----------

    sealed record Play(int CardId, Target? Target);

    static Play? BestSpell(Game g)
    {
        int me = g.PriorityPlayer;
        var p = g.Players[me];
        if (!g.CanCastSpells(me)) return null;
        var baseline = Score(Settle(g.Clone()), me);
        Play? best = null;
        double bestGain = MinGain;
        foreach (var card in p.Hand.Concat(p.Command).Where(c => !c.IsLand).DistinctBy(c => (c.Definition.Id, c.IsCommander)).ToList())
        {
            var def = card.Definition;
            if (def.Type != CardType.Instant && !g.CanActAtSorcerySpeed) continue;
            if (!g.CanAfford(me, g.CostOf(card))) continue;
            var targets = def.Target == TargetKind.None
                ? [null]
                : AllTargets(g).Where(t => g.IsLegalTarget(def, t, me)).Select(t => (Target?)t).ToList();
            foreach (var target in targets)
            {
                var sim = g.Clone();
                try { sim.CastWithAutoTap(card.Id, target); }
                catch (RulesException) { continue; }
                var gain = Score(Settle(sim), me) - baseline;
                if (gain > bestGain) { bestGain = gain; best = new Play(card.Id, target); }
            }
        }
        return best;
    }

    static IEnumerable<Target> AllTargets(Game g) =>
        g.Players.Select(p => Target.OfPlayer(p.Index)).Concat(g.Creatures().Select(c => Target.OfCard(c.Id)));

    // ---------- Ataque ----------

    static List<int> ChooseAttackers(Game g)
    {
        int me = g.ActivePlayer;
        var able = g.Active.Battlefield
            .Where(c => c.IsCreature && c.CanTapToAttack && !c.Has(Keyword.Defender))
            .OrderByDescending(c => c.Power).ToList();
        if (able.Count == 0) return [];

        IEnumerable<List<CardInstance>> Options()
        {
            if (able.Count <= 5)
            {
                for (int mask = (1 << able.Count) - 1; mask >= 0; mask--)
                    yield return able.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                yield break;
            }
            // Con muchas criaturas se prueban solo "las N más fuertes" y "todas menos las que pueden morir sin hacer nada".
            for (int n = able.Count; n >= 0; n--) yield return able.Take(n).ToList();
            yield return able.Where(c => c.Has(Keyword.Flying) || c.Has(Keyword.Vigilance)).ToList();
        }

        List<int> best = [];
        double bestScore = double.MinValue;
        foreach (var option in Options())
        {
            var ids = option.Select(c => c.Id).ToList();
            var sim = g.Clone();
            sim.DeclareAttackers(ids);
            Settle(sim);
            var score = Score(sim, me) - CrackBackRisk(sim, me);
            if (score > bestScore) { bestScore = score; best = ids; }
        }
        return best;
    }

    /// <summary>Penaliza quedarse sin defensa: si el contraataque del rival podría matarte en su turno.</summary>
    static double CrackBackRisk(Game g, int me)
    {
        if (g.IsOver) return 0;
        var mine = g.Players[me];
        var theirs = g.Players[1 - me];
        int blockers = mine.Battlefield.Count(c => c.IsCreature && !c.Tapped);
        var powers = theirs.Battlefield.Where(c => c.IsCreature && !c.Has(Keyword.Defender))
            .Select(c => c.Definition.Power).OrderByDescending(x => x).ToList();
        int unblocked = powers.Skip(blockers).Sum();
        return unblocked >= mine.Life ? 1_000 : 0;
    }

    // ---------- Simulación y evaluación ----------

    /// <summary>Avanza la copia hasta que la pila esté vacía y el combate en curso haya terminado, sin que nadie responda.</summary>
    internal static Game Settle(Game g)
    {
        for (int guard = 0; guard < 200 && !g.IsOver && (g.Stack.Count > 0 || InCombat(g)); guard++)
        {
            if (g.Awaiting == Game.Pending.Blockers) g.DeclareBlockers(BlockerAI.Choose(g, BlockerCombinationsInSims));
            else g.PassPriority();
        }
        return g;
    }

    static bool InCombat(Game g) =>
        g.Awaiting == Game.Pending.Blockers
        || g.Combat.Count > 0 && g.Step is Step.DeclareAttackers or Step.DeclareBlockers or Step.CombatDamage;

    /// <summary>Qué tan bien está el jugador: vidas, criaturas en juego (por su valor impreso) y cartas en la mano.</summary>
    internal static double Score(Game g, int me)
    {
        var mine = g.Players[me];
        var theirs = g.Players[1 - me];
        if (mine.HasLost) return -1_000_000;
        if (theirs.HasLost) return 1_000_000;
        double life = LifeValue(mine.Life) - LifeValue(theirs.Life);
        double board = mine.Battlefield.Where(c => c.IsCreature).Sum(Value) - theirs.Battlefield.Where(c => c.IsCreature).Sum(Value);
        return life + 2 * board + HandCardValue * (mine.Hand.Count - theirs.Hand.Count);
    }

    /// <summary>Cada vida vale más cuantas menos te quedan.</summary>
    static double LifeValue(int life) => life <= 0 ? -50 : life <= 5 ? life * 2.0 : 10 + (life - 5);

    /// <summary>El valor de una criatura según lo impreso: no cuenta los efectos hasta el final del turno.</summary>
    static double Value(CardInstance c)
    {
        var d = c.Definition;
        double v = d.Power + d.Toughness + d.ManaCost.ManaValue;
        foreach (var k in d.Keywords)
            v += k switch
            {
                Keyword.Flying or Keyword.Deathtouch => 2,
                Keyword.Lifelink or Keyword.Trample or Keyword.Vigilance => 1,
                Keyword.Defender => -2,
                _ => 0,
            };
        return Math.Max(1, v);
    }
}
