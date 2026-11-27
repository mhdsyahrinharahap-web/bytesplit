using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace ByteSplit.UI;

public enum FluentButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    DangerGhost
}

public sealed class FluentButton : Control
{
    private ThemePalette _palette = ThemePalette.Light;
    private FluentButtonVariant _variant = FluentButtonVariant.Secondary;
    private GlyphKind _glyph = GlyphKind.None;
    private bool _hovered;
    private bool _pressed;
    private bool _compactText;

    public FluentButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    public GlyphKind Glyph
    {
        get => _glyph;
        set { _glyph = value; UpdatePreferredBounds(); Invalidate(); }
    }

    public bool CompactText
    {
        get => _compactText;
        set { _compactText = value; UpdatePreferredBounds(); Invalidate(); }
    }

    public DialogResult DialogResult { get; set; } = DialogResult.None;

    public FluentButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);
        Cursor = Cursors.Hand;
        Height = 32;
        Padding = new Padding(11, 0, 11, 0);
    }

    public void ApplyPalette(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value ?? string.Empty; UpdatePreferredBounds(); Invalidate(); }
    }

    [AllowNull]
    public override Font Font
    {
        get => base.Font;
        set { base.Font = value; UpdatePreferredBounds(); Invalidate(); }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int iconPart = _glyph != GlyphKind.None ? 16 : 0;
        bool showLabel = !_compactText && !string.IsNullOrEmpty(Text);
        int gap = (iconPart > 0 && showLabel) ? 6 : 0;
        Size textSize = showLabel
            ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding)
            : Size.Empty;

        int padX = showLabel ? Padding.Left + Padding.Right : 16;
        int width = padX + iconPart + gap + textSize.Width;
        return new Size(Math.Max(34, width), Math.Max(30, Height));
    }

    private void UpdatePreferredBounds()
    {
        if (AutoSize)
        {
            Size = GetPreferredSize(Size.Empty);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
        {
            _hovered = false;
            _pressed = false;
        }
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (Enabled) { _hovered = true; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Enabled && e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool wasPressed = _pressed;
        _pressed = false;
        Invalidate();

        if (Enabled && wasPressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            if (DialogResult != DialogResult.None && FindForm() is Form form)
            {
                form.DialogResult = DialogResult;
            }
            OnClick(EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            if (DialogResult != DialogResult.None && FindForm() is Form form)
            {
                form.DialogResult = DialogResult;
            }
            OnClick(EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color bg;
        Color fg;
        Color border;

        if (!Enabled)
        {
            bg = _palette.DisabledBg;
            fg = _palette.DisabledText;
            border = _palette.DisabledBorder;
        }
        else
        {
            switch (_variant)
            {
                case FluentButtonVariant.Primary:
                    bg = _pressed ? _palette.PrimaryPressed : (_hovered ? _palette.PrimaryHover : _palette.Primary);
                    fg = _palette.PrimaryText;
                    border = bg;
                    break;

                case FluentButtonVariant.DangerGhost:
                    bg = _pressed ? _palette.DangerSoft : (_hovered ? _palette.DangerSoft : _palette.Surface);
                    fg = (_hovered || _pressed) ? _palette.Danger : _palette.Text;
                    border = (_hovered || _pressed) ? _palette.Danger : _palette.BorderStrong;
                    break;

                case FluentButtonVariant.Ghost:
                    bg = _pressed ? _palette.SurfaceSelected : (_hovered ? _palette.SurfaceHover : Color.Transparent);
                    fg = _palette.Text;
                    border = (_hovered || _pressed) ? _palette.Border : Color.Transparent;
                    break;

                default:
                    bg = _pressed ? _palette.SurfaceSelected : (_hovered ? _palette.SurfaceHover : _palette.Surface);
                    fg = _palette.Text;
                    border = _palette.BorderStrong;
                    break;
            }
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = ThemePalette.CreateRoundedRect(rect, 6);

        if (bg != Color.Transparent)
        {
            using var bgBrush = new SolidBrush(bg);
            g.FillPath(bgBrush, path);
        }

        if (border != Color.Transparent)
        {
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }

        bool showLabel = !_compactText && !string.IsNullOrEmpty(Text);
        int iconSize = 15;
        int gap = (_glyph != GlyphKind.None && showLabel) ? 6 : 0;
        Size textSize = showLabel
            ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding)
            : Size.Empty;

        int contentW = (_glyph != GlyphKind.None ? iconSize : 0) + gap + textSize.Width;
        int startX = Math.Max(6, (Width - contentW) / 2);

        if (_glyph != GlyphKind.None)
        {
            var iconRect = new Rectangle(startX, (Height - iconSize) / 2, iconSize, iconSize);
            FluentGlyphs.Draw(g, _glyph, iconRect, fg, 1.6f);
            startX += iconSize + gap;
        }

        if (showLabel)
        {
            var textRect = new Rectangle(startX, 0, Width - startX - 4, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }
    }
}

public sealed class FluentCheckBox : Control
{
    private ThemePalette _palette = ThemePalette.Light;
    private bool _checked = true;
    private bool _hovered;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public FluentCheckBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);
        Cursor = Cursors.Hand;
        Height = 32;
    }

    public void ApplyPalette(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value ?? string.Empty; UpdateSize(); Invalidate(); }
    }

    [AllowNull]
    public override Font Font
    {
        get => base.Font;
        set { base.Font = value; UpdateSize(); Invalidate(); }
    }

    private void UpdateSize()
    {
        Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Width = 34 + 8 + textSize.Width + 6;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Checked = !Checked;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            Checked = !Checked;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int trackW = 32;
        int trackH = 18;
        var trackRect = new Rectangle(1, (Height - trackH) / 2, trackW, trackH);
        using GraphicsPath trackPath = ThemePalette.CreateRoundedRect(trackRect, trackH / 2);

        Color trackFill = _checked
            ? (_hovered ? _palette.PrimaryHover : _palette.Primary)
            : (_hovered ? _palette.SurfaceHover : _palette.SurfaceSubtle);
        Color trackBorder = _checked ? trackFill : _palette.BorderStrong;

        using (var fillBrush = new SolidBrush(trackFill))
            g.FillPath(fillBrush, trackPath);
        using (var borderPen = new Pen(trackBorder, 1f))
            g.DrawPath(borderPen, trackPath);

        int knobSize = 12;
        int knobX = _checked ? (trackRect.Right - knobSize - 3) : (trackRect.X + 3);
        int knobY = trackRect.Y + (trackH - knobSize) / 2;
        Color knobColor = _checked ? Color.White : _palette.TextMuted;
        using (var knobBrush = new SolidBrush(knobColor))
            g.FillEllipse(knobBrush, knobX, knobY, knobSize, knobSize);

        var textRect = new Rectangle(trackRect.Right + 8, 0, Width - trackRect.Right - 8, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, _palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
}

public sealed class FluentComboBox : Control
{
    private ThemePalette _palette = ThemePalette.Light;
    private readonly List<string> _items = new();
    private int _selectedIndex = -1;
    private bool _hovered;
    private bool _dropdownOpen;

    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<string> Items => _items;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex == clamped) return;
            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedItem => (_selectedIndex >= 0 && _selectedIndex < _items.Count) ? _items[_selectedIndex] : null;

    public FluentComboBox()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.Selectable,
            true);
        Cursor = Cursors.Hand;
        Size = new Size(124, 32);
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_items.Count > 0 && _selectedIndex < 0)
            _selectedIndex = 0;
        Invalidate();
    }

    public void ApplyPalette(ThemePalette palette)
    {
        _palette = palette;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && !_dropdownOpen)
        {
            Focus();
            ShowPopup();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0) return;

        if (e.KeyCode == Keys.Down)
        {
            e.Handled = true;
            SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
        }
        else if (e.KeyCode == Keys.Up)
        {
            e.Handled = true;
            SelectedIndex = Math.Max(0, _selectedIndex - 1);
        }
        else if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            ShowPopup();
        }
    }

    private void ShowPopup()
    {
        if (_items.Count == 0) return;

        _dropdownOpen = true;
        Invalidate();

        int itemHeight = 30;
        var listBox = new ListBox
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = itemHeight,
            Width = Math.Max(Width, 132),
            Height = _items.Count * itemHeight + 4,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Font = Font
        };

        int hoverIdx = _selectedIndex;
        foreach (string item in _items) listBox.Items.Add(item);
        listBox.SelectedIndex = _selectedIndex;

        var dropDown = new ToolStripDropDown
        {
            Margin = Padding.Empty,
            Padding = new Padding(1),
            BackColor = _palette.BorderStrong
        };

        listBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= _items.Count) return;
            bool isSelected = e.Index == _selectedIndex;
            bool isHover = e.Index == hoverIdx;

            Color bg = isSelected
                ? _palette.PrimarySoft
                : (isHover ? _palette.SurfaceHover : _palette.Surface);
            Color fg = isSelected ? _palette.Primary : _palette.Text;

            using var brush = new SolidBrush(bg);
            e.Graphics.FillRectangle(brush, e.Bounds);

            var textRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, _items[e.Index], Font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        listBox.MouseMove += (_, e) =>
        {
            int idx = listBox.IndexFromPoint(e.Location);
            if (idx != hoverIdx)
            {
                hoverIdx = idx;
                listBox.Invalidate();
            }
        };

        listBox.MouseUp += (_, e) =>
        {
            int idx = listBox.IndexFromPoint(e.Location);
            if (idx >= 0 && idx < _items.Count)
            {
                SelectedIndex = idx;
                dropDown.Close();
            }
        };

        listBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && listBox.SelectedIndex >= 0)
            {
                SelectedIndex = listBox.SelectedIndex;
                dropDown.Close();
            }
        };

        var host = new ToolStripControlHost(listBox)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = listBox.Size
        };
        dropDown.Items.Add(host);

        SynchronizationContext? syncCtx = SynchronizationContext.Current;
        dropDown.Closed += (_, _) =>
        {
            _dropdownOpen = false;
            if (!IsDisposed) Invalidate();

            // Tunda Dispose ke antrean pesan berikutnya agar event closing internal WinForms selesai dulu
            if (syncCtx != null)
            {
                syncCtx.Post(_ =>
                {
                    if (!dropDown.IsDisposed) dropDown.Dispose();
                }, null);
            }
            else if (!dropDown.IsDisposed)
            {
                dropDown.Dispose();
            }
        };

        dropDown.Show(this, new Point(0, Height + 3));
        listBox.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = ThemePalette.CreateRoundedRect(rect, 6);

        Color bg = (_hovered || _dropdownOpen) ? _palette.SurfaceHover : _palette.Surface;
        Color border = _dropdownOpen ? _palette.Primary : _palette.BorderStrong;

        using (var bgBrush = new SolidBrush(bg))
            g.FillPath(bgBrush, path);
        using (var borderPen = new Pen(border, 1f))
            g.DrawPath(borderPen, path);

        string display = SelectedItem ?? string.Empty;
        var textRect = new Rectangle(10, 0, Width - 30, Height);
        TextRenderer.DrawText(g, display, Font, textRect, _palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        var chevRect = new Rectangle(Width - 22, (Height - 12) / 2, 12, 12);
        FluentGlyphs.Draw(g, GlyphKind.ChevronDown, chevRect, _palette.TextMuted, 1.6f);
    }
}