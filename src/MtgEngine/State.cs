namespace MtgEngine;

public enum Zone { Library, Hand, Battlefield, Graveyard, Stack, Exile, Command }

/// <summary>Los pasos del turno, en orden.</summary>
public enum Step
{
    Untap, Upkeep, Draw, Main1, BeginCombat, DeclareAttackers, DeclareBlockers, CombatDamage, EndCombat, Main2, End, Cleanup,
}

/// <summary>Un objetivo: un jugador (por índice) o una carta en juego (por id).</summary>
public readonly record struct Target(int? Player, int? Card)
{
    public static Target OfPlayer(int index) => new(index, null);
    public static Target OfCard(int id) => new(null, id);
    public bool IsPlayer => Player is not null;
    public bool IsCard => Card is not null;
}

/// <summary>Una carta concreta en la partida, con su estado (girada, daño, bonificaciones).</summary>
public sealed class CardInstance
{
    public int Id { get; internal init; }
    public CardDefinition Definition { get; internal init; } = null!;
    public int Owner { get; internal init; }
    public int Controller { get; internal set; }
    public Zone Zone { get; internal set; }
    public bool IsToken { get; internal init; }
    /// <summary>Comandante: se lanza desde la zona de mando y vuelve ahí si deja el campo. Cada lanzamiento cuesta {2} más.</summary>
    public bool IsCommander { get; internal init; }
    public int CommanderCasts { get; internal set; }

    public bool Tapped { get; internal set; }
    public bool SummoningSick { get; internal set; }
    public int Damage { get; internal set; }
    internal bool HitByDeathtouch { get; set; }
    public int PowerBonus { get; internal set; }
    public int ToughnessBonus { get; internal set; }
    internal HashSet<Keyword> TempKeywords { get; private set; } = [];

    public string Name => Definition.Name;
    public bool IsCreature => Definition.Type == CardType.Creature;
    public bool IsLand => Definition.Type == CardType.Land;
    public int Power => Definition.Power + PowerBonus;
    public int Toughness => Definition.Toughness + ToughnessBonus;
    public bool Has(Keyword k) => Definition.Keywords.Contains(k) || TempKeywords.Contains(k);

    /// <summary>Puede atacar o usar habilidades de girar: no tiene mareo de invocación o tiene prisa.</summary>
    public bool CanTapToAttack => !Tapped && (!SummoningSick || Has(Keyword.Haste));

    internal void ClearEndOfTurn()
    {
        Damage = 0;
        HitByDeathtouch = false;
        PowerBonus = ToughnessBonus = 0;
        TempKeywords.Clear();
    }

    internal CardInstance Clone()
    {
        var c = (CardInstance)MemberwiseClone();
        c.TempKeywords = [.. TempKeywords];
        return c;
    }

    public override string ToString() => IsCreature ? $"{Name} ({Power}/{Toughness})" : Name;
}

public sealed class PlayerState
{
    public int Index { get; internal init; }
    public string Name { get; internal init; } = "";
    public int Life { get; internal set; } = 20;
    public List<CardInstance> Library { get; private set; } = [];
    public List<CardInstance> Hand { get; private set; } = [];
    public List<CardInstance> Battlefield { get; private set; } = [];
    public List<CardInstance> Graveyard { get; private set; } = [];
    public List<CardInstance> Command { get; private set; } = [];
    public ManaPool Pool { get; private set; } = new();
    public bool LandPlayedThisTurn { get; internal set; }
    public bool HasLost { get; internal set; }
    internal bool DrewFromEmptyLibrary { get; set; }

    internal void RestorePool(ManaPool pool) => Pool = pool.Clone();

    internal PlayerState Clone(Dictionary<int, CardInstance> cards)
    {
        var p = (PlayerState)MemberwiseClone();
        p.Library = Library.Select(c => cards[c.Id]).ToList();
        p.Hand = Hand.Select(c => cards[c.Id]).ToList();
        p.Battlefield = Battlefield.Select(c => cards[c.Id]).ToList();
        p.Graveyard = Graveyard.Select(c => cards[c.Id]).ToList();
        p.Command = Command.Select(c => cards[c.Id]).ToList();
        p.Pool = Pool.Clone();
        return p;
    }
}

public sealed record StackItem(CardInstance Card, int Controller, Target? Target);

/// <summary>Un atacante y las criaturas que lo bloquean, en el orden en que recibe daño.</summary>
public sealed class Attack
{
    public int AttackerId { get; init; }
    public List<int> BlockerIds { get; init; } = [];
    public bool Blocked { get; internal set; }
}

/// <summary>Una jugada inválida. El mensaje está pensado para mostrárselo al jugador.</summary>
public sealed class RulesException(string message) : Exception(message);
