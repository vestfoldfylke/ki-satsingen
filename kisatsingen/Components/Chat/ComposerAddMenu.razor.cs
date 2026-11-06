using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Chat;

public sealed partial class ComposerAddMenu : ComponentBase
{
    // Fixed: only one composer is ever on screen.
    private const string PopoverId = "composer-add-menu-dropdown";

    private ElementReference _popover;

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required ILogger<ComposerAddMenu> Logger { get; set; }

    [Parameter]
    public bool IsFilePickerDisabled { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnFilesSelected { get; set; }

    // Selecting files is a click inside the popover, which popover="auto"
    // doesn't treat as a dismissal, so the menu would stay open over the upload.
    private async Task RaiseFilesSelectedAsync(InputFileChangeEventArgs args)
    {
        try
        {
            await JS.InvokeVoidAsync("chatClient.hidePopover", _popover);
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "ComposerAddMenu could not close its popover after a file selection");
        }

        await OnFilesSelected.InvokeAsync(args);
    }
}
