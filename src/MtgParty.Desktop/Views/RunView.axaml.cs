using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using MtgParty.Desktop.ViewModels;

namespace MtgParty.Desktop.Views;

public partial class RunView : UserControl
{
    const double FloorHeight = 78, MapWidth = 640, NodeWidth = 104, NodeHeight = 46;
    RunViewModel? subscribed;

    public RunView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (subscribed is not null) subscribed.MapChanged -= DrawMap;
        subscribed = DataContext as RunViewModel;
        if (subscribed is not null) subscribed.MapChanged += DrawMap;
        DrawMap();
    }

    /// <summary>Dibuja el mapa: el piso 0 abajo, el jefe arriba, con líneas entre las salas conectadas.</summary>
    void DrawMap()
    {
        var canvas = this.FindControl<Canvas>("MapCanvas")!;
        canvas.Children.Clear();
        if (subscribed is null || subscribed.MapNodes.Count == 0) return;
        var nodes = subscribed.MapNodes.ToDictionary(n => n.Node.Id);
        int floors = nodes.Values.Max(n => n.Node.Floor) + 1;
        canvas.Width = MapWidth;
        canvas.Height = floors * FloorHeight;

        Point Center(MapNodeViewModel n) =>
            new(n.Node.X * MapWidth, (floors - 1 - n.Node.Floor) * FloorHeight + FloorHeight / 2);

        var line = this.FindResource("LineBrush") as IBrush ?? Brushes.Gray;
        var gold = this.FindResource("GoldBrush") as IBrush ?? Brushes.Goldenrod;
        var table = this.FindResource("TableBrush") as IBrush ?? Brushes.Black;
        foreach (var n in nodes.Values)
            foreach (var next in n.Node.Next)
            {
                bool path = n.Current && nodes[next].Available;
                canvas.Children.Add(new Line
                {
                    StartPoint = Center(n),
                    EndPoint = Center(nodes[next]),
                    Stroke = path ? gold : line,
                    StrokeThickness = path ? 3 : 2,
                });
            }

        foreach (var n in nodes.Values)
        {
            var button = new Button
            {
                Width = NodeWidth,
                Height = NodeHeight,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled = n.Available,
                Opacity = n.Available || n.Current ? 1 : n.Node.Visited ? 0.45 : 0.8,
                BorderBrush = n.Current ? gold : null,
                BorderThickness = new Thickness(n.Current ? 3 : 0),
                Content = new TextBlock { Text = $"{n.Icon}  {n.Label}", FontWeight = n.Available ? FontWeight.Bold : FontWeight.Normal },
            };
            if (n.Available) button.Classes.Add("primary");
            ToolTip.SetTip(button, n.Tip);
            var node = n.Node;
            button.Click += (_, _) => subscribed?.EnterNode(node);
            // Un fondo opaco para que las líneas no se vean a través de las salas deshabilitadas.
            var slot = new Border { Background = table, CornerRadius = new CornerRadius(4), Child = button };
            var c = Center(n);
            Canvas.SetLeft(slot, Math.Clamp(c.X - NodeWidth / 2, 0, MapWidth - NodeWidth));
            Canvas.SetTop(slot, c.Y - NodeHeight / 2);
            canvas.Children.Add(slot);
        }
    }
}
