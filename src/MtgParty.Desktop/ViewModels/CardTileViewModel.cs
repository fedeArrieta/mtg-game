using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgEngine;
using MtgParty.Desktop.Services;
using MediaColor = Avalonia.Media.Color;

namespace MtgParty.Desktop.ViewModels;

/// <summary>
/// Una carta tal como se dibuja: la imagen real si está, o un marco con el texto si no.
/// Encima van los indicadores (girada, daño, fuerza y resistencia modificadas, si ataca o bloquea).
/// </summary>
public partial class CardTileViewModel : ObservableObject
{
    public CardTileViewModel(CardDefinition definition)
    {
        Definition = definition;
        image = CardImages.Ready(definition);
        if (image is null && !string.IsNullOrEmpty(definition.EnglishName)) _ = LoadImageAsync();
    }

    public CardDefinition Definition { get; }

    public virtual string Name => Definition.Name;
    public virtual string Cost => Definition.Type == CardType.Land ? "" : Definition.ManaCost.ToString();
    public string TypeLine => string.IsNullOrEmpty(Definition.TypeLine)
        ? Definition.Type switch
        {
            CardType.Creature => "Criatura",
            CardType.Instant => "Instantáneo",
            CardType.Sorcery => "Conjuro",
            _ => "Tierra",
        }
        : Definition.TypeLine;
    public string Text => Definition.Text;
    public virtual int Power => Definition.Power;
    public virtual int Toughness => Definition.Toughness;
    public bool IsCreature => Definition.Type == CardType.Creature;
    public string Stats => IsCreature ? $"{Power}/{Toughness}" : "";
    public virtual int Damage => 0;
    public virtual bool Tapped => false;
    /// <summary>Una etiqueta arriba de la carta: "Ataca", "Bloquea", "Mareada"…</summary>
    public virtual string Badge => "";
    /// <summary>Un número abajo a la izquierda (cuántas copias, por ejemplo).</summary>
    public virtual string Corner => "";

    /// <summary>La fuerza/resistencia se muestra encima de la imagen solo si cambió o si la criatura tiene daño.</summary>
    public bool ShowStatsOverlay => IsCreature && (!HasImage || Power != Definition.Power || Toughness != Definition.Toughness || Damage > 0);
    public bool StatsBoosted => Power > Definition.Power || Toughness > Definition.Toughness;
    public bool StatsReduced => Power < Definition.Power || Toughness < Definition.Toughness;
    public bool HasDamage => Damage > 0;
    public string DamageText => Damage > 0 ? $"−{Damage}" : "";
    public bool HasBadge => Badge.Length > 0;
    public bool HasCorner => Corner.Length > 0;
    public RotateTransform? TapTransform => Tapped ? new RotateTransform(90) : null;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImage), nameof(ShowStatsOverlay))] Bitmap? image;
    public bool HasImage => Image is not null;

    /// <summary>Elegida (atacante marcado, bloqueador elegido…).</summary>
    [ObservableProperty] bool selected;
    /// <summary>Se puede jugar o usar ahora.</summary>
    [ObservableProperty] bool playable;
    /// <summary>Es un objetivo válido para el hechizo que estás lanzando.</summary>
    [ObservableProperty] bool targetable;

    /// <summary>El color del marco cuando no hay imagen.</summary>
    public IBrush FrameBrush => new SolidColorBrush(FrameColor(Definition));

    public static MediaColor FrameColor(CardDefinition d)
    {
        List<MtgEngine.Color> colors = d.Type == CardType.Land && d.Produces is { } p ? [p] : [.. d.ManaCost.Colors];
        if (colors.Count > 1) return MediaColor.Parse("#E6D28C");
        return colors.FirstOrDefault(MtgEngine.Color.Colorless) switch
        {
            MtgEngine.Color.White => MediaColor.Parse("#F3EEDA"),
            MtgEngine.Color.Blue => MediaColor.Parse("#BCD5EC"),
            MtgEngine.Color.Black => MediaColor.Parse("#C3B9B6"),
            MtgEngine.Color.Red => MediaColor.Parse("#EFC0A4"),
            MtgEngine.Color.Green => MediaColor.Parse("#C2DCBC"),
            _ => MediaColor.Parse("#D8D6D2"),
        };
    }

    async Task LoadImageAsync()
    {
        var bitmap = await CardImages.GetAsync(Definition);
        if (bitmap is not null) Dispatcher.UIThread.Post(() => Image = bitmap);
    }

    [RelayCommand]
    protected virtual void Click() { }
}

/// <summary>Una carta en una partida (puzzle o duelo): mano, campo de batalla o zona de mando.</summary>
public partial class BoardCardViewModel(CardInstance card, CardPlace place, Action<BoardCardViewModel> onClick, string note = "", ManaCost? cost = null)
    : CardTileViewModel(card.Definition)
{
    public CardInstance Card { get; } = card;
    public CardPlace Place { get; } = place;

    public override string Name => Card.Name;
    public override string Cost => Card.IsLand ? "" : (cost ?? Card.Definition.ManaCost).ToString();
    public override int Power => Card.Power;
    public override int Toughness => Card.Toughness;
    public override int Damage => Card.Damage;
    public override bool Tapped => Card.Tapped && Place != CardPlace.Hand;
    public bool OnBattlefield => Place is not (CardPlace.Hand or CardPlace.Command);
    public override string Badge =>
        note.Length > 0 ? note
        : Card.IsCreature && OnBattlefield && Card.SummoningSick && !Card.Has(Keyword.Haste) ? "Mareada"
        : "";
    public override string Corner => Place == CardPlace.Command ? Cost : "";

    protected override void Click() => onClick(this);
}

public enum CardPlace { Hand, YourLands, YourCreatures, EnemyLands, EnemyCreatures, Command }

/// <summary>Un hechizo en la pila: la carta y a qué apunta.</summary>
public sealed record StackEntry(CardTileViewModel Card, string Text);
