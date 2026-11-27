using System.Reflection;
using System.Runtime.InteropServices;

namespace ByteSplit.UI;

public static class IconHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon LoadAppIcon()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ByteSplit.icon.png");
            if (stream != null)
            {
                using var bmp = new Bitmap(stream);
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using var tempIcon = Icon.FromHandle(hIcon);
                    return (Icon)tempIcon.Clone();
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    public static Bitmap? LoadAppBitmap()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ByteSplit.icon.png");
            if (stream != null)
            {
                return new Bitmap(stream);
            }
        }
        catch { }

        return null;
    }
}