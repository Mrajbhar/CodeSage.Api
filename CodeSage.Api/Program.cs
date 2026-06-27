using System.Text;
using CodeSage.Api.Data;
using CodeSage.Api.Hubs;
using CodeSage.Api.Jobs;
using CodeSage.Api.Services.Email;
using CodeSage.Api.Services;
using CodeSage.Api.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
// Hangfire.Mongo also ships a CacheSettings type; alias ours to avoid the ambiguity.
using CacheSettings = CodeSage.Api.Settings.CacheSettings;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));
builder.Services.Configure<OAuthSettings>(builder.Configuration.GetSection("OAuth"));
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<BillingSettings>(builder.Configuration.GetSection("Billing"));
builder.Services.Configure<CacheSettings>(builder.Configuration.GetSection("Cache"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));

// ---- Services ----
builder.Services.AddSingleton<MongoContext>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OAuthService>();
builder.Services.AddScoped<GitHubService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<UsageService>();
builder.Services.AddScoped<RazorpayClient>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<OrgContext>();
builder.Services.AddScoped<AuditService>();
var emailCfg = builder.Configuration.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();
if (string.IsNullOrWhiteSpace(emailCfg.Host))
    builder.Services.AddScoped<IEmailSender, ConsoleEmailSender>();
else
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<CacheService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddScoped<IndexService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<BackgroundJobs>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();


// ---- #1 Distributed cache: Redis when configured, otherwise in-memory ----
var cacheCfg = builder.Configuration.GetSection("Cache").Get<CacheSettings>() ?? new CacheSettings();
if (string.IsNullOrWhiteSpace(cacheCfg.ConnectionString))
    builder.Services.AddDistributedMemoryCache();
else
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = cacheCfg.ConnectionString);

// ---- #3 SignalR ----
builder.Services.AddSignalR();

// ---- #2 Hangfire on Mongo storage ----
var mongoCfg = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>()!;
var mongoUrl = new MongoDB.Driver.MongoUrl(mongoCfg.ConnectionString);
var hangfireDb = string.IsNullOrWhiteSpace(mongoUrl.DatabaseName) ? "codesage" : mongoUrl.DatabaseName;
builder.Services.AddHangfire(cfg => cfg.UseMongoStorage(
    mongoCfg.ConnectionString, hangfireDb + "_jobs",
    new MongoStorageOptions
    {
        CheckConnection = false,   // don't crash the whole app if Mongo is briefly unreachable at boot
        MigrationOptions = new MongoMigrationOptions
        {
            MigrationStrategy = new MigrateMongoMigrationStrategy(),
            BackupStrategy = new NoneMongoBackupStrategy()
        }
    }));
builder.Services.AddHangfireServer();

// ---- Rate limiting (built-in, no package) ----
// "auth" is strict (brute-force / credential-stuffing defence); "global" is a sane default.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Per-IP fixed window: 5 attempts / 30s on sensitive auth endpoints.
    options.AddPolicy("auth", httpContext =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromSeconds(30),
                QueueLimit = 0,
            }));

    // Per-IP general limit so no single client can flood the API.
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
        httpContext => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    options.OnRejected = async (ctx, token) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"message\":\"Too many requests. Please wait a moment and try again.\"}", token);
    };
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// ---- CORS for the React client ----
var app0 = builder.Configuration.GetSection("App").Get<AppSettings>() ?? new AppSettings();
const string ClientCors = "client";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCors, p => p
        .WithOrigins(app0.FrontendBaseUrl)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("X-Org-Id")
        .AllowCredentials());
});

// ---- JWT bearer auth ----
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(5)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseCors(ClientCors);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationsHub>("/hub/notifications").DisableRateLimiting();

// Hangfire dashboard — admin-only outside Development.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new CodeSage.Api.Infrastructure.HangfireAuthFilter(app.Environment.IsDevelopment()) }
});
RecurringJob.AddOrUpdate<BackgroundJobs>("audit-cleanup", j => j.CleanupAuditLogs(), Cron.Daily);

app.Run();