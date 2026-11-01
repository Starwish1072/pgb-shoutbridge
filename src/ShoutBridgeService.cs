using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PgbShoutBridge;

public sealed class ShoutRecord
{
    public string Id { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Text { get; set; } = "";
    public string At { get; set; } = "";
    public bool HasImage { get; set; }
    public bool HasVoice { get; set; }
    public string Source { get; set; } = "";
}

public sealed class ShoutBridgeService : IHostedService, IDisposable
{
    public static string PluginConfigDir = "";
    public static ShoutBridgeService Current;

    private readonly IServiceProvider _services;
    private readonly ILogger<ShoutBridgeService> _logger;
    private TcpListener _listener;
    private CancellationTokenSource _cts;
    private ISpeechService _speech;
    private ShoutNotificationProvider _provider;
    private readonly object _logLock = new();
    private readonly object _historyLock = new();
    private readonly List<ShoutRecord> _history = new();

    public int Port = 18123;
    public bool TtsEnabled = true;
    public int DurationSeconds = 25;
    public int MaxTextLength = 600;
    public string TtsPrefix = "";
    public bool UseSystemTts = true;
    public float TtsVolume = 0.9f;
    public int TtsRate = 0;
    public int TtsEngine = 0;          // 0=SAPI 1=Mimo 2=Edge(插件自带)
    public string MimoApiKey = "";
    public string MimoVoice = "alloy";
    public string MimoModel = "mimo-v2.5-tts";
    public float MimoSpeed = 1.0f;
    public string EdgeVoice = "zh-CN-XiaoxiaoNeural";
    public string EdgeApiUrl = "";
    public string AsrApiUrl = "";
    public string AsrToken = "";
    public string PythonPath = "";
    public bool ShowImage = true;
    public bool SoundEnabled = true;
    public float SoundVolume = 0.8f;
    public int HistorySize = 50;
    public long ShoutCount;
    public string LastShout = "";
    public string LastSender = "";
    public DateTime LastAt = DateTime.MinValue;
    public bool HookAttached;
    public DateTime HookAt = DateTime.MinValue;

    public string ConfigPath => Path.Combine(PluginConfigDir ?? ".", "settings.json");
    public string LogPath => Path.Combine(PluginConfigDir ?? ".", "bridge.log");
    public string HistoryPath => Path.Combine(PluginConfigDir ?? ".", "history.json");

