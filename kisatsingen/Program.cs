using kisatsingen.Components;
using kisatsingen.Data;
using kisatsingen.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cascades authentication state seamlessly to <AuthorizeView> components
builder.Services.AddCascadingAuthenticationState();

// 2. Native Code-Level Microsoft Entra ID Authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("EntraConfiguration"))
    // Injects the downstream API engine
    .EnableTokenAcquisitionToCallDownstreamApi() 
    // Local memory token cache. Swap to AddDistributedTokenCaches + Redis for prod web farms
    .AddInMemoryTokenCaches();

// 3. FORCE AUTOMATIC LOGIN GLOBALLY
builder.Services.AddAuthorization(options =>
{
    // Sets the default application policy to require authentication everywhere
    options.FallbackPolicy = options.DefaultPolicy; 
});

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

builder.Services.AddScoped<IChatRepository, ChatRepository>();

var app = builder.Build();

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(); // Instructs the router to force authentication instantly

app.Run();
