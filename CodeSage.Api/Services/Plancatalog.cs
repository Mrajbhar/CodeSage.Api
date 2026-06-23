namespace CodeSage.Api.Services;

public record Plan(string Id, string Name, int PriceCents, int AiCallsPerMonth, int MemberLimit, string[] Features);

public static class PlanCatalog
{
    // Prices are in INR paise (Razorpay's smallest unit; 100 paise = ₹1).
    // The field is named PriceCents for backwards compatibility with the existing DTOs.
    public static readonly Plan Free = new("free", "Free", 0, 50, 3,
        new[] { "50 AI reviews / month", "Up to 3 members", "Public & private repos" });
    public static readonly Plan Pro = new("pro", "Pro", 99900, 500, 10,
        new[] { "500 AI reviews / month", "Up to 10 members", "Priority model", "Usage analytics" });
    public static readonly Plan Team = new("team", "Team", 249900, 2000, 50,
        new[] { "2,000 AI reviews / month", "Up to 50 members", "Priority model", "Audit history" });

    public static readonly Plan[] All = { Free, Pro, Team };
    public static Plan Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Free;
}