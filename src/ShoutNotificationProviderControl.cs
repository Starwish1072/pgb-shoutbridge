using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;

namespace PgbShoutBridge;

/// <summary>「批改邦喊话提醒」在 提醒 → 高级设置 里的设置控件。</summary>
public class ShoutNotificationProviderControl : NotificationProviderControlBase<ShoutNotificationSettings>
{
    public ShoutNotificationProviderControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        var mask = new CheckBox { Content = "显示遮罩提醒（主界面覆盖层）" };
        var dur = new TextBox { Width = 120 };
        panel.Children.Add(new TextBlock { Text = "批改邦喊话提醒", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        panel.Children.Add(mask);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "提醒时长（秒）", VerticalAlignment = VerticalAlignment.Center },
                dur
            }
        });

        mask.IsChecked = Settings?.ShowMask ?? true;
        dur.Text = (Settings?.DurationSeconds ?? 20).ToString();
        mask.IsCheckedChanged += (_, _) => { if (Settings != null) Settings.ShowMask = mask.IsChecked == true; };
        dur.TextChanged += (_, _) => { if (Settings != null && int.TryParse(dur.Text, out var v) && v >= 3 && v <= 600) Settings.DurationSeconds = v; };

        Content = panel;
    }
}
