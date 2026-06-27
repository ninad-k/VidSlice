namespace VidSlice.Models;

/// <summary>How the user wants the file divided into parts.</summary>
public enum SplitMode
{
    /// <summary>Each part stays under a maximum size (MB).</summary>
    BySize,

    /// <summary>The file is divided into a fixed number of equal parts.</summary>
    ByParts,

    /// <summary>Each part is a fixed duration (minutes).</summary>
    ByDuration,
}
