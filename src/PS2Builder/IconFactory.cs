using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PS2Builder;

public static class IconFactory
{
    public static void Create(string? source, string outputIco)
    {
        using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.FromArgb(18, 22, 35));
        if (!string.IsNullOrWhiteSpace(source) && File.Exists(source))
        {
            using var img = Image.FromFile(source); g.DrawImage(img, new Rectangle(0, 0, 256, 256));
        }
        else
        {
            using var pen = new Pen(Color.White, 8); g.DrawRoundedRectangle(pen, new Rectangle(12, 12, 232, 232), 28);
            using var font = new Font("Segoe UI", 58, FontStyle.Bold, GraphicsUnit.Pixel); using var small = new Font("Segoe UI Symbol", 54, FontStyle.Bold, GraphicsUnit.Pixel);
            var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("PS2", font, Brushes.White, new RectangleF(0, 35, 256, 100), f); g.DrawString("▶", small, Brushes.White, new RectangleF(0, 120, 256, 90), f);
        }
        var h = bmp.GetHicon();
        try { using var icon = Icon.FromHandle(h); using var fs = File.Create(outputIco); icon.Save(fs); }
        finally { DestroyIcon(h); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);

    static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle r, int radius)
    {
        using var p = new GraphicsPath(); int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right-d, r.Y, d, d, 270, 90); p.AddArc(r.Right-d, r.Bottom-d, d, d, 0, 90); p.AddArc(r.X, r.Bottom-d, d, d, 90, 90); p.CloseFigure(); g.DrawPath(pen, p);
    }
}
