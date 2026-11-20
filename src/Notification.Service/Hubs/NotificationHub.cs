using Microsoft.AspNetCore.SignalR;

namespace Notification.Service.Hubs;

public sealed class NotificationHub(ILogger<NotificationHub> logger) : Hub
{
    public const string AllUsersGroup = "all-users";

    public static string GetUserGroup(string username) => $"user:{username.Trim().ToLowerInvariant()}";

    public async Task JoinUserGroup(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        var group = GetUserGroup(username);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await Groups.AddToGroupAsync(Context.ConnectionId, AllUsersGroup);

        logger.LogInformation(
            "SignalR Connection {ConnectionId} joined group {Group}",
            Context.ConnectionId, group);
    }

    public async Task LeaveUserGroup(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        var group = GetUserGroup(username);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }
}
