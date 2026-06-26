namespace CodeSage.Api.Settings;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = null!;
    public string DatabaseName { get; set; } = null!;
}

public class JwtSettings
{
    public string Key { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public class AppSettings
{
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string ApiBaseUrl { get; set; } = "https://localhost:7147";
}

public class OAuthProvider
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";   // GitHub webhook signing secret (auto-review)
}

public class OAuthSettings
{
    public OAuthProvider GitHub { get; set; } = new();
    public OAuthProvider Google { get; set; } = new();
}

public class AiSettings
{
    // OpenAI-compatible. For OpenAI: BaseUrl https://api.openai.com/v1.
    // For Ollama (local, free): BaseUrl http://localhost:11434/v1, any ApiKey, Model e.g. "llama3.1".
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public string EmbedModel { get; set; } = "nomic-embed-text";
}

public class BillingSettings
{
    // Leave KeyId blank to run in simulated mode (plan changes apply instantly, no real charge).
    // Set KeyId + KeySecret + plan ids below to enable live Razorpay subscriptions.
    public string KeyId { get; set; } = "";
    public string KeySecret { get; set; } = "";
    public string WebhookSecret { get; set; } = "";

    // Razorpay plan ids — created once in the Razorpay dashboard.
    // Plans > Create plan (₹ amount, billing cycle = monthly) → copy the plan_<...> id here.
    public string ProPlanId { get; set; } = "";
    public string TeamPlanId { get; set; } = "";
}

public class CacheSettings
{
    // Leave blank to use in-process memory cache; set a Redis connection string to use Redis.
    public string ConnectionString { get; set; } = "";
}

public class EmailSettings
{
    // Leave Host blank to log emails to the console (dev). Set SMTP details to send for real.
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseSsl { get; set; } = true;
    public string FromAddress { get; set; } = "no-reply@codesage.local";
    public string FromName { get; set; } = "CodeSage";
}