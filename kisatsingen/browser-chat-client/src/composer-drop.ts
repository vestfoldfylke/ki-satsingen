// Dropping files anywhere on the page attaches them in the composer, with an
// overlay over the whole window while they are dragged.
//
// The overlay's content takes no pointer events, so once shown, the overlay is
// the only element a drag can enter or leave: its dragleave means the drag left
// the window or was cancelled.

import { attachFiles } from './composer-file-input.js';

const SELECTOR_DROP_OVERLAY = '#composer-drop-overlay';
const DRAG_TYPE_FILES = 'Files';

// Browsers fire dragover at least every 550 ms while a drag is over the page,
// so a longer silence means the drag ended without a dragleave or drop.
const DRAG_SILENCE_MS = 1000;

let hideOverlayTimer = 0;

window.addEventListener('dragenter', (event) => {
    if (findFileDrag(event)) {
        showOverlay();
    }
});

// On every page, so a dropped file never opens in the tab. A page without a
// composer has no overlay, and there the drop is refused.
window.addEventListener('dragover', (event) => {
    const fileDrag = findFileDrag(event);
    if (!fileDrag) {
        return;
    }

    event.preventDefault();
    if (!findOverlay()) {
        fileDrag.dropEffect = 'none';
        return;
    }

    // Some sources, such as Outlook, allow only copy and refuse anything else.
    fileDrag.dropEffect = 'copy';
    window.clearTimeout(hideOverlayTimer);
    hideOverlayTimer = window.setTimeout(hideOverlay, DRAG_SILENCE_MS);
});

window.addEventListener('dragleave', (event) => {
    if (event.target === findOverlay()) {
        hideOverlay();
    }
});

window.addEventListener('drop', (event) => {
    const fileDrag = findFileDrag(event);
    if (!fileDrag) {
        return;
    }

    event.preventDefault();
    hideOverlay();

    const files = readDroppedFiles(fileDrag);
    if (files.length > 0) {
        attachFiles(files);
    }
});

// The drag's data when it carries files.
function findFileDrag(event: DragEvent): DataTransfer | null {
    const data = event.dataTransfer;
    return data?.types.includes(DRAG_TYPE_FILES) ? data : null;
}

// Only works during the drop event: after it, the items are empty. Folders are
// left out, since a folder can't be read as a file.
function readDroppedFiles(fileDrag: DataTransfer): File[] {
    return Array.from(fileDrag.items)
        .filter((item) => item.kind === 'file' && item.webkitGetAsEntry()?.isDirectory !== true)
        .map((item) => item.getAsFile())
        .filter((file) => file !== null);
}

function showOverlay(): void {
    const overlay = findOverlay();
    if (overlay && !overlay.matches(':popover-open')) {
        overlay.showPopover();
    }
}

function hideOverlay(): void {
    const overlay = findOverlay();
    if (overlay?.matches(':popover-open')) {
        overlay.hidePopover();
    }
}

function findOverlay(): HTMLElement | null {
    return document.querySelector<HTMLElement>(SELECTOR_DROP_OVERLAY);
}
