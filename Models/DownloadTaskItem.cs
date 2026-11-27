using System.Text.Json.Serialization;

namespace ByteSplit.Models;

public sealed class DownloadTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;
    public string? FinalUrl { get; set; }
    public string? FileName { get; set; }
    public string? CustomFileName { get; set; }
    public string Category { get; set; } = "Other";
    public string OutputDir { get; set; } = string.Empty;
    public string? BaseOutputDir { get; set; }
    public string? DestPath { get; set; }
    public string? TempPath { get; set; }
    public long TotalSize { get; set; }
    public long Downloaded { get; set; }
    public bool SupportsRange { get; set; }
    public int Connections { get; set; } = 8;
    public bool AutoCategorize { get; set; } = true;
    public string Status { get; set; } = "queued"; // queued, probing, downloading, paused, completed, error, canceled
    public string? Error { get; set; }
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long? CompletedAt { get; set; }
    public List<ChunkInfo> Chunks { get; set; } = new();

    [JsonIgnore]
    public double SpeedBps { get; set; }

    [JsonIgnore]
    public int? EtaSec { get; set; }
}