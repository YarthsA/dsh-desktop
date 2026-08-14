using System.Windows;
using System.Windows.Media.Animation;

namespace DshDesktop;

public partial class SplashWindow : Window
{
    private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(1250);

    public SplashWindow()
    {
        InitializeComponent();
        var pulse = new DoubleAnimation(0.35, 1.0, PulseDuration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Logo.BeginAnimation(OpacityProperty, pulse);

        var glow = new DoubleAnimation(0.5, 0.75, PulseDuration)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Glow.BeginAnimation(OpacityProperty, glow);
    }
}
