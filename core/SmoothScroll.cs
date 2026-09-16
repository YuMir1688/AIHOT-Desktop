using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AiHot;
// Subscribe to rendering only while moving; direct manipulation always takes priority.
internal sealed class SmoothScroll
{
    private readonly ScrollViewer viewer;
    private readonly Stopwatch clock = new();
    private bool active;
    private double target, position;
    internal SmoothScroll(ScrollViewer viewer)
    {
        this.viewer = viewer;
        viewer.CanContentScroll = false;
        viewer.PanningMode = PanningMode.VerticalOnly;
        viewer.PreviewMouseWheel += Wheel;
        viewer.PreviewMouseDown += (_, _) => Cancel();
        viewer.PreviewKeyDown += (_, _) => Cancel();
        viewer.PreviewTouchDown += (_, _) => Cancel();
        viewer.Unloaded += (_, _) => Cancel();
        viewer.ScrollChanged += (_, e) => { if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) Cancel(); };
    }
    internal static double Next(double current, double destination, double seconds)
        => Math.Abs(destination - current) < .35 ? destination : current + (destination - current) * (1 - Math.Exp(-18 * Math.Clamp(seconds, 0, .05)));
    internal void Cancel()
    {
        CompositionTarget.Rendering -= Frame;
        active = false; clock.Reset();
    }
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || SystemParameters.WheelScrollLines == 0) return;
        e.Handled = true;
        if (!active) position = target = viewer.VerticalOffset;
        double distance = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight * .85 : SystemParameters.WheelScrollLines * 18;
        double movement = -e.Delta / 120.0 * distance;
        // Reversing the wheel stops the old direction immediately.
        if (Math.Sign(movement) != Math.Sign(target - position)) target = position;
        target = Math.Clamp(target + movement, 0, viewer.ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation) { Cancel(); viewer.ScrollToVerticalOffset(target); return; }
        if (active) return;
        active = true; clock.Restart(); CompositionTarget.Rendering += Frame;
    }
    private void Frame(object? sender, EventArgs e)
    {
        target = Math.Clamp(target, 0, viewer.ScrollableHeight);
        position = Next(position, target, clock.Elapsed.TotalSeconds); clock.Restart();
        viewer.ScrollToVerticalOffset(position);
        if (Math.Abs(target - position) < .35) { viewer.ScrollToVerticalOffset(target); Cancel(); }
    }
}
