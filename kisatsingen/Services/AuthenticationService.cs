using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace kisatsingen.Services;

public interface IAuthenticationService
{
    Task<string?> GetUserObjectIdentifierAsync();

    /// <exception cref="UserNotAuthenticatedException">No authenticated user or objectidentifier claim found.</exception>
    Task<string> RequireUserObjectIdentifierAsync();
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

    private async Task<ClaimsPrincipal> GetUserAsync()
    {
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        return authState.User;
    }

    public async Task<string?> GetUserObjectIdentifierAsync()
    {
        var user = await GetUserAsync();
        if (user.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("UserIdentity not present ({IdentityPresent}) or user not Authenticated: {IsAuthenticated}", user.Identity is null, user.Identity?.IsAuthenticated == true);
            return null;
        }

        var objectIdClaim = user.Claims.FirstOrDefault(claim => claim.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier");
        return objectIdClaim?.Value;
    }

    public async Task<string> RequireUserObjectIdentifierAsync()
    {
        var userObjectId = await GetUserObjectIdentifierAsync();
        return string.IsNullOrEmpty(userObjectId)
            ? throw new UserNotAuthenticatedException()
            : userObjectId;
    }
}