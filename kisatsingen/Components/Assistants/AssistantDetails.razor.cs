using kisatsingen.Data.Entities;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Assistants;

// An assistant's details (name, description, visibility), and for those who
// may edit it, a menu to edit them or delete the assistant.
public sealed partial class AssistantDetails : ComponentBase
{
    [Parameter, EditorRequired]
    public required Assistant Assistant { get; set; }

    // Raised once the details are saved.
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private string MenuId => $"assistant-menu-{Assistant.Id}";
    private string EditDialogId => $"edit-assistant-details-{Assistant.Id}";
    private string DeleteDialogId => $"delete-assistant-{Assistant.Id}";

    private EditAssistantDetailsDialog? _editDialog;
}
