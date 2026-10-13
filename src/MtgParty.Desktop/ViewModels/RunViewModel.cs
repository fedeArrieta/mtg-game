using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgEngine.Puzzles;
using MtgEngine.Roguelike;

namespace MtgParty.Desktop.ViewModels;

/// <summary>Un color para elegir al empezar la run.</summary>
public partial class ColorChoiceViewModel(Color color, string name, string hint) : ObservableObject
{
    public Color Color { get; } = color;
    public string Name { get; } = name;
    public string Hint { get; } = hint;
    [ObservableProperty] bool selected;
}

/// <summary>Una carta para elegir en el draft o la recompensa, o para sacar del mazo en el descanso.</summary>
public sealed class OfferCardViewModel(string cardId, RunViewModel owner, int count = 1)
{
    readonly CardDefinition def = CardCatalog.Get(cardId);
    public string Id => cardId;
    public string Name => count > 1 ? $"{def.Name} ×{count}" : def.Name;
    public string Cost => def.ManaCost.ToString();
    public string TypeLine => def.Type switch
    {
        CardType.Creature => "Criatura",
        CardType.Instant => "Instantáneo",
        CardType.Sorcery => "Conjuro",
        _ => "Tierra",
    } + " · " + RunContent.RarityOf(cardId) switch { Rarity.Rare => "rara", Rarity.Uncommon => "infrecuente", _ => "común" };
    public string Text => def.Text;
    public string Stats => def.Type == CardType.Creature ? $"{def.Power}/{def.Toughness}" : "";
    public bool IsRare => RunContent.RarityOf(cardId) == Rarity.Rare;
    public RunViewModel Owner => owner;
}

public sealed record RelicViewModel(string Name, string Text);

/// <summary>Una sala del mapa, tal como se dibuja.</summary>
public sealed record MapNodeViewModel(MapNode Node, bool Available, bool Current)
{
    public string Icon => Node.Kind switch
    {
        NodeKind.Combat => "⚔",
        NodeKind.Elite => "☠",
        NodeKind.Challenge => "?",
        NodeKind.Rest => "☕",
        NodeKind.Treasure => "◆",
        _ => "♛",
    };
    public string Label => Node.Kind switch
    {
        NodeKind.Combat => "Duelo",
        NodeKind.Elite => "Élite",
        NodeKind.Challenge => "Desafío",
        NodeKind.Rest => "Descanso",
        NodeKind.Treasure => "Tesoro",
        _ => "Jefe",
    };
    public string Tip => Node.Kind switch
    {
        NodeKind.Combat => "Un duelo contra un rival común. Ganás una carta.",
        NodeKind.Elite => "Un rival más fuerte. Ganás una carta mejor y una reliquia.",
        NodeKind.Challenge => "Un puzzle: ganá este turno. Si lo resolvés, ganás una reliquia.",
        NodeKind.Rest => "Te curás o sacás una carta del mazo.",
        NodeKind.Treasure => "Una reliquia gratis.",
        _ => "El jefe del acto: un Commander con su comandante en la zona de mando.",
    };
}

/// <summary>El modo roguelike: arranque, draft, mapa, duelos, descansos, tesoros y desafíos.</summary>
public partial class RunViewModel : ObservableObject
{
    readonly RunStore store = new();
    Run? run;

    public IReadOnlyList<ColorChoiceViewModel> ColorChoices { get; } =
    [
        new(Color.White, "Blanco", "Soldados, voladores y vidas"),
        new(Color.Blue, "Azul", "Voladores, robar cartas y devolver criaturas"),
        new(Color.Black, "Negro", "Toque mortal y destruir criaturas"),
        new(Color.Red, "Rojo", "Prisa y daño directo"),
        new(Color.Green, "Verde", "Criaturas grandes y trucos de combate"),
    ];

    public ObservableCollection<OfferCardViewModel> Offer { get; } = [];
    public ObservableCollection<OfferCardViewModel> DeckList { get; } = [];
    public ObservableCollection<RelicViewModel> Relics { get; } = [];
    public ObservableCollection<MapNodeViewModel> MapNodes { get; } = [];

