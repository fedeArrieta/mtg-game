using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgEngine;

public enum CardType { Land, Creature, Instant, Sorcery }

public enum Keyword { Flying, Reach, Haste, Vigilance, Trample, Deathtouch, Lifelink, Defender, Hexproof }

/// <summary>Una condición extra para el objetivo de un hechizo.</summary>
public enum TargetRestriction { None, AttackingOrBlocking }

/// <summary>Habilidades estáticas que el motor sabe aplicar.</summary>
public enum StaticAbility
{
    /// <summary>Cuesta {X} menos, donde X es la fuerza total de tus criaturas (Ghalta).</summary>
    CostReducedByTotalPower,
    /// <summary>Tus oponentes no pueden lanzar hechizos durante tu turno (Drómoka).</summary>
    OpponentsCantCastOnYourTurn,
}

/// <summary>Qué puede elegir como objetivo un hechizo al lanzarse.</summary>
public enum TargetKind { None, Any, Creature, Player }

/// <summary>Una carta tal como está impresa. Es inmutable y la comparten todas sus copias.</summary>
public sealed class CardDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>El nombre en inglés, para buscar la imagen de la carta.</summary>
    public string EnglishName { get; init; } = "";
    /// <summary>La línea de tipo impresa, como "Criatura — Oso".</summary>
    public string TypeLine { get; init; } = "";
    public bool Legendary { get; init; }
    public CardType Type { get; init; }
    public string Cost { get; init; } = "";
    public int Power { get; init; }
    public int Toughness { get; init; }
    public List<Keyword> Keywords { get; init; } = [];
    /// <summary>Para tierras: el color de maná que producen al girarse.</summary>
    public Color? Produces { get; init; }
    public TargetKind Target { get; init; }
    public TargetRestriction TargetRestriction { get; init; }
    public List<StaticAbility> Statics { get; init; } = [];
    /// <summary>Para instantáneos y conjuros, lo que hacen al resolverse; para criaturas, lo que hacen al entrar.</summary>
    public List<Effect> Effects { get; init; } = [];
    public string Text { get; init; } = "";

    [JsonIgnore] public ManaCost ManaCost => manaCost ??= ManaCost.Parse(Cost);
    ManaCost? manaCost;

    [JsonIgnore] public bool IsPermanent => Type is CardType.Land or CardType.Creature;
    [JsonIgnore] public bool IsSpell => Type is CardType.Instant or CardType.Sorcery;
}

/// <summary>Las cartas del juego (cartas reales de Magic), cargadas de Resources/cards.json.</summary>
public static class CardCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = true,
    };

    sealed class Data
    {
        public List<CardDefinition> Cards { get; init; } = [];
        /// <summary>Ids viejos (de antes de usar cartas reales) que apuntan a la carta nueva.</summary>
        public Dictionary<string, string> Aliases { get; init; } = [];
    }

    static readonly Lazy<Data> data = new(() =>
    {
        using var stream = typeof(CardCatalog).Assembly.GetManifestResourceStream("MtgEngine.cards.json")!;
        return JsonSerializer.Deserialize<Data>(stream, JsonOptions)!;
    });

    static readonly Lazy<IReadOnlyDictionary<string, CardDefinition>> all = new(() => data.Value.Cards.ToDictionary(c => c.Id));

    public static IReadOnlyDictionary<string, CardDefinition> All => all.Value;

    public static CardDefinition Get(string id) =>
        TryGet(id, out var card) ? card : throw new KeyNotFoundException($"No existe la carta '{id}'.");

    /// <summary>Busca una carta por id, aceptando también los ids viejos.</summary>
    public static bool TryGet(string id, out CardDefinition card)
    {
        if (data.Value.Aliases.TryGetValue(id, out var current)) id = current;
        return All.TryGetValue(id, out card!);
    }

    /// <summary>El id actual de una carta (traduce los ids viejos).</summary>
    public static string Canonical(string id) => data.Value.Aliases.GetValueOrDefault(id, id);
}
