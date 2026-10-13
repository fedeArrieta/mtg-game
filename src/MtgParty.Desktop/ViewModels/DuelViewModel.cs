using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgEngine.Puzzles;
using MtgEngine.Roguelike;

namespace MtgParty.Desktop.ViewModels;

public enum DuelPlace { Hand, YourLands, YourCreatures, EnemyCreatures }

/// <summary>Una carta del tablero del duelo.</summary>
public partial class DuelCardViewModel(CardInstance card, DuelPlace place, string note, DuelViewModel owner) : ObservableObject
{
    public CardInstance Card { get; } = card;
    public DuelPlace Place { get; } = place;
    public string Name => Card.Name;
    public string Cost => Card.IsLand ? "" : Game.CostOf(Card).ToString();
    public string Stats => Card.IsCreature ? $"{Card.Power}/{Card.Toughness - Card.Damage}" : "";
    public string Text => Card.Definition.Text;
    public string State =>
        string.Join(" · ", new[]
        {
            note,
            Card.Tapped ? "girada" : null,
            Card.IsCreature && Card.SummoningSick && !Card.Has(Keyword.Haste) && Place != DuelPlace.Hand ? "mareo" : null,
            Card.Damage > 0 ? $"{Card.Damage} de daño" : null,
        }.Where(s => !string.IsNullOrEmpty(s)));
    public double Opacity => Card.Tapped ? 0.55 : 1;
    [ObservableProperty] bool selected;

    [RelayCommand] void Click() => owner.OnCardClicked(this);
}

/// <summary>Un duelo de la run: tu tablero, el del rival y los controles.</summary>
public partial class DuelViewModel : ObservableObject
{
    readonly Duel duel;
    CardInstance? pendingCast;
    int? selectedBlocker;
    readonly Dictionary<int, int> blocks = [];

    public ObservableCollection<DuelCardViewModel> Hand { get; } = [];
    public ObservableCollection<DuelCardViewModel> YourLands { get; } = [];
    public ObservableCollection<DuelCardViewModel> YourCreatures { get; } = [];
    public ObservableCollection<DuelCardViewModel> EnemyCreatures { get; } = [];
    public ObservableCollection<string> Stack { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] string enemyName = "";
    [ObservableProperty] string enemyText = "";
    [ObservableProperty] int enemyLife;
    [ObservableProperty] string enemyInfo = "";
    [ObservableProperty] string enemyLands = "";
    [ObservableProperty] string commanderInfo = "";
    [ObservableProperty] int yourLife;
    [ObservableProperty] string yourInfo = "";
    [ObservableProperty] string mana = "";
    [ObservableProperty] string stepText = "";
    [ObservableProperty] string prompt = "";
    [ObservableProperty] string message = "";
    [ObservableProperty] bool messageIsError;
    [ObservableProperty] bool isTargeting;
    [ObservableProperty] bool isDeclaringAttackers;
    [ObservableProperty] bool isDeclaringBlockers;
    [ObservableProperty] bool isOver;
    [ObservableProperty] bool isWon;
    [ObservableProperty] string passLabel = "Pasar";

    /// <summary>Se dispara cuando apretás "Seguir" al terminar: si ganaste y con cuántas vidas.</summary>
    public event Action<bool, int>? Finished;

    public DuelViewModel(DuelSetup setup, string enemyText)
    {
        duel = new Duel(setup);
        EnemyName = setup.EnemyName;
        EnemyText = enemyText;
        Refresh();
    }

    // ---------- Acciones ----------

    [RelayCommand]
    void Pass()
    {
        if (IsDeclaringAttackers)
            Act(d => d.DeclareAttackers(YourCreatures.Where(c => c.Selected).Select(c => c.Card.Id).ToList()));
        else if (IsDeclaringBlockers)
            Act(d => d.DeclareBlockers(new Dictionary<int, int>(blocks)));
        else
            Act(d => d.Pass());
    }

    [RelayCommand]
    void PassTurn() => Act(d => d.PassTurn());

    [RelayCommand]
    void TargetEnemy() => CastPending(Target.OfPlayer(Duel.Enemy));

    [RelayCommand]
    void TargetYou() => CastPending(Target.OfPlayer(Duel.Human));

    [RelayCommand]
    void CancelTarget()
    {
        pendingCast = null;
        IsTargeting = false;
        UpdatePrompt();
    }

    [RelayCommand]
    void Continue() => Finished?.Invoke(duel.Status == DuelStatus.Won, duel.Game.Players[Duel.Human].Life);

