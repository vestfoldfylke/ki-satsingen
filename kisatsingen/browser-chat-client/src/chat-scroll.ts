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
// On stream commit: the CSS spacer-during-streaming rule stops applying and
// scrollHeight would shrink, potentially clamping scrollTop and jerking the
// viewport. We prevent that by setting an inline min-height on the spacer that
// keeps scrollHeight >= scrollTop + clientHeight — the current view stays
// exactly where it was. If the user was following, we scrollToBottom instead
// (they wanted to be at the tail, not preserve mid-scroll position).
//
// Chat load / switch: scroll to bottom + following=true (most-recent state).
// Chat load with an active stream: popToTop the last user bubble + following=false.
//
// The composer (.chat-tail) is sticky INSIDE this scrollport, so the bottom
// `bottomInsetPx` of clientHeight is covered by it. Every "is the end of the
// content visible" question is therefore about clientHeight - bottomInsetPx,
// not clientHeight — otherwise scrollToBottom parks the last message behind
// the composer. The inset is republished by a ResizeObserver (the usage row
// appears and disappears, the footer rewraps) and mirrored into CSS as
// --chat-tail-height for the jump pill's offset and the streaming spacer.
//
// Wiring is idempotent, driven by Blazor's OnAfterRenderAsync (gated).

const PIN_THRESHOLD_REM = 3;
const USER_ACTIVITY_WINDOW_MS = 400;
const SCROLL_KEYS = new Set(['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End', ' ']);

let chatLogElement: HTMLElement | null = null;
let scrollToBottomPillElement: HTMLElement | null = null;
let spacerElement: HTMLElement | null = null;
let chatTailElement: HTMLElement | null = null;
let isPinned = true;
let following = false;
let mutationObserver: MutationObserver | null = null;
let tailResizeObserver: ResizeObserver | null = null;
let bottomInsetPx = 0;
let userActivityAt = 0;
let lastScrollTop = 0;
let shouldIgnoreNextScrollEvent = false;

function remToPx(rem: number): number {
    const rootSize = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    return rem * rootSize;
}

// "Content end" is the top of the trailing spacer (or scrollHeight if there
// isn't one). The spacer reserves ~1 screenful of visible transcript so
// popToTop can actually place the user bubble at the top; we exclude it from
// the pin / scroll-to-bottom math so we treat the end of real content — not
// the end of the reserved area — as the bottom.
// offsetTop is measured from .chat-log (position:relative), which puts it in
// the same coordinate space as scrollTop.
function contentEndPx(): number {
    if (!chatLogElement) {
        return 0;
    }
    return spacerElement ? spacerElement.offsetTop : chatLogElement.scrollHeight;
}

// Height of the strip that isn't covered by the sticky composer — the part of
// the scrollport a message can actually be read in.
function visibleHeightPx(): number {
    if (!chatLogElement) {
        return 0;
    }
    return Math.max(0, chatLogElement.clientHeight - bottomInsetPx);
}

// Cache the composer's height rather than reading offsetHeight inside the
// scroll handler — that would force a layout on every scroll event.
function publishTailHeight(): void {
    if (!chatLogElement) {
        return;
    }
    bottomInsetPx = chatTailElement ? chatTailElement.offsetHeight : 0;
    chatLogElement.style.setProperty('--chat-tail-height', `${bottomInsetPx}px`);
}

function computeIsPinned(): boolean {
    if (!chatLogElement) {
        return true;
    }
    return contentEndPx() - chatLogElement.scrollTop - visibleHeightPx() <= remToPx(PIN_THRESHOLD_REM);
}

function updatePill(): void {
    if (!scrollToBottomPillElement) {
        return;
    }
    scrollToBottomPillElement.hidden = isPinned;
}

function markUserActivity(): void {
    userActivityAt = performance.now();
}

function isRecentUserActivity(): boolean {
    return performance.now() - userActivityAt < USER_ACTIVITY_WINDOW_MS;
}

function clearSpacerInlineHeight(): void {
    if (spacerElement) {
        spacerElement.style.minHeight = '';
    }
}

