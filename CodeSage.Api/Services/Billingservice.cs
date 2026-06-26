using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

// Phase 3 #3/#5: plans, Razorpay checkout (subscription OR one-time order), billing history.
// Modes:
//   simulated    — no keys: plan flips instantly (pure dev).
//   subscription — keys + a plan_id configured: recurring Razorpay subscription.
//   order        — keys but NO plan_id: one-time Razorpay order, so you can test the
//                  real gateway without creating dashboard plans. Confirmed client-side
//                  via signature verification (no webhook needed).
public class BillingService
{
    private readonly MongoContext _db;
    private readonly UsageService _usage;
    private readonly RazorpayClient _rzp;
    private readonly BillingSettings _billing;

    public BillingService(MongoContext db, UsageService usage, RazorpayClient rzp, IOptions<BillingSettings> billing)
    {
        _db = db; _usage = usage; _rzp = rzp; _billing = billing.Value;
    }

    public bool IsLive => _rzp.IsConfigured;

    public async Task<BillingSummaryDto> GetSummaryAsync(string orgId)
    {
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        var usage = await _usage.GetCurrentAsync(orgId);
        var history = await _usage.GetHistoryAsync(orgId);
        var events = await _db.BillingEvents.Find(e => e.OrgId == orgId)
            .SortByDescending(e => e.CreatedAt).Limit(20).ToListAsync();

        return new BillingSummaryDto(
            org?.Plan ?? "free", usage, history,
            events.Select(e => new BillingEventDto(e.Type, e.Plan, e.AmountCents, e.Note, e.CreatedAt)).ToList(),
            IsLive);
    }

    public async Task<CheckoutResultDto> CheckoutAsync(string orgId, string planId)
    {
        var plan = PlanCatalog.Get(planId);
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        if (org is null) return new CheckoutResultDto("error", null, null, 0, "Organization not found.");

        // Downgrade to Free: cancel any subscription, flip immediately.
        if (plan.Id == "free")
        {
            if (IsLive && org.RazorpaySubscriptionId is not null)
            {
                try { await _rzp.CancelSubscriptionAsync(org.RazorpaySubscriptionId); } catch { }
            }
            await ApplyPlanAsync(org, plan, "downgrade", IsLive ? "Subscription cancelled" : "Simulated downgrade");
            return new CheckoutResultDto("simulated", null, null, 0, "Switched to Free.");
        }

        // No keys -> simulated flip.
        if (!IsLive)
        {
            var prev = PlanCatalog.Get(org.Plan);
            await ApplyPlanAsync(org, plan, plan.PriceCents >= prev.PriceCents ? "upgrade" : "downgrade",
                "Simulated (no Razorpay keys configured)");
            return new CheckoutResultDto("simulated", null, null, 0, $"Switched to {plan.Name} (simulated).");
        }

        var planRef = plan.Id == "pro" ? _billing.ProPlanId : _billing.TeamPlanId;

        // Keys + a plan_id -> real recurring subscription.
        if (!string.IsNullOrWhiteSpace(planRef))
        {
            try
            {
                var sub = await _rzp.CreateSubscriptionAsync(planRef, orgId);
                org.RazorpaySubscriptionId = sub.Id;
                org.RazorpaySubscriptionStatus = sub.Status;
                await _db.Organizations.ReplaceOneAsync(o => o.Id == orgId, org);
                return new CheckoutResultDto("subscription", null, sub.Id, plan.PriceCents,
                    $"Authorize the recurring payment to switch to {plan.Name}.");
            }
            catch (Exception ex)
            {
                return new CheckoutResultDto("error", null, null, 0, "Razorpay subscription failed. " + ex.Message);
            }
        }

        // Keys but NO plan_id -> one-time order so the gateway can still be tested.
        try
        {
            var orderId = await _rzp.CreateOrderAsync(plan.PriceCents, $"org_{orgId}_{plan.Id}");
            return new CheckoutResultDto("order", orderId, null, plan.PriceCents,
                $"Complete the payment to switch to {plan.Name}.");
        }
        catch (Exception ex)
        {
            return new CheckoutResultDto("error", null, null, 0, "Razorpay order failed. " + ex.Message);
        }
    }

    // Called after a one-time order payment succeeds in the browser.
    public async Task<bool> VerifyOrderPaymentAsync(string orgId, string planId, string orderId, string paymentId, string signature)
    {
        if (!_rzp.VerifyPaymentSignature(orderId, paymentId, signature)) return false;

        var plan = PlanCatalog.Get(planId);
        var org = await _db.Organizations.Find(o => o.Id == orgId).FirstOrDefaultAsync();
        if (org is null) return false;

        await ApplyPlanAsync(org, plan, "charge", $"One-time payment ({paymentId})");
        return true;
    }

    // Webhook path (recurring subscriptions).
    public async Task HandleWebhookEventAsync(string eventType, string subscriptionId, string status, int amountPaise)
    {
        var org = await _db.Organizations.Find(o => o.RazorpaySubscriptionId == subscriptionId).FirstOrDefaultAsync();
        if (org is null) return;
        org.RazorpaySubscriptionStatus = status;
        var planId = GuessPlanFromAmount(amountPaise);

        switch (eventType)
        {
            case "subscription.activated":
            case "subscription.authenticated":
                if (!string.IsNullOrEmpty(planId)) org.Plan = planId;
                await _db.Organizations.ReplaceOneAsync(o => o.Id == org.Id, org);
                await RecordEventAsync(org.Id!, "upgrade", org.Plan, amountPaise, "Activated via Razorpay");
                break;
            case "subscription.charged":
                await _db.Organizations.ReplaceOneAsync(o => o.Id == org.Id, org);
                await RecordEventAsync(org.Id!, "charge", org.Plan, amountPaise, "Monthly charge");
                break;
            case "subscription.cancelled":
            case "subscription.halted":
                org.Plan = "free"; org.RazorpaySubscriptionId = null;
                await _db.Organizations.ReplaceOneAsync(o => o.Id == org.Id, org);
                await RecordEventAsync(org.Id!, "downgrade", "free", 0, $"Subscription {eventType.Split('.')[1]}");
                break;
        }
    }

    private static string GuessPlanFromAmount(int amountPaise)
    {
        if (amountPaise == PlanCatalog.Pro.PriceCents) return "pro";
        if (amountPaise == PlanCatalog.Team.PriceCents) return "team";
        return "free";
    }

    private async Task ApplyPlanAsync(Organization org, Plan plan, string eventType, string? note)
    {
        org.Plan = plan.Id;
        await _db.Organizations.ReplaceOneAsync(o => o.Id == org.Id, org);
        await RecordEventAsync(org.Id!, eventType, plan.Id, plan.PriceCents, note);
    }

    private Task RecordEventAsync(string orgId, string type, string plan, int amount, string? note) =>
        _db.BillingEvents.InsertOneAsync(new BillingEvent
        { OrgId = orgId, Type = type, Plan = plan, AmountCents = amount, Note = note });
}