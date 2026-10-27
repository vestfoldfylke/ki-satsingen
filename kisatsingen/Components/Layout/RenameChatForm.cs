using System.ComponentModel.DataAnnotations;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Layout;

internal sealed class RenameChatForm
{
    // In ErrorMessage - {0} = FieldName, {1} = MaximumLength, {2} = MinimumLength (if set)
    [Required(ErrorMessage = "Gi samtalen et navn")]
    [StringLength(kisatsingen.Data.Entities.Chat.MaxTitleLength, ErrorMessage = "Tittelen kan ha maks {1} tegn.")]
    public string Title { get; set; } = string.Empty;

    public static RenameChatForm FromChat(ChatListEntry chat) => new()
    {
        Title = chat.Title,
    };
}
