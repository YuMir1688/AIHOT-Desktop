using System.Windows.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace YuMir.Cards;
public static class ScrollbarAppearance
{
    private static bool installed;
    public static void Install()
    {
        if (installed) return;
        installed = true;
        EventManager.RegisterClassHandler(typeof(ScrollBar), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Apply((ScrollBar)sender)));
        EventManager.RegisterClassHandler(typeof(Thumb), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => {
            DependencyObject? parent = (Thumb)sender;
            while (parent != null && parent is not ScrollBar) parent = VisualTreeHelper.GetParent(parent);
            if (parent is ScrollBar owner) Apply(owner);
        }));
    }
    private static Track? FindTrack(DependencyObject root)
    {
        if (root is Track track) return track;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var found = FindTrack(VisualTreeHelper.GetChild(root,i)); if (found != null) return found; }
        return null;
    }
    public static void Apply(ScrollBar bar)
    {
        if (!Equals(bar.Tag, "FullCapsuleScrollbar"))
        {
            bar.Tag = "FullCapsuleScrollbar";
            bar.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ScrollBar">
<Grid Background="Transparent" SnapsToDevicePixels="False">
 <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}">
  <Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
  <Track.Thumb><Thumb MinHeight="0" MinWidth="0" Height="Auto" Width="Auto" Margin="0" Padding="0" VerticalAlignment="Stretch" HorizontalAlignment="Stretch"/></Track.Thumb>
  <Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Focusable="False"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
 </Track>
</Grid>
<ControlTemplate.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>
""");
        }
        bar.ApplyTemplate();        var track = bar.Template?.FindName("PART_Track", bar) as Track ?? FindTrack(bar);
        if (track?.Thumb is not Thumb thumb) return;
        if (Equals(thumb.Tag, "UnifiedCapsuleThumb")) return;
        thumb.Tag = "UnifiedCapsuleThumb";
        thumb.Style = null; thumb.MinHeight = 0; thumb.MinWidth = 0; thumb.Height = double.NaN; thumb.Width = double.NaN; thumb.Margin = new Thickness(0);
        var surface = new FrameworkElementFactory(typeof(Capsule));
        surface.SetValue(Capsule.HorizontalProperty, bar.Orientation == System.Windows.Controls.Orientation.Horizontal);
        thumb.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = surface };
        thumb.Opacity = .75;
        thumb.MouseEnter += (_, _) => thumb.Opacity = 1;
        thumb.MouseLeave += (_, _) => thumb.Opacity = .75;
    }
    public sealed class Capsule : FrameworkElement
    {
        public static readonly DependencyProperty HorizontalProperty = DependencyProperty.Register("Horizontal", typeof(bool), typeof(Capsule), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public bool Horizontal { get => (bool)GetValue(HorizontalProperty); set => SetValue(HorizontalProperty, value); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            double thickness = Math.Min(3, Horizontal ? ActualHeight : ActualWidth);
            double length = Math.Max(0, (Horizontal ? ActualWidth : ActualHeight) - 2);
            if (thickness <= 0 || length <= 0) return;
            var rect = Horizontal ? new Rect(1, (ActualHeight-thickness)/2, length, thickness) : new Rect((ActualWidth-thickness)/2, 1, thickness, length);
            double radius = Math.Min(thickness, length)/2;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(96,119,132)), null, rect, radius, radius);
        }
    }
}
