using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MtgEngine.Puzzles;

namespace MtgParty.Desktop.Views;

/// <summary>Abrir y guardar puzzles como archivos .mtgpuzzle (JSON).</summary>
static class PuzzleFiles
{
    static readonly FilePickerFileType Type = new("Puzzle de Mesa del Caos") { Patterns = ["*.mtgpuzzle"] };

    public static async Task<Puzzle?> OpenAsync(Control owner)
    {
        var files = await TopLevel.GetTopLevel(owner)!.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Abrir puzzle",
            FileTypeFilter = [Type],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return null;
        var puzzle = PuzzleLibrary.Deserialize(await File.ReadAllTextAsync(path));
        if (puzzle.Validate() is { Count: > 0 } errors) throw new InvalidDataException(string.Join(" ", errors));
        return puzzle;
    }

    /// <summary>Devuelve la ruta donde se guardó, o null si se canceló.</summary>
    public static async Task<string?> SaveAsync(Control owner, Puzzle puzzle)
    {
        var file = await TopLevel.GetTopLevel(owner)!.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Guardar puzzle",
            SuggestedFileName = $"{puzzle.Id}.mtgpuzzle",
            DefaultExtension = "mtgpuzzle",
            FileTypeChoices = [Type],
        });
        if (file?.TryGetLocalPath() is not { } path) return null;
        await File.WriteAllTextAsync(path, PuzzleLibrary.Serialize(puzzle));
        return path;
    }
}
