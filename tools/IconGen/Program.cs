using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string outPath = args.Length > 0 ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "app.ico");

int[] sizes = { 16, 32, 48, 64, 128, 256 };
var pngs = new List<byte[]>();
foreach (var s in sizes) pngs.Add(RenderPng(s));

WriteIco(outPath, pngs, sizes);
Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length} bytes)");

static byte[] RenderPng(int s)
{
    using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float r = s * 0.22f;
        var bgRect = new RectangleF(s * 0.02f, s * 0.02f, s * 0.96f, s * 0.96f);
        using (var path = RoundRect(bgRect, r))
        using (var bg = new LinearGradientBrush(bgRect, Color.FromArgb(255, 122, 92, 255), Color.FromArgb(255, 86, 64, 220), 135f))
            g.FillPath(bg, path);

        // Lightning bolt: bright yellow, behind mic
        var bolt = new PointF[] {
            new(s * 0.62f, s * 0.10f),
            new(s * 0.32f, s * 0.55f),
            new(s * 0.50f, s * 0.55f),
            new(s * 0.40f, s * 0.92f),
            new(s * 0.78f, s * 0.42f),
            new(s * 0.58f, s * 0.42f),
            new(s * 0.72f, s * 0.10f),
        };
        using (var boltBrush = new SolidBrush(Color.FromArgb(255, 255, 214, 64)))
            g.FillPolygon(boltBrush, bolt);
        using (var boltOutline = new Pen(Color.FromArgb(180, 120, 80, 0), Math.Max(1f, s / 96f)))
            g.DrawPolygon(boltOutline, bolt);

        // Microphone capsule
        float micW = s * 0.30f, micH = s * 0.46f;
        float micX = s * 0.18f, micY = s * 0.18f;
        var micRect = new RectangleF(micX, micY, micW, micH);
        using (var path = RoundRect(micRect, micW * 0.5f))
        using (var grad = new LinearGradientBrush(micRect, Color.White, Color.FromArgb(255, 220, 220, 235), 90f))
            g.FillPath(grad, path);

        using (var line = new Pen(Color.FromArgb(120, 60, 60, 90), Math.Max(1f, s / 96f)))
        {
            float gx = micX + micW * 0.18f;
            float gw = micW * 0.64f;
            for (int i = 1; i <= 3; i++)
            {
                float y = micY + micH * (0.20f + 0.15f * i);
                g.DrawLine(line, gx, y, gx + gw, y);
            }
        }

        using (var stand = new Pen(Color.White, Math.Max(2f, s / 28f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float aX = micX - micW * 0.18f;
            float aY = micY + micH * 0.55f;
            float aW = micW * 1.36f;
            float aH = micH * 0.55f;
            g.DrawArc(stand, aX, aY, aW, aH, 0, 180);
            float postX = micX + micW * 0.50f;
            g.DrawLine(stand, postX, aY + aH * 0.5f, postX, micY + micH + s * 0.04f);
            g.DrawLine(stand, postX - s * 0.10f, micY + micH + s * 0.04f, postX + s * 0.10f, micY + micH + s * 0.04f);
        }
    }

    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static GraphicsPath RoundRect(RectangleF r, float radius)
{
    var path = new GraphicsPath();
    float d = radius * 2;
    path.AddArc(r.X, r.Y, d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

static void WriteIco(string path, List<byte[]> pngs, int[] sizes)
{
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)pngs.Count);

    int dirSize = 6 + 16 * pngs.Count;
    int offset = dirSize;
    for (int i = 0; i < pngs.Count; i++)
    {
        int s = sizes[i];
        bw.Write((byte)(s >= 256 ? 0 : s));
        bw.Write((byte)(s >= 256 ? 0 : s));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write((uint)pngs[i].Length);
        bw.Write((uint)offset);
        offset += pngs[i].Length;
    }
    foreach (var data in pngs) bw.Write(data);
}
