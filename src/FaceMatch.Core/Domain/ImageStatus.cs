namespace FaceMatch.Core.Domain;

/// <summary>Lifecycle of an uploaded image in the indexing pipeline.</summary>
public enum ImageStatus
{
    /// <summary>Stored in object storage, waiting for face indexing.</summary>
    Pending = 0,

    /// <summary>Currently being processed by a worker.</summary>
    Processing = 1,

    /// <summary>Faces detected and indexed (possibly zero faces).</summary>
    Completed = 2,

    /// <summary>Processing failed; see <see cref="ImageAsset.Error"/>.</summary>
    Failed = 3,
}
