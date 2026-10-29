using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace kisatsingen.Services;

public interface IGraphService
{
    Task<List<User>> GetUsersByObjectIds(Guid[] objectIds, CancellationToken cancellationToken);
}

public class GraphService : IGraphService
{
    private readonly string _graphBaseUrl;
    private readonly GraphServiceClient _graphClient;

    private const string DefaultGraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string DefaultGraphScope = "https://graph.microsoft.com/.default";

    private const int MaxFilterInExpressions = 15;

    private readonly string[] _userProperties =
    [
        "displayName",
        "companyName",
        "department",
        "id",
        "userPrincipalName"
    ];

    public GraphService(IConfiguration configuration)
    {
        _graphBaseUrl = configuration["Graph:BaseUrl"] ?? DefaultGraphBaseUrl;
        var graphScope = configuration["Graph:Scope"] ?? DefaultGraphScope;

        var clientId = configuration["EntraAuthConfiguration:ClientId"] ?? throw new InvalidOperationException("Required configuration EntraAuthConfiguration:ClientId is missing. Set it in appsettings.json");
        var clientSecret = configuration["EntraAuthConfiguration:ClientSecret"] ?? throw new InvalidOperationException("Required configuration EntraAuthConfiguration:ClientSecret is missing. Set it in appsettings.json");
        var tenantId = configuration["EntraAuthConfiguration:TenantId"] ?? throw new InvalidOperationException("Required configuration EntraAuthConfiguration:TenantId is missing. Set it in appsettings.json");

        var tokenCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _graphClient = new GraphServiceClient(tokenCredential, [graphScope], _graphBaseUrl);
    }

    public async Task<List<User>> GetUsersByObjectIds(Guid[] objectIds, CancellationToken cancellationToken)
    {
        List<User> allUsers = [];

        foreach (var objectIdChunk in objectIds.Chunk(MaxFilterInExpressions))
        {
            var usersResponse = await GetUsersPage(
                $"{_graphBaseUrl}/users?$filter=id in ('{string.Join("','", objectIdChunk)}')&$select={string.Join(",", _userProperties)}", cancellationToken);

            allUsers.AddRange(usersResponse.Value ?? []);

            while (usersResponse.OdataNextLink is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                usersResponse = await GetUsersPage(usersResponse.OdataNextLink, cancellationToken);
                allUsers.AddRange(usersResponse.Value ?? []);
            }
        }

        return allUsers;
    }

    private async Task<UserCollectionResponse> GetUsersPage(string requestUrl, CancellationToken cancellationToken) =>
        await _graphClient.Users.WithUrl(requestUrl).GetAsync(cancellationToken: cancellationToken) ?? new UserCollectionResponse();
}
