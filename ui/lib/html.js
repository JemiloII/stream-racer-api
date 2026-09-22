// `html` = htm bound to React's createElement (the pages have no JSX), plus the per-page stylesheet loader.
import htm from "htm";
import { createElement } from "react";

export const html = htm.bind(createElement);

// Load a stylesheet once, next to the module that owns it: one .css per page / component, sub-components share the page's.
const loadedStylesheets = new Set();
export function useCss(href) {
  if (loadedStylesheets.has(href)) return;
  loadedStylesheets.add(href);
  const link = document.createElement("link");
  link.rel = "stylesheet"; link.href = href;
  document.head.appendChild(link);
}
