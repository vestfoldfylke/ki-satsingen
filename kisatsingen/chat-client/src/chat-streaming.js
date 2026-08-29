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

const md = new MarkdownIt({
    html: false,        // no raw HTML in source
    linkify: true,      // auto-detect URLs
    breaks: false,      // require explicit hard breaks
    typographer: false,
    highlight: function (str, lang) {
        if (lang && hljs.getLanguage(lang)) {
            try {
                return `<pre><code class="hljs">${hljs.highlight(str,
                { language: lang, ignoreIllegals: true }).value}</code></pre>`
            } catch (__) {}
        }

        return `<pre><code class="hljs">${md.utils.escapeHtml(str)}</code></pre>`
    }
});

const streamStates = new Map();

function renderInto(el, source) {
    if (!el) return;
    const html = md.render(source ?? '');
    el.innerHTML = DOMPurify.sanitize(html);
}

function elForStream(id) {
    return document.getElementById(`stream-${id}`);
}

export function streamStart(id) {
    streamStates.set(id, { buffer: '', el: elForStream(id) });
}

export function streamAppend(id, text) {
    let state = streamStates.get(id);
    if (!state) {
        state = { buffer: '', el: elForStream(id) };
        streamStates.set(id, state);
    }
    state.buffer += text;
    if (!state.el) state.el = elForStream(id);
    renderInto(state.el, state.buffer);
}

export function streamEnd(id) {
    streamStates.delete(id);
}

export function renderMarkdown(element, source) {
    renderInto(element, source);
}

window.chatClient = {
    streamStart,
    streamAppend,
    streamEnd,
    renderMarkdown,
};
