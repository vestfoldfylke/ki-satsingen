const dialog = document.getElementById("components-input-popup");
const messageElement = document.getElementById("components-input-message");
const acceptButton = document.getElementById("components-accept-button");
const acknowledgeButton = document.getElementById("components-acknowledge-button");
const cancelButton = document.getElementById("components-cancel-button");

let pendingResolve = null;

function respond(result) {
    dialog.close();
    if (pendingResolve) {
        pendingResolve(result);
        pendingResolve = null;
    }
}

acceptButton.addEventListener("click", () => respond(true));
acknowledgeButton.addEventListener("click", () => respond(false));
cancelButton.addEventListener("click", () => respond(false));

export function showConfirm(message) {
    return new Promise((resolve) => {
        pendingResolve = resolve;
        messageElement.textContent = message;
        dialog.dataset.mode = "confirm";
        dialog.showModal();
    });
}

export function showAcknowledge(message) {
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