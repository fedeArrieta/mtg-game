using CommunityToolkit.Mvvm.ComponentModel;

namespace MtgParty.Desktop.ViewModels;

/// <summary>La ventana principal: un modo de juego por pestaña.</summary>
public partial class MainWindowViewModel : ObservableObject
{
    public const int PuzzlesTab = 1;

    public RunViewModel Run { get; } = new();
    public PuzzleViewModel Puzzles { get; }
    public PuzzleEditorViewModel Editor { get; }
    public PartyViewModel Party { get; } = new();

    [ObservableProperty] int selectedTab;

    public MainWindowViewModel()
    {
        Puzzles = new PuzzleViewModel();
        Editor = new PuzzleEditorViewModel(Puzzles);
        Editor.PlayRequested += () => SelectedTab = PuzzlesTab;
    }
}
