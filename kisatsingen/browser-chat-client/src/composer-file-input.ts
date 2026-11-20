// The composer's way in for files that don't come through the picker: each
// selection goes on one hidden InputFile, whose change event uploads it.
//
// Blazor's InputFile replaces its file list on every change, and the server
// reads the files after the change, so a second selection while one is read
// would make the first unreadable. Selections that come meanwhile wait here,
// and each is sent on its own.

const SELECTOR_FILE_INPUT = '#composer-file-input';

const queuedSelections: File[][] = [];

// ChatComposer renders a new input for each chat, so the input the queued
// selections were made for tells which chat they belong to.
let queuedFor: HTMLInputElement | null = null;

let readingInput: HTMLInputElement | null = null;

export function attachFiles(files: File[]): void {
    const input = findFileInput();
    if (input !== queuedFor) {
        queuedSelections.length = 0;
        queuedFor = input;
    }

    queuedSelections.push(files);
    sendNextSelection();
}

// The selection on the input is read, so the next queued one can take its place.
export function markComposerSelectionRead(): void {
    readingInput = null;
    sendNextSelection();
}

function sendNextSelection(): void {
    if (isReading()) {
        return;
    }

    // The queued selections were made in a chat that is no longer open, and
    // must not be attached to the one that is.
    const input = findFileInput();
    if (!input || input !== queuedFor) {
        queuedSelections.length = 0;
        return;
    }

    const files = queuedSelections.shift();
    if (!files) {
        return;
    }

    const selection = new DataTransfer();
    for (const file of files) {
        selection.items.add(file);
    }

    input.files = selection.files;
    readingInput = input;
    input.dispatchEvent(new Event('change'));
}

// An input replaced by another chat's is no longer read, whether or not its
// release ever comes: its selection can be dropped before the server sees it.
function isReading(): boolean {
    return readingInput?.isConnected === true;
}

function findFileInput(): HTMLInputElement | null {
    return document.querySelector<HTMLInputElement>(SELECTOR_FILE_INPUT);
}
