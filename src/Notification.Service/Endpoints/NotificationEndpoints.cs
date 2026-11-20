using Microsoft.EntityFrameworkCore;
using Notification.Service.Data;
using Shared.Contracts.Dtos;

namespace Notification.Service.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications").WithTags("Notifications");

        group.MapGet("/", async (
            string? recipient,
            bool? unreadOnly,
            int? limit,
            NotificationDbContext db,
            CancellationToken ct) =>
        {
            var query = db.Notifications.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(recipient))
            {
                var normalized = recipient.Trim().ToLower();
                query = query.Where(n => n.Recipient.ToLower() == normalized);
            }

            if (unreadOnly == true)
            {
                query = query.Where(n => !n.IsRead);
            }

            var take = Math.Clamp(limit ?? 50, 1, 200);
            var items = await query
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(take)
                .Select(n => new NotificationDto(
                    n.Id,
                    n.Recipient,
                    n.DocumentId,
                    n.DocumentTitle,
                    n.EventType,
                    n.Message,
                    n.Actor,
                    n.Comment,
                    n.IsRead,
                    n.CorrelationId,
                    n.CreatedAtUtc))
                .ToListAsync(ct);

            return Results.Ok(items);
        })
        .WithName("GetNotifications")
        .WithSummary("Tra cứu danh sách thông báo theo người nhận");

        group.MapPost("/{id:guid}/read", async (
            Guid id,
            NotificationDbContext db,
            CancellationToken ct) =>
        {
            var record = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id, ct);
            if (record is null)
                return Results.NotFound(new { error = $"Notification {id} not found." });

            record.IsRead = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = record.Id, isRead = record.IsRead });
        })
        .WithName("MarkNotificationAsRead");

        group.MapPost("/read-all", async (
            string recipient,
            NotificationDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(recipient))
                return Results.BadRequest(new { error = "Recipient is required." });

            var normalized = recipient.Trim().ToLower();
            var unread = await db.Notifications
                .Where(n => n.Recipient.ToLower() == normalized && !n.IsRead)
                .ToListAsync(ct);

            foreach (var item in unread)
                item.IsRead = true;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated = unread.Count });
        })
        .WithName("MarkAllNotificationsAsRead");
    }
}