function scrollToBottom(smooth: boolean): void {
    if (!chatLogElement) {
        return;
    }
    // Scroll so the end of real content sits at the top edge of the composer
    // — not into the spacer reservoir below it, and not behind the composer.
    const target = Math.max(0, contentEndPx() - visibleHeightPx());
    chatLogElement.scrollTo({ top: target, behavior: smooth ? 'smooth' : 'auto' });
    isPinned = true;
    following = true;
    updatePill();
}

// Align `el` to the top of the scroll container (padding via scroll-padding-top).
// Explicitly turns following OFF — the user hasn't asked us to follow the stream.
function popToTop(el: HTMLElement | null, smooth: boolean): void {
    if (!chatLogElement || !el) {
        return;
    }
    el.scrollIntoView({ block: 'start', behavior: smooth ? 'smooth' : 'auto' });
    following = false;
    // isPinned + pill state update via the scroll listener that fires from scrollIntoView.
}

// Called when the streaming placeholder is removed (stream just committed).
// Prevents the viewport from jerking down when the CSS-driven spacer collapses
// by pinning the spacer to a specific inline height that keeps scrollHeight >=
// scrollTop + clientHeight. If the user was following the stream, we skip the
// preservation and scroll to the new content bottom instead — that's what they
// were watching.
function preserveScrollAfterCommit(): void {
    if (following) {
        scrollToBottom(false);
        clearSpacerInlineHeight();
        return;
    }

    if (!spacerElement || !chatLogElement) {
        return;
    }

    const desiredScrollTop = lastScrollTop;
    const clientHeight = chatLogElement.clientHeight;
    // Measure the scrollport's content with the spacer's own current
    // contribution removed, then reserve the difference. Deriving it from
    // scrollHeight keeps this correct no matter what else sits below the
    // transcript in flow (the jump pill, the sticky composer) — enumerating
    // those here would rot the moment the tail changes.
    const contentWithoutSpacer = chatLogElement.scrollHeight - spacerElement.offsetHeight;
    const neededSpacerPx = Math.max(0, desiredScrollTop + clientHeight - contentWithoutSpacer);

    spacerElement.style.minHeight = `${neededSpacerPx}px`;

    // If the browser has already clamped scrollTop between the mutation and
    // this callback, put it back — the inline min-height guarantees the target
    // is valid now. This write fires a native 'scroll' event asynchronously,
    // which would otherwise reach onScroll and — if recent user activity is
    // still in its window — let the isRecentUserActivity heuristic mistake
    // this correction for a real drag and reassign `following`. Suppress that
    // one event; it carries no user intent.
    if (chatLogElement.scrollTop !== desiredScrollTop) {
        shouldIgnoreNextScrollEvent = true;
        chatLogElement.scrollTop = desiredScrollTop;
    }

    isPinned = computeIsPinned();
    updatePill();
}

function onScroll(): void {
    if (!chatLogElement) {
        return;
    }
    lastScrollTop = chatLogElement.scrollTop;
    isPinned = computeIsPinned();
    // Only user-initiated scrolls toggle `following`. Programmatic scrolls
    // (popToTop, scrollToBottom during streaming) manage `following` directly.
    // preserveScrollAfterCommit's clamp-correction is programmatic too, but it
    // can't set `following` itself — it fires this event asynchronously after
    // returning — so it flags the resulting event to skip instead.
    if (shouldIgnoreNextScrollEvent) {
        shouldIgnoreNextScrollEvent = false;
    } else if (isRecentUserActivity()) {
        following = isPinned;
    }
    updatePill();
}

function onPillClick(): void {
    scrollToBottom(true);
}

function onKeyDown(e: KeyboardEvent): void {
    if (SCROLL_KEYS.has(e.key)) {
        markUserActivity();
    }
}

function hasClass(node: Node, className: string): node is HTMLElement {
    return node instanceof HTMLElement && node.classList.contains(className);
}

