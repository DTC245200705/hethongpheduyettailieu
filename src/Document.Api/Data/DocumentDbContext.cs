using Document.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Document.Api.Data;

public sealed class DocumentDbContext(DbContextOptions<DocumentDbContext> options) : DbContext(options)
{
    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();
    public DbSet<DocumentHistory> DocumentHistories => Set<DocumentHistory>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentRecord>(entity =>
        {
            entity.ToTable("documents");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Title).HasMaxLength(250).IsRequired();
            entity.Property(d => d.Content).IsRequired();
            entity.Property(d => d.CreatedBy).HasMaxLength(100).IsRequired();
            entity.Property(d => d.AssignedApprover).HasMaxLength(100).IsRequired();
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

            entity.HasIndex(d => d.Status);
            entity.HasIndex(d => d.AssignedApprover);
            entity.HasIndex(d => d.CreatedBy);

            entity.HasMany(d => d.Histories)
                  .WithOne(h => h.Document)
                  .HasForeignKey(h => h.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentHistory>(entity =>
        {
            entity.ToTable("document_histories");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.FromStatus).HasMaxLength(32).IsRequired();
            entity.Property(h => h.ToStatus).HasMaxLength(32).IsRequired();
            entity.Property(h => h.Action).HasMaxLength(64).IsRequired();
            entity.Property(h => h.Actor).HasMaxLength(100).IsRequired();
            entity.Property(h => h.Comment).HasMaxLength(1000);
            entity.HasIndex(h => h.DocumentId);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.EventType).HasMaxLength(100).IsRequired();
            entity.Property(o => o.RoutingKey).HasMaxLength(100).IsRequired();
            entity.Property(o => o.Payload).IsRequired();
            entity.Property(o => o.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(o => o.LastError).HasMaxLength(1000);
            entity.HasIndex(o => o.ProcessedAtUtc);
        });
    }
}
