using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgEngine.Puzzles;

namespace MtgParty.Desktop.ViewModels;

/// <summary>Un puzzle de la lista de la izquierda.</summary>
public partial class PuzzleItemViewModel(Puzzle puzzle, bool isCustom = false) : ObservableObject
{
    public Puzzle Puzzle { get; } = puzzle;
    /// <summary>Armado en el editor o abierto desde un archivo, no viene con el juego.</summary>
    public bool IsCustom { get; } = isCustom;
    public string Title => Puzzle.Title;
    public string Stars => new string('★', Puzzle.Difficulty) + new string('☆', 5 - Puzzle.Difficulty);
    [ObservableProperty] bool solved;
}

public enum CardPlace { Hand, YourLands, YourCreatures, OpponentCreatures }

/// <summary>Una carta en el tablero, tal como se dibuja.</summary>
public partial class CardViewModel(CardInstance card, CardPlace place, PuzzleViewModel owner) : ObservableObject
{
    public CardInstance Card { get; } = card;
    public CardPlace Place { get; } = place;
    public string Name => Card.Name;
    public string Cost => Card.IsLand ? "" : Card.Definition.ManaCost.ToString();
    public string Stats => Card.IsCreature ? $"{Card.Power}/{Card.Toughness - Card.Damage}" : "";
    public string Text => string.IsNullOrEmpty(Card.Definition.Text) ? "" : Card.Definition.Text;
    public string State =>
        string.Join(" · ", new[]
        {
            Card.Tapped ? "girada" : null,
            Card.IsCreature && Card.SummoningSick && !Card.Has(Keyword.Haste) && Place != CardPlace.Hand ? "mareo" : null,
            Card.Damage > 0 ? $"{Card.Damage} de daño" : null,
        }.Where(s => s is not null));
    public double Opacity => Card.Tapped ? 0.55 : 1;
    [ObservableProperty] bool selected;

    [RelayCommand] void Click() => owner.OnCardClicked(this);
}

/// <summary>Pantalla de puzzles: lista, tablero y controles. Vos jugás; el rival bloquea solo.</summary>
public partial class PuzzleViewModel : ObservableObject
{
    readonly SolvedStore solvedStore = new();
    PuzzleSession? session;
    CardInstance? pendingCast;

    public ObservableCollection<PuzzleItemViewModel> Puzzles { get; } = [];
    public ObservableCollection<CardViewModel> Hand { get; } = [];
    public ObservableCollection<CardViewModel> YourLands { get; } = [];
    public ObservableCollection<CardViewModel> YourCreatures { get; } = [];
    public ObservableCollection<CardViewModel> OpponentCreatures { get; } = [];
    public ObservableCollection<string> Stack { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] PuzzleItemViewModel? selected;
    [ObservableProperty] string title = "";
    [ObservableProperty] string description = "";
    [ObservableProperty] int yourLife;
    [ObservableProperty] int opponentLife;
    [ObservableProperty] string mana = "";
    [ObservableProperty] string stepText = "";
    [ObservableProperty] string prompt = "";
    [ObservableProperty] string message = "";
    [ObservableProperty] bool messageIsError;
    [ObservableProperty] bool isTargeting;
    [ObservableProperty] bool isDeclaringAttackers;
    [ObservableProperty] bool isWon;
    [ObservableProperty] bool isFailed;
    [ObservableProperty] bool canUndo;
    [ObservableProperty] string passLabel = "Pasar";

    public PuzzleViewModel()
    {
        var solved = solvedStore.Load();
        foreach (var p in PuzzleLibrary.BuiltIn)
            Puzzles.Add(new PuzzleItemViewModel(p) { Solved = solved.Contains(p.Id) });
        Selected = Puzzles.FirstOrDefault(p => !p.Solved) ?? Puzzles.FirstOrDefault();
    }

