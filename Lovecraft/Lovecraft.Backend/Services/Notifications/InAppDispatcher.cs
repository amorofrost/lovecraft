using Lovecraft.Backend.Hubs;
using Lovecraft.Common.DTOs.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Lovecraft.Backend.Services.Notifications;

public class InAppDispatcher : IInAppDispatcher
{
    private readonly IHubContext<ChatHub> _hub;
    public InAppDispatcher(IHubContext<ChatHub> hub) => _hub = hub;

    public Task DispatchAsync(string userId, NotificationDto notification)
        => _hub.Clients.User(userId).SendAsync("NotificationReceived", notification);
}
