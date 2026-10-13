using System.Text.Json;
using System.Text.Json.Serialization;
using MtgEngine.Puzzles;

namespace MtgEngine.Roguelike;

public enum NodeKind { Combat, Elite, Challenge, Rest, Treasure, Boss }

/// <summary>En qué pantalla está la run.</summary>
public enum RunPhase { Draft, Map, Duel, Reward, Rest, Treasure, Challenge, Defeat, Victory }

/// <summary>Una sala del mapa. <see cref="Next"/> son las salas a las que se puede ir después.</summary>
public sealed class MapNode
{
    public int Id { get; set; }
    public int Floor { get; set; }
    /// <summary>Posición horizontal entre 0 y 1, para dibujar el mapa.</summary>
    public double X { get; set; }
    public NodeKind Kind { get; set; }
    public List<int> Next { get; set; } = [];
    public bool Visited { get; set; }
}

/// <summary>
/// Una run del roguelike: draft inicial, tres actos con un mapa de salas cada uno y un jefe Commander al final de cada acto.
/// Las vidas se mantienen entre duelos. Todo el estado es serializable para guardar y seguir después.
/// </summary>
public sealed class Run
{
    public const int Acts = 3;
    public const int FloorsPerAct = 7; // 6 pisos de salas y el jefe
    public const int StartingHp = 20;
    public const int DraftPicks = 12;
    public const int MinSpells = 8;
    public const int RestHeal = 6;

    public int Seed { get; set; }
    public int Steps { get; set; }
    public List<Color> Colors { get; set; } = [];
    /// <summary>Los hechizos del mazo. Las tierras se calculan solas (<see cref="Lands"/>).</summary>
    public List<string> Spells { get; set; } = [];
    public List<string> Relics { get; set; } = [];
    public int Hp { get; set; } = StartingHp;
    public int MaxHp { get; set; } = StartingHp;
    public int Act { get; set; } = 1;
    public List<MapNode> Map { get; set; } = [];
    public int? Position { get; set; }
    public RunPhase Phase { get; set; }
    /// <summary>Las cartas para elegir en el draft o en la recompensa.</summary>
    public List<string> Offer { get; set; } = [];
    public int DraftPicksLeft { get; set; }
    /// <summary>El rival del duelo actual.</summary>
    public string? Encounter { get; set; }
    /// <summary>El puzzle de la sala de desafío actual.</summary>
    public string? Puzzle { get; set; }
    /// <summary>La última reliquia ganada, para mostrarla.</summary>
    public string? NewRelic { get; set; }
    public List<string> UsedPuzzles { get; set; } = [];
    public List<string> FoughtEnemies { get; set; } = [];
    public int DuelsWon { get; set; }

    // ---------- Arranque y draft ----------

    public static Run Start(int seed, Color first, Color second)
    {
        var run = new Run { Seed = seed, Colors = first == second ? [first] : [first, second], Phase = RunPhase.Draft, DraftPicksLeft = DraftPicks };
        run.Offer = run.MakeOffer(EnemyKind.Normal, initial: true);
        return run;
    }

    Random Rng() => new(unchecked(Seed * 7919 + Steps++ * 104_729));

    int OfferSize => 3 + RelicTotal(RelicEffect.ExtraChoice);

    /// <summary>Cartas para elegir. La mayoría de tus colores; a veces una de otro color para cambiar de rumbo.</summary>
    List<string> MakeOffer(EnemyKind kind, bool initial = false)
    {
        var rng = Rng();
        var offer = new List<string>();
        for (int attempt = 0; offer.Count < OfferSize && attempt < 200; attempt++)
        {
            var rarity = RollRarity(rng, kind, initial);
            bool onColor = rng.NextDouble() < (initial ? 0.9 : 0.8);
            var candidates = RunContent.Pool[rarity]
                .Where(id => !offer.Contains(id) && IsOnColor(id) == onColor).ToList();
            if (candidates.Count == 0) continue;
            offer.Add(candidates[rng.Next(candidates.Count)]);
        }
        return offer;
    }

    static Rarity RollRarity(Random rng, EnemyKind kind, bool initial)
    {
        double r = rng.NextDouble();
        if (kind == EnemyKind.Boss) return Rarity.Rare;
        if (kind == EnemyKind.Elite) return r < 0.3 ? Rarity.Common : r < 0.8 ? Rarity.Uncommon : Rarity.Rare;
        if (initial) return r < 0.75 ? Rarity.Common : r < 0.97 ? Rarity.Uncommon : Rarity.Rare;
        return r < 0.68 ? Rarity.Common : r < 0.95 ? Rarity.Uncommon : Rarity.Rare;
    }

