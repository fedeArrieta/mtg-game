using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MtgParty.Desktop.Services;

/// <summary>Las preferencias de la interfaz (tamaño de las cartas, idioma de las imágenes), guardadas entre sesiones.</summary>
public partial class UiSettings : ObservableObject
{
    public const double MinCardWidth = 80, MaxCardWidth = 220;
    /// <summary>Alto sobre ancho de una carta de Magic (63 × 88 mm).</summary>
    public const double CardRatio = 88.0 / 63.0;

    public static UiSettings Instance { get; } = Load();

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MesaDelCaos", "interfaz.json");

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CardHeight))] double cardWidth = 120;
    public double CardHeight => Math.Round(CardWidth * CardRatio) + 12;
    [ObservableProperty] bool spanishImages = true;

    partial void OnCardWidthChanged(double value) => Save();
    partial void OnSpanishImagesChanged(bool value) => Save();

    sealed record Stored(double CardWidth, bool SpanishImages);

    static UiSettings Load()
    {
        var settings = new UiSettings { loading = true };
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath)) is { } s)
            {
                settings.CardWidth = Math.Clamp(s.CardWidth, MinCardWidth, MaxCardWidth);
                settings.SpanishImages = s.SpanishImages;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        settings.loading = false;
        return settings;
    }

    bool loading;

    void Save()
    {
        if (loading) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored(CardWidth, SpanishImages)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se pierde la preferencia, nada más */ }
    }
}