    public string Progress => $"{Puzzles.Count(p => p.Solved && !p.IsCustom)} de {Puzzles.Count(p => !p.IsCustom)} resueltos";

    partial void OnSelectedChanged(PuzzleItemViewModel? value)
    {
        if (value is null) return;
        session = new PuzzleSession(value.Puzzle);
        Title = value.Puzzle.Title;
        Description = value.Puzzle.Description;
        Say("Ganá antes de que termine este turno.");
        Refresh();
    }

    /// <summary>Agrega un puzzle propio a la lista (o reemplaza el que tenga el mismo título) y lo abre.</summary>
    public void AddCustom(Puzzle puzzle)
    {
        var existing = Puzzles.FirstOrDefault(p => p.IsCustom && p.Puzzle.Title == puzzle.Title);
        var item = new PuzzleItemViewModel(puzzle, isCustom: true);
        if (existing is not null) Puzzles[Puzzles.IndexOf(existing)] = item;
        else Puzzles.Add(item);
        OnPropertyChanged(nameof(Progress));
        Selected = item;
    }

    // ---------- Acciones ----------

    [RelayCommand]
    void Pass()
    {
        if (IsDeclaringAttackers)
        {
            Act(s => s.DeclareAttackers(YourCreatures.Where(c => c.Selected).Select(c => c.Card.Id).ToList()));
            return;
        }
        Act(s => s.Pass());
    }

    [RelayCommand]
    void Undo()
    {
        session?.Undo();
        CancelTarget();
        Say("Deshiciste la última jugada.");
        Refresh();
    }

    [RelayCommand]
    void Restart()
    {
        session?.Restart();
        CancelTarget();
        Say("Puzzle reiniciado.");
        Refresh();
    }

    [RelayCommand]
    void Hint()
    {
        if (session is null) return;
        if (session.Status != PuzzleStatus.Playing) { Say("Deshacé o reiniciá para pedir una pista."); return; }
        var result = PuzzleSolver.Solve(session, 400_000);
        if (!result.Solved) { Say($"Desde acá ya no se puede ganar: deshacé algunas jugadas. {session.Puzzle.Hint}", error: true); return; }
        Say($"Pista: {session.Puzzle.Hint} Próxima jugada sugerida: {result.Moves[0]}.");
    }

    [RelayCommand]
    void TargetOpponent() => CastPending(Target.OfPlayer(Puzzle.Opp));

    [RelayCommand]
    void TargetYou() => CastPending(Target.OfPlayer(Puzzle.You0));

    [RelayCommand]
    void CancelTarget()
    {
        pendingCast = null;
        IsTargeting = false;
        UpdatePrompt();
    }

    internal void OnCardClicked(CardViewModel card)
    {
        if (session is null) return;
        if (IsTargeting)
        {
            if (card.Card.IsCreature && card.Place is CardPlace.YourCreatures or CardPlace.OpponentCreatures)
                CastPending(Target.OfCard(card.Card.Id));
            return;
        }
        if (IsDeclaringAttackers)
        {
            if (card.Place == CardPlace.YourCreatures) card.Selected = !card.Selected;
            return;
        }
        switch (card.Place)
        {
            case CardPlace.Hand when card.Card.IsLand:
                Act(s => s.PlayLand(card.Card.Id));
                break;
            case CardPlace.Hand when card.Card.Definition.Target != TargetKind.None:
                pendingCast = card.Card;
                IsTargeting = true;
                UpdatePrompt();
                break;
            case CardPlace.Hand:
                Act(s => s.Cast(card.Card.Id));
                break;
            case CardPlace.YourLands:
                Act(s => s.TapForMana(card.Card.Id));
                break;
        }
    }

    void CastPending(Target target)
    {
        if (pendingCast is not { } card) return;
        CancelTarget();
        Act(s => s.Cast(card.Id, target));
    }