    bool IsOnColor(string cardId) => CardCatalog.Get(cardId).ManaCost.Colors.All(Colors.Contains);

    /// <summary>Elegís una carta del draft o de la recompensa. En la recompensa podés pasar (null).</summary>
    public void Pick(string? cardId)
    {
        if (Phase is not (RunPhase.Draft or RunPhase.Reward)) throw new InvalidOperationException("Ahora no hay cartas para elegir.");
        if (cardId is not null)
        {
            if (!Offer.Contains(cardId)) throw new InvalidOperationException("Esa carta no está entre las opciones.");
            Spells.Add(cardId);
        }
        else if (Phase == RunPhase.Draft) throw new InvalidOperationException("En el draft inicial hay que elegir una carta.");

        Offer = [];
        if (Phase == RunPhase.Draft)
        {
            if (--DraftPicksLeft > 0) { Offer = MakeOffer(EnemyKind.Normal, initial: true); return; }
            NewAct();
            return;
        }
        if (CurrentNode?.Kind == NodeKind.Boss)
        {
            if (Act == Acts) { Phase = RunPhase.Victory; return; }
            Act++;
            Hp = MaxHp;
            NewAct();
            return;
        }
        Phase = RunPhase.Map;
    }

    // ---------- Mazo ----------

    /// <summary>El mazo completo: tus hechizos y las tierras que les corresponden.</summary>
    public List<string> Deck() => [.. Spells, .. Lands(Spells)];

    /// <summary>
    /// Tierras para un mazo: unas 3 cada 4 hechizos (entre 8 y 17), repartidas según los símbolos de maná
    /// de cada color. Si no hay símbolos de color, se usan los colores de la run.
    /// </summary>
    public static List<string> Lands(IReadOnlyList<string> spells, IReadOnlyList<Color>? fallbackColors = null)
    {
        int total = Math.Clamp((int)Math.Round(spells.Count * 0.75), 8, 17);
        var pips = Enum.GetValues<Color>().Where(c => c != Color.Colorless)
            .ToDictionary(c => c, c => spells.Sum(id => CardCatalog.Get(id).ManaCost[c]));
        if (pips.Values.Sum() == 0)
            foreach (var c in fallbackColors is { Count: > 0 } f ? f : [Color.Green]) pips[c] = 1;
        int sum = pips.Values.Sum();
        // Reparto proporcional; los restos van a los colores con más símbolos.
        var counts = pips.ToDictionary(p => p.Key, p => p.Value * total / sum);
        foreach (var c in pips.OrderByDescending(p => p.Value * total % sum).ThenByDescending(p => p.Value).Select(p => p.Key))
        {
            if (counts.Values.Sum() >= total) break;
            if (pips[c] > 0) counts[c]++;
        }
        while (counts.Values.Sum() < total) counts[pips.MaxBy(p => p.Value).Key]++;
        return counts.SelectMany(p => Enumerable.Repeat(BasicLand(p.Key), p.Value)).ToList();
    }

    public static string BasicLand(Color c) => c switch
    {
        Color.White => "plains",
        Color.Blue => "island",
        Color.Black => "swamp",
        Color.Red => "mountain",
        _ => "forest",
    };

    // ---------- Mapa ----------

    [JsonIgnore] public MapNode? CurrentNode => Position is { } p ? Map.First(n => n.Id == p) : null;

    /// <summary>Las salas a las que podés ir ahora.</summary>
    [JsonIgnore] public IEnumerable<MapNode> Available =>
        Phase != RunPhase.Map ? []
        : CurrentNode is { } here ? here.Next.Select(id => Map.First(n => n.Id == id))
        : Map.Where(n => n.Floor == 0);

    void NewAct()
    {
        Map = GenerateMap(Rng());
        Position = null;
        Phase = RunPhase.Map;
    }

