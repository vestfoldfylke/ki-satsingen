// The knowledge file viewer's text lives only in the browser: the server hands
// it over once and keeps no copy, to avoid using server memory for holding static content from a knowledge file.
//
//   showPlainText(el, text) → shows text as-is; textContent parses no HTML.

export function showPlainText(element: HTMLElement | null, text: string): void {
    if (!element) {
        return;
    }
    element.textContent = text;
}
