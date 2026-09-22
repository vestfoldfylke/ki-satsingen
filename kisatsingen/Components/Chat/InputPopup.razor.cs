namespace kisatsingen.Components.Chat;
public partial class InputPopup ()
{
    public enum PopupMode { Confirm, Acknowledge }
    private bool _isOpen;
    private string _popupHeader = string.Empty;
    private string _popupDescription = string.Empty;
    private PopupMode _mode;
    private TaskCompletionSource<bool>? _tcs;

    public async Task<bool> ShowPopupAsync(string popupHeader, string popupDescription, PopupMode mode)
    {
        _tcs = new TaskCompletionSource<bool>();
        _popupHeader = popupHeader;
        _popupDescription = popupDescription;
        _isOpen = true;
        _mode = mode;
        StateHasChanged();

        return await _tcs.Task;
    }

    private void OnCancel()
    {
        _isOpen = false;
        _tcs?.SetResult(false);
    }

    private void OnConfirm()
    {
        _isOpen = false;
        _tcs?.SetResult(true);
    }

}




/* js
const dialog = document.getElementById("components-input-popup");
const messageElement = document.getElementById("components-input-message");
const acceptButton = document.getElementById("components-accept-button");
const acknowledgeButton = document.getElementById("components-acknowledge-button");
const cancelButton = document.getElementById("components-cancel-button");

let pendingResolve = null;

function respond(result) {
    pendingResolve?.(result);
    pendingResolve = null;
    dialog.close();
}

dialog.addEventListener("close", () => {
    if (pendingResolve) {
        pendingResolve(false);
        pendingResolve = null;
    }
});

acceptButton.addEventListener("click", () => respond(true));
acknowledgeButton.addEventListener("click", () => respond(false));
cancelButton.addEventListener("click", () => respond(false));

export async function showConfirm(message) {
    return new Promise((resolve) => {
        pendingResolve = resolve;
        messageElement.textContent = message;
        dialog.dataset.mode = "confirm";
        dialog.showModal();
    });
}

export async function showAcknowledge(message) {
    return new Promise((resolve) => {
        pendingResolve = resolve;
        messageElement.textContent = message;
        dialog.dataset.mode = "acknowledge";
        dialog.showModal();
    });
}


window.chatClient = window.chatClient || {};
window.chatClient.showConfirm = showConfirm;
window.chatClient.showAcknowledge = showAcknowledge;

*/