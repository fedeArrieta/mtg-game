namespace MtgEngine.Roguelike;

public enum DuelStatus { Playing, Won, Lost }

/// <summary>Cómo arranca un duelo: mazos, vidas y las ventajas que dan las reliquias.</summary>
public sealed class DuelSetup
{
    public string EnemyName { get; init; } = "Rival";
    public int HumanLife { get; init; } = 20;
    public int EnemyLife { get; init; } = 20;
    public IReadOnlyList<string> HumanDeck { get; init; } = [];
    public IReadOnlyList<string> EnemyDeck { get; init; } = [];
    /// <summary>El comandante del jefe, que empieza en la zona de mando.</summary>
    public string? EnemyCommander { get; init; }
    public int HumanExtraCards { get; init; }
    public IReadOnlyList<string> HumanStartingLands { get; init; } = [];
    public int HumanStartingSoldiers { get; init; }
    public int Seed { get; init; }
}

/// <summary>
/// Un duelo completo contra la IA. Vos sos el jugador 0 y empezás. Después de cada jugada tuya, el rival
/// actúa y el duelo avanza solo por los pasos donde no tenés nada que decidir.
/// </summary>
public sealed class Duel
{
    public const int Human = 0, Enemy = 1;
    const int OpeningHand = 7;

    public Game Game { get; }
    public DuelSetup Setup { get; }

    public Duel(DuelSetup setup)
    {
        Setup = setup;
        var rng = new Random(setup.Seed);
        Game = new Game("Vos", setup.EnemyName);
        Game.SetLife(Human, setup.HumanLife);
        Game.SetLife(Enemy, setup.EnemyLife);
        Deal(Human, setup.HumanDeck, OpeningHand + setup.HumanExtraCards, rng);
        Deal(Enemy, setup.EnemyDeck, OpeningHand, rng);
        foreach (var land in setup.HumanStartingLands) Game.AddToBattlefield(Human, land);
        for (int i = 0; i < setup.HumanStartingSoldiers; i++) Game.AddToken(Human, "Soldado", 1, 1);
        if (setup.EnemyCommander is { } commander) Game.AddCommander(Enemy, commander);
        Game.StartAt(Human, Step.Main1);
        Advance();
    }

    /// <summary>Mezcla y reparte. Si la mano tiene menos de 2 tierras o más de 5, se vuelve a mezclar una vez.</summary>
    void Deal(int player, IReadOnlyList<string> deck, int handSize, Random rng)
    {
        var cards = deck.OrderBy(_ => rng.Next()).ToList();
        int Lands() => cards.Take(handSize).Count(id => CardCatalog.Get(id).Type == CardType.Land);
        if (Lands() is < 2 or > 5) cards = [.. cards.OrderBy(_ => rng.Next())];
        foreach (var id in cards.Take(handSize)) Game.AddToHand(player, id);
        foreach (var id in cards.Skip(handSize)) Game.AddToLibrary(player, id);
    }

    public DuelStatus Status =>
        Game.Winner == Human ? DuelStatus.Won
        : Game.IsOver ? DuelStatus.Lost
        : DuelStatus.Playing;

    public bool DeclaringAttackers => Status == DuelStatus.Playing && Game.Awaiting == Game.Pending.Attackers && Game.ActivePlayer == Human;
    public bool DeclaringBlockers => Status == DuelStatus.Playing && Game.Awaiting == Game.Pending.Blockers && Game.ActivePlayer == Enemy;

    // ---------- Tus jugadas ----------

    public void PlayLand(int cardId) => Do(g => g.PlayLand(cardId));
    public void TapForMana(int landId) => Do(g => g.TapForMana(landId), advance: false);
    public void Cast(int cardId, Target? target = null) => Do(g => g.CastWithAutoTap(cardId, target));
    public void DeclareAttackers(IEnumerable<int> ids) => Do(g => g.DeclareAttackers(ids));
    public void DeclareBlockers(IReadOnlyDictionary<int, int> blocks) => Do(g => g.DeclareBlockers(blocks));
    public void Pass() => Do(g => g.PassPriority());

