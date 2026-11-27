using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ByteSplit.UI;

public sealed class ThemePalette
{
    public bool IsDark { get; init; }

    // Aksen Utama
    public Color Primary { get; init; }
    public Color PrimaryHover { get; init; }
    public Color PrimaryPressed { get; init; }
    public Color PrimaryText { get; init; }
    public Color PrimarySoft { get; init; }

    // Permukaan (Surface)
    public Color Background { get; init; }
    public Color Surface { get; init; }
    public Color SurfaceSubtle { get; init; }
    public Color SurfaceHover { get; init; }
    public Color SurfaceSelected { get; init; }

    // Garis & Pembatas
    public Color Border { get; init; }
    public Color BorderStrong { get; init; }

    // Teks (Kontras WCAG AA)
    public Color Text { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextMuted { get; init; }

    // Semantik Status
    public Color Success { get; init; }
    public Color SuccessSoft { get; init; }
    public Color Warning { get; init; }
    public Color WarningSoft { get; init; }
    public Color Danger { get; init; }
    public Color DangerSoft { get; init; }

    // State Disabled
    public Color DisabledBg { get; init; }
    public Color DisabledText { get; init; }
    public Color DisabledBorder { get; init; }

    public static ThemePalette Light { get; } = new()
    {
        IsDark = false,
        Primary = Color.FromArgb(2, 132, 199),         // #0284C7
        PrimaryHover = Color.FromArgb(3, 105, 161),    // #0369A1
        PrimaryPressed = Color.FromArgb(7, 89, 133),   // #075985
        PrimaryText = Color.White,
        PrimarySoft = Color.FromArgb(224, 242, 254),   // #E0F2FE

        Background = Color.FromArgb(243, 246, 250),    // #F3F6FA
        Surface = Color.White,                         // #FFFFFF
        SurfaceSubtle = Color.FromArgb(248, 250, 252), // #F8FAFC
        SurfaceHover = Color.FromArgb(241, 245, 249),  // #F1F5F9
        SurfaceSelected = Color.FromArgb(224, 242, 254),

        Border = Color.FromArgb(226, 232, 240),        // #E2E8F0
        BorderStrong = Color.FromArgb(203, 213, 225),  // #CBD5E1

        Text = Color.FromArgb(15, 23, 42),             // #0F172A (Kontras > 15:1)
        TextSecondary = Color.FromArgb(51, 65, 85),    // #334155 (Kontras > 9:1)
        TextMuted = Color.FromArgb(71, 85, 105),       // #475569 (Kontras > 6.5:1 - lolos WCAG AA)

        Success = Color.FromArgb(5, 150, 105),         // #059669
        SuccessSoft = Color.FromArgb(209, 250, 229),   // #D1FAE5
        Warning = Color.FromArgb(180, 83, 9),          // #B45309
        WarningSoft = Color.FromArgb(254, 243, 199),   // #FEF3C7
        Danger = Color.FromArgb(220, 38, 38),          // #DC2626
        DangerSoft = Color.FromArgb(254, 226, 226),    // #FEE2E2

        DisabledBg = Color.FromArgb(241, 245, 249),
        DisabledText = Color.FromArgb(148, 163, 184),
        DisabledBorder = Color.FromArgb(226, 232, 240)
    };

    public static ThemePalette Dark { get; } = new()
    {
        IsDark = true,
        Primary = Color.FromArgb(14, 165, 233),        // #0EA5E9
        PrimaryHover = Color.FromArgb(56, 189, 248),   // #38BDF8
        PrimaryPressed = Color.FromArgb(2, 132, 199),  // #0284C7
        PrimaryText = Color.White,
        PrimarySoft = Color.FromArgb(23, 52, 82),      // #173452

        Background = Color.FromArgb(20, 24, 31),       // #14181F
        Surface = Color.FromArgb(26, 31, 40),          // #1A1F28
        SurfaceSubtle = Color.FromArgb(31, 37, 48),    // #1F2530
        SurfaceHover = Color.FromArgb(38, 45, 58),     // #262D3A
        SurfaceSelected = Color.FromArgb(28, 54, 84),  // #1C3654

        Border = Color.FromArgb(46, 54, 68),           // #2E3644
        BorderStrong = Color.FromArgb(66, 77, 96),     // #424D60

        Text = Color.FromArgb(241, 245, 249),          // #F1F5F9 (Kontras > 14:1)
        TextSecondary = Color.FromArgb(203, 213, 225), // #CBD5E1 (Kontras > 9:1)
        TextMuted = Color.FromArgb(156, 169, 186),     // #9CA9BA (Kontras > 6:1 - lolos WCAG AA)

        Success = Color.FromArgb(52, 211, 153),        // #34D399
        SuccessSoft = Color.FromArgb(20, 62, 48),      // #143E30
        Warning = Color.FromArgb(251, 191, 36),        // #FBBF24
        WarningSoft = Color.FromArgb(66, 48, 20),      // #423014
        Danger = Color.FromArgb(248, 113, 113),        // #F87171
        DangerSoft = Color.FromArgb(68, 28, 32),       // #441C20

        DisabledBg = Color.FromArgb(28, 33, 42),
        DisabledText = Color.FromArgb(92, 104, 122),
        DisabledBorder = Color.FromArgb(40, 47, 58)
    };

    public static ThemePalette FromMode(bool dark) => dark ? Dark : Light;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyWindowChrome(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero) return;
        int useDark = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, 20, ref useDark, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, 19, ref useDark, sizeof(int));
        }
    }

    public static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return path;

        int r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        if (r <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int d = r * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}