// Composer behavior: Enter-to-send / Shift+Enter-for-newline, and the send
// button's enabled state — both handled entirely client-side so a keystroke
// doesn't round-trip over SignalR just to ask "was that Enter" or "is this
// still empty." The textarea is otherwise uncontrolled from Blazor's side;
// its content only reaches the server once, at send (see takeComposerValue).
//
// Wiring is idempotent, driven by Blazor's OnAfterRenderAsync (gated on
// firstRender — see ChatComposer.razor.cs), same pattern as chat-scroll.ts's
// initChatLog: comparing DOM identity is how we'd notice the empty-state /
// transcript layout swap recreating the composer, though in practice a fresh
// ChatComposer instance only ever calls this once, on its own firstRender.

const SELECTOR_TEXTAREA = '#chat-textarea';
const SELECTOR_SEND_BUTTON = '#composer-send-button';

let textareaElement: HTMLTextAreaElement | null = null;
let isBusy = false;
let isEmpty = true;

function sendButton(): HTMLButtonElement | null {
    return document.querySelector<HTMLButtonElement>(SELECTOR_SEND_BUTTON);
}

// Single writer for the send button's `disabled` — Blazor never touches it
// past its static initial markup (see ChatComposer.razor). Busy comes from
// the server via setComposerBusy; emptiness is tracked locally from keystrokes.
function updateSendButtonDisabled(): void {
    const button = sendButton();
    if (button) {
        button.disabled = isBusy || isEmpty;
    }
}

function onInput(): void {
    isEmpty = (textareaElement?.value.trim().length ?? 0) === 0;
    updateSendButtonDisabled();
}

function onKeyDown(e: KeyboardEvent): void {
    // isComposing: Enter during IME composition (e.g. Japanese, Korean input)
    // confirms the candidate — it isn't a send.
    if (e.key !== 'Enter' || e.shiftKey || e.isComposing) {
        return;
    }
    e.preventDefault();

    const button = sendButton();
    if (button && !button.disabled) {
        button.click();
    }
}

export function initComposer(): void {
    const textarea = document.querySelector<HTMLTextAreaElement>(SELECTOR_TEXTAREA);
    if (!textarea) {
        return;
    }

    // Re-sync before the same-node check below: initComposer is only called
    // on firstRender today (see ChatComposer.razor.cs), so this is dormant
    // rather than dead — it keeps the module's cached isEmpty correct on day
    // one if that firstRender gate is ever loosened, instead of quietly going
    // stale again.
    isEmpty = textarea.value.trim().length === 0;
    updateSendButtonDisabled();

    if (textarea === textareaElement) {
        return;
    }

    if (textareaElement) {
        textareaElement.removeEventListener('keydown', onKeyDown);
        textareaElement.removeEventListener('input', onInput);
    }

    textareaElement = textarea;
    textarea.addEventListener('keydown', onKeyDown);
    textarea.addEventListener('input', onInput);
}

export function setComposerBusy(busy: boolean): void {
    isBusy = busy;
    updateSendButtonDisabled();
}

// Reads and clears the textarea in one call. Read-then-clear as two separate
// interop round trips would leave a window where a second rapid send reads
// the same not-yet-cleared value; C# owns the actual re-entrancy guard
// (Chat.razor.cs's _isSending) that stops a second call from getting this far
// at all, but this still keeps the state this module owns internally
// consistent within a single call.
export function takeComposerValue(): string {
    if (!textareaElement) {
        return '';
    }

    const value = textareaElement.value;
    textareaElement.value = '';
    isEmpty = true;
    updateSendButtonDisabled();
    return value;
}
