using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconArtwork
{
    private static readonly Color Ink = Color.FromArgb(242, 242, 242);
    private static readonly Color Accent = Color.FromArgb(96, 205, 255);
    private static readonly Color Canvas = Color.FromArgb(32, 32, 32);

    // The final parameter is an arc diameter (twice the visible corner radius).
    private static GraphicsPath Round(float x, float y, float width, float height, float diameter)
    {
        GraphicsPath path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Pen Outline(Color color, float width)
    {
        Pen pen = new Pen(color, width);
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;
        return pen;
    }

    private static void DrawBattery(Graphics graphics, Pen ink, Pen accent, bool withBolt)
    {
        using (GraphicsPath body = Round(10, 20, 40, 25, 7))
            graphics.DrawPath(ink, body);
        graphics.DrawLine(ink, 55, 28, 55, 37);

        if (withBolt)
        {
            graphics.DrawLines(accent, new PointF[]
            {
                new PointF(33, 25), new PointF(25, 33), new PointF(32, 33),
                new PointF(28, 41), new PointF(38, 31), new PointF(31, 31)
            });
        }
        else
        {
            graphics.DrawLine(accent, 19, 27, 19, 38);
            graphics.DrawLine(accent, 28, 27, 28, 38);
            graphics.DrawLine(accent, 37, 27, 37, 38);
        }
    }

    private static void DrawLeaf(Graphics graphics, Pen ink, Pen accent)
    {
        using (GraphicsPath leaf = new GraphicsPath())
        {
            leaf.AddBezier(17, 45, 5, 25, 22, 13, 49, 14);
            leaf.AddBezier(49, 14, 51, 38, 37, 52, 17, 45);
            leaf.CloseFigure();
            graphics.DrawPath(ink, leaf);
        }
        graphics.DrawLine(accent, 15, 49, 40, 25);
        graphics.DrawLine(accent, 28, 37, 28, 27);
        graphics.DrawLine(accent, 28, 37, 38, 37);
    }

    private static void DrawBolt(Graphics graphics, Pen ink, Pen accent)
    {
        graphics.DrawPolygon(ink, new PointF[]
        {
            new PointF(36, 12), new PointF(18, 35), new PointF(29, 35),
            new PointF(25, 52), new PointF(46, 27), new PointF(34, 27)
        });
        graphics.DrawLine(accent, 29, 35, 25, 52);
    }

    private static void DrawGauge(Graphics graphics, Pen ink, Pen accent)
    {
        graphics.DrawArc(ink, 12, 15, 40, 40, 180, 180);
        graphics.DrawLine(ink, 17, 46, 47, 46);
        graphics.DrawLine(ink, 18, 28, 21, 30);
        graphics.DrawLine(ink, 32, 19, 32, 22);
        graphics.DrawLine(ink, 46, 28, 43, 30);
        graphics.DrawLine(accent, 32, 35, 43, 24);
        graphics.DrawEllipse(accent, 29, 32, 6, 6);
    }

    public static void Generate(string folder)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        for (int kind = 0; kind < 5; kind++)
        {
            byte[][] images = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bitmap = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (Brush background = new SolidBrush(Canvas))
                using (Pen ink = Outline(Ink, 3))
                using (Pen accent = Outline(Accent, 3))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.ScaleTransform(sizes[i] / 64f, sizes[i] / 64f);
                    using (GraphicsPath tile = Round(2, 2, 60, 60, 16))
                        graphics.FillPath(background, tile);

                    if (kind == 0 || kind == 1)
                        DrawBattery(graphics, ink, accent, kind == 0);
                    else if (kind == 2)
                        DrawLeaf(graphics, ink, accent);
                    else if (kind == 3)
                        DrawBolt(graphics, ink, accent);
                    else
                        DrawGauge(graphics, ink, accent);

                    using (MemoryStream stream = new MemoryStream())
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                        images[i] = stream.ToArray();
                    }
                }
            }

            using (BinaryWriter writer = new BinaryWriter(File.Create(Path.Combine(folder, "Icon" + kind + ".ico"))))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                uint offset = (uint)(6 + 16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)images[i].Length);
                    writer.Write(offset);
                    offset += (uint)images[i].Length;
                }
                foreach (byte[] image in images)
                    writer.Write(image);
            }
        }
    }
}