using kisatsingen.Components.Shared;
using Microsoft.AspNetCore.Components;
using kisatsingen.Services.Chat;

namespace kisatsingen.Components.Chat;

public sealed partial class InstructionsModal : ComponentBase
{
    [Parameter, EditorRequired]
    public required Turn Turn { get; set; }

    [Parameter, EditorRequired]
    public required string Id { get; set; }

    private bool _isNotFound;
    private string? _errorMessage;

    private string PopoverId => $"assistant-turn-instructions-{Turn.Id}";

    private ModalDialog? _dialog;

    public void Load(Turn turn)
    {
        Turn = turn;
        _isNotFound = false;
        _errorMessage = null;
        StateHasChanged();
    }
}
