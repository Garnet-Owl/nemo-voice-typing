using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

string sourcePath = args.Length > 0 ? args[0]
    : Path.Combine("src", "NemoVoiceTyping", "Assets", "app.png");
string outputPath = args.Length > 1 ? args[1]
    : Path.Combine("src", "NemoVoiceTyping", "Assets", "app.ico");

int[] sizes = { 16, 32, 48, 64, 128, 256 };
using var source = Image.FromFile(sourcePath);
var pngs = sizes.Select(size => RenderPng(source, size)).ToArray();
WriteIco(outputPath, pngs, sizes);
Console.WriteLine($"Wrote {outputPath} ({new FileInfo(outputPath).Length} bytes)");

static byte[] RenderPng(Image source, int size)
{
    using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Transparent);
    graphics.CompositingQuality = CompositingQuality.HighQuality;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.DrawImage(source, 0, 0, size, size);

    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

static void WriteIco(string path, byte[][] pngs, int[] sizes)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)pngs.Length);

    int offset = 6 + 16 * pngs.Length;
    for (int index = 0; index < pngs.Length; index++)
    {
        int size = sizes[index];
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)pngs[index].Length);
        writer.Write((uint)offset);
        offset += pngs[index].Length;
    }

    foreach (var png in pngs) writer.Write(png);
}
