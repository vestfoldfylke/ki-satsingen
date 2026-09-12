using kisatsingen.Data.Entities;
using kisatsingen.Data.Repositories;

namespace kisatsingen.Tests;

// The production API requires a caller-allocated Guid so a draft chat can be
// persisted under the id its URL already carries. Tests never care which id a
// chat gets, so this shim keeps the pre-refactor call shape without spreading
// throwaway Guid.NewGuid() across every arrange block.
internal static class ChatRepositoryTestExtensions
{
    public static Task<Chat> CreateChatAsync(this IChatRepository repo, string ownerId, string title, CancellationToken ct = default) =>
        repo.CreateChatAsync(Guid.NewGuid(), ownerId, title, ct);
}
