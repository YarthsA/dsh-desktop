using System.Windows;
using System.Windows.Media.Animation;

namespace DshDesktop;

public partial class SplashWindow : Window
{
    private static readonly TimeSpan PulsePeriod = TimeSpan.FromMilliseconds(870);

    public SplashWindow()
    {
        InitializeComponent();
        var pulse = new DoubleAnimation(0.35, 1.0, PulsePeriod)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Logo.BeginAnimation(OpacityProperty, pulse);

        var glow = new DoubleAnimation(0.45, 0.95, PulsePeriod)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Glow.BeginAnimation(OpacityProperty, glow);
    }
}
