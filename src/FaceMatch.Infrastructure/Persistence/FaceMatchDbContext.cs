using FaceMatch.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace FaceMatch.Infrastructure.Persistence;

public sealed class FaceMatchDbContext(DbContextOptions<FaceMatchDbContext> options) : DbContext(options)
{
    public DbSet<ImageAsset> Images => Set<ImageAsset>();

    public DbSet<Face> Faces => Set<Face>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<ImageAsset>(e =>
        {
            e.ToTable("images");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.ObjectKey).HasMaxLength(1024);
            e.Property(x => x.ContentType).HasMaxLength(128);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(2048);
            e.HasIndex(x => x.Sha256).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.HasMany(x => x.Faces)
                .WithOne(x => x.Image)
                .HasForeignKey(x => x.ImageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Face>(e =>
        {
            e.ToTable("faces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Embedding).HasColumnType($"vector({FaceEmbedding.Dimensions})");
            e.Property(x => x.ThumbnailKey).HasMaxLength(1024);
            e.HasIndex(x => x.ImageId);

            // Approximate nearest-neighbour index for cosine distance (the `<=>` operator).
            e.HasIndex(x => x.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops")
                .HasStorageParameter("m", 16)
                .HasStorageParameter("ef_construction", 64);
        });
    }
}
