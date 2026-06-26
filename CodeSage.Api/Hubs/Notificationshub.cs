using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CodeSage.Api.Hubs;

// Clients connect with ?orgId=... and join that org's group so we can push org-scoped events.
[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var orgId = Context.GetHttpContext()?.Request.Query["orgId"].ToString();
        if (!string.IsNullOrWhiteSpace(orgId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{orgId}");
        await base.OnConnectedAsync();
    }
}