    void Act(Action<PuzzleSession> action)
    {
        if (session is null) return;
        try
        {
            action(session);
            Say("");
        }
        catch (RulesException ex)
        {
            Say(ex.Message, error: true);
        }
        Refresh();
    }

    public void ShowError(string text) => Say(text, error: true);

    void Say(string text, bool error = false)
    {
        Message = text;
        MessageIsError = error;
    }

    // ---------- Dibujo del tablero ----------

    void Refresh()
    {
        if (session is null) return;
        var g = session.Game;
        var you = g.Players[Puzzle.You0];
        var opp = g.Players[Puzzle.Opp];
        YourLife = you.Life;
        OpponentLife = opp.Life;
        Mana = you.Pool.Total == 0 ? "Sin maná flotante" : $"Maná flotante: {you.Pool}";
        StepText = PuzzleSolver.StepName(g.Step);

        Fill(Hand, you.Hand, CardPlace.Hand);
        Fill(YourLands, you.Battlefield.Where(c => c.IsLand), CardPlace.YourLands);
        Fill(YourCreatures, you.Battlefield.Where(c => c.IsCreature), CardPlace.YourCreatures);
        Fill(OpponentCreatures, opp.Battlefield.Where(c => c.IsCreature), CardPlace.OpponentCreatures);

        Stack.Clear();
        foreach (var item in Enumerable.Reverse(g.Stack))
            Stack.Add(item.Target is { } t ? $"{item.Card.Name} → {DescribeTarget(g, t)}" : item.Card.Name);

        Log.Clear();
        foreach (var line in g.History.TakeLast(12)) Log.Add(line);

        IsDeclaringAttackers = session.Status == PuzzleStatus.Playing && g.Awaiting == Game.Pending.Attackers;
        IsWon = session.Status == PuzzleStatus.Won;
        IsFailed = session.Status == PuzzleStatus.Failed;
        CanUndo = session.CanUndo;
        PassLabel = IsDeclaringAttackers ? "Confirmar ataque"
            : g.Stack.Count > 0 ? "Resolver la pila"
            : "Pasar al siguiente paso";

        if (IsWon && Selected is { Solved: false } solvedItem)
        {
            solvedItem.Solved = true;
            solvedStore.Save(Puzzles.Where(p => p.Solved && !p.IsCustom).Select(p => p.Puzzle.Id));
            OnPropertyChanged(nameof(Progress));
        }
        UpdatePrompt();
    }

    void UpdatePrompt()
    {
        Prompt = IsWon ? "¡Ganaste! El rival no tiene más vidas."
            : IsFailed ? "Se terminó el turno y el rival sigue en pie. Deshacé o reiniciá para probar otra línea."
            : IsTargeting ? $"Elegí un objetivo para {pendingCast?.Name}: una criatura, el rival o vos."
            : IsDeclaringAttackers ? "Tocá las criaturas que atacan y confirmá el ataque."
            : "Tocá una carta de tu mano para jugarla o una tierra para girarla.";
    }

    void Fill(ObservableCollection<CardViewModel> target, IEnumerable<CardInstance> cards, CardPlace place)
    {
        target.Clear();
        foreach (var c in cards) target.Add(new CardViewModel(c, place, this));
    }

    static string DescribeTarget(Game g, Target t) =>
        t.IsPlayer ? g.Players[t.Player!.Value].Name : g.FindOnBattlefield(t.Card!.Value)?.Name ?? "(se fue)";
}

/// <summary>Recuerda qué puzzles resolviste, en la carpeta de datos del usuario.</summary>
public sealed class SolvedStore
{
    readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MesaDelCaos", "puzzles-resueltos.txt");

    public HashSet<string> Load()
    {
        try { return File.Exists(path) ? [.. File.ReadAllLines(path)] : []; }
        catch (IOException) { return []; }
    }

    public void Save(IEnumerable<string> ids)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, ids);
        }
        catch (IOException) { /* si no se puede guardar, solo se pierde el progreso */ }
    }
}
