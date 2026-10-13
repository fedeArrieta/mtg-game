using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgEngine.Puzzles;
using MtgEngine.Roguelike;
using MtgParty.Desktop.Services;

namespace MtgParty.Desktop.ViewModels;

/// <summary>Un duelo de la run: tu tablero, el del rival y los controles.</summary>
public partial class DuelViewModel : ObservableObject
{
    readonly Duel duel;
    internal Game Game => duel.Game;
    CardInstance? pendingCast;
    int? selectedBlocker;
    readonly HashSet<int> selectedAttackers = [];
    readonly Dictionary<int, int> blocks = [];

    public ObservableCollection<BoardCardViewModel> Hand { get; } = [];
    public ObservableCollection<BoardCardViewModel> YourLands { get; } = [];
    public ObservableCollection<BoardCardViewModel> YourCreatures { get; } = [];
    public ObservableCollection<BoardCardViewModel> EnemyLands { get; } = [];
    public ObservableCollection<BoardCardViewModel> EnemyCreatures { get; } = [];
    public ObservableCollection<BoardCardViewModel> EnemyCommand { get; } = [];
    public ObservableCollection<StackEntry> Stack { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    /// <summary>Lo que pasó desde tu última jugada: sobre todo, lo que hizo el rival.</summary>
    public ObservableCollection<string> Recent { get; } = [];
    public UiSettings Ui => UiSettings.Instance;
    /// <summary>Desde qué línea del historial empieza lo "reciente".</summary>
    int recentFrom;

    [ObservableProperty] string enemyName = "";
    [ObservableProperty] string enemyText = "";
    [ObservableProperty] int enemyLife;
    [ObservableProperty] string enemyInfo = "";
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
    [ObservableProperty] List<PhaseChip> phases = [];
    [ObservableProperty] bool youTargetable;
    [ObservableProperty] bool enemyTargetable;
    [ObservableProperty] bool hasStack;
    [ObservableProperty] bool hasRecent;
    [ObservableProperty] bool isYourTurn;
    [ObservableProperty] string turnText = "";

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
            Act(d => d.DeclareAttackers(selectedAttackers.ToList()));
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
        Refresh();
    }

    [RelayCommand]
    void Continue() => Finished?.Invoke(duel.Status == DuelStatus.Won, duel.Game.Players[Duel.Human].Life);

    internal void OnCardClicked(BoardCardViewModel card)
    {
        if (IsOver) return;
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
        if (IsDeclaringBlockers)
        {
            ClickWhileBlocking(card);
            return;
        }
        switch (card.Place)
        {
            case CardPlace.Hand when card.Card.IsLand:
                Act(d => d.PlayLand(card.Card.Id));
                break;
            case CardPlace.Hand when card.Card.Definition.Target != TargetKind.None:
                pendingCast = card.Card;
                IsTargeting = true;
                Say("");
                Refresh();
                break;
            case CardPlace.Hand:
                Act(d => d.Cast(card.Card.Id));
                break;
            case CardPlace.YourLands:
                Act(d => d.TapForMana(card.Card.Id));
                break;
        }
    }

