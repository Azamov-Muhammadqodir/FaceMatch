using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FaceMatch.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> tooling to create migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FaceMatchDbContext>
{
    public FaceMatchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=facematch;Username=facematch;Password=facematch";

        var options = new DbContextOptionsBuilder<FaceMatchDbContext>()
            .UseNpgsql(connectionString, o => o.UseVector())
            .Options;

        return new FaceMatchDbContext(options);
    }
}
