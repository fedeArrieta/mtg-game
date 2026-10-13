using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MtgParty.Core;

namespace MtgParty.Desktop.ViewModels;

public partial class PartyViewModel : ObservableObject
{
    [ObservableProperty] string title = "";
    [ObservableProperty] decimal startingLife = 40;
    [ObservableProperty] bool autoEventEachRound = true;
    [ObservableProperty] string status = "";
    [ObservableProperty] bool statusIsError;

    public ObservableCollection<PlayerViewModel> Players { get; } = [];
    public ObservableCollection<ChallengeViewModel> Challenges { get; } = [];
    public ObservableCollection<MissionViewModel> Missions { get; } = [];
    public ObservableCollection<PlaneViewModel> Planes { get; } = [];

    public string ChallengesSummary => Summary(Challenges.Count(c => c.Enabled), Challenges.Count);
    public string MissionsSummary => Summary(Missions.Count(m => m.Enabled), Missions.Count);
    public string PlanesSummary => Summary(Planes.Count(p => p.Enabled), Planes.Count);
    public bool CanAddPlayer => Players.Count < PartyConfig.MaxPlayers;

    public PartyViewModel()
    {
        Players.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(CanAddPlayer)); AddPlayerCommand.NotifyCanExecuteChanged(); };
        Track(Challenges, nameof(ChallengesSummary));
        Track(Missions, nameof(MissionsSummary));
        Track(Planes, nameof(PlanesSummary));
        Apply(CardLibrary.CreateDefaultConfig());
        Status = "Partida nueva con el contenido de la app. Cambiá lo que quieras y exportá el HTML.";
    }

    static string Summary(int active, int total) => $"{active} de {total} activos";

    void Track<T>(ObservableCollection<T> items, string summary) where T : INotifyPropertyChanged
    {
        void Changed(object? s, PropertyChangedEventArgs e) => OnPropertyChanged(summary);
        items.CollectionChanged += (_, e) =>
        {
            foreach (T i in e.NewItems ?? Array.Empty<T>()) i.PropertyChanged += Changed;
            foreach (T i in e.OldItems ?? Array.Empty<T>()) i.PropertyChanged -= Changed;
            OnPropertyChanged(summary);
        };
    }

    public void Apply(PartyConfig config)
    {
        Title = config.Title;
        StartingLife = config.StartingLife;
        AutoEventEachRound = config.AutoEventEachRound;
        Reset(Players, config.Players.Select(PlayerViewModel.From));
        Reset(Challenges, config.Challenges.Select(ChallengeViewModel.From));
        Reset(Missions, config.Missions.Select(MissionViewModel.From));
        Reset(Planes, config.Planes.Select(PlaneViewModel.From));
    }

    static void Reset<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var i in items) target.Add(i);
    }

    public PartyConfig ToConfig() => new()
    {
        Title = Title,
        StartingLife = (int)StartingLife,
        AutoEventEachRound = AutoEventEachRound,
        Players = Players.Select(p => p.ToModel()).ToList(),
        Challenges = Challenges.Select(c => c.ToModel()).ToList(),
        Missions = Missions.Select(m => m.ToModel()).ToList(),
        Planes = Planes.Select(p => p.ToModel()).ToList(),
    };

    public void Report(string message, bool isError = false) { Status = message; StatusIsError = isError; }

    [RelayCommand(CanExecute = nameof(CanAddPlayer))]
    void AddPlayer()
    {
        var used = Players.Select(p => p.ColorIndex).ToHashSet();
        var color = Enumerable.Range(0, ManaColor.All.Count).FirstOrDefault(i => !used.Contains(i));
        Players.Add(new PlayerViewModel { Name = $"Jugador {Players.Count + 1}", ColorIndex = color });
    }

    [RelayCommand] void RemovePlayer(PlayerViewModel p) => Players.Remove(p);
    [RelayCommand] void AddChallenge() => Challenges.Insert(0, new ChallengeViewModel { Title = "Nuevo desafío" });
    [RelayCommand] void RemoveChallenge(ChallengeViewModel c) => Challenges.Remove(c);
    [RelayCommand] void AddMission() => Missions.Insert(0, new MissionViewModel { Text = "Nueva misión", Reward = "Robá 2 cartas." });
    [RelayCommand] void RemoveMission(MissionViewModel m) => Missions.Remove(m);
    [RelayCommand] void AddPlane() => Planes.Insert(0, new PlaneViewModel { Name = "Nuevo plano" });
    [RelayCommand] void RemovePlane(PlaneViewModel p) => Planes.Remove(p);

    /// <summary>Activa o desactiva una lista entera. El parámetro es "lista:on" o "lista:off".</summary>
    [RelayCommand]
    void SetAll(string target)
    {
        var on = target.EndsWith(":on");
        switch (target.Split(':')[0])
        {
            case "challenges": foreach (var c in Challenges) c.Enabled = on; break;
            case "missions": foreach (var m in Missions) m.Enabled = on; break;
            case "planes": foreach (var p in Planes) p.Enabled = on; break;
        }
    }

    [RelayCommand]
    void NewParty()
    {
        Apply(CardLibrary.CreateDefaultConfig());
        Report("Partida nueva con el contenido de la app.");
    }
}
