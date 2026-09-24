using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FaceMatch.Core.Contracts;
using FaceMatch.Core.Domain;
using FaceMatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FaceMatch.Tests;

/// <summary>
/// End-to-end tests against real PostgreSQL (pgvector) and MinIO, e.g. the ones from <c>docker compose up postgres minio</c>.
/// Enabled with <c>FACEMATCH_INTEGRATION=1</c>; each run uses its own database and bucket.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public static bool Enabled => Environment.GetEnvironmentVariable("FACEMATCH_INTEGRATION") == "1";

    public WebApplicationFactory<Program>? Factory { get; private set; }

    public HttpClient Client => Factory!.CreateClient();

    public Task InitializeAsync()
    {
        if (!Enabled)
        {
            return Task.CompletedTask;
        }

        var postgres = Environment.GetEnvironmentVariable("FACEMATCH_TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Username=facematch;Password=facematch";

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", $"{postgres};Database=facematch_test_{_suffix}");
            b.UseSetting("Minio:Endpoint", Environment.GetEnvironmentVariable("FACEMATCH_TEST_MINIO") ?? "localhost:9000");
            b.UseSetting("Minio:AccessKey", Environment.GetEnvironmentVariable("FACEMATCH_TEST_MINIO_USER") ?? "minioadmin");
            b.UseSetting("Minio:SecretKey", Environment.GetEnvironmentVariable("FACEMATCH_TEST_MINIO_PASSWORD") ?? "minioadmin");
            b.UseSetting("Minio:Bucket", $"facematch-test-{_suffix}");
        });

        _ = Factory.Server; // start the host: migrations + bucket creation
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Factory is null)
        {
            return;
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FaceMatchDbContext>().Database.EnsureDeletedAsync();
        }

        await Factory.DisposeAsync();
    }
}

public class ApiIntegrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] Indexed =
    [
        "obama2.jpg", "obama3.jpg", "obama_biden_pair.jpg", "obama_biden_stage.jpg", "biden.jpg",
        "kit_harington.jpeg", "kit_with_rose.jpg", "rose_leslie.jpg", "alex_lacamoire1.jpg", "group.jpg",
    ];

    [SkippableFact]
    public async Task UploadIndexSearchAndDelete_EndToEnd()
    {
        Skip.IfNot(ApiFixture.Enabled, "Set FACEMATCH_INTEGRATION=1 with PostgreSQL and MinIO running.");
        var client = fixture.Client;

        // 1. Upload a batch of photos and index them synchronously.
        var uploads = await UploadAsync(client, Indexed.Select(TestSupport.Sample), wait: true);
        Assert.All(uploads, u => Assert.Equal(UploadOutcome.Accepted, u.Outcome));
        Assert.All(uploads, u => Assert.Equal(ImageStatus.Completed, u.Status));
        Assert.Equal(6, uploads.Single(u => u.FileName == "group.jpg").FaceCount);

        // 2. Re-uploading identical bytes is de-duplicated; non-images are rejected.
        var again = await UploadAsync(client, [TestSupport.Sample("group.jpg")], wait: false);
        Assert.Equal(UploadOutcome.Duplicate, again.Single().Outcome);

        using (var bogus = new MultipartFormDataContent { { new ByteArrayContent("nope"u8.ToArray()), "files", "fake.jpg" } })
        {
            var rejected = await ReadAsync<List<UploadResult>>(await client.PostAsync("/api/images", bogus));
            Assert.Equal(UploadOutcome.Rejected, rejected.Single().Outcome);
        }

        // 3. A photo that is NOT in the index finds exactly the images Obama appears in, including group photos.
        var result = await SearchAsync(client, TestSupport.Sample("obama.jpg"));
        var files = result.Matches.Select(m => m.FileName).ToHashSet();
        Assert.Equal(new HashSet<string> { "obama2.jpg", "obama3.jpg", "obama_biden_pair.jpg", "obama_biden_stage.jpg" }, files);
        Assert.All(result.Matches, m => Assert.True(m.Similarity >= result.MinSimilarity));

        // 4. Selecting another face of a multi-face query switches the person searched for.
        var pair = await SearchAsync(client, TestSupport.Sample("kit_with_rose.jpg"));
        Assert.Equal(2, pair.QueryFaces.Count);
        var other = await SearchAsync(client, TestSupport.Sample("kit_with_rose.jpg"), faceIndex: 1 - pair.SelectedFaceIndex!.Value);
        Assert.NotEqual(
            pair.Matches.Where(m => m.FileName != "kit_with_rose.jpg").Select(m => m.FileName).ToHashSet(),
            other.Matches.Where(m => m.FileName != "kit_with_rose.jpg").Select(m => m.FileName).ToHashSet());

        // 5. "Find similar" from an indexed face excludes the face itself.
        var seed = result.Matches.First();
        var similar = await ReadAsync<SearchResultDto>(await client.GetAsync($"/api/faces/{seed.FaceId}/similar"));
        Assert.DoesNotContain(similar.Matches, m => m.FaceId == seed.FaceId);
        Assert.Contains(similar.Matches, m => m.FileName != seed.FileName);

        // 6. Binary content and thumbnails are served from MinIO.
        var content = await client.GetAsync(seed.ImageUrl);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/jpeg", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(File.ReadAllBytes(TestSupport.Sample(seed.FileName)), await content.Content.ReadAsByteArrayAsync());

        var thumbnail = await client.GetAsync(seed.ThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);

        // 7. Deleting an image removes it from search results.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/images/{seed.ImageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/images/{seed.ImageId}")).StatusCode);
        var afterDelete = await SearchAsync(client, TestSupport.Sample("obama.jpg"));
        Assert.DoesNotContain(afterDelete.Matches, m => m.ImageId == seed.ImageId);
    }

    [SkippableFact]
    public async Task Search_WithoutFace_ReturnsEmptyResult_AndInvalidInputIsBadRequest()
    {
        Skip.IfNot(ApiFixture.Enabled, "Set FACEMATCH_INTEGRATION=1 with PostgreSQL and MinIO running.");
        var client = fixture.Client;

        var noFace = await SearchAsync(client, TestSupport.Sample("masked.jpg"));
        Assert.Empty(noFace.QueryFaces);
        Assert.Empty(noFace.Matches);

        using var form = new MultipartFormDataContent { { new ByteArrayContent("nope"u8.ToArray()), "file", "fake.jpg" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/search", form)).StatusCode);

        using var group = FileForm("file", TestSupport.Sample("group.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/search?faceIndex=42", group)).StatusCode);
    }

    private static async Task<List<UploadResult>> UploadAsync(HttpClient client, IEnumerable<string> paths, bool wait)
    {
        using var form = new MultipartFormDataContent();
        foreach (var path in paths)
        {
            var file = new ByteArrayContent(await File.ReadAllBytesAsync(path));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "files", Path.GetFileName(path));
        }

        return await ReadAsync<List<UploadResult>>(await client.PostAsync($"/api/images?wait={wait}", form));
    }

    private static async Task<SearchResultDto> SearchAsync(HttpClient client, string path, int? faceIndex = null)
    {
        using var form = FileForm("file", path);
        var url = faceIndex is null ? "/api/search" : $"/api/search?faceIndex={faceIndex}";
        return await ReadAsync<SearchResultDto>(await client.PostAsync(url, form));
    }

    private static MultipartFormDataContent FileForm(string field, string path) =>
        new() { { new ByteArrayContent(File.ReadAllBytes(path)), field, Path.GetFileName(path) } };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
}
