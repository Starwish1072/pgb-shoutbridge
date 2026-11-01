using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;

namespace PgbShoutBridge;

public class ShoutBroadcastActionSettingsControl : ActionSettingsControlBase<ShoutBroadcastActionSettings>
{
    private readonly TextBox _sender = new() { Width = 200, Watermark = "发送人（如：教务处）" };
    private readonly TextBox _text = new() { Width = 420, Watermark = "喊话内容", AcceptsReturn = true, Height = 80, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public ShoutBroadcastActionSettingsControl()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "向教室大屏发送一条批改邦喊话（走 ClassIsland 提醒）", FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row1.Children.Add(new TextBlock { Text = "发送人", Width = 60, VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_sender);
        panel.Children.Add(row1);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row2.Children.Add(new TextBlock { Text = "内容", Width = 60, VerticalAlignment = VerticalAlignment.Top });
        row2.Children.Add(_text);
        panel.Children.Add(row2);
        Content = panel;

        _sender.Text = Settings?.Sender ?? "老师";
        _text.Text = Settings?.Text ?? "";
        _sender.TextChanged += (_, _) => { if (Settings != null) Settings.Sender = _sender.Text ?? ""; };
        _text.TextChanged += (_, _) => { if (Settings != null) Settings.Text = _text.Text ?? ""; };
    }
}
