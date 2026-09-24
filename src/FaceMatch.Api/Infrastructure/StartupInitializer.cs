using FaceMatch.Core.Abstractions;
using FaceMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FaceMatch.Api.Infrastructure;

/// <summary>
/// Applies database migrations and creates the MinIO bucket before the app starts serving requests.
/// Registered before the processing worker, so the worker never sees an unmigrated schema.
/// </summary>
public sealed class StartupInitializer(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<StartupInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        if (configuration.GetValue("Database:MigrateOnStartup", true))
        {
            var db = scope.ServiceProvider.GetRequiredService<FaceMatchDbContext>();
            await RetryAsync("database migration", () => db.Database.MigrateAsync(cancellationToken), cancellationToken);
        }

        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        await RetryAsync("object storage initialisation", () => storage.EnsureReadyAsync(cancellationToken), cancellationToken);

        // Load the ONNX models eagerly so the first request is not slow and misconfiguration fails fast.
        scope.ServiceProvider.GetRequiredService<IFaceAnalyzer>();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Dependencies started by docker compose may need a few seconds to accept connections.</summary>
    private async Task RetryAsync(string what, Func<Task> action, CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("{What} failed (attempt {Attempt}/{Max}): {Message}", what, attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(2 * attempt, 10)), cancellationToken);
            }
        }
    }
}
