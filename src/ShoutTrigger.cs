using System;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace PgbShoutBridge;

[TriggerInfo("pgb.shoutbridge.shout-received", "收到批改邦喊话", "\uE7ED")]
public class ShoutTrigger : TriggerBase
{
    public override void Loaded() => ShoutState.ShoutReceived += OnShout;
    public override void UnLoaded() => ShoutState.ShoutReceived -= OnShout;
    private void OnShout(object sender, EventArgs e)
    {
        try { Trigger(); } catch { }
    }
}
