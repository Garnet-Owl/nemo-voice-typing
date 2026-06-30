using System;
using System.Windows;
using System.Windows.Input;
using VoiceTyping.Config;

namespace VoiceTyping;

public partial class FloatingPanel : Window
{
    private readonly AppConfig _config;
    public event Action? ToggleRequested;

    public FloatingPanel(AppConfig config)
    {
        _config = config;
        InitializeComponent();
        Topmost = _config.AlwaysOnTop;
        Loaded += OnLoaded;
        Closing += (_, e) => { e.Cancel = true; Hide(); PersistPosition(); };
        LocationChanged += (_, _) => PersistPosition();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var (w, h) = (Width, Height);
        if (!double.IsNaN(_config.PanelLeft) && !double.IsNaN(_config.PanelTop))
        {
            Left = _config.PanelLeft;
            Top = _config.PanelTop;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - w - 24;
            Top = workArea.Bottom - h - 24;
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

    private void OnMicClick(object sender, RoutedEventArgs e) => ToggleRequested?.Invoke();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    public void SetStatus(string status, bool listening)
    {
        StatusText.Text = status;
        MicButton.Background = listening
            ? System.Windows.Media.Brushes.IndianRed
            : new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x3D, 0x6A, 0x5C, 0xFF));
    }

    public void SetPartial(string text)
    {
        PartialText.Text = text;
    }
}
