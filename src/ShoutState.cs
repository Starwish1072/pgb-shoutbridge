using System;

namespace PgbShoutBridge;

/// <summary>喊话状态：用于规则集中「正在接收批改邦喊话」的判定。</summary>
public static class ShoutState
{
    public static DateTime LastShoutAt = DateTime.MinValue;
    public static int WindowSeconds = 30;
    public static string LastText = "";
    public static string LastSender = "";

    public static event EventHandler ShoutReceived;
    private static DateTime _lastRaise = DateTime.MinValue;
    private static string _lastRaisedText = "";

    public static void Raise(string sender, string text)
    {
        LastShoutAt = DateTime.Now; LastSender = sender ?? ""; LastText = text ?? "";
        if (text == _lastRaisedText && (DateTime.Now - _lastRaise).TotalMilliseconds < 800) return;
        _lastRaise = DateTime.Now; _lastRaisedText = text ?? "";
        try { ShoutReceived?.Invoke(null, EventArgs.Empty); } catch { }
    }

    public static bool IsReceiving => LastShoutAt != DateTime.MinValue && (DateTime.Now - LastShoutAt).TotalSeconds < WindowSeconds;
}
