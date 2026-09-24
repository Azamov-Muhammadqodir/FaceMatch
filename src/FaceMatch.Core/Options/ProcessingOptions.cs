using System.ComponentModel.DataAnnotations;

namespace FaceMatch.Core.Options;

public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>How many images are indexed in parallel.</summary>
    [Range(1, 64)]
    public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>How many times a failing image is retried before it is marked as failed.</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Maximum accepted size of a single uploaded image.</summary>
    [Range(1024, 1L << 31)]
    public long MaxImageBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>Maximum number of files accepted in a single upload request.</summary>
    [Range(1, 10_000)]
    public int MaxFilesPerRequest { get; set; } = 500;
}
