using kisatsingen.Services.Chat;
using Microsoft.AspNetCore.Components;

namespace kisatsingen.Components.Chat;

public sealed partial class ModelPicker : ComponentBase
{
    // Fixed rather than per-instance: only one composer is on screen at a time,
    // and a stable id keeps the popovertarget wiring readable in the DOM.
    private const string PopoverId = "composer-switch-model-dropdown";

    [Parameter, EditorRequired]
    public required IReadOnlyList<ChatModel> Models { get; set; }

    [Parameter, EditorRequired]
    public required ChatModel Selected { get; set; }

    [Parameter]
    public EventCallback<ChatModelKey> OnSelect { get; set; }

    [Parameter]
    public bool IsBusy { get; set; }

    // Null when no turn has reported usage, in which case no model is marked —
    // see ChatModel.WouldOverflow.
    [Parameter]
    public long? EstimatedContextTokens { get; set; }

    // The visible label is the model's name alone, which says nothing about what
    // the control does. Screen readers get the whole sentence.
    private string TriggerLabel => $"Språkmodell: {Selected.DisplayName}. Velg en annen.";
}
