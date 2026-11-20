// Pasting images into the composer. Images only: a document copied in a file
// explorer long ago would otherwise come along with a paste meant for text.

import { attachFiles } from './composer-file-input.js';

const SELECTOR_ANNOUNCEMENT = '#composer-paste-announcement';

// What browsers name a screenshot or a copied picture. A file copied in a file
// explorer keeps its own name.
const GENERIC_PASTED_NAME = /^image\.\w+$/;

// For the page, so two pastes in the same minute get different names.
let pastedImageCount = 0;

export function attachPastedImages(event: ClipboardEvent): void {
    const clipboard = event.clipboardData;

    // Excel and Word also put an image of the copied content on the clipboard;
    // the text is what was meant. Non-empty, since a picture copied alone in
    // Word can come with an empty text part.
    if (!clipboard || clipboard.getData('text/plain').trim() !== '') {
        return;
    }

    // No list of allowed types here: the server refuses the others with its
    // usual message.
    const images = Array.from(clipboard.files).filter((file) => file.type.startsWith('image/'));
    if (images.length === 0) {
        return;
    }

    event.preventDefault();

    const namedImages = images.map((image) => (GENERIC_PASTED_NAME.test(image.name) ? renameImage(image) : image));
    attachFiles(namedImages);
    announceAttached(namedImages);
}

// The extension comes from the type, since the server judges a file by its
// extension, then by its content.
function renameImage(image: File): File {
    pastedImageCount++;
    const extension = image.type.slice('image/'.length);
    const name = `bilde-${formatTimestamp(new Date())}-${pastedImageCount}.${extension}`;
    return new File([image], name, { type: image.type, lastModified: image.lastModified });
}

// 2026-11-18-1430
function formatTimestamp(date: Date): string {
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${date.getFullYear()}-${month}-${day}-${hours}${minutes}`;
}

// The names make each announcement differ, so a screen reader reads a second
// paste too.
function announceAttached(images: File[]): void {
    const announcement = document.querySelector<HTMLElement>(SELECTOR_ANNOUNCEMENT);
    if (announcement) {
        announcement.textContent = `Legger ved ${images.map((image) => image.name).join(', ')}`;
    }
}