    public ShoutBridgeService(IServiceProvider services, ILogger<ShoutBridgeService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            EnsureConfigDir();
            LoadConfig();
            LoadHistory();
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Current = this;
            try
            {
                var hosted = _services.GetService(typeof(System.Collections.Generic.IEnumerable<Microsoft.Extensions.Hosting.IHostedService>)) as System.Collections.Generic.IEnumerable<Microsoft.Extensions.Hosting.IHostedService>;
                _provider = hosted?.OfType<ShoutNotificationProvider>().FirstOrDefault();
                Log(_provider == null ? "未从 DI 取得 ShoutNotificationProvider" : "原生提醒提供方就绪");
            }
            catch (Exception ex) { Log("取得提醒提供方异常: " + ex.Message); }
            ShoutWindow.Log = s => Log(s);
            _ = Task.Run(() => AcceptLoop(_cts.Token));
            _ = Task.Run(async () =>
            {
                await Task.Delay(4000);
                try
                {
                    var actKeys = string.Join(",", ClassIsland.Core.Abstractions.Services.IActionService.ActionInfos.Keys);
                    var ruleKeys = string.Join(",", ClassIsland.Core.Abstractions.Services.IRulesetService.Rules.Keys.Where(k => k.Contains("pgb")).DefaultIfEmpty("(none)"));
                    Log("DIAG Actions(" + ClassIsland.Core.Abstractions.Services.IActionService.ActionInfos.Count + "): " + actKeys);
                    Log("DIAG Rules(pgb): " + ruleKeys);
                }
                catch (Exception ex) { Log("DIAG 失败: " + ex.Message); }
            });
            Log("喊话桥服务已启动 http://127.0.0.1:" + Port + "/  TTS=" + (TtsEnabled ? "on" : "off") + "  Image=" + (ShowImage ? "on" : "off") + "  Sound=" + (SoundEnabled ? "on(" + SoundVolume.ToString("0.00") + ")" : "off") + "  Engine=" + TtsEngine + "  Key=" + (string.IsNullOrEmpty(MimoApiKey) ? "(空)" : MimoApiKey.Length + "字符"));
            _logger.LogInformation("批改邦喊话桥已启动，端口 {Port}", Port);
        }
        catch (Exception ex)
        {
            Log("启动失败: " + ex);
            _logger.LogError(ex, "批改邦喊话桥启动失败");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try { _cts?.Cancel(); _listener?.Stop(); } catch { }
        Log("喊话桥已停止");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); _listener?.Stop(); _cts?.Dispose(); } catch { }
    }

    private void EnsureConfigDir()
    {
        if (string.IsNullOrWhiteSpace(PluginConfigDir))
        {
            PluginConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland", "PluginConfig", "pgb.shoutbridge");
        }
        Directory.CreateDirectory(PluginConfigDir);
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("Port", out var p) && p.TryGetInt32(out var pv) && pv > 1024 && pv < 65535) Port = pv;
                if (r.TryGetProperty("EnableTts", out var t)) TtsEnabled = t.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("DurationSeconds", out var d) && d.TryGetInt32(out var dv) && dv >= 5) DurationSeconds = dv;
                if (r.TryGetProperty("MaxTextLength", out var m) && m.TryGetInt32(out var mv) && mv > 0) MaxTextLength = mv;
                if (r.TryGetProperty("TtsPrefix", out var tp)) TtsPrefix = tp.GetString() ?? "";
                if (r.TryGetProperty("UseSystemTts", out var ust)) UseSystemTts = ust.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("TtsVolume", out var tv) && tv.TryGetSingle(out var tvv)) TtsVolume = Math.Max(0.05f, Math.Min(1f, tvv));
                if (r.TryGetProperty("TtsRate", out var tr) && tr.TryGetInt32(out var trv)) TtsRate = Math.Max(-10, Math.Min(10, trv));
                if (r.TryGetProperty("TtsEngine", out var te) && te.TryGetInt32(out var tev)) TtsEngine = tev;
                else TtsEngine = UseSystemTts ? 0 : 2;
                if (r.TryGetProperty("MimoApiKey", out var mk)) MimoApiKey = mk.GetString() ?? "";
                if (r.TryGetProperty("MimoVoice", out var mvo)) MimoVoice = mvo.GetString() ?? "alloy";
                if (r.TryGetProperty("MimoModel", out var mm)) MimoModel = mm.GetString() ?? "mimo-v2.5-tts";
                if (r.TryGetProperty("MimoSpeed", out var ms) && ms.TryGetSingle(out var msv)) MimoSpeed = msv;
                if (r.TryGetProperty("EdgeVoice", out var ev)) EdgeVoice = ev.GetString() ?? "zh-CN-XiaoxiaoNeural";
                if (r.TryGetProperty("EdgeApiUrl", out var eau)) EdgeApiUrl = eau.GetString() ?? "";
                if (r.TryGetProperty("AsrApiUrl", out var aau)) AsrApiUrl = aau.GetString() ?? "";
                if (r.TryGetProperty("AsrToken", out var atk)) AsrToken = atk.GetString() ?? "";
                if (r.TryGetProperty("PythonPath", out var pp)) PythonPath = pp.GetString() ?? "";
                if (r.TryGetProperty("ShowImage", out var si)) ShowImage = si.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("EnableSound", out var es)) SoundEnabled = es.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("SoundVolume", out var sv2) && sv2.TryGetSingle(out var svv)) SoundVolume = Math.Max(0.05f, Math.Min(1f, svv));
                if (r.TryGetProperty("HistorySize", out var hs) && hs.TryGetInt32(out var hv) && hv > 0) HistorySize = Math.Min(hv, 500);
            }
            else
            {
                var json = JsonSerializer.Serialize(new
                {
                    Port = 18123,
                    EnableTts = true,
                    DurationSeconds = 25,
                    MaxTextLength = 600,
                    TtsPrefix = "",
                    UseSystemTts = true,
                    TtsVolume = 0.9,
                    TtsRate = 0,
                    TtsEngine = 0,
                    MimoApiKey = "",
                    MimoVoice = "alloy",
                    MimoModel = "mimo-v2.5-tts",
                    MimoSpeed = 1.0,
                    EdgeVoice = "zh-CN-XiaoxiaoNeural",
                    EdgeApiUrl = "",
                    AsrApiUrl = "",
                    AsrToken = "",
                    PythonPath = "",
                    ShowImage = true,
                    EnableSound = true,
                    SoundVolume = 0.8,
                    HistorySize = 50
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
            }
        }
        catch (Exception ex) { Log("读取配置失败，使用默认值: " + ex.Message); }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log("接受连接失败: " + ex.Message); break; }
            _ = Task.Run(() => HandleClient(client), ct);
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                stream.ReadTimeout = 10000;
                var head = new List<byte>(1024);
                var one = new byte[1];
                while (head.Count < 64 * 1024)
                {
                    if (stream.Read(one, 0, 1) <= 0) break;
                    head.Add(one[0]);
                    var c = head.Count;
                    if (c >= 4 && head[c - 1] == 10 && head[c - 2] == 13 && head[c - 3] == 10 && head[c - 4] == 13) break;
                }
                var headText = Encoding.UTF8.GetString(head.ToArray());
                var lines = headText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0])) { WriteResponse(stream, 400, "{\"ok\":false}"); return; }
                var first = lines[0].Split(' ');
                var method = first.Length > 0 ? first[0].ToUpperInvariant() : "GET";
                var path = first.Length > 1 ? first[1] : "/";

                int contentLength = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(line.Substring(15).Trim(), out contentLength);
                }
                var body = "";
                if (contentLength > 0 && contentLength < 3 * 1024 * 1024)
                {
                    var buf = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        int n = stream.Read(buf, read, contentLength - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    body = Encoding.UTF8.GetString(buf, 0, read);
                }

                if (method == "OPTIONS") { WriteResponse(stream, 200, "{}"); return; }

                if (path.StartsWith("/shout", StringComparison.OrdinalIgnoreCase))
                {
                    HandleShout(body);
                    WriteResponse(stream, 200, "{\"ok\":true,\"count\":" + ShoutCount + "}");
                }
                else if (path.StartsWith("/hello", StringComparison.OrdinalIgnoreCase))
                {
                    var wasAttached = HookAttached;
                    HookAttached = true;
                    HookAt = DateTime.Now;
                    if (!wasAttached) Log("批改邦页面挂钩已连接: " + (body.Length > 300 ? body.Substring(0, 300) : body));
                    WriteResponse(stream, 200, "{\"ok\":true,\"port\":" + Port + "}");
                }
                else if (path.StartsWith("/test", StringComparison.OrdinalIgnoreCase))
                {
                    HandleTest(body);
                    WriteResponse(stream, 200, "{\"ok\":true}");
                }
                else if (path.StartsWith("/history", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(stream, 200, GetHistoryJson());
                }
                else if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
                {
                    var json = JsonSerializer.Serialize(new
                    {
                        ok = true,
                        port = Port,
                        tts = TtsEnabled,
                        showImage = ShowImage,
                        shoutCount = ShoutCount,
                        lastSender = LastSender,
                        lastShout = LastShout,
                        lastAt = LastAt == DateTime.MinValue ? null : LastAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        hookAttached = HookAttached,
                        hookAt = HookAt == DateTime.MinValue ? null : HookAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        historyCount = HistoryCount,
                        config = ConfigPath,
                        log = LogPath,
                        history = HistoryPath
                    });
                    WriteResponse(stream, 200, json);
                }
                else { WriteResponse(stream, 404, "{\"ok\":false,\"error\":\"not found\"}"); }
            }
        }
        catch (Exception ex) { Log("处理请求异常: " + ex.Message); }
    }

    private int HistoryCount { get { lock (_historyLock) { return _history.Count; } } }

    private void WriteResponse(NetworkStream stream, int status, string json)
    {
        var reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Bad Request";
        var payload = Encoding.UTF8.GetBytes(json);
        var headText = "HTTP/1.1 " + status + " " + reason + "\r\n"
            + "Content-Type: application/json; charset=utf-8\r\n"
            + "Content-Length: " + payload.Length + "\r\n"
            + "Access-Control-Allow-Origin: *\r\n"
            + "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
            + "Access-Control-Allow-Headers: *\r\n"
            + "Connection: close\r\n\r\n";
        var headBytes = Encoding.UTF8.GetBytes(headText);
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private void HandleTest(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            string text = r.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            string sender = r.TryGetProperty("sender", out var s) ? s.GetString() ?? "测试" : "测试";
            bool sound = !r.TryGetProperty("sound", out var sd) || sd.ValueKind != JsonValueKind.False;
            bool speak = !r.TryGetProperty("speak", out var sp2) || sp2.ValueKind != JsonValueKind.False;
            string prefix = r.TryGetProperty("prefix", out var p) ? p.GetString() ?? "" : "";
            bool sysTts = !r.TryGetProperty("systemTts", out var st) || st.ValueKind != JsonValueKind.False;
            float vol = r.TryGetProperty("ttsVolume", out var v) && v.TryGetSingle(out var vv) ? vv : TtsVolume;
            int rate = r.TryGetProperty("ttsRate", out var rt) && rt.TryGetInt32(out var rv) ? rv : TtsRate;
            int dur = r.TryGetProperty("duration", out var dd) && dd.TryGetInt32(out var dv) ? dv : DurationSeconds;
            Log("测试通道触发: [" + sender + "] " + text);
            TestShout(sender, text, sound, speak, dur);
        }
        catch (Exception ex) { Log("测试请求解析失败: " + ex.Message); }
    }

    /// <summary>把语音喊话的音频 POST 到配置的转写接口（multipart: file + 可选 token），返回识别文本。</summary>
    public string TryTranscribe(string audioUrl)
    {
        if (string.IsNullOrWhiteSpace(AsrApiUrl) || string.IsNullOrWhiteSpace(audioUrl)) return "";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var audio = http.GetByteArrayAsync(audioUrl).GetAwaiter().GetResult();
            if (audio == null || audio.Length < 256) { Log("ASR: 音频下载失败或过小"); return ""; }
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(audio);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/webm");
            form.Add(fileContent, "file", "shout.webm");
            if (!string.IsNullOrWhiteSpace(AsrToken)) form.Add(new StringContent(AsrToken.Trim()), "token");
            using var resp = http.PostAsync(AsrApiUrl.Trim(), form).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) { Log("ASR 失败: HTTP " + (int)resp.StatusCode + " " + (body.Length > 200 ? body.Substring(0, 200) : body)); return ""; }
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "";
            Log("ASR 转写结果: " + text);
            return text.Trim();
        }
        catch (Exception ex) { Log("ASR 异常: " + ex.Message); return ""; }
    }

    private void HandleShout(string body)
    {
        string text = "", sender = "老师", imageUrl = "", imageBase64 = "", voiceUrl = "", source = "";
        bool speak = true, hasVoice = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var r = doc.RootElement;
                if (r.TryGetProperty("text", out var t)) text = t.GetString() ?? "";
                if (r.TryGetProperty("sender", out var s)) sender = s.GetString() ?? "老师";
                if (r.TryGetProperty("speech", out var sp) && sp.ValueKind == JsonValueKind.False) speak = false;
                if (r.TryGetProperty("image", out var im)) imageUrl = im.GetString() ?? "";
                if (r.TryGetProperty("imageBase64", out var ib)) imageBase64 = ib.GetString() ?? "";
                if (r.TryGetProperty("voiceUrl", out var vu)) voiceUrl = vu.GetString() ?? "";
                if (r.TryGetProperty("hasVoice", out var hv)) hasVoice = hv.ValueKind == JsonValueKind.True;
                if (r.TryGetProperty("source", out var sc)) source = sc.GetString() ?? "";
            }
        }
        catch (Exception ex) { Log("解析喊话 JSON 失败: " + ex.Message + " body=" + (body.Length > 300 ? body.Substring(0, 300) : body)); }

        text = (text ?? "").Trim();
        if (text.Length == 0 && !string.IsNullOrWhiteSpace(voiceUrl))
        {
            var asr = TryTranscribe(voiceUrl);
            if (!string.IsNullOrWhiteSpace(asr)) { text = asr; sender = string.IsNullOrWhiteSpace(sender) ? "老师" : sender; }
        }
        var hasImage = imageBase64.Length > 0 || imageUrl.Length > 0;
        if (text.Length == 0 && !hasImage) { Log("收到空喊话，已忽略"); return; }
        if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength) + "…";
        if (string.IsNullOrWhiteSpace(sender)) sender = "老师";

        Interlocked.Increment(ref ShoutCount);
        LastShout = text;
        LastSender = sender;
        LastAt = DateTime.Now;
        ShoutState.Raise(sender, text);
        Log("收到喊话 [" + sender + "]: " + text + (hasImage ? "  [图片]" : "") + (hasVoice ? "  [语音]" : ""));

        AddHistory(new ShoutRecord
        {
            Id = "",
            Sender = sender,
            Text = text,
            At = LastAt.ToString("yyyy-MM-dd HH:mm:ss"),
            HasImage = hasImage,
            HasVoice = hasVoice,
            Source = source
        });

        byte[] imgBytes = null;
        if (ShowImage && hasImage)
        {
            if (imageBase64.Length > 0)
            {
                try { imgBytes = Convert.FromBase64String(imageBase64); }
                catch (Exception ex) { Log("图片 base64 解码失败: " + ex.Message); }
            }
            else if (imageUrl.Length > 0)
            {
                imgBytes = DownloadImage(imageUrl);
            }
        }

        // 方案 A：始终使用自绘的飞行动画弹窗（原生提醒提供方保留但不调用）
        var seconds = DurationSeconds;
        Dispatcher.UIThread.Post(() =>
        {
            try { ShoutWindow.Show(sender, text, seconds, imgBytes); }
            catch (Exception ex) { Log("显示弹窗失败: " + ex); }
        });
        PlayAlertSound();
        if (TtsEnabled && speak && text.Length > 0) Speak(text);
    }

    private static byte[] DownloadImage(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ClassIsland-PGB-Bridge/1.0");
            var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            if (bytes != null && bytes.Length > 0 && bytes.Length < 8 * 1024 * 1024) return bytes;
        }
        catch { }
        return null;
    }

    public void PlayAlertSound()
    {
        if (!SoundEnabled) return;
        try
        {
            var audio = _services.GetService(typeof(IAudioService)) as IAudioService;
            if (audio == null) { Log("未找到 IAudioService，跳过提示音"); return; }
            var ms = new MemoryStream(AlertSound.Wav);
            audio.PlayAudioAsync(ms, SoundVolume).GetAwaiter().GetResult();
            _ = Task.Run(async () => { await Task.Delay(8000); ms.Dispose(); });
            Log("提示音已播放");
        }
        catch (Exception ex) { Log("提示音播放失败: " + ex.Message); }
    }

    public void TestSound()
    {
        try
        {
            var audio = _services.GetService(typeof(IAudioService)) as IAudioService;
            if (audio == null) { Log("试听失败：未找到 IAudioService"); return; }
            var ms = new MemoryStream(AlertSound.Wav);
            audio.PlayAudioAsync(ms, SoundVolume).GetAwaiter().GetResult();
            _ = Task.Run(async () => { await Task.Delay(8000); ms.Dispose(); });
            Log("试听提示音已播放");
        }
        catch (Exception ex) { Log("试听失败: " + ex.Message); }
    }

    private static object _sapi;
    public void ReloadConfig() { LoadConfig(); }

    private void Speak(string text)
    {
        var content = (TtsPrefix ?? "") + text;
        switch (TtsEngine)
        {
            case 1: if (SpeakMimo(content)) return; Log("Mimo TTS 失败，回退系统讲述人"); break;
            case 2: if (SpeakEdge(content)) return; Log("Edge TTS 失败，回退系统讲述人"); break;
        }
        if (SpeakSystem(content, TtsVolume, TtsRate)) return;
        SpeakClassIsland(content);
    }

    private bool SpeakMimo(string content)
    {
        if (string.IsNullOrWhiteSpace(MimoApiKey)) { Log("Mimo TTS 未配置 API Key（设置页填写后重试）"); return false; }
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                model = MimoModel,
                input = content,
                voice = string.IsNullOrWhiteSpace(MimoVoice) ? "alloy" : MimoVoice,
                response_format = "mp3",
                speed = MimoSpeed <= 0 ? 1.0 : MimoSpeed
            }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + MimoApiKey.Trim());
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://tokenpapa.ai/v1/audio/speech");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = http.SendAsync(req).GetAwaiter().GetResult();
            var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode || bytes == null || bytes.Length < 512)
            {
                var detail = "";
                try { detail = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>()); } catch { }
                if (detail.Length > 200) detail = detail.Substring(0, 200);
                Log("Mimo TTS 失败: HTTP " + (int)resp.StatusCode + " " + detail);
                return false;
            }
            Log("Mimo TTS 已合成 " + bytes.Length + " 字节，开始播放");
            PlayMp3(bytes, TtsVolume);
            return true;
        }
        catch (Exception ex) { Log("Mimo TTS 异常: " + ex.Message); return false; }
    }

    private bool SpeakEdge(string content)
    {
        try
        {
            var voice = string.IsNullOrWhiteSpace(EdgeVoice) ? "zh-CN-XiaoxiaoNeural" : EdgeVoice;
            var speed = 1.0 + TtsRate / 10.0;
            if (speed <= 0.1) speed = 0.1;

            // 1) 优先走配置的 HTTP 接口（Cloudflare Worker 等）
            if (!string.IsNullOrWhiteSpace(EdgeApiUrl))
            {
                var payload = JsonSerializer.Serialize(new { input = content, voice = voice, speed = speed, pitch = "0", style = "general" },
                    new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var req = new HttpRequestMessage(HttpMethod.Post, EdgeApiUrl.Trim());
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = http.SendAsync(req).GetAwaiter().GetResult();
                var bytes = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode && bytes != null && bytes.Length > 512)
                {
                    Log("Edge TTS(HTTP) 已获取 " + bytes.Length + " 字节，开始播放");
                    PlayMp3(bytes, TtsVolume);
                    return true;
                }
                var detail = "";
                try { detail = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>()); } catch { }
                if (detail.Length > 200) detail = detail.Substring(0, 200);
                Log("Edge TTS(HTTP) 失败: HTTP " + (int)resp.StatusCode + " " + detail);
                return false;
            }

            // 2) 用 rany2/edge-tts（Python 参考实现）合成，最稳
            if (SpeakEdgePython(content, voice, TtsRate * 10)) return true;

            // 3) 兜底：直连微软 WS（DRM 校验可能被拒）
            var audio = EdgeTtsClient.Synthesize(content, voice, TtsRate * 10);
            if (audio == null || audio.Length < 512) { Log("Edge TTS 未取到音频（长度 " + (audio?.Length ?? 0) + "）"); return false; }
            Log("Edge TTS(直连) 已合成 " + audio.Length + " 字节，开始播放");
            PlayMp3(audio, TtsVolume);
            return true;
        }
        catch (Exception ex) { Log("Edge TTS 失败: " + ex.Message); return false; }
    }

    private static readonly string EdgeDriverScript = "import sys, asyncio\r\nimport edge_tts\r\nasync def main():\r\n    text = open(sys.argv[1], encoding='utf-8').read()\r\n    voice = sys.argv[2]\r\n    rate = sys.argv[3]\r\n    out = sys.argv[4]\r\n    c = edge_tts.Communicate(text, voice, rate=rate)\r\n    await c.save(out)\r\nasyncio.run(main())\r\n";

    /// <summary>取 Edge 音色列表（来自 python edge-tts --list-voices，失败则用内置常用表）。</summary>
    public List<string[]> GetEdgeVoices()
    {
        var list = new List<string[]>();
        try
        {
            var py = string.IsNullOrWhiteSpace(PythonPath) ? FindPython() : PythonPath;
            if (!string.IsNullOrWhiteSpace(py))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(py)
                { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                psi.ArgumentList.Add("-m"); psi.ArgumentList.Add("edge_tts"); psi.ArgumentList.Add("--list-voices");
                using var p = System.Diagnostics.Process.Start(psi);
                var outp = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(20000);
                foreach (var raw in outp.Split('\n'))
                {
                    var line = raw.TrimEnd();
                    if (line.Length < 20 || line.StartsWith("Name") || line.StartsWith("---")) continue;
                    var parts = System.Text.RegularExpressions.Regex.Split(line, @"\s{2,}");
                    if (parts.Length < 2) continue;
                    var shortName = parts[0].Trim();
                    if (!shortName.Contains("-") || !shortName.EndsWith("Neural")) continue;
                    var gender = parts[1].Trim();
                    var seg = shortName.Split('-');
                    var locale = seg.Length >= 2 ? seg[0] + "-" + seg[1] : "";
                    list.Add(new[] { shortName, gender, locale });
                }
                list.Sort((a, b) =>
                {
                    int Rank(string[] v) => v[2] == "zh-CN" ? 0 : v[2] == "zh-TW" ? 1 : v[2] == "zh-HK" ? 2 : v[2].StartsWith("en-") ? 3 : 4;
                    var r = Rank(a).CompareTo(Rank(b));
                    return r != 0 ? r : string.CompareOrdinal(a[0], b[0]);
                });
                Log("Edge 音色列表载入 " + list.Count + " 个");
            }
        }
        catch (Exception ex) { Log("读取音色列表失败: " + ex.Message); }
        if (list.Count == 0)
        {
            foreach (var v in EdgeFallbackVoices()) list.Add(v);
        }
        return list;
    }

    public static List<string[]> EdgeFallbackVoices()
    {
        return new List<string[]>
        {
            new[] { "zh-CN-XiaoxiaoNeural", "Female", "zh-CN" },
            new[] { "zh-CN-XiaoyiNeural", "Female", "zh-CN" },
            new[] { "zh-CN-YunxiNeural", "Male", "zh-CN" },
            new[] { "zh-CN-YunjianNeural", "Male", "zh-CN" },
            new[] { "zh-CN-YunyangNeural", "Male", "zh-CN" },
            new[] { "zh-CN-YunxiaNeural", "Male", "zh-CN" },
            new[] { "zh-CN-liaoning-XiaobeiNeural", "Female", "zh-CN" },
            new[] { "zh-CN-shaanxi-XiaoniNeural", "Female", "zh-CN" },
            new[] { "zh-TW-HsiaoChenNeural", "Female", "zh-TW" },
            new[] { "zh-TW-YunJheNeural", "Male", "zh-TW" },
            new[] { "zh-HK-HiuMaanNeural", "Female", "zh-HK" },
            new[] { "zh-HK-WanLungNeural", "Male", "zh-HK" },
            new[] { "en-US-AriaNeural", "Female", "en-US" },
            new[] { "en-US-GuyNeural", "Male", "en-US" }
        };
    }

    /// <summary>用指定音色试听（不改配置）。</summary>
    public void PreviewVoice(string voice, string text)
    {
        Task.Run(() =>
        {
            var t = string.IsNullOrWhiteSpace(text) ? "同学们注意，这是音色试听。" : text;
            if (!SpeakEdgePython(t, string.IsNullOrWhiteSpace(voice) ? "zh-CN-XiaoxiaoNeural" : voice, TtsRate * 10))
                Log("试听失败: 音色 " + voice);
        });
    }

    private bool SpeakEdgePython(string content, string voice, int ratePercent)
    {
        string tmpDir = Path.GetTempPath();
        string txt = Path.Combine(tmpDir, "pgb-edge-" + Guid.NewGuid().ToString("N") + ".txt");
        string outp = Path.Combine(tmpDir, "pgb-edge-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            var py = string.IsNullOrWhiteSpace(PythonPath) ? FindPython() : PythonPath;
            if (string.IsNullOrWhiteSpace(py)) { Log("Edge(Python): 未找到 python，跳过后备方案"); return false; }
            var script = Path.Combine(Path.GetTempPath(), "pgb_edge_tts_driver.py");
            if (!File.Exists(script)) File.WriteAllText(script, EdgeDriverScript, new UTF8Encoding(false));
            File.WriteAllText(txt, content, new UTF8Encoding(false));
            var rate = (ratePercent >= 0 ? "+" : "") + ratePercent + "%";
            var psi = new System.Diagnostics.ProcessStartInfo(py)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add(txt);
            psi.ArgumentList.Add(voice);
            psi.ArgumentList.Add(rate);
            psi.ArgumentList.Add(outp);
            using var p = System.Diagnostics.Process.Start(psi);
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(30000);
            if (!File.Exists(outp) || new FileInfo(outp).Length < 512)
            {
                if (stderr.Contains("No module named") && EnsureEdgeTtsInstalled(py))
                {
                    Log("Edge(Python): 已自动安装 edge-tts，重试一次");
                    var p2 = System.Diagnostics.Process.Start(psi);
                    p2.StandardError.ReadToEnd(); p2.WaitForExit(40000);
                }
                if (!File.Exists(outp) || new FileInfo(outp).Length < 512)
                {
                    Log("Edge(Python) 失败: " + (stderr.Length > 200 ? stderr.Substring(0, 200) : stderr));
                    return false;
                }
            }
            var bytes = File.ReadAllBytes(outp);
            Log("Edge(Python) 已合成 " + bytes.Length + " 字节，开始播放");
            PlayMp3(bytes, TtsVolume);
            return true;
        }
        catch (Exception ex) { Log("Edge(Python) 异常: " + ex.Message); return false; }
        finally
        {
            try { if (File.Exists(txt)) File.Delete(txt); } catch { }
            try { if (File.Exists(outp)) File.Delete(outp); } catch { }
        }
    }

    private static bool EnsureEdgeTtsInstalled(string py)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(py)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-m"); psi.ArgumentList.Add("pip"); psi.ArgumentList.Add("install"); psi.ArgumentList.Add("--quiet"); psi.ArgumentList.Add("edge-tts");
            using var p = System.Diagnostics.Process.Start(psi);
            p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string FindPython()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("PGB_PYTHON") ?? "",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python314", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python313", "python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe")
        };
        foreach (var c in candidates) if (!string.IsNullOrWhiteSpace(c) && File.Exists(c)) return c;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';'))
        {
            try
            {
                var f = Path.Combine(dir.Trim(), "python.exe");
                if (File.Exists(f)) return f;
            }
            catch { }
        }
        return "";
    }

    private void PlayMp3(byte[] bytes, float volume)
    {
        try
        {
            var audio = _services.GetService(typeof(IAudioService)) as IAudioService;
            if (audio == null) { Log("播放失败：未找到 IAudioService"); return; }
            var ms = new MemoryStream(bytes);
            audio.PlayAudioAsync(ms, Math.Max(0.05f, Math.Min(1f, volume))).GetAwaiter().GetResult();
            Task.Run(async () => { await Task.Delay(8000); try { ms.Dispose(); } catch { } });
            Log("音频已交给 ClassIsland 音频引擎播放");
        }
        catch (Exception ex) { Log("音频播放失败: " + ex.Message); }
    }

    private bool SpeakSystem(string content, float volume, int rate)
    {
        try
        {
            if (_sapi == null)
            {
                var t = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (t != null) _sapi = Activator.CreateInstance(t);
            }
            if (_sapi != null)
            {
                dynamic sp = _sapi;
                sp.Volume = (int)Math.Max(0, Math.Min(100, volume * 100));
                sp.Rate = Math.Max(-10, Math.Min(10, rate));
                sp.Speak(content, 1);
                Log("已用系统讲述人播报: " + content);
                return true;
            }
            Log("系统没有 SAPI 讲述人，回退到 ClassIsland TTS");
        }
        catch (Exception ex) { Log("系统讲述人失败，回退: " + ex.Message); }
        return false;
    }

    private void SpeakClassIsland(string content)
    {
        try
        {
            _speech ??= _services.GetService(typeof(ISpeechService)) as ISpeechService;
            if (_speech == null) { Log("未找到 ISpeechService，跳过 TTS"); return; }
            _speech.EnqueueSpeechQueue(content);
            Log("已加入 ClassIsland TTS 队列: " + content);
        }
        catch (Exception ex) { Log("TTS 播报失败: " + ex.Message); }
    }

    public void TestShout(string sender, string text, bool playSound, bool speak, int durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(text)) { Log("测试内容为空，跳过"); return; }
        var d = durationSeconds >= 5 ? durationSeconds : DurationSeconds;
        Dispatcher.UIThread.Post(() => { try { ShoutWindow.Show(sender ?? "测试", text, d, null); } catch (Exception ex) { Log("测试弹窗失败: " + ex); } });
        if (playSound) PlayAlertSound();
        if (speak) Speak(text);
    }

    public void TestShout(string sender, string text, bool playSound, bool speak, string prefix, bool useSystemTts, float ttsVolume, int ttsRate, int durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(text)) { Log("测试内容为空，跳过"); return; }
        var d = durationSeconds >= 5 ? durationSeconds : DurationSeconds;
        Dispatcher.UIThread.Post(() => { try { ShoutWindow.Show(sender ?? "测试", text, d, null); } catch (Exception ex) { Log("测试弹窗失败: " + ex); } });
        if (playSound) PlayAlertSound();
        if (speak)
        {
            var content = (prefix ?? "") + text;
            if (useSystemTts) { if (!SpeakSystem(content, ttsVolume, ttsRate)) SpeakClassIsland(content); }
            else SpeakClassIsland(content);
        }
    }

    private void AddHistory(ShoutRecord rec)
    {
        try
        {
            lock (_historyLock)
            {
                _history.Insert(0, rec);
                while (_history.Count > HistorySize) _history.RemoveAt(_history.Count - 1);
            }
            _ = Task.Run(SaveHistory);
        }
        catch { }
    }

    private void SaveHistory()
    {
        try
        {
            string json;
            lock (_historyLock) { json = JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true }); }
            File.WriteAllText(HistoryPath, json, new UTF8Encoding(false));
        }
        catch { }
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return;
            var list = JsonSerializer.Deserialize<List<ShoutRecord>>(File.ReadAllText(HistoryPath));
            if (list == null) return;
            lock (_historyLock)
            {
                _history.Clear();
                _history.AddRange(list.Take(HistorySize));
            }
        }
        catch { }
    }

    private string GetHistoryJson()
    {
        try
        {
            lock (_historyLock) { return JsonSerializer.Serialize(_history); }
        }
        catch { return "[]"; }
    }

    public void Log(string message)
    {
        try
        {
            lock (_logLock)
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine;
                File.AppendAllText(LogPath, line, new UTF8Encoding(false));
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    var old = File.ReadAllLines(LogPath);
                    var keep = new string[Math.Min(old.Length, 1500)];
                    Array.Copy(old, old.Length - keep.Length, keep, 0, keep.Length);
                    File.WriteAllLines(LogPath, keep, new UTF8Encoding(false));
                }
            }
        }
        catch { }
    }
}