    /// <summary>Pasa hasta que termine tu turno (sin atacar si no declaraste) y sigue hasta tu próxima decisión.</summary>
    public void PassTurn()
    {
        RequirePlaying();
        for (int guard = 0; guard < 500 && Status == DuelStatus.Playing && Game.ActivePlayer == Human; guard++)
        {
            if (Game.PriorityPlayer == Enemy) DuelAI.Act(Game);
            else if (Game.Awaiting == Game.Pending.Attackers) Game.DeclareAttackers([]);
            else Game.PassPriority();
        }
        Advance();
    }

    void Do(Action<Game> action, bool advance = true)
    {
        RequirePlaying();
        if (Game.PriorityPlayer != Human) throw new RulesException("Esperá: le toca decidir al rival.");
        action(Game);
        if (advance) Advance();
    }

    void RequirePlaying()
    {
        if (Status != DuelStatus.Playing) throw new RulesException("El duelo ya terminó.");
    }

    // ---------- Avance automático ----------

    /// <summary>Hace jugar al rival y pasa por vos los pasos en los que no tenés nada que hacer.</summary>
    public void Advance()
    {
        for (int guard = 0; guard < 5_000 && Status == DuelStatus.Playing; guard++)
        {
            if (Game.PriorityPlayer == Enemy) DuelAI.Act(Game);
            else if (NeedsHuman()) return;
            else if (Game.Awaiting == Game.Pending.Attackers) Game.DeclareAttackers([]);
            else if (Game.Awaiting == Game.Pending.Blockers) Game.DeclareBlockers(new Dictionary<int, int>());
            else Game.PassPriority();
        }
    }

    /// <summary>Si en este momento tenés una decisión real que tomar.</summary>
    bool NeedsHuman()
    {
        var g = Game;
        var me = g.Players[Human];
        if (g.Awaiting == Game.Pending.Attackers)
            return me.Battlefield.Any(c => c.IsCreature && c.CanTapToAttack && !c.Has(Keyword.Defender));
        if (g.Awaiting == Game.Pending.Blockers)
            return me.Battlefield.Any(b => b.IsCreature && !b.Tapped && g.Combat.Any(a => CanBlock(b, g.FindOnBattlefield(a.AttackerId))));
        if (g.Stack.Count > 0)
            return g.Stack[^1].Controller != Human && HasCastableInstant();
        if (g.ActivePlayer == Human && g.IsMainPhase)
            return HasPlayable();
        if (g.Step == Step.DeclareBlockers && g.Combat.Count > 0)
            return HasCastableInstant();
        return false;
    }

    static bool CanBlock(CardInstance blocker, CardInstance? attacker) =>
        attacker is not null && (!attacker.Has(Keyword.Flying) || blocker.Has(Keyword.Flying) || blocker.Has(Keyword.Reach));

    bool HasCastableInstant() =>
        Game.Players[Human].Hand.Any(c => c.Definition.Type == CardType.Instant && Game.CanAfford(Human, c.Definition.ManaCost));

    bool HasPlayable()
    {
        var me = Game.Players[Human];
        if (!me.LandPlayedThisTurn && me.Hand.Any(c => c.IsLand)) return true;
        return me.Hand.Concat(me.Command).Any(c => !c.IsLand && Game.CanAfford(Human, Game.CostOf(c)));
    }

    /// <summary>Una partida en la que la IA juega por los dos lados (para probar el balance).</summary>
    public static Game Simulate(DuelSetup setup)
    {
        var g = new Duel(setup).Game;
        for (int guard = 0; guard < 20_000 && !g.IsOver; guard++)
        {
            DuelAI.Act(g);
            if (g.Turn > 60) break;
        }
        return g;
    }
}
