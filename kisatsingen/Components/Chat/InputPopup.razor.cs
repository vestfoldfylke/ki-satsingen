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