function onLogMutation(mutations: MutationRecord[]): void {
    // New user bubble → user just sent → pop it to the top. The next stream
    // gets a fresh 100dvh spacer via the CSS sibling rule, so clear any inline
    // height left over from the previous commit.
    //
    // Only pop when the transcript actually overflows — on short chats where
    // everything still fits above the composer, popping would jerk the message
    // up now and preserveScrollAfterCommit would jerk it back down when the
    // stream commits and the spacer collapses.
    for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
            if (hasClass(node, 'user-bubble')) {
                clearSpacerInlineHeight();
                if (contentEndPx() > visibleHeightPx()) {
                    popToTop(node, true);
                }
                return;
            }
        }
    }

    // Streaming placeholder removed → stream just committed. Preserve the
    // viewport position so a short response doesn't cause a downward jump when
    // the spacer collapses.
    for (const mutation of mutations) {
        for (const node of mutation.removedNodes) {
            if (hasClass(node, 'assistant-streaming')) {
                preserveScrollAfterCommit();
                return;
            }
        }
    }

    // Anything else (e.g., streaming placeholder appearing on send, though that
    // hits the user-bubble branch first): treat as a regular content change.
    notifyContentChanged();
}

export function initChatLog(): void {
    const log = document.querySelector<HTMLElement>('.chat-log');
    const pill = document.querySelector<HTMLElement>('.chat-jump-latest');

    if (!log) {
        return;
    }

    if (log !== chatLogElement) {
        if (mutationObserver) {
            mutationObserver.disconnect();
            mutationObserver = null;
        }
        if (tailResizeObserver) {
            tailResizeObserver.disconnect();
            tailResizeObserver = null;
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
        spacerElement = log.querySelector<HTMLElement>('.chat-log-spacer');
        chatTailElement = log.querySelector<HTMLElement>('.chat-tail');

        log.addEventListener('scroll', onScroll, { passive: true });
        log.addEventListener('wheel', markUserActivity, { passive: true });
        log.addEventListener('touchstart', markUserActivity, { passive: true });
        log.addEventListener('pointerdown', markUserActivity);
        log.addEventListener('keydown', onKeyDown);

        if (pill) {
            pill.addEventListener('click', onPillClick);
            pill.hidden = true;
        }

        // Observe the inner max-width wrapper — that's where Blazor adds
        // message children.
        const inner = log.querySelector('.chat-log-inner');
        if (!inner) {
            console.error('.chat-log-inner missing inside .chat-log — DOM structure changed');
            return;
        }
        mutationObserver = new MutationObserver(onLogMutation);
        mutationObserver.observe(inner, { childList: true });

        if (chatTailElement) {
            tailResizeObserver = new ResizeObserver(publishTailHeight);
            tailResizeObserver.observe(chatTailElement);
        }
    }

    // Synchronously, before any scroll positioning below: the observer's first
    // callback is async and every scroll target depends on this inset.
    publishTailHeight();

    // Any inline spacer height from a previous session's commit is stale here.
    clearSpacerInlineHeight();

    // Initial scroll target after wire (or on chat switch / visibility flip):
    //   - active stream + user bubble present → user just sent; pop to top,
    //     following stays false so streaming doesn't drag them back.
    //   - otherwise → scroll to bottom, following true (most-recent state).
    const hasStream = log.querySelector('.assistant-streaming') !== null;
    const bubbles = log.querySelectorAll<HTMLElement>('.user-bubble');
    const lastUser = bubbles[bubbles.length - 1] ?? null;

    if (hasStream && lastUser) {
        popToTop(lastUser, false);
        return
    }

    scrollToBottom(false);
}

// Called from chat-streaming.js after every renderInto (streaming tokens),
// and from onLogMutation on non-user-bubble mutations.
// Gated on `following` — user intent, not DOM position — so popToTop's
// "no auto-scroll after send" survives even on short chats where the user
// bubble happens to end up near the bottom.
export function notifyContentChanged(): void {
    if (!chatLogElement) {
        return;
    }
    if (following) {
        scrollToBottom(false);
        return
    }

    // Content grew; scrollHeight changed. Refresh pill visibility.
    isPinned = computeIsPinned();
    updatePill();
}
