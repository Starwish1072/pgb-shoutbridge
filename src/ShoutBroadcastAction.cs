using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace PgbShoutBridge;

public class ShoutBroadcastActionSettings
{
    public string Sender { get; set; } = "老师";
    public string Text { get; set; } = "";
    public bool PlaySpeech { get; set; } = true;
}

[ActionInfo("pgb.shoutbridge.broadcast", "批改邦喊话", "\uE7ED", false, "")]
public class ShoutBroadcastAction : ActionBase<ShoutBroadcastActionSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        var s = Settings;
        var text = s?.Text ?? "";
        if (string.IsNullOrWhiteSpace(text)) return;
        var sender = string.IsNullOrWhiteSpace(s?.Sender) ? "老师" : s.Sender;
        ShoutState.LastShoutAt = DateTime.Now;
        ShoutState.LastText = text;
        ShoutState.LastSender = sender;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            try { ShoutNotificationProvider.Current?.ShowShout(sender, text); }
            catch (Exception ex) { ShoutBridgeService.Current?.Log("自动化喊话失败: " + ex.Message); }
        });
    }
}
