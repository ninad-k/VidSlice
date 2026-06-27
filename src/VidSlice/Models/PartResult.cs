namespace VidSlice.Models;

/// <summary>
/// One output part after splitting, with the data needed to verify it stayed
/// under the size limit. Bound directly to the results grid in the UI.
/// </summary>
public sealed class PartResult
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public long SizeBytes { get; init; }
    public double DurationSeconds { get; init; }
    public long MaxBytes { get; init; }

    public double SizeMb => Math.Round(SizeBytes / 1024d / 1024d, 2);
    public double DurationMinutes => Math.Round(DurationSeconds / 60d, 1);
    public bool UnderLimit => SizeBytes < MaxBytes;
    public string StatusText => UnderLimit ? "✓ OK" : "✗ Over limit";
}
