using ExpertiseApi.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpertiseApi.Data;

public class ExpertiseDbContext(DbContextOptions<ExpertiseDbContext> options) : DbContext(options)
{
    public DbSet<ExpertiseEntry> ExpertiseEntries => Set<ExpertiseEntry>();
    public DbSet<EmbeddingMetadata> EmbeddingMetadata => Set<EmbeddingMetadata>();
    public DbSet<ExpertiseAuditLog> ExpertiseAuditLogs => Set<ExpertiseAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<ExpertiseEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.Domain).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Body).IsRequired();
            entity.Property(e => e.Source).IsRequired();

            entity.Property(e => e.Tags).HasColumnType("text[]");
            entity.HasIndex(e => e.Tags).HasMethod("gin");

            entity.Property(e => e.EntryType)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.Severity)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("now()");

            entity.HasIndex(e => e.Embedding).HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            entity.HasIndex(e => e.Domain);
            entity.HasIndex(e => e.DeprecatedAt);

            entity.Property(e => e.Tenant).IsRequired();
            entity.HasIndex(e => e.Tenant);

            entity.Property(e => e.Visibility)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.AuthorPrincipal).IsRequired();

            entity.Property(e => e.ReviewState)
                .HasConversion<string>()
                .IsRequired();

            entity.HasIndex(e => new { e.Tenant, e.ReviewState })
                .IncludeProperties(e => new { e.Id, e.EntryType, e.Severity });

            entity.HasGeneratedTsVectorColumn(
                e => e.SearchVector,
                "english",
                e => new { e.Title, e.Body });

            entity.HasIndex(e => e.SearchVector)
                .HasMethod("gin");
        });

        modelBuilder.Entity<EmbeddingMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ModelName).IsRequired();
        });

        modelBuilder.Entity<ExpertiseAuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
            entity.Property(e => e.Timestamp).HasDefaultValueSql("now()");

            entity.Property(e => e.Action)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(e => e.Tenant).IsRequired();
            entity.Property(e => e.Principal).IsRequired();

            entity.HasOne<ExpertiseEntry>()
                .WithMany()
                .HasForeignKey(e => e.EntryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.EntryId, e.Timestamp })
                .IncludeProperties(e => e.Action);

            entity.HasIndex(e => new { e.Principal, e.Timestamp });
        });
    }
}
