using Avalonia.Controls;
using Avalonia.Interactivity;
using MtgParty.Desktop.ViewModels;

namespace MtgParty.Desktop.Views;

public partial class PuzzleEditorView : UserControl
{
    public PuzzleEditorView() => InitializeComponent();

    PuzzleEditorViewModel Vm => (PuzzleEditorViewModel)DataContext!;

    async void OnOpen(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await PuzzleFiles.OpenAsync(this) is { } puzzle)
            {
                Vm.Load(puzzle);
                Vm.Report($"Abriste \"{puzzle.Title}\".");
            }
        }
        catch (Exception ex)
        {
            Vm.Report($"No se pudo abrir el puzzle: {ex.Message}", error: true);
        }
    }

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        var puzzle = Vm.ToPuzzle();
        if (puzzle.Validate() is { Count: > 0 } errors)
        {
            Vm.Report(string.Join(" ", errors), error: true);
            return;
        }
        try
        {
            if (await PuzzleFiles.SaveAsync(this, puzzle) is { } path)
                Vm.Report($"Guardaste el puzzle en {path}. Mandá ese archivo y se abre desde la pestaña Puzzles.");
        }
        catch (Exception ex)
        {
            Vm.Report($"No se pudo guardar: {ex.Message}", error: true);
        }
    }
}
