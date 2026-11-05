using kisatsingen.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

public sealed partial class InstructionsModal : ComponentBase
{
    [Parameter, EditorRequired]
    public required string Instructions { get; set; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    private ModalDialog? _dialog;

    public void Load(string instructions)
    {
        Instructions = instructions;
        StateHasChanged();
    }
}
