using System;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace PgbShoutBridge;

[SettingsPageInfo("pgb.shoutbridge", "批改邦喊话")]
public class ShoutSettingsPage : SettingsPageBase
{
    private readonly TextBox _port = new() { Width = 120 };
    private readonly TextBox _duration = new() { Width = 120 };
    private readonly TextBox _history = new() { Width = 120 };
    private readonly CheckBox _image = new() { Content = "弹窗中显示图片喊话" };

    private readonly CheckBox _sound = new() { Content = "弹窗播放提示音" };
    private readonly Slider _soundVolume = new() { Minimum = 5, Maximum = 100, Width = 260 };

    private readonly CheckBox _tts = new() { Content = "启用语音播报" };
    private readonly ComboBox _ttsEngine = new() { Width = 240 };
    private readonly Slider _ttsVolume = new() { Minimum = 5, Maximum = 100, Width = 260 };
    private readonly Slider _ttsRate = new() { Minimum = -10, Maximum = 10, Width = 260, TickFrequency = 1 };
    private readonly TextBox _prefix = new() { Width = 260 };
    private readonly TextBox _mimoKey = new() { Width = 360, Watermark = "Bearer Token（必填）", PasswordChar = '*' };
    private readonly TextBox _mimoVoice = new() { Width = 160, Text = "alloy" };
    private readonly TextBox _mimoSpeed = new() { Width = 100, Text = "1.0" };
    private readonly ComboBox _edgeVoice = new() { Width = 320 };
    private readonly Button _voiceRefresh = new() { Content = "刷新音色" };
    private readonly Button _voicePreview = new() { Content = "试听" };
    private readonly TextBox _edgeUrl = new() { Width = 420, Watermark = "https://your-worker.workers.dev/v1/audio/speech（留空则直连微软）" };
    private readonly TextBox _asrUrl = new() { Width = 420, Watermark = "https://your-worker.workers.dev/v1/audio/transcriptions（可选）" };
    private readonly TextBox _asrToken = new() { Width = 260, Watermark = "ASR token（可选）", PasswordChar = '*' };
    private string _edgeVoiceValue = "zh-CN-XiaoxiaoNeural";
    private readonly TextBox _pyPath = new() { Width = 420, Watermark = "留空自动查找 python.exe" };

