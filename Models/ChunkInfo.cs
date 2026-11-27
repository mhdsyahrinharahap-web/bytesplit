namespace ByteSplit.Models;

public sealed class ChunkInfo
{
    public int Id { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public long Downloaded { get; set; }
    public string Status { get; set; } = "pending"; // pending, downloading, retrying, done, paused, error

    public long Size => End >= Start && End >= 0 ? (End - Start + 1) : 0;
}