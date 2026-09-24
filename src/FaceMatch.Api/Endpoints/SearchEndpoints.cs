using FaceMatch.Core.Contracts;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FaceMatch.Api.Endpoints;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/search", SearchByImageAsync)
            .DisableAntiforgery()
            .WithTags("Search")
            .WithSummary("Find a person by photo")
            .WithDescription(
                "Multipart form with a single `file`. The largest face in the photo is used as the query " +
                "(or the one selected with `faceIndex`, see `queryFaces` in the response). Returns every indexed face " +
                "— across all images, including group photos — whose cosine similarity is at least `minSimilarity`.");

        app.MapGet("/api/faces/{id:guid}/similar", SearchByFaceAsync)
            .WithTags("Search")
            .WithSummary("Find other photos of an already indexed face");

        return app;
    }

    private static async Task<Results<Ok<SearchResultDto>, ProblemHttpResult>> SearchByImageAsync(
        IFormFile file,
        FaceSearchService search,
        IOptions<ProcessingOptions> processing,
        CancellationToken ct,
        [FromQuery] int topK = 20,
        [FromQuery] float? minSimilarity = null,
        [FromQuery] int? faceIndex = null)
    {
        if (file.Length == 0)
        {
            return TypedResults.Problem("The query file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (file.Length > processing.Value.MaxImageBytes)
        {
            return TypedResults.Problem("The query image is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var result = await search.SearchByImageAsync(buffer, new SearchOptions(topK, minSimilarity, faceIndex), ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<SearchResultDto>, NotFound>> SearchByFaceAsync(
        Guid id,
        FaceSearchService search,
        CancellationToken ct,
        [FromQuery] int topK = 20,
        [FromQuery] float? minSimilarity = null) =>
        await search.SearchByFaceAsync(id, new SearchOptions(topK, minSimilarity), ct) is { } result
            ? TypedResults.Ok(result)
            : TypedResults.NotFound();
}
