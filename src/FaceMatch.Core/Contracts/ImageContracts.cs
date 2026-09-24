using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Domain;

namespace FaceMatch.Core.Contracts;

/// <summary>A file received from a client, abstracted away from ASP.NET's IFormFile.</summary>
public sealed record IncomingFile(string FileName, string? ContentType, long Length, Func<Stream> OpenReadStream);

public enum UploadOutcome
{
    Accepted,
    Duplicate,
    Rejected,
}

public sealed record UploadResult(
    string FileName,
    UploadOutcome Outcome,
    Guid? ImageId,
    ImageStatus? Status,
    int? FaceCount,
    string? Error);

public sealed record FaceDto(Guid Id, FaceBox Box, float Confidence, string ThumbnailUrl);

public sealed record ImageSummaryDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    ImageStatus Status,
    int FaceCount,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    string ContentUrl);

public sealed record ImageDetailsDto(ImageSummaryDto Image, IReadOnlyList<FaceDto> Faces);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record StatsDto(int Images, int Pending, int Processing, int Completed, int Failed, long Faces);

public static class ResourceUrls
{
    public static string ImageContent(Guid imageId) => $"/api/images/{imageId}/content";

    public static string FaceThumbnail(Guid faceId) => $"/api/faces/{faceId}/thumbnail";
}
