using Microsoft.EntityFrameworkCore;
using Notification.Service.Entities;

namespace Notification.Service.Data;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Recipient).HasMaxLength(100).IsRequired();
            entity.Property(n => n.DocumentTitle).HasMaxLength(250).IsRequired();
            entity.Property(n => n.EventType).HasMaxLength(100).IsRequired();
            entity.Property(n => n.Message).HasMaxLength(1000).IsRequired();
            entity.Property(n => n.Actor).HasMaxLength(100).IsRequired();
            entity.Property(n => n.Comment).HasMaxLength(1000);
            entity.Property(n => n.CorrelationId).HasMaxLength(100).IsRequired();

            entity.HasIndex(n => n.Recipient);
            entity.HasIndex(n => n.CreatedAtUtc);
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");
            entity.HasKey(p => p.EventId);
            entity.Property(p => p.EventType).HasMaxLength(100).IsRequired();
            entity.Property(p => p.CorrelationId).HasMaxLength(100).IsRequired();
        });
    }
}
