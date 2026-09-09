using kisatsingen.Components;
using kisatsingen.Constants;
using kisatsingen.Data;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenAI;
using Prometheus;
using Vestfold.Extensions.Logging;
using Vestfold.Extensions.Metrics;

var builder = WebApplication.CreateBuilder(args);

// ─── Framework ─────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ─── Observability ─────────────────────────────────────
builder.Logging.AddVestfoldLogging();

builder.Services.AddVestfoldMetrics();
builder.Services.UseHttpClientMetrics();

// ─── Authentication & authorization ────────────────────
// Cascades authentication state seamlessly to <AuthorizeView> components
builder.Services.AddCascadingAuthenticationState();

// Native Code-Level Microsoft Entra ID Authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("EntraConfiguration"));

builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = OpenIdConnectResponseType.Code;

    // NOTE: Enable if you want metrics per Role per User signin
    /*var existingOnTokenValidated = options.Events.OnTokenValidated;
    options.Events.OnTokenValidated = async ctx =>
    {
        await existingOnTokenValidated(ctx);

        var metricsService = ctx.HttpContext.RequestServices.GetRequiredService<IMetricsService>();

        foreach (var role in AppConstants.ContributionRoles)
        {
            if (ctx.Principal?.IsInRole(role) ?? false)
            {
                metricsService.Count($"{MetricConstants.MetricsAppPrefix}_{role}LoginCount", $"Number of {role} logins");
                break;
            }
        }
    };*/
});

builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("IsAdministrator", policy => policy.RequireRole(AppConstants.AdminRole))
    .AddPolicy("CanContributeAppWide", policy => policy.RequireRole(AppConstants.ContributionRoles));

// ─── Application configuration ─────────────────────────
var openAiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured. Set it via user-secrets or environment variables.");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";

builder.Services.AddChatClient(new OpenAIClient(openAiKey)
    .GetChatClient(openAiModel)
    .AsIChatClient())
    .UseFunctionInvocation();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// ─── Application services ──────────────────────────────
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<ChatSession>();
builder.Services.AddScoped<CircuitHandler, BlazorCircuitObserver>();

var app = builder.Build();

// ─── One-time startup: database ────────────────────────
if (app.Environment.IsDevelopment())
{
    using var migrationContext = AppDbContext.CreateForMigrations(app.Configuration);
    migrationContext.Database.Migrate();
}
else
{
    // Pending-migration check only — this uses the low-privilege DefaultConnection
    // via the registered factory, never the migration user. Actual migrations for
    // non-Development environments are applied by their own CI/CD job.
    using var db = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();

    var pending = db.Database.GetPendingMigrations().ToArray();
    if (pending.Length != 0)
    {
        app.Logger.LogWarning("{Count} pending migration(s). Run \"dotnet ef database update\" before continuing.", pending.Length);
    }
}

// ─── Security response headers (apply to every response) ───
var scriptSrc = app.Environment.IsDevelopment()
    ? "'self' 'unsafe-inline'"                          // dotnet-watch injects an inline bootstrapper
    : "'self'";
var connectSrc = app.Environment.IsDevelopment()
    ? "'self' ws://localhost:* wss://localhost:*"       // browser-refresh WebSocket on a random port
    : "'self'";

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; " +
        $"script-src {scriptSrc}; " +
        "style-src 'self' 'unsafe-inline' https://altinncdn.no; " +
        "font-src 'self' https://altinncdn.no; " +
        "img-src 'self' data:; " +
        $"connect-src {connectSrc}; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'";

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

// ─── Errors & transport ────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// ─── Observability endpoints ───────────────────────────
app.UseMetricServer();
app.UseHttpMetrics();

// ─── Auth ──────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ─── Endpoints ─────────────────────────────────────────
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

app.Run();
