// Scroll behavior for the chat log.
//
// State:
//   isPinned  — DOM truth: is the user currently within threshold of the bottom.
//               Updated on every scroll event.
//   following — user intent: does the user want the log to auto-scroll to bottom
//               as new content arrives. Only toggled by USER-INITIATED actions
//               (manual scroll, pill click). Programmatic scrolls (popToTop,
//               scrollToBottom triggered by notifyContentChanged) never toggle it.
//
// On send: popToTop places the new user bubble near the top of the viewport
// (padding via scroll-padding-top on .chat-log). following is set to false —
// the user hasn't asked us to follow the stream yet.
//
// During streaming: notifyContentChanged auto-scrolls only if following. Ways
// to opt in to following mid-stream:
//   - Scroll down to the bottom yourself.
//   - Click the pill.
//
// Any manual scroll updates following to match the resulting isPinned. Manual
// vs programmatic is distinguished by a "user activity in the last ~400ms"
// timestamp fed by wheel / touchstart / pointerdown / scroll keys.
//
// Chat load / switch: scroll to bottom + following=true (most-recent state).
// Chat load with an active stream: popToTop the last user bubble + following=false.
//
// Wiring is idempotent, driven by Blazor's OnAfterRenderAsync (gated).

const PIN_THRESHOLD_REM = 3;
const USER_ACTIVITY_WINDOW_MS = 400;
const SCROLL_KEYS = new Set(['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' ']);

let chatLogElement = null;
let scrollToBottomPillElement = null;
let spacerElement = null;
let isPinned = true;
let following = false;
let mutationObserver = null;
let userActivityAt = 0;

function remToPx(rem) {
    const rootSize = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    return rem * rootSize;
}

// "Content end" is the top of the trailing spacer (or scrollHeight if there
// isn't one). The spacer reserves ~1 viewport of scroll space so popToTop
// can actually place the user bubble at the top; we exclude it from the
// pin / scroll-to-bottom math so we treat the end of real content — not the
// end of the reserved area — as the bottom.
function contentEndPx() {
    if (!chatLogElement) {
        return 0;
    }
    return spacerElement ? spacerElement.offsetTop : chatLogElement.scrollHeight;
}

function computeIsPinned() {
    if (!chatLogElement) {
        return true;
    }
    return contentEndPx() - chatLogElement.scrollTop - chatLogElement.clientHeight <= remToPx(PIN_THRESHOLD_REM);
}

function updatePill() {
    if (!scrollToBottomPillElement) {
        return;
    }
    scrollToBottomPillElement.hidden = isPinned;
}

function markUserActivity() {
    userActivityAt = performance.now();
}

function isRecentUserActivity() {
    return performance.now() - userActivityAt < USER_ACTIVITY_WINDOW_MS;
}

function scrollToBottom(smooth) {
    if (!chatLogElement) {
        return;
    }
    // Scroll so the end of real content sits at the viewport bottom — not
    // into the spacer reservoir below.
    const target = Math.max(0, contentEndPx() - chatLogElement.clientHeight);
    chatLogElement.scrollTo({ top: target, behavior: smooth ? 'smooth' : 'auto' });
    isPinned = true;
    following = true;
    updatePill();
}

// Align `el` to the top of the scroll container (padding via scroll-padding-top).
// Explicitly turns following OFF — the user hasn't asked us to follow the stream.
function popToTop(el, smooth) {
    if (!chatLogElement || !el) {
        return;
    }
    el.scrollIntoView({ block: 'start', behavior: smooth ? 'smooth' : 'auto' });
    following = false;
    // isPinned + pill state update via the scroll listener that fires from scrollIntoView.
}

function onScroll() {
    isPinned = computeIsPinned();
    // Only user-initiated scrolls toggle `following`. Programmatic scrolls
    // (popToTop, scrollToBottom during streaming) manage `following` directly.
    if (isRecentUserActivity()) {
        following = isPinned;
    }
    updatePill();
}

function onPillClick() {
    scrollToBottom(true);
}

function onKeyDown(e) {
    if (SCROLL_KEYS.has(e.key)) {
        markUserActivity();
    }
}

function onLogMutation(mutations) {
    // Newly-added user bubble → user just sent a message → pop it to the top.
    for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
            if (node.nodeType !== 1) {
                continue;
            }
            if (node.classList?.contains('user-bubble')) {
                popToTop(node, true);
                return;
            }
        }
    }
    // Other mutations (streaming placeholder appearing, assistant turn commit
    // swap): behave like any content change — scroll if following, else refresh
    // pill state.
    notifyContentChanged();
}

export function initChatLog() {
    const log = document.querySelector('.chat-log');
    const pill = document.querySelector('.chat-jump-latest');

    if (!log) {
        return;
    }

    if (log !== chatLogElement) {
        if (mutationObserver) {
            mutationObserver.disconnect();
            mutationObserver = null;
        }
        if (chatLogElement) {
            chatLogElement.removeEventListener('scroll', onScroll);
            chatLogElement.removeEventListener('wheel', markUserActivity);
            chatLogElement.removeEventListener('touchstart', markUserActivity);
            chatLogElement.removeEventListener('pointerdown', markUserActivity);
            chatLogElement.removeEventListener('keydown', onKeyDown);
        }
        if (scrollToBottomPillElement) {
            scrollToBottomPillElement.removeEventListener('click', onPillClick);
        }

        chatLogElement = log;
        scrollToBottomPillElement = pill;
        spacerElement = log.querySelector('.chat-log-spacer');

        log.addEventListener('scroll', onScroll, { passive: true });
        log.addEventListener('wheel', markUserActivity, { passive: true });
        log.addEventListener('touchstart', markUserActivity, { passive: true });
        log.addEventListener('pointerdown', markUserActivity);
        log.addEventListener('keydown', onKeyDown);

        if (pill) {
            pill.addEventListener('click', onPillClick);
            pill.hidden = true;
        }

        mutationObserver = new MutationObserver(onLogMutation);
        mutationObserver.observe(log, { childList: true });
    }

    // Initial scroll target after wire (or on chat switch / visibility flip):
    //   - active stream + user bubble present → user just sent; pop to top,
    //     following stays false so streaming doesn't drag them back.
    //   - otherwise → scroll to bottom, following true (most-recent state).
    const hasStream = log.querySelector('.assistant-streaming') !== null;
    const bubbles = log.querySelectorAll('.user-bubble');
    const lastUser = bubbles[bubbles.length - 1];

    if (hasStream && lastUser) {
        popToTop(lastUser, false);
    } else {
        scrollToBottom(false);
    }
}

// Called from chat-streaming.js after every renderInto (streaming tokens),
// and from onLogMutation on non-user-bubble mutations.
// Gated on `following` — user intent, not DOM position — so popToTop's
// "no auto-scroll after send" survives even on short chats where the user
// bubble happens to end up near the bottom.
export function notifyContentChanged() {
    if (!chatLogElement) {
        return;
    }
    if (following) {
        scrollToBottom(false);
    } else {
        // Content grew; scrollHeight changed. Refresh pill visibility.
        isPinned = computeIsPinned();
        updatePill();
    }
}
