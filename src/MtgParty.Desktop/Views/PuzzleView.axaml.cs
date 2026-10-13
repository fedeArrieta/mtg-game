using Avalonia.Controls;
using Avalonia.Interactivity;
using MtgParty.Desktop.ViewModels;

namespace MtgParty.Desktop.Views;

public partial class PuzzleView : UserControl
{
    public PuzzleView() => InitializeComponent();

    async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var vm = (PuzzleViewModel)DataContext!;
        try
        {
            if (await PuzzleFiles.OpenAsync(this) is { } puzzle) vm.AddCustom(puzzle);
        }
        catch (Exception ex)
        {
            vm.ShowError($"No se pudo abrir el puzzle: {ex.Message}");
        }
    }
}