    /// <summary>Tocás una criatura tuya para elegirla y después al atacante que bloquea. Tocarla de nuevo la libera.</summary>
    void ClickWhileBlocking(BoardCardViewModel card)
    {
        var g = duel.Game;
        if (card.Place == CardPlace.YourCreatures && !card.Card.Tapped)
        {
            if (blocks.Remove(card.Card.Id) || selectedBlocker == card.Card.Id) selectedBlocker = null;
            else selectedBlocker = card.Card.Id;
            Say("");
        }
        else if (card.Place == CardPlace.EnemyCreatures && selectedBlocker is { } blocker)
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
        recentFrom = duel.Game.History.Count;
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

        if (!IsDeclaringAttackers) selectedAttackers.Clear();
        IsOver = duel.Status != DuelStatus.Playing;
        IsWon = duel.Status == DuelStatus.Won;

        YourLife = you.Life;
        EnemyLife = enemy.Life;
        YourInfo = $"Mano: {you.Hand.Count}\nBiblioteca: {you.Library.Count}\nCementerio: {you.Graveyard.Count}";
        EnemyInfo = $"Mano: {enemy.Hand.Count}\nBiblioteca: {enemy.Library.Count}\nCementerio: {enemy.Graveyard.Count}";
        CommanderInfo = enemy.Command.Count > 0 ? "Zona de mando" : "";
        Mana = you.Pool.Total == 0 ? "" : $"Maná flotante: {you.Pool}";
        IsYourTurn = g.ActivePlayer == Duel.Human;
        TurnText = $"Turno {g.Turn} · {(IsYourTurn ? "tu turno" : "turno del rival")}";
        StepText = $"{TurnText} · {PuzzleSolver.StepName(g.Step)}";
        Phases = PhaseChip.For(g.Step);

        var attacking = g.Combat.ToDictionary(a => a.AttackerId);
        string Note(CardInstance c)
        {
            if (attacking.ContainsKey(c.Id)) return g.Combat.Any(a => a.AttackerId == c.Id && a.Blocked) ? "Ataca (bloqueada)" : "Ataca";
            if (blocks.TryGetValue(c.Id, out var a)) return $"Bloquea a {g.FindOnBattlefield(a)?.Name}";
            if (selectedBlocker == c.Id) return "¿A quién bloquea?";
            var blocked = g.Combat.FirstOrDefault(x => x.BlockerIds.Contains(c.Id));
            return blocked is null ? "" : $"Bloquea a {g.FindOnBattlefield(blocked.AttackerId)?.Name}";
        }
        Fill(Hand, you.Hand, CardPlace.Hand, _ => "");
        Fill(YourLands, you.Battlefield.Where(c => !c.IsCreature), CardPlace.YourLands, _ => "");
        Fill(YourCreatures, you.Battlefield.Where(c => c.IsCreature), CardPlace.YourCreatures, Note);
        Fill(EnemyLands, enemy.Battlefield.Where(c => !c.IsCreature), CardPlace.EnemyLands, _ => "");
        Fill(EnemyCreatures, enemy.Battlefield.Where(c => c.IsCreature), CardPlace.EnemyCreatures, Note);
        EnemyCommand.Clear();
        foreach (var c in enemy.Command) EnemyCommand.Add(new BoardCardViewModel(c, CardPlace.Command, OnCardClicked, "", g.CostOf(c)));
        Highlight(g);

        Stack.Clear();
        foreach (var item in Enumerable.Reverse(g.Stack))
        {
            var who = item.Controller == Duel.Human ? "Vos" : EnemyName;
            Stack.Add(new StackEntry(new CardTileViewModel(item.Card.Definition),
                item.Target is { } t ? $"{item.Card.Name} ({who}) → {DescribeTarget(g, t)}" : $"{item.Card.Name} ({who})"));
        }
        HasStack = Stack.Count > 0;

        Log.Clear();
        foreach (var line in g.History.TakeLast(60).Reverse()) Log.Add(line);
        Recent.Clear();
        foreach (var line in g.History.Skip(Math.Min(recentFrom, g.History.Count)).TakeLast(12)) Recent.Add(line);
        HasRecent = Recent.Count > 0;

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
            : IsTargeting ? $"Elegí un objetivo para {pendingCast?.Name}: lo que está resaltado en naranja. Esc cancela."
            : IsDeclaringAttackers ? "Tocá las criaturas que atacan (las verdes pueden) y confirmá el ataque con Espacio."
            : IsDeclaringBlockers ? "Te atacan. Tocá una criatura tuya en verde y después al atacante en naranja que bloquea. Confirmá con Espacio."
            : g.Stack.Count > 0 && g.Stack[^1].Controller == Duel.Enemy ? $"El rival lanzó {g.Stack[^1].Card.Name}. Podés responder con un instantáneo o dejar que se resuelva."
            : g.ActivePlayer == Duel.Enemy ? "Es el turno del rival. Podés lanzar un instantáneo antes de seguir."
            : g.Step == Step.DeclareBlockers ? "Ya están los bloqueos. Es tu momento para un truco antes del daño."
            : "Las cartas en verde se pueden jugar ahora. Las tierras se giran solas al lanzar.";
    }

    void Fill(ObservableCollection<BoardCardViewModel> target, IEnumerable<CardInstance> cards, CardPlace place, Func<CardInstance, string> note)
    {
        target.Clear();
        foreach (var c in cards) target.Add(new BoardCardViewModel(c, place, OnCardClicked, note(c)));
    }

    /// <summary>Resalta lo que se puede tocar ahora: cartas jugables, atacantes o bloqueadores posibles u objetivos válidos.</summary>
    void Highlight(Game g)
    {
        const int me = Duel.Human;
        YouTargetable = EnemyTargetable = false;
        if (IsOver) return;
        if (IsTargeting && pendingCast is { } spell)
        {
            var def = spell.Definition;
            foreach (var c in YourCreatures.Concat(EnemyCreatures))
                c.Targetable = g.IsLegalTarget(def, Target.OfCard(c.Card.Id), me);
            YouTargetable = g.IsLegalTarget(def, Target.OfPlayer(me), me);
            EnemyTargetable = g.IsLegalTarget(def, Target.OfPlayer(Duel.Enemy), me);
            foreach (var c in Hand) c.Selected = c.Card.Id == spell.Id;
            return;
        }
        if (IsDeclaringAttackers)
        {
            foreach (var c in YourCreatures)
            {
                c.Playable = c.Card.CanTapToAttack && !c.Card.Has(Keyword.Defender);
                c.Selected = selectedAttackers.Contains(c.Card.Id);
            }
            return;
        }
        if (IsDeclaringBlockers)
        {
            var attackers = g.Combat.Select(a => a.AttackerId).ToHashSet();
            foreach (var c in YourCreatures)
            {
                c.Selected = c.Card.Id == selectedBlocker || blocks.ContainsKey(c.Card.Id);
                c.Playable = !c.Selected && !c.Card.Tapped;
            }
            if (selectedBlocker is { } blocker && g.FindOnBattlefield(blocker) is { } b)
                foreach (var c in EnemyCreatures)
                    c.Targetable = attackers.Contains(c.Card.Id)
                        && (!c.Card.Has(Keyword.Flying) || b.Has(Keyword.Flying) || b.Has(Keyword.Reach));
            return;
        }
        foreach (var c in Hand) c.Playable = g.CanPlayNow(me, c.Card);
    }

    static string DescribeTarget(Game g, Target t) =>
        t.IsPlayer ? (t.Player == Duel.Human ? "vos" : g.Players[t.Player!.Value].Name) : g.FindOnBattlefield(t.Card!.Value)?.Name ?? "(se fue)";
}
