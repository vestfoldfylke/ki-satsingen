using kisatsingen.Components;
using kisatsingen.Constants;
using kisatsingen.Data;
using kisatsingen.Data.Repositories;
using kisatsingen.Services;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;
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

builder.Services.AddHealthChecks();

// ─── Authentication & authorization ────────────────────
// Azure Web App terminates TLS at a reverse proxy; honor its X-Forwarded-* headers
// so the OIDC middleware builds the correct https redirect_uri.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;

    // App Gateway → App Service front-end → Kestrel: two proxies. (Needed to prevent RemoteIpAddress becomes App Gateway's private IP, not the real client)
    options.ForwardLimit = 2;

    // App Gateway overrides Host with <app>.azurewebsites.net and stashes
    // the original public host here. Remove this line if App Gateway is
    // configured to preserve the client Host header.
    options.ForwardedHostHeaderName = "X-Original-Host";

    // Trust boundary is enforced by App Service Access Restrictions
    // (only App Gateway's subnet allowed). Header spoofing is blocked
    // at the network edge, not by IP allowlisting here.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Cascades authentication state seamlessly to <AuthorizeView> components
builder.Services.AddCascadingAuthenticationState();

// Native Code-Level Microsoft Entra ID Authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("EntraAuthConfiguration"));

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
    .AddPolicy("CanContributeAppWide", policy => policy.RequireRole(AppConstants.ContributionRoles))
    .AddPolicy("CanReadMetrics", policy => policy.RequireRole(AppConstants.MetricsRole));

// ─── Application configuration ─────────────────────────
var openAiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured. Set it via user-secrets or environment variables.");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";

builder.Services.AddChatClient(new OpenAIClient(openAiKey)
    .GetChatClient(openAiModel)
    .AsIChatClient())
    .UseFunctionInvocation();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString)
{
    Name = "ChatDb"
};

var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(dataSource));

// ─── Application services ──────────────────────────────
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<ChatSession>();
builder.Services.AddScoped<CircuitHandler, BlazorCircuitObserver>();

var app = builder.Build();

// ─── One-time startup: database ────────────────────────
if (app.Environment.IsDevelopment())
{
    await using var migrationContext = AppDbContext.CreateForMigrations(app.Configuration);
    await migrationContext.Database.MigrateAsync();
}
else
{
    // Pending-migration check only — this uses the low-privilege DefaultConnection
    // via the registered factory, never the migration user. Actual migrations for
    // non-Development environments are applied by their own CI/CD job.
    await using var db = await app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
    if (pending.Length != 0)
    {
        app.Logger.LogWarning("{Count} pending migration(s). Run \"dotnet ef database update\" before continuing.", pending.Length);
    }
}

app.MapHealthChecks("/healthz");

// ─── Forwarded headers (must run before auth/HTTPS redirect) ───
app.UseForwardedHeaders();

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
app.UseHttpMetrics();

// ─── Auth ──────────────────────────────────────────────
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ─── Endpoints ─────────────────────────────────────────
app.MapMetrics().RequireAuthorization("CanReadMetrics");
app.MapStaticAssets();
var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

/*
.NET 11 - for the CloseOnAuthenticationExpiration
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options =>
    {
        options.ConfigureConnection = dispatcherOptions =>
        {
            dispatcherOptions.CloseOnAuthenticationExpiration = true;
        };
    });
*/

razorComponents.Add(endpoint =>
{
    var dispatcherOptions = endpoint.Metadata.OfType<HttpConnectionDispatcherOptions>().FirstOrDefault();
    if (dispatcherOptions is not null)
    {
        dispatcherOptions.CloseOnAuthenticationExpiration = true;
    }
});

razorComponents.RequireAuthorization();

app.Run();
