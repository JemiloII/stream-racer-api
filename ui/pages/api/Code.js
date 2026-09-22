// A code block with a language label and a copy button; highlighted with highlight.js when the CDN script loaded.
import { useState } from "react";
import { html } from "../../lib/html.js";
import { escapeHtml } from "../../lib/text.js";

export const formatJson = (value) => (typeof value === "string" ? value : JSON.stringify(value, null, 2));

function highlight(code, language) {
  try { return window.hljs ? window.hljs.highlight(code, { language }).value : escapeHtml(code); } catch { return escapeHtml(code); }
}

export default function Code({ code, lang = "json", label }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => { try { await navigator.clipboard.writeText(code); setCopied(true); setTimeout(() => setCopied(false), 1200); } catch {} };
  return html`
    <div class="code">
      <div class="code-bar"><span>${label || lang}</span><button class="copy" onClick=${copy}>${copied ? "copied" : "copy"}</button></div>
      <pre><code dangerouslySetInnerHTML=${{ __html: highlight(code, lang) }}></code></pre>
    </div>`;
}
