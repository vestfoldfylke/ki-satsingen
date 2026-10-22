using kisatsingen.Services;
using Microsoft.Graph.Models;

namespace kisatsingen.Tests.Services;

// Answers from Users the way Graph does: ids it does not know are simply left
// out of the result. Every call's ids are kept in Calls, so a test can see what
// the cache actually asked for. Set FailWith to make every call throw, the way
// a throttled or unauthorised Graph would.
internal sealed class FakeGraphService : IGraphService
{
    public List<User> Users { get; } = [];

    public List<string[]> Calls { get; } = [];

    public Exception? FailWith { get; set; }

    public Task<List<User>> GetUsersByObjectIds(string[] objectIds, CancellationToken cancellationToken)
    {
        Calls.Add(objectIds);

        if (FailWith is not null)
        {
            throw FailWith;
        }

        return Task.FromResult(Users.Where(user => objectIds.Contains(user.Id)).ToList());
    }

    public static User CreateUser(string id, string displayName) => new()
    {
        Id = id,
        DisplayName = displayName,
        CompanyName = "Vestfold fylkeskommune",
        Department = "Digitalisering",
        UserPrincipalName = $"{id}@vestfoldfylke.no"
    };
}
