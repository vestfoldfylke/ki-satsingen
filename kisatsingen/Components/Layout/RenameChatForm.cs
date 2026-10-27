using System.ComponentModel.DataAnnotations;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Layout;

internal sealed class RenameChatForm
{
    // In ErrorMessage - {0} = FieldName, {1} = MaximumLength, {2} = MinimumLength (if set)
    [Required(ErrorMessage = "Gi samtalen en tittel")]
    [StringLength(kisatsingen.Data.Entities.Chat.MaxTitleLength, MinimumLength = 1, ErrorMessage = "Tittelen må være mellom {2} og {1} tegn.")]
    public string Title { get; set; } = string.Empty;

    public static RenameChatForm FromChat(ChatListEntry chat) => new()
    {
        Title = chat.Title,
    };
}
