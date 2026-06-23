using CodeSage.Api.Data;
using CodeSage.Api.Dtos;
using CodeSage.Api.Models;
using CodeSage.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeSage.Api.Services;

// Phase 3 #3/#5: plans, Razorpay subscription checkout, and billing history.
// Live Razorpay is gated behind a configured KeyId+KeySecret; without it, upgrades
// are simulated so the whole flow remains usable in development.
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
        if (org is null) return new CheckoutResultDto(false, null, "Organization not found.");

        // Downgrade to Free: cancel any active subscription and flip immediately.
        if (plan.Id == "free")
        {
            if (IsLive && org.RazorpaySubscriptionId is not null)
            {
                try { await _rzp.CancelSubscriptionAsync(org.RazorpaySubscriptionId); }
                catch { /* webhook will eventually reconcile */ }
            }
            await ApplyPlanAsync(org, plan, "downgrade", note: IsLive ? "Subscription cancelled" : "Simulated downgrade");
            return new CheckoutResultDto(true, null, "Switched to Free.");
        }

        if (!IsLive)
        {
            var prev = PlanCatalog.Get(org.Plan);
            await ApplyPlanAsync(org, plan, plan.PriceCents >= prev.PriceCents ? "upgrade" : "downgrade",
                note: "Simulated (no Razorpay keys configured)");
            return new CheckoutResultDto(true, null, $"Switched to {plan.Name} (simulated).");
        }

        // Live path: create a Razorpay subscription and hand it to the browser.
        var planRef = plan.Id == "pro" ? _billing.ProPlanId : _billing.TeamPlanId;
        if (string.IsNullOrWhiteSpace(planRef))
            return new CheckoutResultDto(false, null,
                $"Set Billing:{(plan.Id == "pro" ? "ProPlanId" : "TeamPlanId")} in appsettings to the Razorpay plan_id for {plan.Name}.");

        try
        {
            var sub = await _rzp.CreateSubscriptionAsync(planRef, orgId);
            org.RazorpaySubscriptionId = sub.Id;
            org.RazorpaySubscriptionStatus = sub.Status;
            await _db.Organizations.ReplaceOneAsync(o => o.Id == orgId, org);

            // Returning `subscriptionId` lets the browser open Razorpay Checkout with it.
            // The plan does NOT change until the webhook tells us it's been authenticated/activated.
            return new CheckoutResultDto(false,
                sub.Id,  // we reuse the Url field on the DTO to carry the subscription id
                $"Razorpay subscription created. Authorize the recurring payment to switch to {plan.Name}.");
        }
        catch (Exception ex)
        {
            return new CheckoutResultDto(false, null,
                "Razorpay refused the subscription. " + ex.Message);
        }
    }

    // Called by RazorpayWebhookController after signature is verified.
    public async Task HandleWebhookEventAsync(string eventType, string subscriptionId, string status, int amountPaise)
    {
        var org = await _db.Organizations.Find(o => o.RazorpaySubscriptionId == subscriptionId).FirstOrDefaultAsync();
        if (org is null) return;

        org.RazorpaySubscriptionStatus = status;

        // Decide which plan id this subscription corresponds to.
        var planId = subscriptionId == org.RazorpaySubscriptionId
            ? GuessPlanFromSettings(amountPaise)
            : org.Plan;

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
                org.Plan = "free";
                org.RazorpaySubscriptionId = null;
                await _db.Organizations.ReplaceOneAsync(o => o.Id == org.Id, org);
                await RecordEventAsync(org.Id!, "downgrade", "free", 0, $"Subscription {eventType.Split('.')[1]}");
                break;
        }
    }

    private string GuessPlanFromSettings(int amountPaise)
    {
        // The webhook payload carries the amount; map it back to a plan.
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
        {
            OrgId = orgId,
            Type = type,
            Plan = plan,
            AmountCents = amount,
            Note = note
        });
}