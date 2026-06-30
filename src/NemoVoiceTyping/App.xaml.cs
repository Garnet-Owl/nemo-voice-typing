using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using NemoVoiceTyping.Config;
using NemoVoiceTyping.Services;

namespace NemoVoiceTyping;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstance;
    private AppConfig _config = null!;
    private FloatingPanel? _panel;
    private TaskbarIcon? _tray;
    private HotkeyService? _hotkey;
    private DictationController? _dictation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, "Global\\NemoVoiceTyping.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Nemo Voice Typing is already running.", "Nemo Voice Typing",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _config = AppConfig.Load();

        _tray = (TaskbarIcon)Resources["TrayIcon"];
        _tray.Icon = TrayIconFactory.Create();
        if (_tray.ContextMenu?.FindName("StartupMenu") is MenuItem startupItem)
            startupItem.IsChecked = StartupRegistration.IsEnabled();

        _panel = new FloatingPanel(_config);
        _panel.ToggleRequested += ToggleDictation;
        _panel.ExitRequested += () => Shutdown();
        _panel.Show();
        _panel.Hide();

        _hotkey = new HotkeyService();
        _hotkey.Pressed += ToggleDictation;
        if (!_hotkey.Register(_panel, _config.Hotkey))
        {
            MessageBox.Show($"Could not register hotkey '{_config.Hotkey}'. It may be in use.",
                "Nemo Voice Typing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _dictation = new DictationController(_config, _panel);
    }

    private void ToggleDictation()
    {
        if (_panel != null && !_panel.IsVisible) _panel.Show();
        _dictation?.Toggle();
    }

    private void OnShowPanel(object sender, RoutedEventArgs e) => _panel?.Show();
    private void OnToggleDictation(object sender, RoutedEventArgs e) => ToggleDictation();

    private void OnTrayLeftClick(object sender, RoutedEventArgs e)
    {
        if (_panel == null) return;
        if (_panel.IsVisible) _panel.Hide(); else _panel.Show();
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            StartupRegistration.SetEnabled(mi.IsChecked);
            _config.RunAtStartup = mi.IsChecked;
            _config.Save();
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => Shutdown();

    protected override void OnExit(ExitEventArgs e)
    {
        _dictation?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
