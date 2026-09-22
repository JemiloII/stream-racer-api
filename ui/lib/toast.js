// The one-line status toast at the bottom of the control page (#toast, styled in shared.css).
const TOAST_MS = 1600;

export function toast(message, isError) {
  let element = document.getElementById("toast");
  if (!element) { element = document.createElement("div"); element.id = "toast"; document.body.appendChild(element); }
  element.textContent = message; element.className = "show" + (isError ? " bad" : "");
  clearTimeout(element._hideTimer); element._hideTimer = setTimeout(() => (element.className = ""), TOAST_MS);
}
