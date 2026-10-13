using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MtgParty.Desktop.Views;

/// <summary>
/// Los atajos de teclado del tablero (Espacio, Esc, Ctrl+Z) necesitan que el foco quede en la vista:
/// se lo devuelve al mostrarse y después de cada clic.
/// </summary>
static class BoardFocus
{
    public static void Attach(UserControl view)
    {
        view.AttachedToVisualTree += (_, _) => view.Focus();
        view.DataContextChanged += (_, _) => view.Focus();
        view.AddHandler(InputElement.PointerReleasedEvent, (_, _) => view.Focus(), RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
