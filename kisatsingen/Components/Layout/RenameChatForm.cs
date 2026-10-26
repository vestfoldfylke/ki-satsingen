using System.ComponentModel.DataAnnotations;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Layout;

// Shared by creating and editing, so both refuse the same input; the limits
// are the entity's own, so the form refuses what the repository would.
internal sealed class RenameChatForm
{
    // In ErrorMessage - {0} = FieldName, {1} = MaximumLength, {2} = MinimumLength (if set)
    [Required(ErrorMessage = "Gi samtalen en tittel")]
    [StringLength(kisatsingen.Data.Entities.Chat.MaxTitleLength, ErrorMessage = "Tittelen kan ha opptil {1} tegn.")]
    public string Title { get; set; } = string.Empty;

    public static RenameChatForm FromChat(ChatListEntry chat) => new()
    {
        Title = chat.Title,
    };
}
