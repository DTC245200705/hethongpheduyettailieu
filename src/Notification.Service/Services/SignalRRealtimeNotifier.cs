using Microsoft.AspNetCore.SignalR;
using Notification.Service.Hubs;
using Shared.Contracts.Dtos;

namespace Notification.Service.Services;

public sealed class SignalRRealtimeNotifier(
    IHubContext<NotificationHub> hubContext,
    ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task BroadcastAsync(List<NotificationDto> notifications, CancellationToken ct)
    {
        foreach (var dto in notifications)
        {
            var group = NotificationHub.GetUserGroup(dto.Recipient);
            await hubContext.Clients.Group(group).SendAsync("ReceiveNotification", dto, ct);

            logger.LogInformation(
                "Pushed SignalR notification {NotificationId} to {Group} ({EventType})",
                dto.Id, group, dto.EventType);
        }

        if (notifications.Count > 0)
        {
            await hubContext.Clients
                .Group(NotificationHub.AllUsersGroup)
                .SendAsync("DocumentUpdated", notifications[0].DocumentId, ct);
        }
    }
}
