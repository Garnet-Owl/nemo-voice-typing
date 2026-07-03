using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NemoVoiceTyping.Config;

namespace NemoVoiceTyping;

public partial class FloatingPanel : Window
{
    private readonly AppConfig _config;
    private bool _listening;
    private readonly Rectangle[] _bars;
    private readonly DispatcherTimer _decayTimer;
    private readonly DispatcherTimer _loadingTimer;
    private double _currentLevel;
    private double _loadingPhase;

    public event Action? ToggleRequested;
    public event Action? ExitRequested;
    public event Action<string>? StatusChanged;

    public FloatingPanel(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        Topmost = _config.AlwaysOnTop;
        Loaded += OnLoaded;
        Closing += (_, e) => { e.Cancel = true; Hide(); PersistPosition(); };
        LocationChanged += (_, _) => PersistPosition();

        _bars = new[] { Bar0, Bar1, Bar2, Bar3, Bar4 };

        _decayTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _decayTimer.Tick += (_, _) => Decay();

        _loadingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _loadingTimer.Tick += (_, _) => PulseLoading();

        LoadingText.Visibility = Visibility.Collapsed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!double.IsNaN(_config.PanelLeft) && !double.IsNaN(_config.PanelTop))
        {
            Left = _config.PanelLeft;
            Top = _config.PanelTop;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            // Default to middle of the right edge with a small margin.
            Left = wa.Right - Width - 16;
            Top = wa.Top + (wa.Height - Height) / 2;
        }

        // The saved position may be stale (resolution change, monitor
        // unplugged, taskbar moved) — pull the panel back on-screen.
        ClampToWorkArea();
        IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true) ClampToWorkArea();
        };
    }

    /// <summary>
    /// Keeps the whole panel inside the work area (screen minus taskbar)
    /// of the monitor it is currently on, so it can never be lost past a
    /// screen edge or hidden under the taskbar.
    /// </summary>
    private void ClampToWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not { } target) return;

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        // Work area is in physical pixels; Left/Top are DIPs.
        var toDip = target.TransformFromDevice;
        var min = toDip.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
        var max = toDip.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));

        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        // Max-then-min so that if the panel is somehow larger than the
        // work area, the top-left corner (with the mic button) wins.
        var left = Math.Max(min.X, Math.Min(Left, max.X - width));
        var top = Math.Max(min.Y, Math.Min(Top, max.Y - height));

        if (left != Left) Left = left;
        if (top != Top) Top = top;
    }

    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private void PersistPosition()
    {
        if (WindowState != WindowState.Minimized && IsLoaded)
        {
            _config.PanelLeft = Left;
            _config.PanelTop = Top;
            _config.Save();
        }
    }

    private void OnDragBegin(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var before = (Left, Top);
        // DragMove blocks until the button is released, so the snap-back
        // happens the instant the user drops the panel past an edge.
        DragMove();
        ClampToWorkArea();
        // A press-and-release with no movement is a click on the pill's
        // empty space (beside the mic button) — open the timeout picker.
        if ((Left, Top) == before) ToggleTimeoutPopup();
    }

    private static readonly int[] TimeoutPresets = { 15, 30, 60, 300, 600, 3600 };

    private void ToggleTimeoutPopup()
    {
        if (TimeoutPopup.IsOpen)
        {
            TimeoutPopup.IsOpen = false;
            return;
        }
        BuildTimeoutOptions();
        CustomTimeoutBox.Text = Array.IndexOf(TimeoutPresets, _config.IdleTimeoutSeconds) >= 0
            ? ""
            : Services.DurationText.Format(_config.IdleTimeoutSeconds);
        TimeoutPopup.IsOpen = true;
    }

    private void BuildTimeoutOptions()
    {
        TimeoutGrid.Children.Clear();
        foreach (var seconds in TimeoutPresets)
        {
            var button = new System.Windows.Controls.Button
            {
                Style = (Style)Resources["TimeoutOption"],
                Content = Services.DurationText.Format(seconds),
                Tag = seconds,
            };
            if (seconds == _config.IdleTimeoutSeconds)
                button.Background = (Brush)Resources["OptionAccent"];
            button.Click += (s, _) => ApplyTimeout((int)((FrameworkElement)s).Tag);
            TimeoutGrid.Children.Add(button);
        }
    }

    private void ApplyTimeout(int seconds)
    {
        _config.IdleTimeoutSeconds = seconds;
        _config.Save();
        TimeoutPopup.IsOpen = false;
    }

    private void OnCustomTimeoutSet(object sender, RoutedEventArgs e) => ApplyCustomTimeout();

    private void OnCustomTimeoutKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyCustomTimeout();
    }

    private void ApplyCustomTimeout()
    {
        if (Services.DurationText.TryParseSeconds(CustomTimeoutBox.Text, out var seconds))
        {
            ApplyTimeout(seconds);
        }
        else
        {
            CustomTimeoutBox.BorderBrush = Brushes.IndianRed;
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ContextMenu != null)
        {
            fe.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void OnMicClick(object sender, RoutedEventArgs e) => ToggleRequested?.Invoke();
    private void OnHide(object sender, RoutedEventArgs e) => Hide();
    private void OnExit(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();

    public void SetListening(bool listening)
    {
        _listening = listening;
        MicButton.Background = listening
            ? (Brush)Resources["MicActive"]
            : (Brush)Resources["MicIdle"];

        if (listening) _decayTimer.Start();
        else
        {
            _decayTimer.Stop();
            _currentLevel = 0;
            foreach (var b in _bars) b.Height = 4;
        }
    }

    public void SetLoading(bool loading)
    {
        if (loading)
        {
            LoadingText.Visibility = Visibility.Visible;
            _loadingTimer.Start();
        }
        else
        {
            _loadingTimer.Stop();
            LoadingText.Visibility = Visibility.Collapsed;
            LoadingText.Text = "";
            foreach (var b in _bars) b.Opacity = 1;
            StatusChanged?.Invoke("");
        }
    }

    public void SetLoadingText(string text)
    {
        LoadingText.Text = text;
        StatusChanged?.Invoke(text);
    }

    private void PulseLoading()
    {
        _loadingPhase += 0.14;
        for (int i = 0; i < _bars.Length; i++)
        {
            var op = 0.4 + 0.6 * (0.5 + 0.5 * Math.Sin(_loadingPhase - i * 0.7));
            _bars[i].Opacity = op;
            _bars[i].Height = 10;
        }
    }

    /// <summary>0..1 instantaneous level from the audio thread.</summary>
    public void PushAudioLevel(double level)
    {
        if (!_listening) return;
        var shaped = Math.Pow(Math.Clamp(level, 0, 1), 0.5);
        if (shaped > _currentLevel) _currentLevel = shaped;
    }

    private void Decay()
    {
        const double maxBar = 22.0;
        const double minBar = 4.0;
        ReadOnlySpan<double> shape = stackalloc double[] { 0.55, 0.8, 1.0, 0.8, 0.55 };
        var t = Environment.TickCount;
        for (int i = 0; i < _bars.Length; i++)
        {
            var wobble = 0.85 + 0.15 * Math.Sin((t / 80.0) + i * 0.7);
            var target = minBar + (maxBar - minBar) * _currentLevel * shape[i] * wobble;
            _bars[i].Height = Math.Max(minBar, target);
        }
        _currentLevel *= 0.86;
    }
}
