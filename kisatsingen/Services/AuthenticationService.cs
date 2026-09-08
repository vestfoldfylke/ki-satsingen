using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Services;

public interface IAuthenticationService
{
    Task<string?> GetUserObjectIdentifierAsync();
}

public class AuthenticationService : IAuthenticationService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(AuthenticationStateProvider authenticationStateProvider, ILogger<AuthenticationService> logger)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _logger = logger;
    }

    public async Task<string?> GetUserObjectIdentifierAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("UserIdentity not present ({IdentityPresent}) or user not Authenticated: {IsAuthenticated}", authState.User.Identity is null, authState.User.Identity?.IsAuthenticated == true);
            return null;
        }

        var objectIdClaim = authState.User.Claims.FirstOrDefault(claim => claim.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier");
        return objectIdClaim?.Value;
    }
}