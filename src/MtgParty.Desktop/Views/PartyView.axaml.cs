using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MtgParty.Core;
using MtgParty.Desktop.ViewModels;

namespace MtgParty.Desktop.Views;

public partial class PartyView : UserControl
{
    static readonly FilePickerFileType PartyFile = new("Partida de Mesa del Caos") { Patterns = ["*.mtgparty"] };
    static readonly FilePickerFileType HtmlFile = new("Página web") { Patterns = ["*.html"] };

    public PartyView() => InitializeComponent();

    IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)!.StorageProvider;

    PartyViewModel Vm => (PartyViewModel)DataContext!;

    async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Abrir partida", FileTypeFilter = [PartyFile] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            Vm.Apply(PartyJson.Load(path));
            Vm.Report($"Abriste {Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Vm.Report($"No se pudo abrir {Path.GetFileName(path)}: {ex.Message}", isError: true);
        }
    }

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Guardar partida",
            SuggestedFileName = FileName(Vm.Title, "mtgparty"),
            DefaultExtension = "mtgparty",
            FileTypeChoices = [PartyFile],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            PartyJson.Save(Vm.ToConfig(), path);
            Vm.Report($"Guardaste la partida en {path}.");
        }
        catch (Exception ex)
        {
            Vm.Report($"No se pudo guardar: {ex.Message}", isError: true);
        }
    }

    async void OnExport(object? sender, RoutedEventArgs e)
    {
        var config = Vm.ToConfig();
        var errors = HtmlExporter.Validate(config);
        if (errors.Count > 0)
        {
            Vm.Report(string.Join(" ", errors), isError: true);
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Exportar HTML para compartir",
            SuggestedFileName = FileName(config.Title, "html"),
            DefaultExtension = "html",
            FileTypeChoices = [HtmlFile],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        try
        {
            HtmlExporter.ExportToFile(config, path);
        }
        catch (Exception ex)
        {
            Vm.Report($"No se pudo exportar: {ex.Message}", isError: true);
            return;
        }
        Vm.Report($"Listo: {path}. Mandá ese archivo al grupo y ábranlo en el navegador del celular.");
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* Si no hay navegador asociado, el archivo igual quedó guardado. */ }
    }

    static string FileName(string title, string extension)
    {
        var name = string.Concat((string.IsNullOrWhiteSpace(title) ? "Mesa del Caos" : title)
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).Trim();
        return $"{name}.{extension}";
    }
}
