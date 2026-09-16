using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
namespace AiHot;
internal static class Theme
{
    internal static bool IsLight { get; private set; }
    private sealed class Swatch : INotifyPropertyChanged
    {
        public Color Color { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Set(Color color) { Color = color; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Color))); }
    }
    private static readonly Dictionary<string, (Swatch swatch, SolidColorBrush brush)> colors = new();
    internal static SolidColorBrush Brush(string hex)
    {
        hex = hex.ToUpperInvariant();
        if (!colors.TryGetValue(hex, out var entry))
        {
            var swatch = new Swatch(); swatch.Set(Map(hex));
            var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(nameof(Swatch.Color)) { Source = swatch });
            entry = (swatch, brush); colors[hex] = entry;
        }
        return entry.brush;
    }
    private static Color Map(string hex)
    {
        var original = (Color)ColorConverter.ConvertFromString(hex);
        if (!IsLight) return original;
        string? mapped = hex switch
        {
            "#73E5C1" or "#8FF4D2" or "#68CBAE" or "#B3F4DF" => "#087F68",
            "#103B2E" or "#102C26" or "#133A30" => "#FFFFFF",
            "#E8A29E" => "#B44045",
            "#8495A9" or "#637889" or "#8EA6AD" or "#8A9CB0" or "#92A7B9" => "#59717F",
            "#23493F" or "#254A40" or "#1C3538" or "#2A403F" => "#D8EDE5",
            "#1A3031" or "#203A3B" or "#162B2C" => "#E6F3EF",
            _ => null
        };
        if (mapped != null) return (Color)ColorConverter.ConvertFromString(mapped);
        double brightness = (original.R + original.G + original.B) / 3.0;
        string baseColor = brightness < 30 ? "#F3F6F8" : brightness < 55 ? "#FFFFFF" : brightness < 100 ? "#CBD8DE" : brightness < 190 ? "#4C6878" : "#1C303B";
        var result = (Color)ColorConverter.ConvertFromString(baseColor); result.A = original.A; return result;
    }
    internal static void Apply(bool light)
    {
        IsLight = light;
        foreach (string hex in new[] { "#11161F", "#EAF0F7", "#DCE5F0", "#202A38", "#73E5C1", "#EDF3FA", "#182431", "#304053", "#D6E1EC", "#354254", "#A4B3C4", "#407866", "#8FF4D2", "#17232F", "#3A4D5C", "#DDE8F2", "#2A403F", "#30404D", "#68CBAE", "#B3F4DF", "#334756", "#6D8593", "#90B6AF", "#FA1C2D38", "#FA101823", "#FA172330", "#64738C94", "#353E5063", "#133A30", "#EDF7F4", "#8EA6AD", "#344656", "#8A9CB0", "#92A7B9" }) Brush(hex);
        foreach (var pair in colors)
        {
            pair.Value.swatch.Set(Map(pair.Key));
            Application.Current.Resources["ThemeBrush" + pair.Key.Substring(1)] = pair.Value.brush;
            Application.Current.Resources["ThemeColor" + pair.Key.Substring(1)] = Map(pair.Key);
        }
    }
    internal static void Toggle()
    {
        Apply(!IsLight);
        var settings = Storage.Read<Settings>("settings.json"); settings.LightTheme = IsLight; Storage.Save("settings.json", settings);
    }
}
