using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PgbShoutBridge;

/// <summary>按 rany2/edge-tts 的协议直连微软 Edge 朗读服务合成 mp3。</summary>
public static class EdgeTtsClient
{
    private const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string GecVersion = "1-130.0.2849.68";

    public static byte[] Synthesize(string text, string voice, int ratePercent)
    {
        var connId = Guid.NewGuid().ToString("N");
        var gec = Gec();
        var qs = "?TrustedClientToken=" + TrustedToken + "&Sec-MS-GEC=" + gec
               + "&Sec-MS-GEC-Version=" + GecVersion + "&ConnectionId=" + connId;
        var urls = new[] {
            "wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1" + qs,
            "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" + qs
        };

        Exception last = null;
        foreach (var url in urls)
        {
        using var ws = new ClientWebSocket();
        try
        {
            ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            ws.Options.SetRequestHeader("Pragma", "no-cache");
            ws.Options.SetRequestHeader("Cache-Control", "no-cache");
            ws.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0");
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ws.ConnectAsync(new Uri(url), cts.Token).GetAwaiter().GetResult();

            var config = "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
            Send(ws, "X-Timestamp:" + Ts() + "\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" + config);

            var safeText = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            var rate = (ratePercent >= 0 ? "+" : "") + ratePercent + "%";
            var ssml = "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='zh-CN'>"
                     + "<voice name='" + voice + "'><prosody pitch='+0Hz' rate='" + rate + "' volume='+0%'>" + safeText + "</prosody></voice></speak>";
            Send(ws, "X-RequestId:" + Guid.NewGuid().ToString("N") + "\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:" + Ts() + "Z\r\nPath:ssml\r\n\r\n" + ssml);

            var audio = new List<byte>(64 * 1024);
            var buf = new byte[8192];
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && ws.State == WebSocketState.Open)
            {
                var ms = new System.IO.MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).GetAwaiter().GetResult();
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buf, 0, res.Count);
                } while (!res.EndOfMessage);
                if (res.MessageType == WebSocketMessageType.Close) break;

                var frame = ms.ToArray();
                if (frame.Length == 0) continue;
                if (res.MessageType == WebSocketMessageType.Binary && frame.Length > 2)
                {
                    var headerLen = (frame[0] << 8) | frame[1];
                    var header = Encoding.UTF8.GetString(frame, 2, Math.Min(headerLen, frame.Length - 2));
                    if (header.Contains("Path:audio")) audio.AddRange(new ArraySegment<byte>(frame, 2 + headerLen, frame.Length - 2 - headerLen));
                }
                else if (res.MessageType == WebSocketMessageType.Text)
                {
                    var txt = Encoding.UTF8.GetString(frame);
                    if (txt.Contains("Path:turn.end")) break;
                }
            }
            try { ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(2000); } catch { }
            var result = audio.ToArray();
            if (result.Length > 512) return result;
            last = new Exception("音频为空");
        }
        catch (Exception ex) { last = ex; }
        finally { try { ws.Dispose(); } catch { } }
        }
        throw last ?? new Exception("Edge TTS 无响应");
    }

    private static void Send(ClientWebSocket ws, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static string Ts()
    {
        return DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Gec()
    {
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ticks = (unix + 11644473600L) / 300 * 300 * 10_000_000L;
        var sha = SHA256.HashData(Encoding.ASCII.GetBytes(ticks.ToString() + TrustedToken));
        var sb = new StringBuilder(sha.Length * 2);
        foreach (var b in sha) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }
}
