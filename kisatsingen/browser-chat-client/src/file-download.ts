// Saves a file the server streams over the circuit, such as a presentation
// built when its download button is clicked. Nothing is stored on the server,
// so there is no URL to link to; the file becomes a blob URL for the moment
// the browser needs to save it.

// The parts of Blazor's DotNetStreamReference this uses.
type DotNetStream = {
    arrayBuffer(): Promise<ArrayBuffer>;
};

// Some browsers (Safari, older Firefox) start the download after click()
// returns; revoking the URL in the same tick cancels it.
const REVOKE_DELAY_MS = 10_000;

export const downloadFile = async (fileName: string, contentType: string, stream: DotNetStream): Promise<void> => {
    const blob = new Blob([await stream.arrayBuffer()], { type: contentType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.hidden = true;
    document.body.append(anchor);
    try {
        anchor.click();
    } finally {
        anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), REVOKE_DELAY_MS);
    }
};
