using System;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;

namespace PgbShoutBridge;

[NotificationProviderInfo("6f3c1e2a-9b7d-4c55-9f4e-2a8b1c3d5e60", "批改邦喊话提醒", "收到教室远程喊话时发出提醒。")]
[NotificationChannelInfo("6f3c1e2a-9b7d-4c55-9f4e-2a8b1c3d5e61", "喊话提醒", "收到教室远程喊话时提醒。", typeof(ShoutNotificationProviderControl))]
public class ShoutNotificationProvider : NotificationProviderBase<ShoutNotificationSettings>
{
    public static ShoutNotificationProvider Current;

    public void ShowShout(string sender, string text)
    {
        Current = this;
        ShoutState.Raise(sender, text);
        var seconds = Settings?.DurationSeconds ?? 20;
        var body = string.IsNullOrWhiteSpace(sender) ? text : sender + "：" + text;
        var mask = NotificationContent.CreateSimpleTextContent(body ?? "", null);
        var overlay = NotificationContent.CreateSimpleTextContent(text ?? "", null);
        if (mask != null) mask.Duration = TimeSpan.FromSeconds(seconds);
        if (overlay != null) overlay.Duration = TimeSpan.FromSeconds(seconds);
        ShowNotification(new NotificationRequest
        {
            MaskContent = mask,
            OverlayContent = overlay
        });
    }
}
