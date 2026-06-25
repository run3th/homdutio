using System.Text;
using Homdutio.Api.Auth;
using Homdutio.Api.Email;
using Homdutio.Api.Households;
using Homdutio.Api.Tasks;
using Homdutio.Data;
using Homdutio.Data.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Persistence: EF Core on SQL Server (LocalDB in dev, Azure SQL in prod). The connection string
// comes from configuration (user-secrets locally, App Service connection-strings in Azure) — never
// the repo. EnableRetryOnFailure handles Azure SQL transient faults (expected under the Basic 5-DTU
// cap); note its execution strategy disallows user-initiated transactions spanning the retry boundary.
// The connection string is read lazily from IConfiguration so test hosts (WebApplicationFactory) can
// override it after builder time.
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

// Identity user store mounted on the existing context (no cookies/UI — JWT bearer only).
// RequireConfirmedAccount=false: the only permitted v1 transactional email is password reset (S-08),
// so account-confirmation email is out of scope. SignInManager gives password check + lockout.
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

builder.Services.AddHttpContextAccessor();

// Stateless JWT bearer (roadmap 2026-05-30, superseding cookie sessions). Issuer/Audience/lifetime are
// non-secret config; the signing key comes from user-secrets locally / App Service settings in prod —
// never the repo. MapInboundClaims=false keeps the raw "sub"/"email" claim names on the principal.
// JwtBearer validation is configured from the bound JwtOptions (via DI) — not an eager snapshot — so it
// stays in lockstep with issuance (JwtTokenService) and remains overridable by test hosts.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtAccessor) =>
    {
        var jwt = jwtAccessor.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<JwtTokenService>();

// Scoped (not singleton like JwtTokenService) because it depends on the scoped ApplicationDbContext —
// token minting is stateless, but refresh persistence is per-request DB work.
builder.Services.AddScoped<RefreshTokenService>();

// Email is reset-only (S-08), the single permitted v1 transactional email. The live Azure
// Communication Services sender is selected only when an endpoint is configured (App Service setting
// in prod, optionally user-secrets locally); otherwise — local dev and the integration-test host — a
// no-op sender logs the link instead of sending, so nothing user-facing depends on a live provider
// or an ACS resource. Auth is by Entra ID / managed identity (DefaultAzureCredential): the App
// Service's managed identity is granted access to the ACS resource — no connection string or key.
builder.Services.Configure<AcsEmailOptions>(builder.Configuration.GetSection(AcsEmailOptions.SectionName));

var acsEndpoint = builder.Configuration[$"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.Endpoint)}"];
if (string.IsNullOrWhiteSpace(acsEndpoint))
{
    builder.Services.AddScoped<IEmailSender, NoOpEmailSender>();
}
else
{
    builder.Services.AddSingleton(new EmailClient(new Uri(acsEndpoint), new DefaultAzureCredential()));
    builder.Services.AddScoped<IEmailSender, AcsEmailSender>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Mapped before the SPA fallback so these routes return their payloads, not index.html.
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapHouseholdEndpoints();
app.MapTaskEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

// Exposed so the integration-test project (Homdutio.Api.Tests) can drive the host via WebApplicationFactory<Program>.
public partial class Program;
