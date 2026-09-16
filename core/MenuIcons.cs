using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AiHot;
internal static class MenuIcons
{
    internal static FrameworkElement Create(string key, bool danger = false)
    {
        string data = key switch
        {
            "≡" => "M4,3 L20,3 20,21 4,21 Z M8,8 L16,8 M8,12 L16,12 M8,16 L13,16",
            "Ⅱ" => "M7,5 L7,19 M17,5 L17,19",
            "▶" => "M7,4 L20,12 7,20 Z",
            "→" => "M4,12 L19,12 M13,6 L19,12 13,18",
            "◇" => "M8,3 L16,3 M9,3 L9,9 5,14 19,14 15,9 15,3 M12,14 L12,22",
            "⚙" => "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M9,15 L9,21",
            "left" => "M4,3 L4,21 M8,6 L20,6 M8,12 L16,12 M8,18 L20,18",
            "center" => "M12,2 L12,22 M4,6 L20,6 M7,12 L17,12 M4,18 L20,18",
            "right" => "M20,3 L20,21 M4,6 L16,6 M8,12 L16,12 M4,18 L16,18",
            "−" => "M4,4 L20,4 20,20 4,20 Z M8,15 L16,15",
            "◐" => "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,3 L12,21",
            _ => "M7,7 L17,17 M17,7 L7,17"
        };
        var canvas = new Canvas { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        canvas.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(data), Stroke = danger ? Ui.Brush("#E8A29E") : Ui.Mint, StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        return new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = Ui.Brush(danger ? "#30282E" : "#203A3B"), Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }
}