    [ObservableProperty] RunPhase? phase;
    [ObservableProperty] bool hasSavedRun;
    [ObservableProperty] string header = "";
    [ObservableProperty] string pickTitle = "";
    [ObservableProperty] string pickSubtitle = "";
    [ObservableProperty] bool canSkipPick;
    [ObservableProperty] RelicViewModel? newRelic;
    [ObservableProperty] DuelViewModel? duel;
    [ObservableProperty] PuzzleViewModel? challenge;
    [ObservableProperty] string restHealText = "";
    [ObservableProperty] bool canRemoveCards;
    [ObservableProperty] string endTitle = "";
    [ObservableProperty] string endText = "";
    [ObservableProperty] string message = "";

    public bool IsStart => Phase is null;
    public bool IsPicking => Phase is RunPhase.Draft or RunPhase.Reward;
    public bool IsMap => Phase == RunPhase.Map;
    public bool IsDuel => Phase == RunPhase.Duel;
    public bool IsRest => Phase == RunPhase.Rest;
    public bool IsTreasure => Phase == RunPhase.Treasure;
    public bool IsChallenge => Phase == RunPhase.Challenge;
    public bool IsEnd => Phase is RunPhase.Defeat or RunPhase.Victory;
    public bool HasRun => Phase is not null;
    public int Act => run?.Act ?? 1;

    /// <summary>Se pide redibujar el mapa (lo escucha la vista, que dibuja las líneas).</summary>
    public event Action? MapChanged;

    public RunViewModel()
    {
        HasSavedRun = store.Load() is { Phase: not (RunPhase.Defeat or RunPhase.Victory) };
    }

    partial void OnPhaseChanged(RunPhase? value)
    {
        foreach (var name in new[] { nameof(IsStart), nameof(IsPicking), nameof(IsMap), nameof(IsDuel), nameof(IsRest), nameof(IsTreasure), nameof(IsChallenge), nameof(IsEnd), nameof(HasRun), nameof(Act) })
            OnPropertyChanged(name);
    }

    // ---------- Arranque ----------

    [RelayCommand]
    void ToggleColor(ColorChoiceViewModel choice)
    {
        if (!choice.Selected && ColorChoices.Count(c => c.Selected) >= 2)
            ColorChoices.First(c => c.Selected && c != choice).Selected = false;
        choice.Selected = !choice.Selected;
    }

    [RelayCommand]
    void NewRun()
    {
        var chosen = ColorChoices.Where(c => c.Selected).Select(c => c.Color).ToList();
        if (chosen.Count == 0) { Message = "Elegí uno o dos colores para empezar."; return; }
        Message = "";
        run = Run.Start(Random.Shared.Next(), chosen[0], chosen[^1]);
        Sync();
    }

    [RelayCommand]
    void ContinueRun()
    {
        run = store.Load();
        if (run is null) { HasSavedRun = false; return; }
        Sync();
    }

    [RelayCommand]
    void Abandon()
    {
        store.Delete();
        run = null;
        HasSavedRun = false;
        Duel = null;
        Challenge = null;
        Phase = null;
    }

    // ---------- Draft y recompensas ----------

    [RelayCommand]
    void Pick(OfferCardViewModel card) => Do(r => r.Pick(card.Id));

    [RelayCommand]
    void SkipPick() => Do(r => r.Pick(null));

    // ---------- Mapa ----------

    public void EnterNode(MapNode node) => Do(r => r.Enter(node.Id));

    // ---------- Descanso, tesoro y desafío ----------

    [RelayCommand]
    void Heal() => Do(r => r.Rest());

    [RelayCommand]
    void RemoveCard(OfferCardViewModel card) => Do(r => r.Rest(card.Id));

    [RelayCommand]
    void ContinueFromTreasure() => Do(r => r.Continue());

    [RelayCommand]
    void ClaimChallenge() => Do(r => r.FinishChallenge(Challenge?.IsWon == true));

    [RelayCommand]
    void GiveUpChallenge() => Do(r => r.FinishChallenge(false));

    void Do(Action<Run> action)
    {
        if (run is null) return;
        try
        {
            action(run);
            Message = "";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }
        Sync();
    }

    // ---------- Sincronizar la pantalla con la run ----------

