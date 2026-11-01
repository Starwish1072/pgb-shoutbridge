using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PgbShoutBridge;

public static class ShoutWindow
{
    public static Action<string> Log;

    private const double CardW = 1000;
    private const double CardH = 420;
    private const int FrameMs = 15;
    private const double InMs = 460;
    private const double OutMs = 360;
    private const double RippleMs = 1500;

    private static Window _window;
    private static Canvas _canvas;
    private static Canvas _fx;
    private static Border _card;
    private static TextBlock _title, _body, _meta;
    private static Image _image;
    private static ScaleTransform _scale;
    private static TranslateTransform _move;
    private static DispatcherTimer _frame, _autoClose;
    private static Bitmap _bitmap;
    private static Ellipse[] _rings = Array.Empty<Ellipse>();
    private static Ellipse _glow;
    private static double _cardLeft, _cardTop, _screenW = 1920, _screenH = 1080;

    private static bool _visible, _closing;
    private static double _phase, _duration = InMs;
    private static double _fromSx = 0.16, _fromSy = 0.16, _fromTx, _fromTy, _toTx, _toTy;
    private static double _autoCloseSeconds = 25;
    private static long _lastTick;
    private static long _fxStart;
    private static bool _fxLogged;
    private static IntPtr _islandHwnd;
    private static bool _islandHidden;
    private static IntPtr _hwnd;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int idx, int val);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public static void Show(string sender, string text, int seconds, byte[] imageBytes = null)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Show(sender, text, seconds, imageBytes)); return; }
        EnsureCreated();
        _autoCloseSeconds = Math.Max(5, seconds);

        _body.Text = text ?? "";
        _body.IsVisible = !string.IsNullOrWhiteSpace(text);
        ApplyImage(imageBytes);
        _meta.Text = string.Format("{0}  ·  {1:HH:mm}", string.IsNullOrWhiteSpace(sender) ? "老师" : sender, DateTime.Now);

        if (!_visible) { _visible = true; _closing = false; _window.Show(); _window.Topmost = true; MakeClickThrough(); StartIn(); }
        else if (!_closing)
        {
            StartRipple();
            RestartAutoClose();
        }
    }

    private static void StartIn()
    {
        _card.Opacity = 0.0;
        var s = IslandStart();
        _fromTx = s.Tx; _fromTy = s.Ty; _fromSx = s.Sx; _fromSy = s.Sy;
        _toTx = 0; _toTy = 0;
        _duration = InMs; _phase = 0; _lastTick = 0;
        StartRipple();
        EnsureFrameTimer();
        _frame.Start();
        RestartAutoClose();
        try { if (_islandHwnd != IntPtr.Zero) { ShowWindow(_islandHwnd, 0); _islandHidden = true; } } catch { }
        Log?.Invoke("入场: 岛窗口=" + (_islandHwnd != IntPtr.Zero ? "找到" : "未找到") + " 起始=" + _fromSx.ToString("0.000") + "x" + _fromSy.ToString("0.000") + " | 涟漪自弹窗中心扩散");
    }

    private static void StartRipple()
    {
        if (_rings == null || _rings.Length == 0 || _glow == null) return;
        _fxStart = Environment.TickCount64;
        for (int i = 0; i < _rings.Length; i++)
        {
            _rings[i].Width = 0; _rings[i].Height = 0; _rings[i].Opacity = 0;
            Canvas.SetLeft(_rings[i], _screenW / 2); Canvas.SetTop(_rings[i], _screenH / 2);
        }
        _glow.Width = 0; _glow.Height = 0;
        Canvas.SetLeft(_glow, _screenW / 2); Canvas.SetTop(_glow, _screenH / 2);
    }

    private static void StartOut()
    {
        if (_closing || !_visible) return;
        _closing = true;
        _autoClose?.Stop();
        _fromSx = _scale.ScaleX; _fromSy = _scale.ScaleY;
        _fromTx = _move.X; _fromTy = _move.Y;
        var s = IslandStart();
        _toTx = s.Tx; _toTy = s.Ty;
        _duration = OutMs; _phase = 0; _lastTick = 0;
        EnsureFrameTimer();
        _frame.Start();
    }

    private static (double Tx, double Ty, double Sx, double Sy) IslandStart()
    {
        try
        {
            if (_islandHwnd == IntPtr.Zero) _islandHwnd = FindWindow(null, "ClassIsland");
            if (_islandHwnd != IntPtr.Zero && GetWindowRect(_islandHwnd, out var r) && _window != null)
            {
                var scaling = _window.RenderScaling > 0 ? _window.RenderScaling : 1.0;
                double ix = r.L / scaling, iy = r.T / scaling, iw = (r.R - r.L) / scaling, ih = (r.B - r.T) / scaling;
                return (ix - _cardLeft, iy - _cardTop, iw / CardW, ih / CardH);
            }
        }
        catch { }
        return (-_cardLeft + 40, -_cardTop + 30, 0.16, 0.16);
    }

    private static void EnsureFrameTimer()
    {
        if (_frame != null) return;
        _frame = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        _frame.Tick += (_, _) => OnFrame();
    }

    private static void OnFrame()
    {
        var now = Environment.TickCount64;
        if (_lastTick == 0) _lastTick = now;
        _phase += now - _lastTick;
        _lastTick = now;
        var t = Math.Min(1.0, _phase / _duration);
        var e = _closing ? EaseInOutCubic(t) : EaseOutQuint(t);

        _scale.ScaleX = _fromSx + (1.0 - _fromSx) * e;
        _scale.ScaleY = _fromSy + (1.0 - _fromSy) * e;
        _move.X = _fromTx + (_toTx - _fromTx) * e;
        _move.Y = _fromTy + (_toTy - _fromTy) * e;
        _card.Opacity = _closing ? Math.Max(0.0, 1.0 - t * 1.15) : Math.Min(1.0, t / 0.45);

        UpdateRipple(now);

        if (t >= 1.0 && _closing)
        {
            _frame.Stop();
            _visible = false; _closing = false;
            RestoreIsland();
            try { _window.Hide(); } catch { }
        }
    }

    private static void UpdateRipple(long now)
    {
        if (_fxStart == 0) return;
        var elapsed = (double)(now - _fxStart);
        for (int i = 0; i < _rings.Length; i++)
        {
            var start = i * 180.0;
            var p = (elapsed - start) / RippleMs;
            if (p < 0) { _rings[i].Opacity = 0; continue; }
            if (p > 1) { _rings[i].Opacity = 0; continue; }
            var ease = 1 - Math.Pow(1 - p, 3);
            var size = 220 + ease * 1500;
            _rings[i].Width = size; _rings[i].Height = size;
            _rings[i].Opacity = Math.Max(0, (1 - p) * (i == 0 ? 0.9 : 0.55));
            Canvas.SetLeft(_rings[i], _screenW / 2 - size / 2);
            Canvas.SetTop(_rings[i], _screenH / 2 - size / 2);
        }
        if (!_fxLogged && elapsed > 300) { _fxLogged = true; Log?.Invoke("涟漪状态: r0=" + _rings[0].Width.ToString("0") + "px opacity=" + _rings[0].Opacity.ToString("0.00") + " canvas=" + _fx.Width + "x" + _fx.Height); }
        var gp = Math.Min(1.0, elapsed / 700.0);
        var gsize = 240 + gp * 520;
        _glow.Width = gsize; _glow.Height = gsize; _glow.Opacity = 0.35 * (1 - gp);
        Canvas.SetLeft(_glow, _screenW / 2 - gsize / 2);
        Canvas.SetTop(_glow, _screenH / 2 - gsize / 2);
        if (elapsed > 3000) _fxStart = 0;
    }

    private static void RestoreIsland()
    {
        try { if (_islandHidden && _islandHwnd != IntPtr.Zero) { ShowWindow(_islandHwnd, 5); _islandHidden = false; } } catch { }
    }

    private static double EaseOutQuint(double t) => 1 - Math.Pow(1 - t, 5);
    private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    private static void RestartAutoClose()
    {
        if (_autoClose == null)
        {
            _autoClose = new DispatcherTimer();
            _autoClose.Tick += (_, _) => { _autoClose.Stop(); StartOut(); };
        }
        _autoClose.Stop();
        _autoClose.Interval = TimeSpan.FromSeconds(_autoCloseSeconds);
        _autoClose.Start();
    }

    private static void MakeClickThrough()
    {
        try
        {
            _hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_hwnd == IntPtr.Zero) return;
            var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
        catch { }
    }

    private static void ApplyImage(byte[] bytes)
    {
        try
        {
            if (bytes == null || bytes.Length == 0) { _image.IsVisible = false; return; }
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            _image.Source = bmp;
            _image.IsVisible = true;
            var old = _bitmap; _bitmap = bmp;
            try { old?.Dispose(); } catch { }
        }
        catch { _image.IsVisible = false; }
    }

    public static void Hide()
    {
        if (!_visible) return;
        StartOut();
    }

    private static void EnsureCreated()
    {
        if (_window != null) return;

        _title = new TextBlock { Text = "教室喊话", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#FF9FE8FF")), Margin = new Thickness(0, 0, 0, 6) };
        _image = new Image { Stretch = Stretch.Uniform, MaxHeight = 280, MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };
        _body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 42, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, LineHeight = 54, MaxHeight = 190 };
        _meta = new TextBlock { FontSize = 17, Foreground = Brushes.White, Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0) };

        var panel = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_title); panel.Children.Add(_image); panel.Children.Add(_body); panel.Children.Add(_meta);

        var glass = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        glass.GradientStops.Add(new GradientStop(Color.Parse("#40FFFFFF"), 0.0));
        glass.GradientStops.Add(new GradientStop(Color.Parse("#10FFFFFF"), 0.45));
        glass.GradientStops.Add(new GradientStop(Color.Parse("#00000000"), 1.0));
        var highlight = new Border { Background = glass, IsHitTestVisible = false };

        var grid = new Grid(); grid.Children.Add(panel); grid.Children.Add(highlight);

        _scale = new ScaleTransform(0.16, 0.16);
        _move = new TranslateTransform(0, 0);
        var group = new TransformGroup(); group.Children.Add(_scale); group.Children.Add(_move);

        _card = new Border
        {
            Width = CardW, Height = CardH,
            Background = new SolidColorBrush(Color.Parse("#E00C1626")),
            BorderBrush = new SolidColorBrush(Color.Parse("#66FFFFFF")),
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(52, 40, 52, 40),
            ClipToBounds = true,
            RenderTransformOrigin = RelativePoint.TopLeft,
            RenderTransform = group,
            BoxShadow = BoxShadows.Parse("0 24 64 10 #B0000000"),
            Child = grid,
            IsHitTestVisible = false
        };

        // 涟漪层
        _fx = new Canvas { IsHitTestVisible = false, Background = Brushes.Transparent };
        var glowBrush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative)
        };
        glowBrush.GradientStops.Add(new GradientStop(Color.Parse("#8A7FD4FF"), 0.0));
        glowBrush.GradientStops.Add(new GradientStop(Color.Parse("#00000000"), 1.0));
        _glow = new Ellipse { Fill = glowBrush, Width = 0, Height = 0 };

        _rings = new Ellipse[3];
        for (int i = 0; i < _rings.Length; i++)
        {
            var ring = new Ellipse
            {
                Stroke = new SolidColorBrush(Color.Parse(i == 0 ? "#FF9FE8FF" : "#CCFFFFFF")),
                StrokeThickness = i == 0 ? 6 : 3,
                Width = 0, Height = 0, Opacity = 0
            };
            _rings[i] = ring;
            _fx.Children.Add(ring);
        }
        _fx.Children.Add(_glow);

        _canvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
        _canvas.Children.Add(_card);
        _canvas.Children.Add(_fx);

        _window = new Window
        {
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = new SolidColorBrush(Colors.Transparent),
            Content = _canvas
        };

        _window.Opened += (_, _) =>
        {
            try
            {
                var screen = _window.Screens.Primary ?? _window.Screens.All[0];
                var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
                _screenW = screen.Bounds.Width / scaling;
                _screenH = screen.Bounds.Height / scaling;
                _window.Width = _screenW;
                _window.Height = _screenH;
                _window.Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
                _canvas.Width = _screenW; _canvas.Height = _screenH;
                _fx.Width = _screenW; _fx.Height = _screenH;
                _cardLeft = _screenW / 2 - CardW / 2;
                _cardTop = _screenH / 2 - CardH / 2;
                Canvas.SetLeft(_card, _cardLeft);
                Canvas.SetTop(_card, _cardTop);
                MakeClickThrough();
            }
            catch (Exception ex) { Log?.Invoke("窗口初始化异常: " + ex.Message); }
        };
    }
}
