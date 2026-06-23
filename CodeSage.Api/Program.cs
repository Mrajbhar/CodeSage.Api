using System.Text;
using CodeSage.Api.Data;
using CodeSage.Api.Services;
using CodeSage.Api.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("App"));
builder.Services.Configure<OAuthSettings>(builder.Configuration.GetSection("OAuth"));
builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("Ai"));
builder.Services.Configure<BillingSettings>(builder.Configuration.GetSection("Billing"));

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
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

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
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseCors(ClientCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();