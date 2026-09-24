using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Contracts;
using FaceMatch.Core.Domain;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FaceMatch.Api.Endpoints;

public static class ImageEndpoints
{
    public static IEndpointRouteBuilder MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        var images = app.MapGroup("/api/images").WithTags("Images");

        images.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .WithSummary("Upload one or many images")
            .WithDescription(
                "Multipart form with one or more `files`. Originals go to MinIO, faces are detected and indexed in the background. " +
                "Pass `wait=true` to index synchronously and get face counts in the response.");

        images.MapGet("/", ListAsync).WithSummary("List images");
        images.MapGet("/{id:guid}", GetAsync).WithSummary("Image details with detected faces");
        images.MapGet("/{id:guid}/content", GetContentAsync).WithSummary("Original image bytes");
        images.MapPost("/{id:guid}/reindex", ReindexAsync).WithSummary("Detect and index the faces again");
        images.MapDelete("/{id:guid}", DeleteAsync).WithSummary("Delete an image, its faces and stored objects");

        app.MapGet("/api/faces/{id:guid}/thumbnail", GetFaceThumbnailAsync).WithTags("Faces").WithSummary("Face thumbnail (JPEG)");
        app.MapGet("/api/stats", (ImageCatalogService catalog, CancellationToken ct) => catalog.GetStatsAsync(ct))
            .WithTags("Images")
            .WithSummary("Indexing statistics");

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<UploadResult>>, ProblemHttpResult>> UploadAsync(
        [FromForm] IFormFileCollection files,
        ImageIngestionService ingestion,
        IOptions<ProcessingOptions> options,
        CancellationToken ct,
        [FromQuery] bool wait = false)
    {
        if (files.Count == 0)
        {
            return TypedResults.Problem("Attach at least one file in the multipart field 'files'.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (files.Count > options.Value.MaxFilesPerRequest)
        {
            return TypedResults.Problem(
                $"At most {options.Value.MaxFilesPerRequest} files can be uploaded per request.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var incoming = files
            .Select(f => new IncomingFile(f.FileName, f.ContentType, f.Length, f.OpenReadStream))
            .ToList();

        return TypedResults.Ok(await ingestion.IngestAsync(incoming, wait, ct));
    }

    private static Task<PagedResult<ImageSummaryDto>> ListAsync(
        ImageCatalogService catalog,
        CancellationToken ct,
        int page = 1,
        int pageSize = 50,
        ImageStatus? status = null) =>
        catalog.ListAsync(page, pageSize, status, ct);

    private static async Task<Results<Ok<ImageDetailsDto>, NotFound>> GetAsync(Guid id, ImageCatalogService catalog, CancellationToken ct) =>
        await catalog.GetAsync(id, ct) is { } details ? TypedResults.Ok(details) : TypedResults.NotFound();

    private static async Task<IResult> GetContentAsync(
        Guid id,
        HttpContext http,
        ImageCatalogService catalog,
        IObjectStorage storage,
        CancellationToken ct)
    {
        var image = await catalog.FindImageAsync(id, ct);
        return image is null
            ? TypedResults.NotFound()
            : StreamObject(http, storage, image.ObjectKey, image.ContentType);
    }

    private static async Task<IResult> GetFaceThumbnailAsync(
        Guid id,
        HttpContext http,
        ImageCatalogService catalog,
        IObjectStorage storage,
        CancellationToken ct)
    {
        var key = await catalog.FindThumbnailKeyAsync(id, ct);
        return key is null ? TypedResults.NotFound() : StreamObject(http, storage, key, "image/jpeg");
    }

    private static async Task<Results<Accepted, NotFound, Conflict>> ReindexAsync(
        Guid id,
        ImageCatalogService catalog,
        ImageIngestionService ingestion,
        CancellationToken ct)
    {
        if (await catalog.FindImageAsync(id, ct) is null)
        {
            return TypedResults.NotFound();
        }

        return await ingestion.ReindexAsync(id, ct) ? TypedResults.Accepted($"/api/images/{id}") : TypedResults.Conflict();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, ImageCatalogService catalog, CancellationToken ct) =>
        await catalog.DeleteAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound();

    /// <summary>Objects are immutable (keys contain a fresh id), so they can be cached aggressively.</summary>
    private static IResult StreamObject(HttpContext http, IObjectStorage storage, string key, string contentType)
    {
        http.Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return Results.Stream(body => storage.ReadAsync(key, body, http.RequestAborted), contentType);
    }
}
