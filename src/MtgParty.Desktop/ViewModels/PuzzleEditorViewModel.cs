using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgEngine.Puzzles;

namespace MtgParty.Desktop.ViewModels;

/// <summary>Una carta del catálogo, para elegirla en el editor.</summary>
public sealed record CatalogCard(CardDefinition Definition)
{
    CardTileViewModel? tile;
    /// <summary>La carta dibujada, para la vista previa (se crea recién cuando se muestra).</summary>
    public CardTileViewModel Tile => tile ??= new(Definition);
    public string Label => Definition.Type == CardType.Land
        ? Definition.Name
        : $"{Definition.Name}  {Definition.ManaCost}";
    public override string ToString() => Label;
}

/// <summary>Una carta puesta en una zona del puzzle que se está editando.</summary>
public partial class EditorCardViewModel(CardDefinition definition, bool onBattlefield) : ObservableObject
{
    public CardDefinition Definition { get; } = definition;
    public bool OnBattlefield { get; } = onBattlefield;
    public string Name => Definition.Name;
    public string Detail => Definition.Type == CardType.Creature
        ? $"{Definition.ManaCost} · {Definition.Power}/{Definition.Toughness}"
        : Definition.Type == CardType.Land ? "Tierra" : Definition.ManaCost.ToString();
    public bool CanBeSick => OnBattlefield && Definition.Type == CardType.Creature;
    public CardTileViewModel Tile { get; } = new(definition);
    [ObservableProperty] bool tapped;
    [ObservableProperty] bool sick;
}

/// <summary>Editor de puzzles: armás el tablero, verificás que tenga solución, lo probás y lo guardás.</summary>
public partial class PuzzleEditorViewModel : ObservableObject
{
    readonly PuzzleViewModel player;

    public IReadOnlyList<CatalogCard> Catalog { get; } =
        CardCatalog.All.Values.OrderBy(c => c.Type).ThenBy(c => c.Name).Select(c => new CatalogCard(c)).ToList();

    public ObservableCollection<EditorCardViewModel> YourHand { get; } = [];
    public ObservableCollection<EditorCardViewModel> YourBattlefield { get; } = [];
    public ObservableCollection<EditorCardViewModel> OpponentBattlefield { get; } = [];
    public ObservableCollection<string> Solution { get; } = [];

    [ObservableProperty] CatalogCard? selectedCard;
    [ObservableProperty] string title = "Mi puzzle";
    [ObservableProperty] decimal difficulty = 2;
    [ObservableProperty] string description = "";
    [ObservableProperty] string hint = "";
    [ObservableProperty] decimal yourLife = 20;
    [ObservableProperty] decimal opponentLife = 5;
    [ObservableProperty] string status = "Elegí una carta del catálogo y agregala a una zona.";
    [ObservableProperty] bool statusIsError;
    [ObservableProperty] bool isVerifying;

    public bool HasSolution => Solution.Count > 0;

