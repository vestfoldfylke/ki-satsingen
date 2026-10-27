using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Identity.Web;

namespace kisatsingen.Services;

public interface IAuthenticationService
{
    Task<Guid?> GetUserObjectIdentifierAsync();

    /// <exception cref="UserNotAuthenticatedException">No authenticated user or valid objectidentifier claim found.</exception>
    Task<Guid> RequireUserObjectIdentifierAsync();
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

    public async Task<Guid?> GetUserObjectIdentifierAsync()
    {
        var user = await GetUserAsync();
        if (user.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("UserIdentity not present ({IdentityPresent}) or user not Authenticated: {IsAuthenticated}", user.Identity is null, user.Identity?.IsAuthenticated == true);
            return null;
        }

        var objectIdClaim = user.GetObjectId();
        if (!Guid.TryParse(objectIdClaim, out var objectId))
        {
            _logger.LogWarning("The objectidentifier claim is missing or not a GUID: {ObjectIdClaim}. The user is treated as not authenticated", objectIdClaim);
            return null;
        }

        return objectId;
    }

    public async Task<Guid> RequireUserObjectIdentifierAsync()
    {
        var userObjectId = await GetUserObjectIdentifierAsync();
        return userObjectId ?? throw new UserNotAuthenticatedException();
    }
}