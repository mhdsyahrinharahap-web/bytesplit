namespace ByteSplit.Models;

public sealed class AppSettings
{
    public string DefaultOutputDir { get; set; } = string.Empty;
    public long SpeedLimitBps { get; set; }
    public bool ClipboardMonitor { get; set; } = true;
    public string Theme { get; set; } = "light";
}