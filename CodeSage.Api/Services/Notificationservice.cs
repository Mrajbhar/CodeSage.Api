using CodeSage.Api.Data;
using CodeSage.Api.Hubs;
using CodeSage.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace CodeSage.Api.Services;

// Phase 4 #3 + history: store every notification, then push it live to the org.
public class NotificationService
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly MongoContext _db;
    public NotificationService(IHubContext<NotificationsHub> hub, MongoContext db) { _hub = hub; _db = db; }

    public async Task SendToOrgAsync(string orgId, string type, string message)
    {
        var note = new Notification { OrgId = orgId, Type = type, Message = message };
        await _db.Notifications.InsertOneAsync(note);                 // persist
        await _hub.Clients.Group($"org:{orgId}").SendAsync("notify", new
        {
            id = note.Id, type, message, at = note.CreatedAt, read = false
        });                                                          // push live
    }
}