using IdempotencyEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IdempotencyEngine.Data;

/// <summary>
/// EF Core DbContext configured strictly for READ-ONLY queries.
/// All queries run with AsNoTracking by default.
/// Never call SaveChanges() on this context.
/// </summary>
public class IdempotencyDbContext : DbContext
{
    public IdempotencyDbContext(DbContextOptions<IdempotencyDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProcessedIncident> ProcessedIncidents => Set<ProcessedIncident>();
    public DbSet<IncidentVariant> IncidentVariants => Set<IncidentVariant>();
    public DbSet<VariantSolution> VariantSolutions => Set<VariantSolution>();
    public DbSet<DomainItem> Domains => Set<DomainItem>();
    public DbSet<IncidentLink> IncidentLinks => Set<IncidentLink>();
    public DbSet<ProcessedIngest> ProcessedIngests => Set<ProcessedIngest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ProcessedIncidents mapping
        modelBuilder.Entity<ProcessedIncident>(entity =>
        {
            entity.ToTable("ProcessedIncidents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Domain).HasColumnName("domain");
            entity.Property(e => e.ShapeHash).HasColumnName("shape_hash");
            entity.Property(e => e.ShapeVersion).HasColumnName("shape_version");
            entity.Property(e => e.ShapeTemplate).HasColumnName("shape_template");
            entity.Property(e => e.Origin).HasColumnName("origin");
            entity.Property(e => e.ErrorEmbedding).HasColumnName("error_embedding").HasColumnType("vector(384)");
            entity.Property(e => e.EmbeddingModel).HasColumnName("embedding_model");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // IncidentVariants mapping
        modelBuilder.Entity<IncidentVariant>(entity =>
        {
            entity.ToTable("IncidentVariants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id");
            entity.Property(e => e.ContextFingerprint).HasColumnName("context_fingerprint");
            entity.Property(e => e.ContextEmbedding).HasColumnName("context_embedding").HasColumnType("vector(384)");
            entity.Property(e => e.EmbeddingModel).HasColumnName("embedding_model");
            entity.Property(e => e.ContextJson).HasColumnName("context_json").HasColumnType("jsonb");
            entity.Property(e => e.ContextSample).HasColumnName("context_sample");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Verified).HasColumnName("verified");
            entity.Property(e => e.MergedByEmbedding).HasColumnName("merged_by_embedding");
            entity.Property(e => e.OccurrenceCount).HasColumnName("occurrence_count");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.FirstSeenAt).HasColumnName("first_seen_at");
            entity.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
        });

        // VariantSolutions mapping
        modelBuilder.Entity<VariantSolution>(entity =>
        {
            entity.ToTable("VariantSolutions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.VariantId).HasColumnName("variant_id");
            entity.Property(e => e.SolutionText).HasColumnName("solution_text");
            entity.Property(e => e.PatchContent).HasColumnName("patch_content");
            entity.Property(e => e.SolutionType).HasColumnName("solution_type");
            entity.Property(e => e.ConfidenceScore).HasColumnName("confidence_score");
            entity.Property(e => e.Verified).HasColumnName("verified");
            entity.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            entity.Property(e => e.SupersededAt).HasColumnName("superseded_at");
            entity.Property(e => e.SupersededReason).HasColumnName("superseded_reason");
        });

        // Domains mapping
        modelBuilder.Entity<DomainItem>(entity =>
        {
            entity.ToTable("Domains");
            entity.HasKey(e => e.Name);
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ShapeVersion).HasColumnName("shape_version");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // IncidentLinks mapping
        modelBuilder.Entity<IncidentLink>(entity =>
        {
            entity.ToTable("IncidentLinks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FromIncident).HasColumnName("from_incident");
            entity.Property(e => e.ToIncident).HasColumnName("to_incident");
            entity.Property(e => e.Similarity).HasColumnName("similarity");
            entity.Property(e => e.Relation).HasColumnName("relation");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // ProcessedIngests mapping
        modelBuilder.Entity<ProcessedIngest>(entity =>
        {
            entity.ToTable("ProcessedIngests");
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).HasColumnName("event_id");
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        });
    }
}
