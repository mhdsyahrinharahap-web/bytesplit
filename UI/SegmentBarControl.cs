using System.Drawing.Drawing2D;
using ByteSplit.Models;

namespace ByteSplit.UI;

public sealed class SegmentBarControl : Control
{
    private List<ChunkInfo> _chunks = new();
    private int _fallbackPercent;
    private ThemePalette _palette = ThemePalette.Light;

    public SegmentBarControl()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.ResizeRedraw |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint,
            true);
        Height = 14;
    }

    public void SetData(List<ChunkInfo>? chunks, int fallbackPercent, ThemePalette palette)
    {
        _chunks = chunks ?? new List<ChunkInfo>();
        _fallbackPercent = Math.Clamp(fallbackPercent, 0, 100);
        _palette = palette;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var trackBrush = new SolidBrush(_palette.SurfaceSubtle);
        using var borderPen = new Pen(_palette.BorderStrong, 1f);
        using var activeBrush = new SolidBrush(_palette.Primary);
        using var pausedBrush = new SolidBrush(_palette.Warning);
        using var doneBrush = new SolidBrush(_palette.Success);

        int count = _chunks.Count > 0 ? _chunks.Count : 1;
        int gap = count > 1 ? 4 : 0;
        float totalGap = gap * (count - 1);
        float segWidth = Math.Max(4f, (Width - 1 - totalGap) / count);

        for (int i = 0; i < count; i++)
        {
            int x = (int)Math.Round(i * (segWidth + gap));
            int w = (int)Math.Max(4, Math.Round(segWidth));
            var rect = new Rectangle(x, 0, w, Height - 1);

            using GraphicsPath segPath = ThemePalette.CreateRoundedRect(rect, 3);
            g.FillPath(trackBrush, segPath);

            float pct;
            Brush fillBrush;

            if (_chunks.Count > 0)
            {
                ChunkInfo c = _chunks[i];
                bool isDone = c.Status is "done" or "completed";
                bool isPausedOrRetry = c.Status is "paused" or "retrying";
                pct = isDone
                    ? 100f
                    : (c.Size > 0 ? Math.Clamp((float)c.Downloaded / c.Size * 100f, 0f, 100f) : 0f);
                fillBrush = isDone ? doneBrush : (isPausedOrRetry ? pausedBrush : activeBrush);
            }
            else
            {
                pct = _fallbackPercent;
                fillBrush = _fallbackPercent >= 100 ? doneBrush : activeBrush;
            }

            if (pct > 0f)
            {
                int fillW = (int)Math.Round(rect.Width * (pct / 100f));
                if (fillW > 0)
                {
                    g.SetClip(segPath);
                    g.FillRectangle(fillBrush, rect.X, rect.Y, fillW, rect.Height + 1);
                    g.ResetClip();
                }
            }

            g.DrawPath(borderPen, segPath);
        }
    }
}