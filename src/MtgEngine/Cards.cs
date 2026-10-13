using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgEngine;

public enum CardType { Land, Creature, Instant, Sorcery }

public enum Keyword { Flying, Reach, Haste, Vigilance, Trample, Deathtouch, Lifelink, Defender }

/// <summary>Qué puede elegir como objetivo un hechizo al lanzarse.</summary>
public enum TargetKind { None, Any, Creature, Player }

/// <summary>Una carta tal como está impresa. Es inmutable y la comparten todas sus copias.</summary>
public sealed class CardDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public CardType Type { get; init; }
    public string Cost { get; init; } = "";
    public int Power { get; init; }
    public int Toughness { get; init; }
    public List<Keyword> Keywords { get; init; } = [];
    /// <summary>Para tierras: el color de maná que producen al girarse.</summary>
    public Color? Produces { get; init; }
    public TargetKind Target { get; init; }
    /// <summary>Para instantáneos y conjuros, lo que hacen al resolverse; para criaturas, lo que hacen al entrar.</summary>
    public List<Effect> Effects { get; init; } = [];
    public string Text { get; init; } = "";

    [JsonIgnore] public ManaCost ManaCost => manaCost ??= ManaCost.Parse(Cost);
    ManaCost? manaCost;

    [JsonIgnore] public bool IsPermanent => Type is CardType.Land or CardType.Creature;
    [JsonIgnore] public bool IsSpell => Type is CardType.Instant or CardType.Sorcery;
}

/// <summary>Las cartas originales del juego, cargadas de Resources/cards.json.</summary>
public static class CardCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    static readonly Lazy<IReadOnlyDictionary<string, CardDefinition>> all = new(() =>
    {
        using var stream = typeof(CardCatalog).Assembly.GetManifestResourceStream("MtgEngine.cards.json")!;
        var cards = JsonSerializer.Deserialize<List<CardDefinition>>(stream, JsonOptions)!;
        return cards.ToDictionary(c => c.Id);
    });

    public static IReadOnlyDictionary<string, CardDefinition> All => all.Value;

    public static CardDefinition Get(string id) =>
        All.TryGetValue(id, out var card) ? card : throw new KeyNotFoundException($"No existe la carta '{id}'.");
}
