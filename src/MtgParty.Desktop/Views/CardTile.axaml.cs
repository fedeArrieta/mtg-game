using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using MtgParty.Desktop.Services;

namespace MtgParty.Desktop.Views;

/// <summary>Una carta dibujada. Su tamaño sale del control deslizante de la interfaz, multiplicado por <see cref="Scale"/>.</summary>
public partial class CardTile : UserControl
{
    public static readonly StyledProperty<double> ScaleProperty = AvaloniaProperty.Register<CardTile, double>(nameof(Scale), 1.0);

    /// <summary>Más chica para las tierras, más grande para elegir cartas en el draft.</summary>
    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public CardTile()
    {
        InitializeComponent();
        Resize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScaleProperty) Resize();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UiSettings.Instance.PropertyChanged += OnSettingsChanged;
        Resize();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UiSettings.Instance.PropertyChanged -= OnSettingsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UiSettings.CardWidth)) Resize();
    }

    void Resize()
    {
        var width = Math.Round(UiSettings.Instance.CardWidth * Scale);
        Box.Width = width;
        Box.Height = Math.Round(width * UiSettings.CardRatio);
    }
}
