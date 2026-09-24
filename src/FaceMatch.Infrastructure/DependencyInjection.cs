using FaceMatch.Core.Abstractions;
using FaceMatch.Core.Options;
using FaceMatch.Infrastructure.Faces;
using FaceMatch.Infrastructure.Persistence;
using FaceMatch.Infrastructure.Processing;
using FaceMatch.Infrastructure.Services;
using FaceMatch.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;

namespace FaceMatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFaceMatchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FaceRecognitionOptions>()
            .Bind(configuration.GetSection(FaceRecognitionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddDbContext<FaceMatchDbContext>(o => o.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddSingleton<IMinioClient>(sp =>
        {
            var storage = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new MinioClient()
                .WithEndpoint(storage.Endpoint)
                .WithCredentials(storage.AccessKey, storage.SecretKey)
                .WithSSL(storage.UseSsl)
                .Build();
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IObjectStorage, MinioObjectStorage>();
        services.AddSingleton<IFaceAnalyzer, FaceAiSharpAnalyzer>();
        services.AddSingleton<IImageProcessingQueue, ImageProcessingQueue>();

        services.AddScoped<ImageIndexer>();
        services.AddScoped<ImageIngestionService>();
        services.AddScoped<ImageCatalogService>();
        services.AddScoped<FaceSearchService>();

        services.AddHostedService<ImageProcessingWorker>();
        return services;
    }
}
