using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Identity.Web;

namespace kisatsingen.Services;

// Every log written while a circuit handles one of the user's inputs carries
// who the user is and which circuit it is.
internal sealed class UserLogScopeCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    ILogger<UserLogScopeCircuitHandler> logger) : CircuitHandler
{
    private const string UserIdPropertyName = "UserId";
    private const string CircuitIdPropertyName = "CircuitId";

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();

            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                [UserIdPropertyName] = authenticationState.User.GetObjectId(),
                [CircuitIdPropertyName] = context.Circuit.Id,
            });

            await next(context);
        };
}
