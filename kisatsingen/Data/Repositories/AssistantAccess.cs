using System.Linq.Expressions;
using kisatsingen.Data.Entities;

namespace kisatsingen.Data.Repositories;

// Who may use an assistant (see it, chat with it, read its files), in one
// place, so every query that touches assistants applies the same rule.
internal static class AssistantAccess
{
    public static Expression<Func<Assistant, bool>> UsableBy(string userId) =>
        assistant => assistant.OwnerId == userId || assistant.Visibility == AssistantVisibility.Organization;
}