    private readonly TextBox _testText = new() { Width = 420, Watermark = "输入要测试的喊话内容…", AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _testSound = new() { Content = "带提示音", IsChecked = true };
    private readonly CheckBox _testTts = new() { Content = "带语音", IsChecked = true };

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    private static string Dir => string.IsNullOrWhiteSpace(ShoutBridgeService.PluginConfigDir)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland", "PluginConfig", "pgb.shoutbridge")
        : ShoutBridgeService.PluginConfigDir;

    private static string ConfigPath => Path.Combine(Dir, "settings.json");
    private static string LogPath => Path.Combine(Dir, "bridge.log");
    private static string HistoryPath => Path.Combine(Dir, "history.json");

    public ShoutSettingsPage()
    {
        _ttsEngine.Items.Add(new ComboBoxItem { Content = "系统讲述人 (SAPI)" });
        _ttsEngine.Items.Add(new ComboBoxItem { Content = "Mimo TTS (tokenpapa)" });
        _ttsEngine.Items.Add(new ComboBoxItem { Content = "Edge TTS（插件直连）" });

        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "批改邦「教室远程喊话」桥接", FontSize = 20, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new TextBlock { Text = "收到喊话时：岛飞下变成玻璃弹窗 + 提示音 + 语音播报。保存后需重启 ClassIsland 生效（测试功能即时生效）。", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(Section("基础"));
        panel.Children.Add(Row("监听端口", _port));
        panel.Children.Add(Row("弹窗时长（秒）", _duration));
        panel.Children.Add(Row("历史保留条数", _history));
        panel.Children.Add(_image);

        panel.Children.Add(Section("提示音"));
        panel.Children.Add(_sound);
        panel.Children.Add(Row("提示音音量", _soundVolume));
        var trySound = new Button { Content = "试听提示音" };
        trySound.Click += (_, _) => { try { ShoutBridgeService.Current?.TestSound(); SetStatus("已播放试听提示音（无声请检查系统音量）", false); } catch (Exception ex) { SetStatus("试听失败：" + ex.Message, true); } };
        panel.Children.Add(trySound);

        panel.Children.Add(Section("语音播报 (TTS)"));
        panel.Children.Add(_tts);
        panel.Children.Add(Row("播报引擎", _ttsEngine));
        panel.Children.Add(Row("语音音量", _ttsVolume));
        panel.Children.Add(Row("语速", _ttsRate));
        panel.Children.Add(Row("播报前缀", _prefix));
        panel.Children.Add(Row("Mimo API Key", _mimoKey));
        panel.Children.Add(Row("Mimo 音色", _mimoVoice));
        panel.Children.Add(Row("Mimo 语速", _mimoSpeed));
        var voiceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        voiceRow.Children.Add(_edgeVoice); voiceRow.Children.Add(_voiceRefresh); voiceRow.Children.Add(_voicePreview);
        var voiceWrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        voiceWrap.Children.Add(new TextBlock { Text = "朗读角色", Width = 150, VerticalAlignment = VerticalAlignment.Center });
        voiceWrap.Children.Add(voiceRow);
        panel.Children.Add(voiceWrap);
        _voiceRefresh.Click += (_, _) => LoadVoices(null);
        _voicePreview.Click += (_, _) => PreviewSelectedVoice();
        panel.Children.Add(Row("Edge 接口地址", _edgeUrl));
        panel.Children.Add(Row("ASR 转写接口", _asrUrl));
        panel.Children.Add(Row("ASR Token", _asrToken));
        panel.Children.Add(Row("Python 路径", _pyPath));
        var tryTts = new Button { Content = "试听语音（读下面输入框的内容）" };
        tryTts.Click += (_, _) => { Test(useStored: false); };
        panel.Children.Add(tryTts);

        panel.Children.Add(Section("测试弹窗"));
        var testRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        testRow.Children.Add(_testText);
        testRow.Children.Add(new StackPanel { Spacing = 6, Children = { _testSound, _testTts } });
        panel.Children.Add(testRow);
        var testBtn = new Button { Content = "发送测试弹窗（完整效果）" };
        testBtn.Click += (_, _) => Test(useStored: true);
        panel.Children.Add(testBtn);
        panel.Children.Add(new TextBlock { Text = "提示：测试用当前界面上的所有设置，即时生效，无需重启。", Opacity = 0.6, TextWrapping = TextWrapping.Wrap });

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };
        var save = new Button { Content = "保存配置" };
        save.Click += (_, _) => Save();
        var openDir = new Button { Content = "打开配置目录" };
        openDir.Click += (_, _) => OpenPath(Dir, false);
        var openLog = new Button { Content = "查看运行日志" };
        openLog.Click += (_, _) => OpenPath(LogPath, true);
        var openHist = new Button { Content = "查看喊话历史" };
        openHist.Click += (_, _) => { EnsureHistoryFile(); OpenPath(HistoryPath, true); };
        bar.Children.Add(save); bar.Children.Add(openDir); bar.Children.Add(openLog); bar.Children.Add(openHist);
        panel.Children.Add(bar);
        panel.Children.Add(_status);

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Load();
    }

    private static TextBlock Section(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 16, Margin = new Thickness(0, 10, 0, 0) };

    private static StackPanel Row(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock { Text = label, Width = 150, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> FriendlyVoices = new()
    {
        ["zh-CN-XiaoxiaoNeural"] = "晓晓（女·温柔）",
        ["zh-CN-XiaoyiNeural"] = "晓伊（女·亲切）",
        ["zh-CN-YunxiNeural"] = "云希（男·活泼）",
        ["zh-CN-YunjianNeural"] = "云健（男·浑厚）",
        ["zh-CN-YunyangNeural"] = "云扬（男·播报）",
        ["zh-CN-YunxiaNeural"] = "云夏（男·少年）",
        ["zh-CN-liaoning-XiaobeiNeural"] = "小北（女·东北）",
        ["zh-CN-shaanxi-XiaoniNeural"] = "小妮（女·陕西）",
        ["zh-TW-HsiaoChenNeural"] = "曉臻（女·台湾）",
        ["zh-TW-YunJheNeural"] = "雲哲（男·台湾）",
        ["zh-HK-HiuMaanNeural"] = "曉曼（女·粤语）",
        ["zh-HK-WanLungNeural"] = "雲龍（男·粤语）",
        ["en-US-AriaNeural"] = "Aria（女·英语）",
        ["en-US-GuyNeural"] = "Guy（男·英语）"
    };

    private void LoadVoices(string select)
    {
        try
        {
            var svc = ShoutBridgeService.Current;
            var current = select ?? SelectedVoice();
            _edgeVoice.Items.Clear();
            var voices = svc != null ? svc.GetEdgeVoices() : ShoutBridgeService.EdgeFallbackVoices();
            int idx = 0, sel = -1;
            foreach (var v in voices)
            {
                var shortName = v[0];
                var label = FriendlyVoices.TryGetValue(shortName, out var friendly)
                    ? friendly
                    : shortName + (v.Length > 1 && v[1] != "" ? "（" + (v[1] == "Female" ? "女" : v[1] == "Male" ? "男" : v[1]) + (v.Length > 2 && v[2] != "" ? "·" + v[2] : "") + "）" : "");
                var item = new ComboBoxItem { Content = label, Tag = shortName };
                _edgeVoice.Items.Add(item);
                if (!string.IsNullOrWhiteSpace(current) && string.Equals(shortName, current, StringComparison.OrdinalIgnoreCase)) sel = idx;
                idx++;
            }
            if (sel >= 0) _edgeVoice.SelectedIndex = sel;
            else if (_edgeVoice.ItemCount > 0) _edgeVoice.SelectedIndex = 0;
        }
        catch (Exception ex) { SetStatus("加载音色失败：" + ex.Message, true); }
    }

    private string SelectedVoice()
    {
        if (_edgeVoice.SelectedItem is ComboBoxItem it && it.Tag is string s) return s;
        return "zh-CN-XiaoxiaoNeural";
    }

    private void PreviewSelectedVoice()
    {
        try
        {
            var svc = ShoutBridgeService.Current;
            if (svc == null) { SetStatus("服务未就绪", true); return; }
            var voice = SelectedVoice();
            var text = string.IsNullOrWhiteSpace(_testText.Text) ? "同学们注意，这是音色试听。" : _testText.Text;
            svc.PreviewVoice(voice, text);
            SetStatus("正在用 " + voice + " 试听…（约 2-4 秒）", false);
        }
        catch (Exception ex) { SetStatus("试听失败：" + ex.Message, true); }
    }

    private void Test(bool useStored)
    {
        try
        {
            var text = _testText.Text ?? "";
            if (string.IsNullOrWhiteSpace(text)) { SetStatus("请先在上方输入框里写点内容再测试", true); return; }
            var svc = ShoutBridgeService.Current;
            if (svc == null) { SetStatus("桥接服务未就绪", true); return; }
            Save();
            svc.ReloadConfig();
            svc.TestShout("测试", text, _testSound.IsChecked == true, _testTts.IsChecked == true, 15);
            SetStatus("测试已发送：动画弹窗 + " + (_testSound.IsChecked == true ? "提示音" : "无提示音") + " + " + (_testTts.IsChecked == true ? "语音" : "无语音"), false);
        }
        catch (Exception ex) { SetStatus("测试失败：" + ex.Message, true); }
    }

    private void Load()
    {
        try
        {
            int port = 18123, duration = 25, history = 50, rate = 0;
            float sndVol = 0.8f, ttsVol = 0.9f;
            string prefix = "";
            bool tts = true, image = true, sound = true, sysTts = true;
            if (File.Exists(ConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("Port", out var p) && p.TryGetInt32(out var pv)) port = pv;
                if (r.TryGetProperty("DurationSeconds", out var d) && d.TryGetInt32(out var dv)) duration = dv;
                if (r.TryGetProperty("HistorySize", out var h) && h.TryGetInt32(out var hv)) history = hv;
                if (r.TryGetProperty("TtsPrefix", out var t)) prefix = t.GetString() ?? "";
                if (r.TryGetProperty("EnableTts", out var e)) tts = e.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("ShowImage", out var si)) image = si.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("EnableSound", out var sd)) sound = sd.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("UseSystemTts", out var ust)) sysTts = ust.ValueKind != JsonValueKind.False;
                if (r.TryGetProperty("SoundVolume", out var sv) && sv.TryGetSingle(out var svv)) sndVol = svv;
                if (r.TryGetProperty("TtsVolume", out var tv) && tv.TryGetSingle(out var tvv)) ttsVol = tvv;
                if (r.TryGetProperty("TtsRate", out var tr) && tr.TryGetInt32(out var trv)) rate = trv;
            }
            _port.Text = port.ToString();
            _duration.Text = duration.ToString();
            _history.Text = history.ToString();
            _prefix.Text = prefix;
            _tts.IsChecked = tts;
            _image.IsChecked = image;
            _sound.IsChecked = sound;
            int engine = 0, mimoSpeedInt = 0;
            string mimoKey = "", mimoVoice = "alloy", edgeVoice = "zh-CN-XiaoxiaoNeural", edgeApiUrl = "", asrUrl = "", asrToken = "", pythonPath = "";
            float mimoSpeed = 1.0f;
            if (File.Exists(ConfigPath))
            {
                try
                {
                    using var doc2 = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                    var r2 = doc2.RootElement;
                    if (r2.TryGetProperty("TtsEngine", out var te) && te.TryGetInt32(out var tev)) engine = tev;
                    if (r2.TryGetProperty("MimoApiKey", out var mk)) mimoKey = mk.GetString() ?? "";
                    if (r2.TryGetProperty("MimoVoice", out var mv)) mimoVoice = mv.GetString() ?? "alloy";
                    if (r2.TryGetProperty("MimoSpeed", out var ms) && ms.TryGetSingle(out var msv)) mimoSpeed = msv;
                    if (r2.TryGetProperty("EdgeVoice", out var ev)) edgeVoice = ev.GetString() ?? "zh-CN-XiaoxiaoNeural";
                    if (r2.TryGetProperty("EdgeApiUrl", out var eu)) edgeApiUrl = eu.GetString() ?? "";
                    if (r2.TryGetProperty("AsrApiUrl", out var au2)) asrUrl = au2.GetString() ?? "";
                    if (r2.TryGetProperty("AsrToken", out var at2)) asrToken = at2.GetString() ?? "";
                    if (r2.TryGetProperty("PythonPath", out var pp2)) pythonPath = pp2.GetString() ?? "";
                }
                catch { }
            }
            _mimoKey.Text = mimoKey;
            _mimoVoice.Text = mimoVoice;
            _mimoSpeed.Text = mimoSpeed.ToString("0.##");
            _edgeVoiceValue = edgeVoice;
            _edgeUrl.Text = edgeApiUrl;
            LoadVoices(edgeVoice);
            _asrUrl.Text = asrUrl;
            _asrToken.Text = asrToken;
            _pyPath.Text = pythonPath;
            _ttsEngine.SelectedIndex = Math.Max(0, Math.Min(2, engine));
            _soundVolume.Value = Math.Max(5, Math.Min(100, sndVol * 100));
            _ttsVolume.Value = Math.Max(5, Math.Min(100, ttsVol * 100));
            _ttsRate.Value = Math.Max(-10, Math.Min(10, rate));
            SetStatus("配置目录：" + Dir, false);
        }
        catch (Exception ex) { SetStatus("读取配置失败：" + ex.Message, true); }
    }

