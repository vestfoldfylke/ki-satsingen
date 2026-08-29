using kisatsingen.Components;
using kisatsingen.Data;
using kisatsingen.Data.Repositories;
using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Authentication.Cookies;
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

        foreach (var role in AppConstants.Roles)
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
    .AddPolicy("IsAdministrator", policy => policy.RequireRole("Administrator"))
    .AddPolicy("CanContributeAppWide", policy => policy.RequireRole("Contributor", "Administrator"));

// ─── Application configuration ─────────────────────────
var openAiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured. Set it via user-secrets or environment variables.");
var openAiModel = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini";

builder.Services.AddChatClient(new OpenAIClient(openAiKey)
    .GetChatClient(openAiModel)
    .AsIChatClient())
    .UseFunctionInvocation();

var connectionString = builder.Configuration.GetConnectionString("AppDb")
    ?? "Data Source=./dev-db/local-test.db";

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ─── Application services ──────────────────────────────
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<ChatSession>();

var app = builder.Build();

// ─── One-time startup: database ────────────────────────
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    if (db.Database.IsSqlite())
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
}

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
