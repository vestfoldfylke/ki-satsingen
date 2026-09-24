using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

public sealed partial class ModelPicker : ComponentBase
{
    // Fixed: only one composer is ever on screen.
    private const string PopoverId = "composer-switch-model-dropdown";

    [Parameter, EditorRequired]
    public required IReadOnlyList<ChatModel> Models { get; set; }

    [Parameter, EditorRequired]
    public required ChatModel Selected { get; set; }

    [Parameter]
    public EventCallback<ChatModelKey> OnSelect { get; set; }

    [Parameter]
    public bool IsBusy { get; set; }

    [Parameter]
    public long? EstimatedContextTokens { get; set; }

    // The visible label is just the model name, which doesn't say what the control
    // does; screen readers get the whole sentence.
    private string TriggerLabel => $"Språkmodell: {Selected.DisplayName}. Velg en annen.";
}