    static List<MapNode> GenerateMap(Random rng)
    {
        var floors = new List<List<MapNode>>();
        int id = 0;
        for (int f = 0; f < FloorsPerAct; f++)
        {
            int width = f == 0 ? 3 : f == FloorsPerAct - 1 ? 1 : rng.Next(2, 5);
            var floor = new List<MapNode>();
            for (int i = 0; i < width; i++)
                floor.Add(new MapNode { Id = id++, Floor = f, X = (i + 0.5) / width, Kind = RollKind(rng, f) });
            floors.Add(floor);
        }
        for (int f = 0; f + 1 < floors.Count; f++)
        {
            var next = floors[f + 1];
            foreach (var node in floors[f])
            {
                var byDistance = next.OrderBy(n => Math.Abs(n.X - node.X)).ToList();
                node.Next.Add(byDistance[0].Id);
                if (byDistance.Count > 1 && rng.NextDouble() < 0.45) node.Next.Add(byDistance[1].Id);
            }
            foreach (var orphan in next.Where(n => !floors[f].Any(a => a.Next.Contains(n.Id))))
                floors[f].OrderBy(a => Math.Abs(a.X - orphan.X)).First().Next.Add(orphan.Id);
        }
        // Que no haya dos salas iguales sin pelea seguidas en todo un camino es difícil de garantizar; alcanza con que el primer piso sea de combate.
        return floors.SelectMany(f => f).ToList();
    }

    static NodeKind RollKind(Random rng, int floor)
    {
        if (floor == 0) return NodeKind.Combat;
        if (floor == FloorsPerAct - 1) return NodeKind.Boss;
        if (floor == FloorsPerAct - 2) return NodeKind.Rest;
        double r = rng.NextDouble();
        if (floor == 1) return r < 0.6 ? NodeKind.Combat : r < 0.8 ? NodeKind.Challenge : NodeKind.Treasure;
        return r < 0.42 ? NodeKind.Combat
            : r < 0.6 ? NodeKind.Elite
            : r < 0.75 ? NodeKind.Challenge
            : r < 0.88 ? NodeKind.Rest
            : NodeKind.Treasure;
    }

    /// <summary>Entrás a una sala del mapa.</summary>
    public void Enter(int nodeId)
    {
        var node = Available.FirstOrDefault(n => n.Id == nodeId) ?? throw new InvalidOperationException("No podés ir a esa sala desde acá.");
        Position = node.Id;
        node.Visited = true;
        NewRelic = null;
        switch (node.Kind)
        {
            case NodeKind.Combat or NodeKind.Elite or NodeKind.Boss:
                Encounter = ChooseEnemy(node.Kind).Id;
                FoughtEnemies.Add(Encounter);
                Phase = RunPhase.Duel;
                break;
            case NodeKind.Rest:
                Phase = RunPhase.Rest;
                break;
            case NodeKind.Treasure:
                NewRelic = GainRandomRelic();
                if (NewRelic is null) { Offer = MakeOffer(EnemyKind.Elite); Phase = RunPhase.Reward; }
                else Phase = RunPhase.Treasure;
                break;
            case NodeKind.Challenge:
                var puzzles = PuzzleLibrary.BuiltIn.Where(p => !UsedPuzzles.Contains(p.Id)).ToList();
                if (puzzles.Count == 0) puzzles = [.. PuzzleLibrary.BuiltIn];
                Puzzle = puzzles[Rng().Next(puzzles.Count)].Id;
                UsedPuzzles.Add(Puzzle);
                Phase = RunPhase.Challenge;
                break;
        }
    }

    Enemy ChooseEnemy(NodeKind kind)
    {
        var wanted = kind switch { NodeKind.Elite => EnemyKind.Elite, NodeKind.Boss => EnemyKind.Boss, _ => EnemyKind.Normal };
        var all = RunContent.Enemies.Where(e => e.Kind == wanted && (wanted != EnemyKind.Boss || e.Act == Act)).ToList();
        var fresh = all.Where(e => !FoughtEnemies.Contains(e.Id)).ToList();
        var pool = fresh.Count > 0 ? fresh : all;
        return pool[Rng().Next(pool.Count)];
    }

    /// <summary>Volver al mapa desde el tesoro.</summary>
    public void Continue()
    {
        if (Phase != RunPhase.Treasure) throw new InvalidOperationException("Ahora no se puede seguir.");
        Phase = RunPhase.Map;
    }

    // ---------- Duelos ----------

    /// <summary>Las vidas del rival: crecen con el acto y bajan con el Colmillo Sangriento.</summary>
    public int EnemyLife(Enemy e) =>
        Math.Max(1, e.Life + (e.Kind == EnemyKind.Boss ? 0 : 2 * (Act - 1)) - RelicTotal(RelicEffect.EnemyLife));

