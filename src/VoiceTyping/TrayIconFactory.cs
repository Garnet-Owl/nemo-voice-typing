using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace VoiceTyping;

/// <summary>
/// Generates a minimal tray icon programmatically so we don't need to ship a .ico file.
/// </summary>
internal static class TrayIconFactory
{
    public static System.Drawing.Icon Create()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(255, 106, 92, 255));
            g.FillEllipse(bg, 1, 1, size - 2, size - 2);

            using var fg = new SolidBrush(Color.White);
            // mic body
            g.FillRoundedRect(fg, 12, 7, 8, 13, 4);
            // stand
            using var pen = new Pen(Color.White, 2);
            g.DrawArc(pen, 9, 13, 14, 12, 0, 180);
            g.DrawLine(pen, 16, 22, 16, 26);
            g.DrawLine(pen, 12, 26, 20, 26);
        }

        var hIcon = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    private static void FillRoundedRect(this Graphics g, Brush brush, int x, int y, int w, int h, int r)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
