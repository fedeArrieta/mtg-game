using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgEngine.Puzzles;
using MtgParty.Desktop.Services;

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

/// <summary>Pantalla de puzzles: lista, tablero y controles. Vos jugás; el rival bloquea solo.</summary>
public partial class PuzzleViewModel : ObservableObject
{
    readonly SolvedStore? solvedStore = new();
    PuzzleSession? session;
    CardInstance? pendingCast;
    /// <summary>Las criaturas marcadas para atacar (sobreviven a que se redibuje el tablero).</summary>
    readonly HashSet<int> selectedAttackers = [];

    public ObservableCollection<PuzzleItemViewModel> Puzzles { get; } = [];
    public ObservableCollection<BoardCardViewModel> Hand { get; } = [];
    public ObservableCollection<BoardCardViewModel> YourLands { get; } = [];
    public ObservableCollection<BoardCardViewModel> YourCreatures { get; } = [];
    public ObservableCollection<BoardCardViewModel> EnemyLands { get; } = [];
    public ObservableCollection<BoardCardViewModel> EnemyCreatures { get; } = [];
    public ObservableCollection<StackEntry> Stack { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public UiSettings Ui => UiSettings.Instance;

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
    [ObservableProperty] List<PhaseChip> phases = [];
    [ObservableProperty] bool youTargetable;
    [ObservableProperty] bool enemyTargetable;
    [ObservableProperty] string yourInfo = "";
    [ObservableProperty] bool hasStack;

    public PuzzleViewModel()
    {
        var solved = solvedStore!.Load();
        foreach (var p in PuzzleLibrary.BuiltIn)
            Puzzles.Add(new PuzzleItemViewModel(p) { Solved = solved.Contains(p.Id) });
        Selected = Puzzles.FirstOrDefault(p => !p.Solved) ?? Puzzles.FirstOrDefault();
    }

    /// <summary>Un solo puzzle, sin lista, pistas ni progreso guardado: las salas de desafío del roguelike.</summary>
    public PuzzleViewModel(Puzzle challenge)
    {
        solvedStore = null;
        IsLibrary = false;
        Puzzles.Add(new PuzzleItemViewModel(challenge, isCustom: true));
        Selected = Puzzles[0];
    }

    /// <summary>Si muestra la lista de puzzles, las pistas y el botón para abrir archivos.</summary>
    public bool IsLibrary { get; } = true;

    public string Progress => $"{Puzzles.Count(p => p.Solved && !p.IsCustom)} de {Puzzles.Count(p => !p.IsCustom)} resueltos";

    partial void OnSelectedChanged(PuzzleItemViewModel? value)
    {
        if (value is null) return;
        session = new PuzzleSession(value.Puzzle);
        pendingCast = null;
        IsTargeting = false;
        selectedAttackers.Clear();
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
            Act(s => s.DeclareAttackers(selectedAttackers.ToList()));
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
        if (session is not null) Refresh();
    }

    internal void OnCardClicked(BoardCardViewModel card)
    {
        if (session is null) return;
        if (IsTargeting)
        {
            if (card.Card.IsCreature && card.Place is CardPlace.YourCreatures or CardPlace.EnemyCreatures)
                CastPending(Target.OfCard(card.Card.Id));
            return;
        }
        if (IsDeclaringAttackers)
        {
            if (card.Place == CardPlace.YourCreatures && card.Playable)
            {
                if (!selectedAttackers.Remove(card.Card.Id)) selectedAttackers.Add(card.Card.Id);
                card.Selected = selectedAttackers.Contains(card.Card.Id);
            }
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
                Say("");
                Refresh();
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
        YourInfo = $"Mano: {you.Hand.Count}\nBiblioteca: {you.Library.Count}\nCementerio: {you.Graveyard.Count}";
        Mana = you.Pool.Total == 0 ? "" : $"Maná flotante: {you.Pool}";
        StepText = PuzzleSolver.StepName(g.Step);
        Phases = PhaseChip.For(g.Step);

        IsDeclaringAttackers = session.Status == PuzzleStatus.Playing && g.Awaiting == Game.Pending.Attackers;
        if (!IsDeclaringAttackers) selectedAttackers.Clear();
        IsWon = session.Status == PuzzleStatus.Won;
        IsFailed = session.Status == PuzzleStatus.Failed;
        CanUndo = session.CanUndo;

        var attacking = g.Combat.ToDictionary(a => a.AttackerId);
        string Note(CardInstance c) =>
            attacking.ContainsKey(c.Id) ? "Ataca"
            : g.Combat.Any(a => a.BlockerIds.Contains(c.Id)) ? "Bloquea"
            : "";
        Fill(Hand, you.Hand, CardPlace.Hand, _ => "");
        Fill(YourLands, you.Battlefield.Where(c => !c.IsCreature), CardPlace.YourLands, _ => "");
        Fill(YourCreatures, you.Battlefield.Where(c => c.IsCreature), CardPlace.YourCreatures, Note);
        Fill(EnemyLands, opp.Battlefield.Where(c => !c.IsCreature), CardPlace.EnemyLands, _ => "");
        Fill(EnemyCreatures, opp.Battlefield.Where(c => c.IsCreature), CardPlace.EnemyCreatures, Note);
        Highlight(g);

        Stack.Clear();
        foreach (var item in Enumerable.Reverse(g.Stack))
            Stack.Add(new StackEntry(new CardTileViewModel(item.Card.Definition),
                item.Target is { } t ? $"{item.Card.Name} → {DescribeTarget(g, t)}" : item.Card.Name));
        HasStack = Stack.Count > 0;

        Log.Clear();
        foreach (var line in g.History.TakeLast(40).Reverse()) Log.Add(line);

        PassLabel = IsDeclaringAttackers ? "Confirmar ataque"
            : g.Stack.Count > 0 ? "Resolver la pila"
            : "Pasar al siguiente paso";

        if (IsWon && Selected is { Solved: false } solvedItem && solvedStore is not null)
        {
            solvedItem.Solved = true;
            solvedStore.Save(Puzzles.Where(p => p.Solved && !p.IsCustom).Select(p => p.Puzzle.Id));
            OnPropertyChanged(nameof(Progress));
        }
        UpdatePrompt();
    }

    /// <summary>Resalta lo que se puede tocar ahora: cartas jugables, atacantes posibles u objetivos válidos.</summary>
    void Highlight(Game g)
    {
        const int me = Puzzle.You0;
        if (IsTargeting && pendingCast is { } spell)
        {
            var def = spell.Definition;
            foreach (var c in YourCreatures.Concat(EnemyCreatures))
                c.Targetable = g.IsLegalTarget(def, Target.OfCard(c.Card.Id), me);
            YouTargetable = g.IsLegalTarget(def, Target.OfPlayer(me), me);
            EnemyTargetable = g.IsLegalTarget(def, Target.OfPlayer(Puzzle.Opp), me);
            foreach (var c in Hand) c.Selected = c.Card.Id == spell.Id;
            return;
        }
        YouTargetable = EnemyTargetable = false;
        if (IsDeclaringAttackers)
        {
            foreach (var c in YourCreatures)
            {
                c.Playable = c.Card.CanTapToAttack && !c.Card.Has(Keyword.Defender);
                c.Selected = selectedAttackers.Contains(c.Card.Id);
            }
            return;
        }
        if (session!.Status != PuzzleStatus.Playing) return;
        foreach (var c in Hand) c.Playable = g.CanPlayNow(me, c.Card);
    }
    void UpdatePrompt()
    {
        Prompt = IsWon ? "¡Ganaste! El rival no tiene más vidas."
            : IsFailed ? "Se terminó el turno y el rival sigue en pie. Deshacé o reiniciá para probar otra línea."
            : IsTargeting ? $"Elegí un objetivo para {pendingCast?.Name}: lo que está resaltado en naranja. Esc cancela."
            : IsDeclaringAttackers ? "Tocá las criaturas que atacan y confirmá el ataque (Espacio)."
            : "Las cartas en verde se pueden jugar ahora. Tocá una tierra para girarla a mano.";
    }

    void Fill(ObservableCollection<BoardCardViewModel> target, IEnumerable<CardInstance> cards, CardPlace place, Func<CardInstance, string> note)
    {
        target.Clear();
        foreach (var c in cards) target.Add(new BoardCardViewModel(c, place, OnCardClicked, note(c)));
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
