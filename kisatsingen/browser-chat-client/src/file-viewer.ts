// Shows a knowledge file's text; textContent parses no HTML.

export function showPlainText(element: HTMLElement | null, text: string): void {
    if (!element) {
        return;
    }
    element.textContent = text;
}