    public DuelSetup CreateDuel()
    {
        if (Phase != RunPhase.Duel || Encounter is null) throw new InvalidOperationException("No hay un duelo en curso.");
        var enemy = RunContent.Enemy(Encounter);
        var deck = Deck();
        var mainLand = deck.Where(id => CardCatalog.Get(id).Type == CardType.Land)
            .GroupBy(id => id).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "forest";
        return new DuelSetup
        {
            EnemyName = enemy.Name,
            HumanLife = Hp,
            EnemyLife = EnemyLife(enemy),
            HumanDeck = deck,
            EnemyDeck = [.. enemy.Deck, .. Lands(enemy.Deck)],
            EnemyCommander = enemy.Commander,
            HumanExtraCards = RelicTotal(RelicEffect.ExtraCard),
            HumanStartingLands = Enumerable.Repeat(mainLand, RelicTotal(RelicEffect.StartLand)).ToList(),
            HumanStartingSoldiers = RelicTotal(RelicEffect.StartSoldier),
            Seed = Rng().Next(),
        };
    }

    /// <summary>Termina el duelo. Si ganaste, te quedan las vidas con las que terminaste y elegís recompensa.</summary>
    public void FinishDuel(bool won, int lifeLeft)
    {
        if (Phase != RunPhase.Duel) throw new InvalidOperationException("No hay un duelo en curso.");
        if (!won) { Hp = 0; Phase = RunPhase.Defeat; return; }
        DuelsWon++;
        Hp = Math.Clamp(lifeLeft + RelicTotal(RelicEffect.HealAfterWin), 1, MaxHp);
        var kind = RunContent.Enemy(Encounter!).Kind;
        if (kind != EnemyKind.Normal) NewRelic = GainRandomRelic();
        Offer = MakeOffer(kind);
        Phase = RunPhase.Reward;
    }

    // ---------- Otras salas ----------

    /// <summary>En el descanso: o te curás, o sacás una carta del mazo.</summary>
    public void Rest(string? removeCard = null)
    {
        if (Phase != RunPhase.Rest) throw new InvalidOperationException("No estás en un descanso.");
        if (removeCard is null) Hp = Math.Min(MaxHp, Hp + RestHealAmount);
        else
        {
            if (Spells.Count <= MinSpells) throw new InvalidOperationException($"El mazo no puede tener menos de {MinSpells} hechizos.");
            if (!Spells.Remove(removeCard)) throw new InvalidOperationException("Esa carta no está en tu mazo.");
        }
        Phase = RunPhase.Map;
    }

    [JsonIgnore] public int RestHealAmount => RestHeal + RelicTotal(RelicEffect.RestHeal);

    /// <summary>Termina la sala de desafío. Si resolviste el puzzle, ganás una reliquia (o una carta rara si ya las tenés todas).</summary>
    public void FinishChallenge(bool solved)
    {
        if (Phase != RunPhase.Challenge) throw new InvalidOperationException("No estás en una sala de desafío.");
        if (!solved) { Phase = RunPhase.Map; return; }
        NewRelic = GainRandomRelic();
        if (NewRelic is null) { Offer = MakeOffer(EnemyKind.Boss); Phase = RunPhase.Reward; }
        else Phase = RunPhase.Treasure;
    }

    // ---------- Reliquias ----------

    public int RelicTotal(RelicEffect effect) =>
        Relics.Select(RunContent.Relic).Where(r => r.Effect == effect).Sum(r => r.Amount);

    string? GainRandomRelic()
    {
        var left = RunContent.Relics.Where(r => !Relics.Contains(r.Id)).ToList();
        if (left.Count == 0) return null;
        var relic = left[Rng().Next(left.Count)];
        Relics.Add(relic.Id);
        if (relic.Effect == RelicEffect.MaxHp)
        {
            MaxHp += relic.Amount;
            Hp += relic.Amount;
        }
        return relic.Id;
    }

    // ---------- Guardar ----------

    public string ToJson() => JsonSerializer.Serialize(this, CardCatalog.JsonOptions);

    public static Run FromJson(string json) =>
        JsonSerializer.Deserialize<Run>(json, CardCatalog.JsonOptions) ?? throw new InvalidDataException("La run guardada está vacía.");
}
