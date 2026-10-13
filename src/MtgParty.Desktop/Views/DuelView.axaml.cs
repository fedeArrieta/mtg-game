using Avalonia.Controls;

namespace MtgParty.Desktop.Views;

public partial class DuelView : UserControl
{
    public DuelView()
    {
        InitializeComponent();
        BoardFocus.Attach(this);
    }
}
