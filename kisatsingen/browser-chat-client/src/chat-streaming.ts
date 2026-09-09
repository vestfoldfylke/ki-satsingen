// Client-side Markdown renderer for chat.
//
// Lifecycle for one assistant response:
//   streamStart(id)                → init buffer for a new stream (once).
//   streamAppend(id, text) × N     → server pushes tokens over SignalR.
//   streamEnd(id)                  → drop buffer; Blazor then removes the
//                                    stream-{id} div and renders the committed
//                                    <AssistantTurn>, which calls renderMarkdown.
//   renderMarkdown(el, source)     → final render on the committed message,
//                                    with hljs syntax highlighting and code-copy
//                                    buttons injected per <pre>.
//
// Streaming skips hljs on purpose: re-highlighting every code block on every
// token is O(n²) in response length. hljs and copy buttons run once on commit.

import MarkdownIt from 'markdown-it';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/core';
import "highlight.js/styles/a11y-light.min.css"
import './copy-button.css';
import { initChatLog, notifyContentChanged } from './chat-scroll.js';
import { initComposer, setComposerBusy, takeComposerValue } from './chat-composer.js';

import bash from 'highlight.js/lib/languages/bash';
import csharp from 'highlight.js/lib/languages/csharp';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import python from 'highlight.js/lib/languages/python';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';

hljs.registerLanguage('bash', bash);
hljs.registerLanguage('csharp', csharp);
hljs.registerLanguage('cs', csharp);
hljs.registerLanguage('javascript', javascript);
hljs.registerLanguage('js', javascript);
hljs.registerLanguage('json', json);
hljs.registerLanguage('python', python);
hljs.registerLanguage('py', python);
hljs.registerLanguage('sql', sql);
hljs.registerLanguage('typescript', typescript);
hljs.registerLanguage('ts', typescript);
hljs.registerLanguage('html', xml);
hljs.registerLanguage('xml', xml);
hljs.registerLanguage('yaml', yaml);
hljs.registerLanguage('yml', yaml);

// No `highlight` callback: during streaming, code blocks emit plain
// <pre><code class="language-xxx"> and hljs runs once against the finalized DOM
// in renderInto({highlight: true}). Avoids re-highlighting every code block on
// every token — the dominant O(n²) cost during a stream.
const md = new MarkdownIt({
    html: false,        // no raw HTML in source
    linkify: true,      // auto-detect URLs
    breaks: false,      // require explicit hard breaks
    typographer: false,
});

interface StreamState {
    id: string;
    buffer: string;
    el: HTMLElement | null;
    rafHandle: number;
}

const streamStates = new Map<string, StreamState>();
const COPIED_FEEDBACK_MS = 1200;
const COPY_ICON_SVG = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>';

async function copyToClipboard(text: string, button: HTMLElement): Promise<void> {
    if (!text) {
        return;
    }
    try {
        await navigator.clipboard.writeText(text);
        button.classList.add('is-copied');
        setTimeout(() => button.classList.remove('is-copied'), COPIED_FEEDBACK_MS);
    } catch (err) {
        console.warn('Copy failed', err);
    }
}

// Wrap each <pre> in a positioned container and drop a copy button in.
// Idempotent: skips already-wrapped <pre> so re-renders of the same content
// don't stack duplicates.
function injectCodeBlockCopy(el: HTMLElement): void {
    el.querySelectorAll('pre').forEach(pre => {
        if (pre.parentElement?.classList.contains('chat-code-block')) {
            return;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'chat-code-block';
        pre.parentNode?.insertBefore(wrapper, pre);
        wrapper.appendChild(pre);

        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'chat-code-copy';
        button.setAttribute('aria-label', 'Copy code');
        button.innerHTML = COPY_ICON_SVG;
        button.addEventListener('click', () => {
            const code = pre.querySelector('code');
            copyToClipboard(code?.textContent ?? '', button);
        });
        wrapper.appendChild(button);
    });
}

// Message-level copy: read joined prose from the turn's .markdown-fallback divs.
// Delegated at document to catch any AssistantTurn Blazor inserts later.
document.addEventListener('click', event => {
    const button = event.target instanceof Element
        ? event.target.closest<HTMLElement>('[data-copy-turn-id]')
        : null;
    if (!button) {
        return;
    }

    const turn = button.closest<HTMLElement>('.assistant-turn');
    if (!turn) {
        return;
    }

    const parts = turn.querySelectorAll('.markdown-fallback');
    const text = Array.from(parts)
        .map(el => el.textContent)
        .filter(t => t)
        .join('\n\n');
    copyToClipboard(text, button);
});

function renderInto(el: HTMLElement | null, source: string | null | undefined, { highlight = false }: { highlight?: boolean } = {}): void {
    if (!el) {
        return;
    }
    const html = md.render(source ?? '');
    el.innerHTML = DOMPurify.sanitize(html);
    if (highlight) {
        el.querySelectorAll('pre code').forEach(node => hljs.highlightElement(node as HTMLElement));
        injectCodeBlockCopy(el);
    }
    // Notify content changed, so that we can do fancy auto-scrolling
    notifyContentChanged();
}

function elForStream(id: string): HTMLElement | null {
    return document.getElementById(`stream-${id}`);
}

// Coalesce rapid streamAppend calls into one render per animation frame.
// Bounds work at display refresh (~60Hz) regardless of token arrival rate.
// rafHandle: 0 = no frame queued; a positive integer = frame in flight.
// Element lookup is deferred to render time: the DOM node may not exist yet
// when streamStart/streamAppend arrive (Blazor's render batch and this JS
// interop call race over SignalR).
function scheduleRender(state: StreamState): void {
    if (state.rafHandle) {
        return;
    }
    state.rafHandle = requestAnimationFrame(() => {
        state.rafHandle = 0;
        state.el ??= elForStream(state.id);
        renderInto(state.el, state.buffer);
    });
}

export function streamStart(id: string): void {
    streamStates.set(id, { id, buffer: '', el: null, rafHandle: 0 });
}

export function streamAppend(id: string, text: string): void {
    let state = streamStates.get(id);
    if (!state) {
        state = { id, buffer: '', el: null, rafHandle: 0 };
        streamStates.set(id, state);
    }
    state.buffer += text;
    scheduleRender(state);
}

// Cancel any in-flight frame before dropping state — otherwise it would render
// into a DOM node Blazor is about to remove.
export function streamEnd(id: string): void {
    const state = streamStates.get(id);
    if (state?.rafHandle) {
        cancelAnimationFrame(state.rafHandle);
    }
    streamStates.delete(id);
}

// Called from AssistantTurn.OnAfterRenderAsync for every committed message.
// This is the only path that runs hljs and injects copy buttons.
export function renderMarkdown(element: HTMLElement | null, source: string): void {
    renderInto(element, source, {highlight: true});
}

declare global {
    interface Window {
        chatClient: {
            streamStart: typeof streamStart;
            streamAppend: typeof streamAppend;
            streamEnd: typeof streamEnd;
            renderMarkdown: typeof renderMarkdown;
            initChatLog: typeof initChatLog;
            initComposer: typeof initComposer;
            setComposerBusy: typeof setComposerBusy;
            takeComposerValue: typeof takeComposerValue;
        };
    }
}

window.chatClient = {
    streamStart,
    streamAppend,
    streamEnd,
    renderMarkdown,
    initChatLog,
    initComposer,
    setComposerBusy,
    takeComposerValue,
};
