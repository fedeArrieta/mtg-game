using System.Text;

namespace MtgEngine;

public enum Color { White, Blue, Black, Red, Green, Colorless }

public static class Colors
{
    public const int Count = 6;
    static readonly char[] Symbols = ['W', 'U', 'B', 'R', 'G', 'C'];

    public static char Symbol(this Color c) => Symbols[(int)c];

    public static Color FromSymbol(char s) =>
        Array.IndexOf(Symbols, char.ToUpperInvariant(s)) is var i and >= 0
            ? (Color)i
            : throw new FormatException($"Símbolo de maná desconocido: {s}");
}

/// <summary>Coste de maná, escrito como "{2}{R}{R}". {C} pide maná incoloro específico.</summary>
public sealed class ManaCost
{
    public static readonly ManaCost Zero = new(0, new int[MtgEngine.Colors.Count]);

    public int Generic { get; }
    readonly int[] colored;

    ManaCost(int generic, int[] colored) { Generic = generic; this.colored = colored; }

    public int this[Color c] => colored[(int)c];
    public int ManaValue => Generic + colored.Sum();

    public IEnumerable<Color> Colors =>
        Enum.GetValues<Color>().Where(c => c != Color.Colorless && colored[(int)c] > 0);

    public static ManaCost Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Zero;
        int generic = 0;
        var colored = new int[MtgEngine.Colors.Count];
        foreach (var symbol in text.Split('{', StringSplitOptions.RemoveEmptyEntries))
        {
            var s = symbol.TrimEnd('}').Trim();
            if (int.TryParse(s, out var n)) generic += n;
            else if (s.Length == 1) colored[(int)MtgEngine.Colors.FromSymbol(s[0])]++;
            else throw new FormatException($"Coste de maná inválido: {text}");
        }
        return new ManaCost(generic, colored);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Generic > 0 || ManaValue == 0) sb.Append('{').Append(Generic).Append('}');
        foreach (var c in Enum.GetValues<Color>())
            for (int i = 0; i < colored[(int)c]; i++) sb.Append('{').Append(c.Symbol()).Append('}');
        return sb.ToString();
    }
}

/// <summary>Maná flotante de un jugador. Se vacía al terminar cada paso.</summary>
public sealed class ManaPool
{
    readonly int[] amounts = new int[Colors.Count];

    public int this[Color c] => amounts[(int)c];
    public int Total => amounts.Sum();

    public void Add(Color c, int amount = 1) => amounts[(int)c] += amount;
    public void Clear() => Array.Clear(amounts);

    public bool CanPay(ManaCost cost) => TryPay(cost, commit: false);
    public bool Pay(ManaCost cost) => TryPay(cost, commit: true);

    bool TryPay(ManaCost cost, bool commit)
    {
        var left = (int[])amounts.Clone();
        foreach (var c in Enum.GetValues<Color>())
        {
            left[(int)c] -= cost[c];
            if (left[(int)c] < 0) return false;
        }
        // El genérico se paga primero con incoloro y después con el color que más sobra.
        for (int g = cost.Generic; g > 0; g--)
        {
            var from = left[(int)Color.Colorless] > 0
                ? (int)Color.Colorless
                : Array.IndexOf(left, left.Max());
            if (left[from] == 0) return false;
            left[from]--;
        }
        if (commit) left.CopyTo(amounts, 0);
        return true;
    }

    internal ManaPool Clone()
    {
        var p = new ManaPool();
        amounts.CopyTo(p.amounts, 0);
        return p;
    }

    public override string ToString() =>
        Total == 0 ? "vacío" : string.Concat(Enum.GetValues<Color>().SelectMany(c => Enumerable.Repeat($"{{{c.Symbol()}}}", this[c])));
}