    internal void OnCardClicked(DuelCardViewModel card)
    {
        if (IsOver) return;
        if (IsTargeting)
        {
            if (card.Card.IsCreature && card.Place is DuelPlace.YourCreatures or DuelPlace.EnemyCreatures)
                CastPending(Target.OfCard(card.Card.Id));
            return;
        }
        if (IsDeclaringAttackers)
        {
            if (card.Place == DuelPlace.YourCreatures) card.Selected = !card.Selected;
            return;
        }
        if (IsDeclaringBlockers)
        {
            ClickWhileBlocking(card);
            return;
        }
        switch (card.Place)
        {
            case DuelPlace.Hand when card.Card.IsLand:
                Act(d => d.PlayLand(card.Card.Id));
                break;
            case DuelPlace.Hand when card.Card.Definition.Target != TargetKind.None:
                pendingCast = card.Card;
                IsTargeting = true;
                UpdatePrompt();
                break;
            case DuelPlace.Hand:
                Act(d => d.Cast(card.Card.Id));
                break;
            case DuelPlace.YourLands:
                Act(d => d.TapForMana(card.Card.Id));
                break;
        }
    }

    /// <summary>Tocás una criatura tuya para elegirla y después al atacante que bloquea. Tocarla de nuevo la libera.</summary>
    void ClickWhileBlocking(DuelCardViewModel card)
    {
        var g = duel.Game;
        if (card.Place == DuelPlace.YourCreatures && !card.Card.Tapped)
        {
            if (blocks.Remove(card.Card.Id) || selectedBlocker == card.Card.Id) selectedBlocker = null;
            else selectedBlocker = card.Card.Id;
            Say("");
        }
        else if (card.Place == DuelPlace.EnemyCreatures && selectedBlocker is { } blocker)
        {
            if (g.Combat.All(a => a.AttackerId != card.Card.Id)) { Say($"{card.Name} no está atacando.", error: true); return; }
            var b = g.FindOnBattlefield(blocker)!;
            if (card.Card.Has(Keyword.Flying) && !b.Has(Keyword.Flying) && !b.Has(Keyword.Reach))
            {
                Say($"{b.Name} no puede bloquear a {card.Name}: vuela.", error: true);
                return;
            }
            blocks[blocker] = card.Card.Id;
            selectedBlocker = null;
            Say("");
        }
        Refresh();
    }

    void CastPending(Target target)
    {
        if (pendingCast is not { } card) return;
        CancelTarget();
        Act(d => d.Cast(card.Id, target));
    }

    void Act(Action<Duel> action)
    {
        if (IsOver) return;
        try
        {
            action(duel);
            Say("");
        }
        catch (RulesException ex)
        {
            Say(ex.Message, error: true);
        }
        Refresh();
    }

    void Say(string text, bool error = false)
    {
        Message = text;
        MessageIsError = error;
    }

    // ---------- Dibujo ----------

