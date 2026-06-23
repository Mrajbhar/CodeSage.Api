namespace CodeSage.Api.Dtos;

public record PlanDto(string Id, string Name, int PriceCents, int AiCallsPerMonth, int MemberLimit, string[] Features);
public record UsageDto(string Period, int Used, int Limit, int Explain, int Review);
public record UsagePeriodDto(string Period, int Explain, int Review, int Total);
public record BillingEventDto(string Type, string Plan, int AmountCents, string? Note, DateTime CreatedAt);
public record BillingSummaryDto(string Plan, UsageDto Usage, List<UsagePeriodDto> History, List<BillingEventDto> Events, bool BillingLive);

public record CheckoutRequest(string Plan);
public record CheckoutResultDto(bool Simulated, string? Url, string Message);