    public PuzzleEditorViewModel(PuzzleViewModel player)
    {
        this.player = player;
        Solution.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSolution));
        SelectedCard = Catalog.FirstOrDefault();
    }

    /// <summary>Lo pide la ventana para cambiar a la pestaña de puzzles al probar.</summary>
    public event Action? PlayRequested;

    // ---------- Armar el tablero ----------

    [RelayCommand] void AddToHand() => Add(YourHand, onBattlefield: false);
    [RelayCommand] void AddToYourBattlefield() => Add(YourBattlefield, onBattlefield: true);
    [RelayCommand] void AddToOpponentBattlefield() => Add(OpponentBattlefield, onBattlefield: true);

    void Add(ObservableCollection<EditorCardViewModel> zone, bool onBattlefield)
    {
        if (SelectedCard is null) return;
        Report("");
        zone.Add(new EditorCardViewModel(SelectedCard.Definition, onBattlefield));
        Solution.Clear();
    }

    [RelayCommand]
    void Remove(EditorCardViewModel card)
    {
        YourHand.Remove(card);
        YourBattlefield.Remove(card);
        OpponentBattlefield.Remove(card);
        Solution.Clear();
    }

    [RelayCommand]
    void New()
    {
        Load(new Puzzle { Title = "Mi puzzle", Difficulty = 2, Opponent = new() { Life = 5 } });
        Report("Puzzle nuevo.");
    }

    /// <summary>Carga el puzzle que está abierto en la pestaña Puzzles, para usarlo de base.</summary>
    [RelayCommand]
    void CopyFromCurrent()
    {
        if (player.Selected?.Puzzle is not { } p) return;
        var copy = PuzzleLibrary.Deserialize(PuzzleLibrary.Serialize(p));
        copy.Id = "";
        copy.Title += " (copia)";
        Load(copy);
        Report($"Copiaste \"{p.Title}\". Cambiá lo que quieras.");
    }

    public void Load(Puzzle p)
    {
        Title = p.Title;
        Difficulty = p.Difficulty;
        Description = p.Description;
        Hint = p.Hint;
        YourLife = p.You.Life;
        OpponentLife = p.Opponent.Life;
        Fill(YourHand, p.You.Hand.Select(id => new PuzzlePermanent { Card = id }), onBattlefield: false);
        Fill(YourBattlefield, p.You.Battlefield, onBattlefield: true);
        Fill(OpponentBattlefield, p.Opponent.Battlefield, onBattlefield: true);
        Solution.Clear();
    }

    static void Fill(ObservableCollection<EditorCardViewModel> zone, IEnumerable<PuzzlePermanent> cards, bool onBattlefield)
    {
        zone.Clear();
        foreach (var c in cards)
            if (CardCatalog.TryGet(c.Card, out var def))
                zone.Add(new EditorCardViewModel(def, onBattlefield) { Tapped = c.Tapped, Sick = c.Sick });
    }

    public Puzzle ToPuzzle() => new()
    {
        Id = Slug(Title),
        Title = Title.Trim(),
        Difficulty = (int)Difficulty,
        Description = Description.Trim(),
        Hint = Hint.Trim(),
        You = new PuzzleSide
        {
            Life = (int)YourLife,
            Hand = YourHand.Select(c => c.Definition.Id).ToList(),
            Battlefield = YourBattlefield.Select(ToPermanent).ToList(),
        },
        Opponent = new PuzzleSide
        {
            Life = (int)OpponentLife,
            Battlefield = OpponentBattlefield.Select(ToPermanent).ToList(),
        },
    };

    static PuzzlePermanent ToPermanent(EditorCardViewModel c) => new() { Card = c.Definition.Id, Tapped = c.Tapped, Sick = c.Sick && c.CanBeSick };

    static string Slug(string title)
    {
        var chars = title.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => char.IsLetterOrDigit(ch) || ch == ' ')
            .Select(ch => ch == ' ' ? '-' : ch);
        var slug = string.Concat(chars);
        return string.IsNullOrEmpty(slug) ? "puzzle" : slug;
    }

    // ---------- Verificar, probar ----------

    [RelayCommand]
    async Task Verify()
    {
        var puzzle = ToPuzzle();
        if (puzzle.Validate() is { Count: > 0 } errors) { Report(string.Join(" ", errors), error: true); return; }
        IsVerifying = true;
        Report("Buscando una solución…");
        try
        {
            var result = await Task.Run(() => PuzzleSolver.Solve(puzzle, 400_000));
            Solution.Clear();
            if (result.Solved)
            {
                foreach (var move in result.Moves) Solution.Add(move);
                var harder = PuzzleLibrary.Deserialize(PuzzleLibrary.Serialize(puzzle));
                harder.Opponent.Life++;
                var tight = !(await Task.Run(() => PuzzleSolver.Solve(harder, 400_000))).Solved;
                Report(tight
                    ? $"Tiene solución, en {result.Moves.Count} jugadas. Con una vida más para el rival ya no se puede: está bien ajustado."
                    : $"Tiene solución, en {result.Moves.Count} jugadas. Ojo: también se gana con una vida más para el rival, así que podés hacerlo más difícil.");
            }
            else if (result.StatesExplored >= 400_000)
                Report("No encontré solución, pero el puzzle es tan grande que no pude revisar todas las líneas.", error: true);
            else
                Report("No tiene solución: ninguna línea gana antes de que termine el turno.", error: true);
        }
        finally { IsVerifying = false; }
    }

    [RelayCommand]
    void Play()
    {
        var puzzle = ToPuzzle();
        if (puzzle.Validate() is { Count: > 0 } errors) { Report(string.Join(" ", errors), error: true); return; }
        player.AddCustom(puzzle);
        Report($"\"{puzzle.Title}\" quedó en la lista de puzzles para jugarlo.");
        PlayRequested?.Invoke();
    }

    public void Report(string message, bool error = false)
    {
        Status = message;
        StatusIsError = error;
    }
}