    private void Save()
    {
        try
        {
            if (!int.TryParse(_port.Text, out var port) || port < 1024 || port > 65535) { SetStatus("端口需为 1024-65535 之间的整数", true); return; }
            if (!int.TryParse(_duration.Text, out var duration) || duration < 5 || duration > 600) { SetStatus("弹窗时长需为 5-600 秒", true); return; }
            if (!int.TryParse(_history.Text, out var history) || history < 1 || history > 500) { SetStatus("历史条数需为 1-500", true); return; }

            var maxLen = 600;
            if (File.Exists(ConfigPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                    if (doc.RootElement.TryGetProperty("MaxTextLength", out var m) && m.TryGetInt32(out var mv)) maxLen = mv;
                }
                catch { }
            }

            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(new
            {
                Port = port,
                EnableTts = _tts.IsChecked == true,
                TtsEngine = _ttsEngine.SelectedIndex,
                UseSystemTts = _ttsEngine.SelectedIndex == 0,
                MimoApiKey = _mimoKey.Text ?? "",
                MimoVoice = string.IsNullOrWhiteSpace(_mimoVoice.Text) ? "alloy" : _mimoVoice.Text,
                MimoSpeed = float.TryParse(_mimoSpeed.Text, out var msp) ? msp : 1.0f,
                EdgeVoice = SelectedVoice(),
                EdgeApiUrl = _edgeUrl.Text ?? "",
                AsrApiUrl = _asrUrl.Text ?? "",
                AsrToken = _asrToken.Text ?? "",
                PythonPath = _pyPath.Text ?? "",
                TtsVolume = (float)(_ttsVolume.Value / 100.0),
                TtsRate = (int)_ttsRate.Value,
                DurationSeconds = duration,
                MaxTextLength = maxLen,
                TtsPrefix = _prefix.Text ?? "",
                ShowImage = _image.IsChecked == true,
                EnableSound = _sound.IsChecked == true,
                SoundVolume = (float)(_soundVolume.Value / 100.0),
                HistorySize = history
            }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.WriteAllText(ConfigPath, json);
            SetStatus("已保存到 " + ConfigPath + "，重启 ClassIsland 后生效（测试功能即时生效）。", false);
        }
        catch (Exception ex) { SetStatus("保存失败：" + ex.Message, true); }
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = new SolidColorBrush(Color.Parse(error ? "#FFE05A5A" : "#FF7FD4FF"));
    }

    private static void EnsureHistoryFile()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(HistoryPath)) File.WriteAllText(HistoryPath, "[]", new System.Text.UTF8Encoding(false));
        }
        catch { }
    }

    private void OpenPath(string path, bool isFile)
    {
        try
        {
            if (isFile)
            {
                if (!File.Exists(path)) { SetStatus("文件不存在：" + path, true); return; }
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            else
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) { SetStatus("打开失败：" + ex.Message, true); }
    }
}
