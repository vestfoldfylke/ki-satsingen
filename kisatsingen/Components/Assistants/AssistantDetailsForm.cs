using System.ComponentModel.DataAnnotations;
using kisatsingen.Data.Entities;

namespace kisatsingen.Components.Assistants;

// Shared by creating and editing, so both refuse the same input; the limits
// are the entity's own, so the form refuses what the repository would.
internal sealed class AssistantDetailsForm
{
    // In ErrorMessage - {0} = FieldName, {1} = MaximumLength, {2} = MinimumLength (if set)
    [Required(ErrorMessage = "Gi assistenten et navn.")]
    [StringLength(Assistant.MaxNameLength, ErrorMessage = "Navnet kan ha opptil {1} tegn.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(Assistant.MaxDescriptionLength, ErrorMessage = "Beskrivelsen kan ha opptil {1} tegn.")]
    public string? Description { get; set; }

    public AssistantVisibility Visibility { get; set; } = AssistantVisibility.Private;

    public static AssistantDetailsForm From(Assistant assistant) => new()
    {
        Name = assistant.Name,
        Description = assistant.Description,
        Visibility = assistant.Visibility
    };
}
