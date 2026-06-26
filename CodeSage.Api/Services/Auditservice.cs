using CodeSage.Api.Data;
using CodeSage.Api.Models;

namespace CodeSage.Api.Services;

// Phase 4 #5: records who did what, per organization, for the activity timeline.
public class AuditService
{
    private readonly MongoContext _db;
    public AuditService(MongoContext db) => _db = db;

    public Task LogAsync(string orgId, string? actorId, string actorName, string action, string target) =>
        _db.AuditLogs.InsertOneAsync(new AuditLog
        {
            OrgId = orgId,
            ActorId = actorId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "System" : actorName,
            Action = action,
            Target = target
        });
}