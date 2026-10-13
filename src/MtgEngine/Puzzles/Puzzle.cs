using System.Text.Json;

namespace MtgEngine.Puzzles;

/// <summary>Una carta en juego al empezar el puzzle.</summary>
public sealed class PuzzlePermanent
{
    public string Card { get; set; } = "";
    public bool Tapped { get; set; }
    /// <summary>Entró este turno: tiene mareo de invocación.</summary>
    public bool Sick { get; set; }
}

public sealed class PuzzleSide
{
    public int Life { get; set; } = 20;
    public List<string> Hand { get; set; } = [];
    public List<PuzzlePermanent> Battlefield { get; set; } = [];
    /// <summary>Cartas de arriba de la biblioteca, en orden. Si está vacía se completa con tierras para que nadie pierda por robar.</summary>
    public List<string> Library { get; set; } = [];
    public List<string> Graveyard { get; set; } = [];
}

/// <summary>
/// Un tablero armado de antemano: es tu fase principal y tenés que ganar antes de que termine el turno.
/// El rival no tiene cartas en la mano y bloquea de la mejor forma según lo que ve en la mesa.
/// </summary>
public sealed class Puzzle
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>De 1 a 5.</summary>
    public int Difficulty { get; set; } = 1;
    public string Description { get; set; } = "";
    public string Hint { get; set; } = "";
    public PuzzleSide You { get; set; } = new();
    public PuzzleSide Opponent { get; set; } = new();

    public const int You0 = 0, Opp = 1;

    /// <summary>Arma la partida en el estado inicial del puzzle.</summary>
    public Game CreateGame()
    {
        var g = new Game("Vos", "Rival");
        Setup(g, You0, You);
        Setup(g, Opp, Opponent);
        g.StartAt(You0, Step.Main1);
        return g;
    }

    static void Setup(Game g, int player, PuzzleSide side)
    {
        g.SetLife(player, side.Life);
        foreach (var c in side.Hand) g.AddToHand(player, c);
        foreach (var p in side.Battlefield) g.AddToBattlefield(player, p.Card, p.Tapped, p.Sick);
        foreach (var c in side.Library.Count > 0 ? side.Library : Enumerable.Repeat("llanura", 10)) g.AddToLibrary(player, c);
        foreach (var c in side.Graveyard) g.AddToGraveyard(player, c);
    }

    /// <summary>Problemas que impiden jugar el puzzle (cartas inexistentes, datos fuera de rango).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Title)) errors.Add("Falta el título.");
        if (Difficulty is < 1 or > 5) errors.Add("La dificultad va de 1 a 5.");
        if (Opponent.Life < 1 || You.Life < 1) errors.Add("Las vidas tienen que ser al menos 1.");
        var ids = new[] { You, Opponent }
            .SelectMany(s => s.Hand.Concat(s.Library).Concat(s.Graveyard).Concat(s.Battlefield.Select(b => b.Card)));
        foreach (var id in ids.Distinct().Where(id => !CardCatalog.All.ContainsKey(id)))
            errors.Add($"No existe la carta '{id}'.");
        return errors;
    }
}

public static class PuzzleLibrary
{
    static readonly Lazy<IReadOnlyList<Puzzle>> builtIn = new(() =>
    {
        using var stream = typeof(PuzzleLibrary).Assembly.GetManifestResourceStream("MtgEngine.puzzles.json")!;
        return JsonSerializer.Deserialize<List<Puzzle>>(stream, CardCatalog.JsonOptions)!;
    });

    /// <summary>Los puzzles que vienen con el juego, de menor a mayor dificultad.</summary>
    public static IReadOnlyList<Puzzle> BuiltIn => builtIn.Value;

    public static string Serialize(Puzzle puzzle) => JsonSerializer.Serialize(puzzle, CardCatalog.JsonOptions);

    public static Puzzle Deserialize(string json) =>
        JsonSerializer.Deserialize<Puzzle>(json, CardCatalog.JsonOptions) ?? throw new InvalidDataException("El archivo de puzzle está vacío.");
}
