using CommunityToolkit.Mvvm.ComponentModel;
using MtgParty.Core;

namespace MtgParty.Desktop.ViewModels;

public sealed record Option(string Label);

public partial class PlayerViewModel : ObservableObject
{
    public static IReadOnlyList<Option> ColorOptions { get; } =
        [new("Blanco"), new("Azul"), new("Negro"), new("Rojo"), new("Verde")];

    [ObservableProperty] string name = "";
    [ObservableProperty] int colorIndex;

    public static PlayerViewModel From(Player p) =>
        new() { Name = p.Name, ColorIndex = Math.Max(0, ManaColor.All.ToList().IndexOf(p.Color)) };

    public Player ToModel() => new() { Name = Name, Color = ManaColor.All[Math.Clamp(ColorIndex, 0, ManaColor.All.Count - 1)] };
}

public partial class ChallengeViewModel : ObservableObject
{
    public static IReadOnlyList<Option> DurationOptions { get; } =
        [new("Ahora"), new("Una vez"), new("Esta ronda"), new("Toda la partida")];

    [ObservableProperty] bool enabled = true;
    [ObservableProperty] string title = "";
    [ObservableProperty] string text = "";
    [ObservableProperty] int durationIndex = (int)ChallengeDuration.Once;

    public static ChallengeViewModel From(Challenge c) =>
        new() { Enabled = c.Enabled, Title = c.Title, Text = c.Text, DurationIndex = (int)c.Duration };

    public Challenge ToModel() => new() { Enabled = Enabled, Title = Title, Text = Text, Duration = (ChallengeDuration)DurationIndex };
}

public partial class MissionViewModel : ObservableObject
{
    [ObservableProperty] bool enabled = true;
    [ObservableProperty] string text = "";
    [ObservableProperty] string reward = "";

    public static MissionViewModel From(Mission m) => new() { Enabled = m.Enabled, Text = m.Text, Reward = m.Reward };

    public Mission ToModel() => new() { Enabled = Enabled, Text = Text, Reward = Reward };
}

public partial class PlaneViewModel : ObservableObject
{
    [ObservableProperty] bool enabled = true;
    [ObservableProperty] string name = "";
    [ObservableProperty] string staticEffect = "";
    [ObservableProperty] string chaosEffect = "";

    public static PlaneViewModel From(Plane p) =>
        new() { Enabled = p.Enabled, Name = p.Name, StaticEffect = p.StaticEffect, ChaosEffect = p.ChaosEffect };

    public Plane ToModel() => new() { Enabled = Enabled, Name = Name, StaticEffect = StaticEffect, ChaosEffect = ChaosEffect };
}
