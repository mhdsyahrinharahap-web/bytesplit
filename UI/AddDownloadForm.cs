using System.Drawing.Drawing2D;

namespace ByteSplit.UI;

public sealed class AddDownloadForm : Form
{
    private readonly ThemePalette _palette;
    private readonly TextBox _txtUrl;
    private readonly TextBox _txtCustomName;
    private readonly TextBox _txtFolder;
    private readonly FluentComboBox _cmbConnections;
    private readonly FluentCheckBox _chkAutoCategorize;

    public string ResultUrl => _txtUrl.Text.Trim();
    public string ResultCustomName => _txtCustomName.Text.Trim();
    public string ResultFolder => _txtFolder.Text.Trim();
    public int ResultConnections => int.TryParse(_cmbConnections.SelectedItem?.Split(' ')[0], out int c) ? c : 8;
    public bool ResultAutoCategorize => _chkAutoCategorize.Checked;

    public AddDownloadForm(string defaultFolder, string? prefillUrl, bool fromClipboard, ThemePalette palette, Icon appIcon)
    {
        _palette = palette;

        Text = "Tambah Unduhan Baru";
        Icon = appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = _palette.Surface;
        ForeColor = _palette.Text;

        int y = 18;

        if (fromClipboard)
        {
            var banner = new Panel
            {
                Location = new Point(20, y),
                Size = new Size(476, 32)
            };
            banner.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, banner.Width - 1, banner.Height - 1);
                using GraphicsPath path = ThemePalette.CreateRoundedRect(rect, 6);
                using var brush = new SolidBrush(_palette.PrimarySoft);
                e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(
                    e.Graphics,
                    "Tautan unduhan terdeteksi otomatis dari clipboard.",
                    new Font("Segoe UI", 9f, FontStyle.Bold),
                    rect,
                    _palette.Primary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            Controls.Add(banner);
            y += 42;
        }

        var lblUrl = new Label { Text = "URL Unduhan (HTTP / HTTPS):", Location = new Point(20, y), AutoSize = true, ForeColor = _palette.TextSecondary };
        y += 24;
        _txtUrl = new TextBox { BorderStyle = BorderStyle.None, Text = prefillUrl ?? string.Empty, Font = Font };
        var urlBox = WrapInput(_txtUrl, 20, y, 384);
        var btnPaste = new FluentButton { Text = "Tempel", Location = new Point(412, y), Size = new Size(84, 32), Variant = FluentButtonVariant.Secondary };
        btnPaste.ApplyPalette(_palette);
        btnPaste.Click += (_, _) =>
        {
            if (Clipboard.ContainsText()) _txtUrl.Text = Clipboard.GetText().Trim();
        };

        y += 44;
        var lblCustom = new Label { Text = "Simpan Sebagai (Opsional):", Location = new Point(20, y), AutoSize = true, ForeColor = _palette.TextSecondary };
        y += 24;
        _txtCustomName = new TextBox
        {
            BorderStyle = BorderStyle.None,
            PlaceholderText = "Kosongkan untuk memakai nama asli dari server",
            Font = Font
        };
        var customBox = WrapInput(_txtCustomName, 20, y, 476);

        y += 44;
        var lblFolder = new Label { Text = "Folder Penyimpanan:", Location = new Point(20, y), AutoSize = true, ForeColor = _palette.TextSecondary };
        y += 24;
        _txtFolder = new TextBox { BorderStyle = BorderStyle.None, Text = defaultFolder, Font = Font };
        var folderBox = WrapInput(_txtFolder, 20, y, 384);
        var btnBrowse = new FluentButton { Text = "Pilih...", Glyph = GlyphKind.Folder, Location = new Point(412, y), Size = new Size(84, 32), Variant = FluentButtonVariant.Secondary };
        btnBrowse.ApplyPalette(_palette);
        btnBrowse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _txtFolder.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtFolder.Text = dlg.SelectedPath;
        };

        y += 44;
        var lblConn = new Label { Text = "Jumlah Koneksi:", Location = new Point(20, y), AutoSize = true, ForeColor = _palette.TextSecondary };
        y += 24;
        _cmbConnections = new FluentComboBox { Location = new Point(20, y), Size = new Size(156, 32) };
        _cmbConnections.SetItems(new[] { "1 Koneksi", "2 Koneksi", "4 Koneksi", "8 Koneksi", "12 Koneksi", "16 Koneksi" });
        _cmbConnections.SelectedIndex = 3;
        _cmbConnections.ApplyPalette(_palette);

        _chkAutoCategorize = new FluentCheckBox
        {
            Text = "Kelompokkan ke folder kategori",
            Location = new Point(192, y),
            Checked = true
        };
        _chkAutoCategorize.ApplyPalette(_palette);

        y += 50;
        var btnStart = new FluentButton
        {
            Text = "Mulai Unduh",
            Glyph = GlyphKind.Downloading,
            Variant = FluentButtonVariant.Primary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Height = 34
        };
        btnStart.ApplyPalette(_palette);
        btnStart.Size = btnStart.GetPreferredSize(Size.Empty);
        btnStart.Location = new Point(496 - btnStart.Width, y);

        var btnCancel = new FluentButton
        {
            Text = "Batal",
            Variant = FluentButtonVariant.Secondary,
            DialogResult = DialogResult.Cancel,
            Height = 34
        };
        btnCancel.ApplyPalette(_palette);
        btnCancel.Size = btnCancel.GetPreferredSize(Size.Empty);
        btnCancel.Location = new Point(btnStart.Left - btnCancel.Width - 8, y);

        btnStart.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtUrl.Text))
            {
                MessageBox.Show(this, "URL unduhan wajib diisi.", "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtUrl.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        ClientSize = new Size(516, btnStart.Bottom + 20);

        Controls.AddRange(new Control[]
        {
            lblUrl, urlBox, btnPaste,
            lblCustom, customBox,
            lblFolder, folderBox, btnBrowse,
            lblConn, _cmbConnections, _chkAutoCategorize,
            btnCancel, btnStart
        });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemePalette.ApplyWindowChrome(Handle, _palette.IsDark);
    }

    private Panel WrapInput(TextBox txt, int x, int y, int width)
    {
        var box = new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, 32),
            BackColor = _palette.SurfaceSubtle
        };
        txt.BackColor = _palette.SurfaceSubtle;
        txt.ForeColor = _palette.Text;
        txt.Location = new Point(10, 7);
        txt.Width = width - 20;
        box.Controls.Add(txt);
        box.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, box.Width - 1, box.Height - 1);
            using GraphicsPath path = ThemePalette.CreateRoundedRect(rect, 6);
            using var pen = new Pen(txt.Focused ? _palette.Primary : _palette.BorderStrong, 1f);
            e.Graphics.DrawPath(pen, path);
        };
        txt.GotFocus += (_, _) => box.Invalidate();
        txt.LostFocus += (_, _) => box.Invalidate();
        return box;
    }
}