using System.Text.Json;
using System.Text.Json.Serialization;

namespace MtgParty.Core;

/// <summary>Cuánto dura un desafío una vez que se roba.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter))]
public enum ChallengeDuration
{
    /// <summary>Se resuelve en el momento.</summary>
    Now,
    /// <summary>Queda activo hasta que alguien lo cumple.</summary>
    Once,
    /// <summary>Dura hasta que empieza la ronda siguiente.</summary>
    Round,
    /// <summary>Dura toda la partida.</summary>
    Game,
}

internal sealed class CamelCaseEnumConverter() : JsonStringEnumConverter<ChallengeDuration>(JsonNamingPolicy.CamelCase, allowIntegerValues: false);

public sealed class Challenge
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public ChallengeDuration Duration { get; set; } = ChallengeDuration.Once;
    public bool Enabled { get; set; } = true;
}

public sealed class Mission
{
    public string Text { get; set; } = "";
    public string Reward { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class Plane
{
    public string Name { get; set; } = "";
    public string StaticEffect { get; set; } = "";
    public string ChaosEffect { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

/// <summary>Colores de maná, usados para identificar a cada jugador en la mesa.</summary>
public static class ManaColor
{
    public const string White = "w", Blue = "u", Black = "b", Red = "r", Green = "g";
    public static readonly IReadOnlyList<string> All = [White, Blue, Black, Red, Green];
}

public sealed class Player
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = ManaColor.White;
}

/// <summary>Todo lo que define una partida: se guarda como .mtgparty y se exporta a HTML.</summary>
public sealed class PartyConfig
{
    public const int MinPlayers = 2, MaxPlayers = 6;

    public string Title { get; set; } = "Mesa del Caos";
    public int StartingLife { get; set; } = 40;
    public bool AutoEventEachRound { get; set; } = true;
    public List<Player> Players { get; set; } = [];
    public List<Challenge> Challenges { get; set; } = [];
    public List<Mission> Missions { get; set; } = [];
    public List<Plane> Planes { get; set; } = [];
}
