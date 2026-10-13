using System.Text.Json;

namespace MtgEngine.Roguelike;

public enum Rarity { Common, Uncommon, Rare }

public enum EnemyKind { Normal, Elite, Boss }

public enum RelicEffect { StartLand, ExtraCard, StartSoldier, EnemyLife, MaxHp, HealAfterWin, ExtraChoice, RestHeal }

/// <summary>Un rival de la run: su mazo (sin tierras, se agregan solas), sus vidas y, si es jefe, su comandante.</summary>
public sealed class Enemy
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public EnemyKind Kind { get; init; }
    /// <summary>Para los jefes: en qué acto aparecen.</summary>
    public int Act { get; init; }
    public int Life { get; init; }
    public string? Commander { get; init; }
    public List<string> Deck { get; init; } = [];
    public string Text { get; init; } = "";
}

/// <summary>Una ventaja permanente para el resto de la run.</summary>
public sealed class Relic
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public RelicEffect Effect { get; init; }
    public int Amount { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>Los datos del roguelike: cartas que se pueden draftear, reliquias y rivales (Resources/roguelike.json).</summary>
public static class RunContent
{
    sealed class Data
    {
        public Dictionary<Rarity, List<string>> Pool { get; init; } = [];
        public List<Relic> Relics { get; init; } = [];
        public List<Enemy> Enemies { get; init; } = [];
    }

    static readonly Lazy<Data> data = new(() =>
    {
        using var stream = typeof(RunContent).Assembly.GetManifestResourceStream("MtgEngine.roguelike.json")!;
        return JsonSerializer.Deserialize<Data>(stream, CardCatalog.JsonOptions)!;
    });

    public static IReadOnlyDictionary<Rarity, List<string>> Pool => data.Value.Pool;
    public static IReadOnlyList<Relic> Relics => data.Value.Relics;
    public static IReadOnlyList<Enemy> Enemies => data.Value.Enemies;

    public static Relic Relic(string id) => Relics.First(r => r.Id == id);
    public static Enemy Enemy(string id) => Enemies.First(e => e.Id == id);

    public static Rarity RarityOf(string cardId) =>
        Pool.FirstOrDefault(p => p.Value.Contains(cardId)).Key;
}
