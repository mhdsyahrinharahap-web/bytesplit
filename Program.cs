using System.Threading;
using ByteSplit.UI;

namespace ByteSplit;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(true, @"Local\ByteSplit_Native_SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show(
                "ByteSplit sudah berjalan. Periksa ikon ByteSplit di System Tray pojok kanan bawah.",
                "ByteSplit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}