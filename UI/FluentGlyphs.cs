using System.Drawing.Drawing2D;

namespace ByteSplit.UI;

public enum GlyphKind
{
    None,
    Add,
    Pause,
    Play,
    Refresh,
    Trash,
    Folder,
    Sun,
    Moon,
    Info,
    ChevronDown,
    AllItems,
    Downloading,
    CheckCircle,
    Archive,
    Document,
    Music,
    AppWindow,
    Video,
    OtherFile,
    CloudDownload
}

public static class FluentGlyphs
{
    public static void Draw(Graphics g, GlyphKind kind, Rectangle rect, Color color, float strokeWidth = 1.6f)
    {
        if (kind == GlyphKind.None || rect.Width <= 0 || rect.Height <= 0)
            return;

        SmoothingMode prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float x = rect.X;
        float y = rect.Y;
        float w = rect.Width;
        float h = rect.Height;
        float cx = x + w / 2f;
        float cy = y + h / 2f;

        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var brush = new SolidBrush(color);

        switch (kind)
        {
            case GlyphKind.Add:
                g.DrawLine(pen, cx, y + h * 0.2f, cx, y + h * 0.8f);
                g.DrawLine(pen, x + w * 0.2f, cy, x + w * 0.8f, cy);
                break;

            case GlyphKind.Pause:
                g.DrawLine(pen, x + w * 0.35f, y + h * 0.22f, x + w * 0.35f, y + h * 0.78f);
                g.DrawLine(pen, x + w * 0.65f, y + h * 0.22f, x + w * 0.65f, y + h * 0.78f);
                break;

            case GlyphKind.Play:
            {
                PointF[] pts =
                {
                    new(x + w * 0.30f, y + h * 0.22f),
                    new(x + w * 0.76f, cy),
                    new(x + w * 0.30f, y + h * 0.78f)
                };
                g.DrawPolygon(pen, pts);
                break;
            }

            case GlyphKind.Refresh:
            {
                var arcRect = new RectangleF(x + w * 0.18f, y + h * 0.18f, w * 0.64f, h * 0.64f);
                g.DrawArc(pen, arcRect, -45f, 290f);
                g.DrawLine(pen, x + w * 0.78f, y + h * 0.20f, x + w * 0.78f, y + h * 0.42f);
                g.DrawLine(pen, x + w * 0.56f, y + h * 0.42f, x + w * 0.78f, y + h * 0.42f);
                break;
            }

            case GlyphKind.Trash:
                g.DrawLine(pen, x + w * 0.20f, y + h * 0.28f, x + w * 0.80f, y + h * 0.28f);
                g.DrawLine(pen, x + w * 0.38f, y + h * 0.18f, x + w * 0.62f, y + h * 0.18f);
                g.DrawLine(pen, x + w * 0.28f, y + h * 0.28f, x + w * 0.32f, y + h * 0.82f);
                g.DrawLine(pen, x + w * 0.72f, y + h * 0.28f, x + w * 0.68f, y + h * 0.82f);
                g.DrawLine(pen, x + w * 0.32f, y + h * 0.82f, x + w * 0.68f, y + h * 0.82f);
                break;

            case GlyphKind.Folder:
            {
                PointF[] pts =
                {
                    new(x + w * 0.14f, y + h * 0.26f),
                    new(x + w * 0.42f, y + h * 0.26f),
                    new(x + w * 0.50f, y + h * 0.36f),
                    new(x + w * 0.86f, y + h * 0.36f),
                    new(x + w * 0.86f, y + h * 0.78f),
                    new(x + w * 0.14f, y + h * 0.78f)
                };
                g.DrawPolygon(pen, pts);
                break;
            }

            case GlyphKind.Sun:
            {
                float r = w * 0.20f;
                g.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4.0;
                    float cos = (float)Math.Cos(ang);
                    float sin = (float)Math.Sin(ang);
                    g.DrawLine(pen, cx + cos * w * 0.30f, cy + sin * h * 0.30f, cx + cos * w * 0.42f, cy + sin * h * 0.42f);
                }
                break;
            }

            case GlyphKind.Moon:
            {
                var arcRect = new RectangleF(x + w * 0.18f, y + h * 0.18f, w * 0.64f, h * 0.64f);
                g.DrawArc(pen, arcRect, 45f, 240f);
                g.DrawArc(pen, x + w * 0.34f, y + h * 0.14f, w * 0.48f, h * 0.48f, 95f, 155f);
                break;
            }

            case GlyphKind.Info:
                g.DrawEllipse(pen, x + w * 0.16f, y + h * 0.16f, w * 0.68f, h * 0.68f);
                g.DrawLine(pen, cx, y + h * 0.46f, cx, y + h * 0.68f);
                g.FillEllipse(brush, cx - 1.1f, y + h * 0.30f, 2.2f, 2.2f);
                break;

            case GlyphKind.ChevronDown:
                g.DrawLine(pen, x + w * 0.24f, y + h * 0.38f, cx, y + h * 0.64f);
                g.DrawLine(pen, cx, y + h * 0.64f, x + w * 0.76f, y + h * 0.38f);
                break;

            case GlyphKind.AllItems:
                g.DrawLine(pen, x + w * 0.20f, y + h * 0.30f, x + w * 0.80f, y + h * 0.30f);
                g.DrawLine(pen, x + w * 0.20f, cy, x + w * 0.80f, cy);
                g.DrawLine(pen, x + w * 0.20f, y + h * 0.70f, x + w * 0.80f, y + h * 0.70f);
                break;

            case GlyphKind.Downloading:
                g.DrawLine(pen, cx, y + h * 0.18f, cx, y + h * 0.66f);
                g.DrawLine(pen, x + w * 0.32f, y + h * 0.48f, cx, y + h * 0.66f);
                g.DrawLine(pen, x + w * 0.68f, y + h * 0.48f, cx, y + h * 0.66f);
                g.DrawLine(pen, x + w * 0.22f, y + h * 0.80f, x + w * 0.78f, y + h * 0.80f);
                break;

            case GlyphKind.CheckCircle:
                g.DrawEllipse(pen, x + w * 0.16f, y + h * 0.16f, w * 0.68f, h * 0.68f);
                g.DrawLine(pen, x + w * 0.32f, cy, x + w * 0.45f, y + h * 0.63f);
                g.DrawLine(pen, x + w * 0.45f, y + h * 0.63f, x + w * 0.68f, y + h * 0.37f);
                break;

            case GlyphKind.Archive:
                g.DrawRectangle(pen, x + w * 0.20f, y + h * 0.20f, w * 0.60f, h * 0.60f);
                g.DrawLine(pen, cx, y + h * 0.20f, cx, y + h * 0.80f);
                g.DrawLine(pen, x + w * 0.38f, y + h * 0.36f, x + w * 0.62f, y + h * 0.36f);
                g.DrawLine(pen, x + w * 0.38f, y + h * 0.56f, x + w * 0.62f, y + h * 0.56f);
                break;

            case GlyphKind.Document:
                g.DrawRectangle(pen, x + w * 0.22f, y + h * 0.16f, w * 0.56f, h * 0.68f);
                g.DrawLine(pen, x + w * 0.34f, y + h * 0.36f, x + w * 0.66f, y + h * 0.36f);
                g.DrawLine(pen, x + w * 0.34f, y + h * 0.50f, x + w * 0.66f, y + h * 0.50f);
                g.DrawLine(pen, x + w * 0.34f, y + h * 0.64f, x + w * 0.54f, y + h * 0.64f);
                break;

            case GlyphKind.Music:
                g.DrawLine(pen, x + w * 0.44f, y + h * 0.22f, x + w * 0.44f, y + h * 0.68f);
                g.DrawLine(pen, x + w * 0.44f, y + h * 0.22f, x + w * 0.74f, y + h * 0.30f);
                g.DrawEllipse(pen, x + w * 0.24f, y + h * 0.58f, w * 0.22f, h * 0.20f);
                break;

            case GlyphKind.AppWindow:
                g.DrawRectangle(pen, x + w * 0.16f, y + h * 0.22f, w * 0.68f, h * 0.56f);
                g.DrawLine(pen, x + w * 0.16f, y + h * 0.38f, x + w * 0.84f, y + h * 0.38f);
                break;

            case GlyphKind.Video:
            {
                g.DrawRectangle(pen, x + w * 0.16f, y + h * 0.24f, w * 0.68f, h * 0.52f);
                PointF[] tri =
                {
                    new(x + w * 0.42f, y + h * 0.38f),
                    new(x + w * 0.62f, cy),
                    new(x + w * 0.42f, y + h * 0.62f)
                };
                g.DrawPolygon(pen, tri);
                break;
            }

            case GlyphKind.OtherFile:
                g.DrawRectangle(pen, x + w * 0.22f, y + h * 0.16f, w * 0.56f, h * 0.68f);
                g.FillEllipse(brush, cx - 1.2f, cy - 1.2f, 2.4f, 2.4f);
                break;

            case GlyphKind.CloudDownload:
            {
                var cloudRect = new RectangleF(x + w * 0.16f, y + h * 0.22f, w * 0.68f, h * 0.42f);
                g.DrawArc(pen, cloudRect, 175f, 190f);
                g.DrawLine(pen, x + w * 0.22f, y + h * 0.52f, x + w * 0.78f, y + h * 0.52f);
                g.DrawLine(pen, cx, y + h * 0.44f, cx, y + h * 0.80f);
                g.DrawLine(pen, x + w * 0.36f, y + h * 0.66f, cx, y + h * 0.80f);
                g.DrawLine(pen, x + w * 0.64f, y + h * 0.66f, cx, y + h * 0.80f);
                break;
            }
        }

        g.SmoothingMode = prevSmoothing;
    }

    public static GlyphKind ForCategory(string? category) => category?.Trim().ToLowerInvariant() switch
    {
        "compressed" => GlyphKind.Archive,
        "documents" => GlyphKind.Document,
        "music" => GlyphKind.Music,
        "programs" => GlyphKind.AppWindow,
        "video" => GlyphKind.Video,
        _ => GlyphKind.OtherFile
    };
}