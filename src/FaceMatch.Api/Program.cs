using System.Text.Json.Serialization;
using FaceMatch.Api.Endpoints;
using FaceMatch.Api.Infrastructure;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Must be registered before the infrastructure: hosted services start in registration order,
// and the image processing worker needs a migrated database and an existing bucket.
builder.Services.AddHostedService<StartupInitializer>();
builder.Services.AddFaceMatchInfrastructure(builder.Configuration);

// Bulk uploads: allow large multipart bodies (per-file limits are enforced by the ingestion service).
var maxRequestBytes = builder.Configuration.GetValue<long?>("Http:MaxRequestBodyBytes") ?? 1024L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxRequestBytes;
    o.ValueCountLimit = builder.Configuration.GetValue<int?>($"{ProcessingOptions.SectionName}:MaxFilesPerRequest") ?? 500;
});

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<MinioHealthCheck>("minio");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("FaceMatch API"));
app.MapHealthChecks("/health");

app.MapImageEndpoints();
app.MapSearchEndpoints();

app.Run();

/// <summary>Entry point marker for integration tests.</summary>
public partial class Program;
