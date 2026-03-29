using System.Windows;
using System.Windows.Media.Animation;

namespace Shelly.Animations;

/// <summary>
/// Easing function with configurable overshoot, creating an organic
/// "magnification" feel when used with EasingMode.EaseOut.
/// The animation slightly exceeds the target then settles back.
/// </summary>
public class SpringEase : EasingFunctionBase
{
    /// <summary>How far past the target the animation overshoots (0.07 = 7%).</summary>
    public double Overshoot { get; set; } = 0.07;

    protected override double EaseInCore(double t)
    {
        // Cubic with pull-back: f(t) = t^2 * ((s+1)*t - s)
        // When WPF applies EaseOut inversion (1 - f(1-t)), this produces
        // a smooth overshoot-then-settle curve. The peak overshoot roughly
        // matches the Overshoot property value.
        double s = Overshoot * 20.0;
        return t * t * ((s + 1.0) * t - s);
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
