using System.Windows;
using System.Windows.Controls;
namespace YuMir.Cards;
public static class ReaderAttribution
{
    public static void Fix(StackPanel detail, ScrollViewer scroll)
    {
        ReaderSidebar.Apply(Window.GetWindow(detail));
        if (scroll.Parent is not DockPanel host) return;
        detail.MaxWidth = 900; detail.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (host.Tag is not string tag || tag != "YuMir.ReaderLayout") { host.Resources.MergedDictionaries.Add(PickerStyles.Create()); host.Tag = "YuMir.ReaderLayout"; }
        var labels = detail.Children.OfType<TextBlock>().Where(t => t.Text == "资讯整理 · AIHOT" || t.Text == "桌面体验策划｜YuMir").ToArray();
        if (labels.Length == 0) return;
        var footer = host.Children.OfType<StackPanel>().FirstOrDefault(p => Equals(p.Tag, "YuMir.FixedAttribution"));
        if (footer == null)
        {
            footer = new StackPanel { Tag = "YuMir.FixedAttribution", Margin = new Thickness(detail.Margin.Left + scroll.Padding.Left + scroll.BorderThickness.Left, 10, detail.Margin.Right, 16), HorizontalAlignment = HorizontalAlignment.Left };
            DockPanel.SetDock(footer, Dock.Bottom);
            host.Children.Insert(host.Children.IndexOf(scroll), footer);
        }
        footer.Children.Clear();
        foreach (var label in labels) { detail.Children.Remove(label); footer.Children.Add(label); }
    }
}
