using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VoiceTyping.Config;

namespace VoiceTyping;

public partial class FloatingPanel : Window
{
    private readonly AppConfig _config;
    private bool _listening;
    private readonly Rectangle[] _bars;
    private readonly DispatcherTimer _decayTimer;
    private double _currentLevel;

    public event Action? ToggleRequested;
    public event Action? ExitRequested;

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
    }

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
        if (e.ChangedButton == MouseButton.Left) DragMove();
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