    void Refresh()
    {
        var g = duel.Game;
        var you = g.Players[Duel.Human];
        var enemy = g.Players[Duel.Enemy];
        IsDeclaringAttackers = duel.DeclaringAttackers;
        IsDeclaringBlockers = duel.DeclaringBlockers;
        if (!IsDeclaringBlockers) { blocks.Clear(); selectedBlocker = null; }

        YourLife = you.Life;
        EnemyLife = enemy.Life;
        YourInfo = $"Biblioteca: {you.Library.Count} · Cementerio: {you.Graveyard.Count}";
        EnemyInfo = $"Mano: {enemy.Hand.Count} · Biblioteca: {enemy.Library.Count} · Cementerio: {enemy.Graveyard.Count}";
        EnemyLands = enemy.Battlefield.Any(c => c.IsLand)
            ? "Tierras: " + string.Join(", ", enemy.Battlefield.Where(c => c.IsLand).GroupBy(c => c.Name)
                .Select(grp => $"{grp.Key} ×{grp.Count()}{(grp.Count(c => c.Tapped) is > 0 and var t ? $" ({t} girada{(t > 1 ? "s" : "")})" : "")}"))
            : "Sin tierras en juego";
        CommanderInfo = enemy.Command.FirstOrDefault() is { } cmd
            ? $"Comandante en la zona de mando: {cmd.Name} (cuesta {Game.CostOf(cmd)})"
            : "";
        Mana = you.Pool.Total == 0 ? "Sin maná flotante" : $"Maná flotante: {you.Pool}";
        StepText = $"Turno {g.Turn} · {(g.ActivePlayer == Duel.Human ? "tu turno" : "turno del rival")} · {PuzzleSolver.StepName(g.Step)}";

        var attacking = g.Combat.ToDictionary(a => a.AttackerId);
        string Note(CardInstance c)
        {
            if (attacking.ContainsKey(c.Id)) return "ataca";
            if (blocks.TryGetValue(c.Id, out var a)) return $"bloquea a {g.FindOnBattlefield(a)?.Name}";
            if (selectedBlocker == c.Id) return "elegí a quién bloquea";
            var blocked = g.Combat.FirstOrDefault(x => x.BlockerIds.Contains(c.Id));
            return blocked is null ? "" : $"bloquea a {g.FindOnBattlefield(blocked.AttackerId)?.Name}";
        }
        var selectedAttackers = YourCreatures.Where(c => c.Selected).Select(c => c.Card.Id).ToHashSet();
        Fill(Hand, you.Hand, DuelPlace.Hand, _ => "");
        Fill(YourLands, you.Battlefield.Where(c => c.IsLand), DuelPlace.YourLands, _ => "");
        Fill(YourCreatures, you.Battlefield.Where(c => c.IsCreature), DuelPlace.YourCreatures, Note);
        Fill(EnemyCreatures, enemy.Battlefield.Where(c => c.IsCreature), DuelPlace.EnemyCreatures, Note);
        foreach (var c in YourCreatures)
            c.Selected = IsDeclaringAttackers ? selectedAttackers.Contains(c.Card.Id) : c.Card.Id == selectedBlocker || blocks.ContainsKey(c.Card.Id);

        Stack.Clear();
        foreach (var item in Enumerable.Reverse(g.Stack))
        {
            var who = item.Controller == Duel.Human ? "Vos" : EnemyName;
            Stack.Add(item.Target is { } t ? $"{item.Card.Name} ({who}) → {DescribeTarget(g, t)}" : $"{item.Card.Name} ({who})");
        }

        Log.Clear();
        foreach (var line in g.History.TakeLast(40).Reverse()) Log.Add(line);

        IsOver = duel.Status != DuelStatus.Playing;
        IsWon = duel.Status == DuelStatus.Won;
        PassLabel = IsDeclaringAttackers ? "Confirmar ataque"
            : IsDeclaringBlockers ? "Confirmar bloqueos"
            : g.Stack.Count > 0 ? "Dejar que se resuelva"
            : g.ActivePlayer == Duel.Human && g.Step == Step.Main1 ? "Ir al combate"
            : g.ActivePlayer == Duel.Human && g.Step == Step.Main2 ? "Terminar el turno"
            : "Seguir";
        UpdatePrompt();
    }

    void UpdatePrompt()
    {
        var g = duel.Game;
        Prompt = IsWon ? "¡Ganaste el duelo!"
            : IsOver ? "Perdiste el duelo. La run termina acá."
            : IsTargeting ? $"Elegí un objetivo para {pendingCast?.Name}: una criatura, el rival o vos."
            : IsDeclaringAttackers ? "Tocá las criaturas que atacan y confirmá el ataque."
            : IsDeclaringBlockers ? "Te atacan. Tocá una criatura tuya y después al atacante que bloquea. Confirmá cuando termines."
            : g.Stack.Count > 0 && g.Stack[^1].Controller == Duel.Enemy ? $"El rival lanzó {g.Stack[^1].Card.Name}. Podés responder con un instantáneo o dejar que se resuelva."
            : g.ActivePlayer == Duel.Enemy ? "Es el turno del rival. Podés lanzar un instantáneo antes de seguir."
            : g.Step == Step.DeclareBlockers ? "Ya están los bloqueos. Es tu momento para un truco antes del daño."
            : "Tocá una carta de tu mano para jugarla. Las tierras se giran solas al lanzar.";
    }

    void Fill(ObservableCollection<DuelCardViewModel> target, IEnumerable<CardInstance> cards, DuelPlace place, Func<CardInstance, string> note)
    {
        target.Clear();
        foreach (var c in cards) target.Add(new DuelCardViewModel(c, place, note(c), this));
    }

    static string DescribeTarget(Game g, Target t) =>
        t.IsPlayer ? (t.Player == Duel.Human ? "vos" : g.Players[t.Player!.Value].Name) : g.FindOnBattlefield(t.Card!.Value)?.Name ?? "(se fue)";
}
