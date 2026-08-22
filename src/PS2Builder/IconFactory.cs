using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PS2Builder;

public static class IconFactory
{
    static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    public static void Create(string? source, string outputIco)
    {
        using var sourceImage = LoadSourceImage(source);

        var images = new List<byte[]>(IconSizes.Length);
        foreach (var size in IconSizes)
        {
            using var bmp = Render(sourceImage, size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            images.Add(ms.ToArray());
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputIco)!);
        using var fs = File.Create(outputIco);
        using var writer = new BinaryWriter(fs);

        // ICONDIR
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        var offset = 6 + images.Count * 16;
        for (var i = 0; i < images.Count; i++)
        {
            var size = IconSizes[i];
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0); // palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var png in images)
            writer.Write(png);
    }

    static Image? LoadSourceImage(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            return null;

        if (string.Equals(Path.GetExtension(source), ".ico", StringComparison.OrdinalIgnoreCase))
        {
            using var original = new Icon(source);
            using var icon = new Icon(original, 256, 256);
            return icon.ToBitmap();
        }

        // Clone the bitmap so Image.FromFile does not keep the user's artwork locked
        // during the rest of the build process.
        using var loaded = Image.FromFile(source);
        return new Bitmap(loaded);
    }

    static Bitmap Render(Image? source, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        if (source is not null)
        {
            var scale = Math.Min((double)size / source.Width, (double)size / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var x = (size - width) / 2;
            var y = (size - height) / 2;
            g.DrawImage(source, new Rectangle(x, y, width, height));
            return bmp;
        }

        g.Clear(Color.FromArgb(18, 22, 35));
        var border = Math.Max(1, size / 32);
        var inset = Math.Max(1, size / 20);
        var radius = Math.Max(2, size / 9);
        using var pen = new Pen(Color.White, border);
        g.DrawRoundedRectangle(pen, new Rectangle(inset, inset, size - inset * 2 - 1, size - inset * 2 - 1), radius);

        using var font = new Font("Segoe UI", Math.Max(5, size * 0.23f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var playFont = new Font("Segoe UI Symbol", Math.Max(5, size * 0.21f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("PS2", font, Brushes.White, new RectangleF(0, size * 0.15f, size, size * 0.40f), format);
        g.DrawString("▶", playFont, Brushes.White, new RectangleF(0, size * 0.47f, size, size * 0.34f), format);
        return bmp;
    }

    static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle r, int radius)
    {
        using var p = new GraphicsPath();
        var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        g.DrawPath(pen, p);
    }
}
