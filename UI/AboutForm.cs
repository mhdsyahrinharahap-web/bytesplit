using System.Drawing.Drawing2D;

namespace ByteSplit.UI;

public sealed class AboutForm : Form
{
    private readonly ThemePalette _palette;

    public AboutForm(ThemePalette palette, Icon appIcon, Image? logoImage)
    {
        _palette = palette;

        Text = "Tentang ByteSplit";
        Icon = appIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 250);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = _palette.Surface;
        ForeColor = _palette.Text;

        var picLogo = new PictureBox
        {
            Location = new Point(22, 22),
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = logoImage
        };

        var lblTitle = new Label
        {
            Text = "ByteSplit",
            Location = new Point(82, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = _palette.Text
        };

        var lblVersion = new Label
        {
            Text = "Versi v2.0.0-beta.1  •  Native Windows (.NET 8)",
            Location = new Point(84, 48),
            AutoSize = true,
            ForeColor = _palette.Primary,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var lblDesc = new Label
        {
            Text = "Multi-connection HTTP/HTTPS download manager native untuk Windows.",
            Location = new Point(22, 86),
            Size = new Size(416, 62),
            ForeColor = _palette.TextSecondary
        };

        var devCard = new Panel
        {
            Location = new Point(22, 154),
            Size = new Size(416, 36)
        };
        devCard.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, devCard.Width - 1, devCard.Height - 1);
            using GraphicsPath path = ThemePalette.CreateRoundedRect(rect, 6);
            using var brush = new SolidBrush(_palette.SurfaceSubtle);
            using var pen = new Pen(_palette.Border, 1f);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);

            TextRenderer.DrawText(
                e.Graphics,
                "Developer: Muhammad Syahrin Harahap",
                new Font("Segoe UI", 9.2f, FontStyle.Bold),
                new Rectangle(12, 0, rect.Width - 24, rect.Height),
                _palette.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };

        var btnClose = new FluentButton
        {
            Text = "Tutup",
            Variant = FluentButtonVariant.Secondary,
            DialogResult = DialogResult.OK,
            Height = 32
        };
        btnClose.ApplyPalette(_palette);
        btnClose.Size = btnClose.GetPreferredSize(Size.Empty);
        btnClose.Location = new Point(ClientSize.Width - btnClose.Width - 22, 202);

        Controls.AddRange(new Control[] { picLogo, lblTitle, lblVersion, lblDesc, devCard, btnClose });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemePalette.ApplyWindowChrome(Handle, _palette.IsDark);
    }
}