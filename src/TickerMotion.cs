using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
namespace YuMir.Cards;
public static class TickerMotion
{
    public static void Tick(object window, double delta)
    {
        var type = window.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Get(string name) => type.GetField(name, flags)!.GetValue(window)!;
        var headline = (TextBlock)Get("Headline");
        var track = (System.Windows.FrameworkElement)Get("Track");
        var shift = (TranslateTransform)Get("Shift");
        double time = (double)Get("dwell") + Math.Clamp(delta, 0, .1);
        type.GetField("dwell", flags)!.SetValue(window, time);
        double distance = Math.Max(0, headline.ActualWidth - track.ActualWidth);
        if (distance < 1)
        {
            shift.X = 0;
            if (time > Math.Clamp(headline.Text.Length * .22, 6, 12)) type.GetMethod("Advance", flags)!.Invoke(window, null);
            return;
        }
        var settings = Get("settings");
        var speedProperty = settings.GetType().GetProperty("Speed", flags);
        double speed = Math.Clamp(Convert.ToDouble(speedProperty != null ? speedProperty.GetValue(settings) : settings.GetType().GetField("Speed", flags)!.GetValue(settings)), 12, 48);
        // A constant-speed middle section with smooth acceleration and deceleration.
        const double ramp = .8;
        double duration = Math.Max(1.6, distance / speed + ramp);
        double t = Math.Clamp(time - 2, 0, duration);
        double velocity = distance / (duration - ramp);
        double offset = t < ramp ? velocity * (t*t/(2*ramp) - ramp/(4*Math.PI*Math.PI)*(1-Math.Cos(2*Math.PI*t/ramp))) :
            t > duration-ramp ? distance - velocity * ((duration-t)*(duration-t)/(2*ramp) - ramp/(4*Math.PI*Math.PI)*(1-Math.Cos(2*Math.PI*(duration-t)/ramp))) : velocity*(t-ramp/2);
        shift.X = -Math.Clamp(offset, 0, distance);
        if (time >= duration + 4) type.GetMethod("Advance", flags)!.Invoke(window, null);
    }
}
