// One route of the API reference: a collapsible card with the parameter table (editable values), a "Try it" panel
// that sends the request from the browser, the example response and the matching curl line.
import { useState } from "react";
import { html } from "../../lib/html.js";
import { authHeaders, getToken } from "../../lib/api.js";
import { escapeHtml } from "../../lib/text.js";
import Code, { formatJson } from "./Code.js";

// `/boom/:x` + {x: "shibikox", seconds: 3} → "/boom/shibikox?seconds=3" (empty path params and query values are dropped).
function buildUrl(route, values) {
  let path = route.path.replace(/:(\w+)/g, (_, name) => encodeURIComponent(values[name] ?? ""));
  path = path.replace(/\/+$/, "").replace(/\/\//g, "/") || "/";
  const queryParts = route.params.filter((param) => param.in === "query" && values[param.name] !== undefined && values[param.name] !== "").map((param) => `${param.name}=${encodeURIComponent(values[param.name])}`);
  return path + (queryParts.length ? "?" + queryParts.join("&") : "");
}

function curlFor(route, url, body) {
  const tokenFlag = getToken() ? ` -H "Authorization: Bearer ${getToken()}"` : "";
  if (route.method === "GET") return `curl${tokenFlag} "${location.origin}${url}"`;
  const bodyFlag = body ? ` -H "Content-Type: application/json" -d '${body.replace(/'/g, "'\\''")}'` : " -d ''";
  return `curl -X ${route.method}${tokenFlag}${bodyFlag} "${location.origin}${url}"`;
}

// Inline `code` spans in a description.
const describe = (text) => escapeHtml(text).replace(/`([^`]+)`/g, "<code>$1</code>");

export default function RouteCard({ route }) {
  const [open, setOpen] = useState(false);
  const initialValues = {}; for (const param of route.params) if (param.in !== "body") initialValues[param.name] = param.example ?? param.default ?? "";
  const [values, setValues] = useState(initialValues);
  const [body, setBody] = useState(route.body ? formatJson(route.body) : "");
  const [result, setResult] = useState(null);
  const [busy, setBusy] = useState(false);
  const url = buildUrl(route, values);
  const send = async () => {
    setBusy(true); const startedAt = performance.now();
    try {
      const response = await fetch(url, { method: route.method, headers: { "Content-Type": "application/json", ...authHeaders() }, body: route.method === "GET" ? undefined : (body || "") });
      const contentType = response.headers.get("content-type") || "";
      const text = contentType.startsWith("image/") ? `(${contentType}, ${response.headers.get("content-length") || "?"} bytes)` : await response.text();
      let pretty = text; try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch {}
      setResult({ status: response.status, ms: Math.round(performance.now() - startedAt), body: pretty });
    } catch (error) { setResult({ status: 0, ms: 0, body: String(error) }); }
    setBusy(false);
  };
  return html`
    <div class=${"route " + route.method.toLowerCase() + (open ? " open" : "")}>
      <button class="route-head" onClick=${() => setOpen(!open)}>
        <span class="m">${route.method}</span><code class="path">${route.path}</code><span class="sum">${route.summary}</span><span class="chev">${open ? "▾" : "▸"}</span>
      </button>
      ${open ? html`
        <div class="route-body">
          ${route.description ? html`<p class="desc" dangerouslySetInnerHTML=${{ __html: describe(route.description) }}></p>` : null}
          ${route.note ? html`<p class="desc note">${route.note}</p>` : null}
          <div class="cols">
            <div>
              <h4>Parameters</h4>
              ${route.params.length ? html`
                <table class="params"><thead><tr><th>name</th><th>in</th><th>type</th><th>description</th><th>value</th></tr></thead><tbody>
                  ${route.params.map((param) => html`
                    <tr key=${param.name}>
                      <td><code>${param.name}</code></td><td>${param.in}</td><td>${param.type}</td>
                      <td>${param.description}${param.default !== undefined ? html` <small>default ${String(param.default)}</small>` : null}</td>
                      <td>${param.in === "body" ? html`<textarea rows="4" spellCheck="false" value=${body} onInput=${(event) => setBody(event.target.value)}></textarea>`
                                                : html`<input value=${values[param.name] ?? ""} placeholder=${param.example ?? ""} onInput=${(event) => setValues({ ...values, [param.name]: event.target.value })} />`}</td>
                    </tr>`)}
                </tbody></table>` : html`<p class="desc">none</p>`}
              ${route.errors ? html`<h4>Errors</h4><p class="desc">${route.errors}</p>` : null}
            </div>
            <div>
              <h4>Try it</h4>
              <div class="try">
                <code class="url">${route.method} ${url}</code>
                <button onClick=${send} disabled=${busy} aria-busy=${busy}>Execute</button>
              </div>
              ${result ? html`
                <div class="res-head"><span class=${"code-status " + (result.status < 300 ? "ok" : "bad")}>${result.status || "ERR"}</span><span>${result.ms} ms</span></div>
                <${Code} code=${result.body} label="response" />` : html`
                <h5>Example response</h5>
                <${Code} code=${formatJson(route.response)} label="200" />`}
              <h5>curl</h5>
              <${Code} code=${curlFor(route, url, route.method === "GET" ? "" : body)} lang="bash" label="bash" />
            </div>
          </div>
        </div>` : null}
    </div>`;
}
