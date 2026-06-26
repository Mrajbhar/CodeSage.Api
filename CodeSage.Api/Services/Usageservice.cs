using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

// Phase 3 #4: counts AI calls per org per month and enforces the plan cap.
public class UsageService
{
    private readonly MongoContext _db;
    public UsageService(MongoContext db) => _db = db;

    private static string CurrentPeriod => DateTime.UtcNow.ToString("yyyy-MM");

    public async Task<(bool ok, int used, int limit)> CheckAsync(string orgId)
    {
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        if (org is null) return (true, 0, int.MaxValue);   // unknown org -> don't block

        var limit = PlanCatalog.Get(org.Plan).AiCallsPerMonth;
        var usage = await _db.Usage.Find(u => u.OrgId == orgId && u.Period == CurrentPeriod).FirstOrDefaultAsync();
        var used = usage is null ? 0 : usage.ExplainCalls + usage.ReviewCalls;
        return (used < limit, used, limit);
    }

    public Task IncrementAsync(string orgId, string kind)
    {
        var period = CurrentPeriod;
        var inc = kind == "review"
            ? Builders<Usage>.Update.Inc(u => u.ReviewCalls, 1)
            : Builders<Usage>.Update.Inc(u => u.ExplainCalls, 1);
        var update = inc.SetOnInsert(u => u.OrgId, orgId).SetOnInsert(u => u.Period, period);

        return _db.Usage.UpdateOneAsync(
            u => u.OrgId == orgId && u.Period == period, update, new UpdateOptions { IsUpsert = true });
    }

    public async Task<UsageDto> GetCurrentAsync(string orgId)
    {
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        var limit = PlanCatalog.Get(org?.Plan).AiCallsPerMonth;
        var u = await _db.Usage.Find(x => x.OrgId == orgId && x.Period == CurrentPeriod).FirstOrDefaultAsync();
        var explain = u?.ExplainCalls ?? 0;
        var review = u?.ReviewCalls ?? 0;
        return new UsageDto(CurrentPeriod, explain + review, limit, explain, review);
    }

    public async Task<List<UsagePeriodDto>> GetHistoryAsync(string orgId, int months = 6)
    {
        var periods = Enumerable.Range(0, months)
            .Select(i => DateTime.UtcNow.AddMonths(-i).ToString("yyyy-MM"))
            .ToList();

        var docs = await _db.Usage.Find(u => u.OrgId == orgId && periods.Contains(u.Period)).ToListAsync();
        var byPeriod = docs.ToDictionary(d => d.Period);

        return periods.AsEnumerable().Reverse().Select(p =>
        {
            byPeriod.TryGetValue(p, out var d);
            var e = d?.ExplainCalls ?? 0; var r = d?.ReviewCalls ?? 0;
            return new UsagePeriodDto(p, e, r, e + r);
        }).ToList();
    }
}