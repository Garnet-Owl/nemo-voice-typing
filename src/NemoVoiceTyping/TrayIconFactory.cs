using System;
using System.IO;

namespace NemoVoiceTyping;

/// <summary>
/// Loads the app.ico embedded resource and exposes it as a <see cref="System.Drawing.Icon"/>
/// for the tray. The same .ico is set as the WPF window icon via the project's
/// &lt;ApplicationIcon&gt; property.
/// </summary>
internal static class TrayIconFactory
{
    public static System.Drawing.Icon Create()
    {
        var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
        using var stream = System.Windows.Application.GetResourceStream(uri).Stream;
        return new System.Drawing.Icon(stream);
    }
}

