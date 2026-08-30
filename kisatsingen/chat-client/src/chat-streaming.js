// Client-side markdown renderer for chat.
//
// Lifecycle for one assistant response:
//   streamStart(id)                → init buffer for a new stream (once)
//   streamAppend(id, text) × N     → server pushes tokens over SignalR
//   streamEnd(id)                  → drop buffer; Blazor then removes the
//                                    stream-{id} div and renders the committed
//                                    <AssistantMessage>, which calls renderMarkdown
//   renderMarkdown(el, source)     → final render on the committed message,
//                                    with hljs syntax highlighting
//
// Streaming skips hljs on purpose: re-highlighting every code block on every
// token is O(n²) in response length. hljs runs once on the committed message.

import MarkdownIt from 'markdown-it';
import DOMPurify from 'dompurify';
import hljs from 'highlight.js/lib/core';
import "highlight.js/styles/a11y-light.min.css"

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

const streamStates = new Map();

function renderInto(el, source, {highlight = false} = {}) {
    if (!el) {
        return;
    }
    const html = md.render(source ?? '');
    el.innerHTML = DOMPurify.sanitize(html);
    if (highlight) {
        el.querySelectorAll('pre code').forEach(node => hljs.highlightElement(node));
    }
}

function elForStream(id) {
    return document.getElementById(`stream-${id}`);
}

// Coalesce rapid streamAppend calls into one render per animation frame.
// Bounds work at display refresh (~60Hz) regardless of token arrival rate.
// rafHandle: 0 = no frame queued; a positive integer = frame in flight.
// Element lookup is deferred to render time: the DOM node may not exist yet
// when streamStart/streamAppend arrive (Blazor's render batch and this JS
// interop call race over SignalR).
function scheduleRender(state) {
    if (state.rafHandle) {
        return;
    }
    state.rafHandle = requestAnimationFrame(() => {
        state.rafHandle = 0;
        state.el ??= elForStream(state.id);
        renderInto(state.el, state.buffer);
    });
}

export function streamStart(id) {
    streamStates.set(id, { id, buffer: '', el: null, rafHandle: 0 });
}

export function streamAppend(id, text) {
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
export function streamEnd(id) {
    const state = streamStates.get(id);
    if (state?.rafHandle) {
        cancelAnimationFrame(state.rafHandle);
    }
    streamStates.delete(id);
}

// Called from AssistantMessage.OnAfterRenderAsync for every committed message.
// This is the only path that runs hljs.
export function renderMarkdown(element, source) {
    renderInto(element, source, {highlight: true});
}

window.chatClient = {
    streamStart,
    streamAppend,
    streamEnd,
    renderMarkdown,
};