    void Sync()
    {
        if (run is null) return;
        store.Save(run);
        if (run.Phase is RunPhase.Defeat or RunPhase.Victory) { store.Delete(); HasSavedRun = false; }
        else HasSavedRun = true;

        var spells = run.Spells.Count;
        Header = $"Acto {run.Act} de {Run.Acts}  ·  Vida {run.Hp}/{run.MaxHp}  ·  Mazo: {spells} hechizos"
            + (spells > 0 ? $" y {run.Deck().Count - spells} tierras" : "");

        Relics.Clear();
        foreach (var r in run.Relics.Select(RunContent.Relic)) Relics.Add(new RelicViewModel(r.Name, r.Text));
        NewRelic = run.NewRelic is { } relicId ? new RelicViewModel(RunContent.Relic(relicId).Name, RunContent.Relic(relicId).Text) : null;

        DeckList.Clear();
        foreach (var g in run.Spells.GroupBy(x => x).OrderBy(g => CardCatalog.Get(g.Key).ManaCost.ManaValue).ThenBy(g => g.Key))
            DeckList.Add(new OfferCardViewModel(g.Key, this, g.Count()));

        Offer.Clear();
        foreach (var id in run.Offer) Offer.Add(new OfferCardViewModel(id, this));
        CanSkipPick = run.Phase == RunPhase.Reward;
        PickTitle = run.Phase == RunPhase.Draft ? "Draft inicial" : "Recompensa";
        PickSubtitle = run.Phase == RunPhase.Draft
            ? $"Elegí una carta para tu mazo. Te quedan {run.DraftPicksLeft} elecciones. Las tierras se agregan solas."
            : "Elegí una carta para sumar a tu mazo, o salteá si no te sirve ninguna.";

        RestHealText = $"Curarte {run.RestHealAmount} vidas ({run.Hp}/{run.MaxHp})";
        CanRemoveCards = run.Spells.Count > Run.MinSpells;

        if (run.Phase == RunPhase.Duel && Duel is null) StartDuel();
        if (run.Phase != RunPhase.Duel) Duel = null;
        if (run.Phase == RunPhase.Challenge && Challenge is null) StartChallenge();
        if (run.Phase != RunPhase.Challenge) Challenge = null;

        if (run.Phase == RunPhase.Victory)
        {
            EndTitle = "¡Ganaste la run!";
            EndText = $"Derrotaste a los tres jefes. Ganaste {Plural(run.DuelsWon, "duelo")} y juntaste {Plural(run.Relics.Count, "reliquia")}.";
        }
        else if (run.Phase == RunPhase.Defeat)
        {
            EndTitle = "Fin de la run";
            EndText = $"Llegaste al acto {run.Act} y ganaste {Plural(run.DuelsWon, "duelo")}. ¡A la próxima!";
        }

        MapNodes.Clear();
        var available = run.Available.Select(n => n.Id).ToHashSet();
        foreach (var n in run.Map) MapNodes.Add(new MapNodeViewModel(n, available.Contains(n.Id), n.Id == run.Position));

        Phase = run.Phase;
        MapChanged?.Invoke();
    }

    static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    void StartDuel()
    {
        var enemy = RunContent.Enemy(run!.Encounter!);
        // No se guarda después de CreateDuel: si cerrás la app en medio del duelo, al volver es el mismo duelo.
        var vm = new DuelViewModel(run.CreateDuel(), enemy.Text);
        vm.Finished += (won, life) => Do(r => r.FinishDuel(won, life));
        Duel = vm;
    }

    void StartChallenge()
    {
        var puzzle = PuzzleLibrary.BuiltIn.First(p => p.Id == run!.Puzzle);
        Challenge = new PuzzleViewModel(puzzle);
    }
}

/// <summary>Guarda la run en curso en la carpeta de datos del usuario para seguirla otro día.</summary>
public sealed class RunStore
{
    readonly string path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MesaDelCaos", "run.json");

    public Run? Load()
    {
        try { return File.Exists(path) ? Run.FromJson(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException) { return null; }
    }

    public void Save(Run run)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, run.ToJson());
        }
        catch (IOException) { /* si no se puede guardar, solo se pierde la posibilidad de seguir después */ }
    }

    public void Delete()
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }
}
