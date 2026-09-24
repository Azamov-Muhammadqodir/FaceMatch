using FaceMatch.Core.Abstractions;

namespace FaceMatch.Core.Contracts;

public sealed record SearchOptions(int TopK = 20, float? MinSimilarity = null, int? FaceIndex = null);

/// <summary>A face found in the query image.</summary>
public sealed record QueryFaceDto(int Index, FaceBox Box, float Confidence);

public sealed record FaceMatchDto(
    Guid FaceId,
    Guid ImageId,
    string FileName,
    float Similarity,
    FaceBox Box,
    int ImageWidth,
    int ImageHeight,
    string ImageUrl,
    string ThumbnailUrl);

public sealed record SearchResultDto(
    IReadOnlyList<QueryFaceDto> QueryFaces,
    int? SelectedFaceIndex,
    float MinSimilarity,
    int MatchedImages,
    IReadOnlyList<FaceMatchDto> Matches);
