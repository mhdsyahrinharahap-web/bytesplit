using System.Drawing.Drawing2D;

namespace ByteSplit.UI;

public sealed class RefreshLinkForm : Form
{
    private readonly ThemePalette _palette;
    private readonly TextBox _txtNewUrl;

    public string ResultNewUrl => _txtNewUrl.Text.Trim();

    public RefreshLinkForm(string fileName, ThemePalette palette, Icon appIcon)
    {
        _palette = palette;

        Text = "Perbarui Link Kedaluwarsa";
        Icon = appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = _palette.Surface;
        ForeColor = _palette.Text;

        var lblFile = new Label
        {
            Text = $"File: {fileName}",
            Location = new Point(20, 18),
            Size = new Size(460, 22),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = _palette.Text,
            AutoEllipsis = true
        };

        var lblPrompt = new Label
        {
            Text = "Tempel tautan baru (ukuran file harus sama untuk melanjutkan progres):",
            Location = new Point(20, 46),
            AutoSize = true,
            ForeColor = _palette.TextSecondary
        };

        _txtNewUrl = new TextBox { BorderStyle = BorderStyle.None, Font = Font };
        var urlBox = new Panel
        {
            Location = new Point(20, 72),
            Size = new Size(368, 32),
            BackColor = _palette.SurfaceSubtle
        };
        _txtNewUrl.BackColor = _palette.SurfaceSubtle;
        _txtNewUrl.ForeColor = _palette.Text;
        _txtNewUrl.Location = new Point(10, 7);
        _txtNewUrl.Width = 348;
        urlBox.Controls.Add(_txtNewUrl);
        urlBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, urlBox.Width - 1, urlBox.Height - 1);
            using GraphicsPath path = ThemePalette.CreateRoundedRect(rect, 6);
            using var pen = new Pen(_txtNewUrl.Focused ? _palette.Primary : _palette.BorderStrong, 1f);
            e.Graphics.DrawPath(pen, path);
        };
        _txtNewUrl.GotFocus += (_, _) => urlBox.Invalidate();
        _txtNewUrl.LostFocus += (_, _) => urlBox.Invalidate();

        var btnPaste = new FluentButton { Text = "Tempel", Location = new Point(396, 72), Size = new Size(84, 32), Variant = FluentButtonVariant.Secondary };
        btnPaste.ApplyPalette(_palette);
        btnPaste.Click += (_, _) =>
        {
            if (Clipboard.ContainsText()) _txtNewUrl.Text = Clipboard.GetText().Trim();
        };

        var btnSave = new FluentButton
        {
            Text = "Lanjutkan Unduhan",
            Glyph = GlyphKind.Play,
            Variant = FluentButtonVariant.Primary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Height = 34
        };
        btnSave.ApplyPalette(_palette);
        btnSave.Size = btnSave.GetPreferredSize(Size.Empty);
        btnSave.Location = new Point(480 - btnSave.Width, 122);

        var btnCancel = new FluentButton
        {
            Text = "Batal",
            Variant = FluentButtonVariant.Secondary,
            DialogResult = DialogResult.Cancel,
            Height = 34
        };
        btnCancel.ApplyPalette(_palette);
        btnCancel.Size = btnCancel.GetPreferredSize(Size.Empty);
        btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - 8, 122);

        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtNewUrl.Text))
            {
                MessageBox.Show(this, "URL baru wajib diisi.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        ClientSize = new Size(500, btnSave.Bottom + 20);
        Controls.AddRange(new Control[] { lblFile, lblPrompt, urlBox, btnPaste, btnCancel, btnSave });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemePalette.ApplyWindowChrome(Handle, _palette.IsDark);
    }
}