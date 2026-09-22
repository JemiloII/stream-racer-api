// Text helpers shared by the React pages and the plain-DOM browser sources (so: no React imports here).
export const escapeHtml = (text) => String(text ?? "").replace(/[&<>"]/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[char]));

// Two-letter placeholder for a racer without a picture.
export const initialsOf = (racer) => (racer.displayName || racer.login || "?").slice(0, 2).toUpperCase();

export const ordinal = (place) => place === 1 ? "1st" : place === 2 ? "2nd" : place === 3 ? "3rd" : place + "th";
