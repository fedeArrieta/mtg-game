namespace MtgParty.Desktop.ViewModels;

/// <summary>La ventana principal: un modo de juego por pestaña.</summary>
public sealed class MainWindowViewModel
{
    public PuzzleViewModel Puzzles { get; } = new();
    public PartyViewModel Party { get; } = new();
}
