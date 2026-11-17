// Small helpers the page's modules share: elements by id, HTML escaping, messages to the host, the
// log, names from addresses, and the icons.

export const $ = (id) => document.getElementById(id);
export const escHtml = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
export const post = (msg) => chrome.webview.postMessage(msg);

export const ICON = {
  phone: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2Z"/></svg>',
  video: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/></svg>',
  edit: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>',
  copy: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>',
  check: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>',
  videoOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/><path d="M2 2l20 20"/></svg>',
  mic: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/></svg>',
  // A hat over round glasses: privacy mode, as browsers show a private window.
  incognito: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2 11h20"/><path d="m5 11 2.1-6a1 1 0 0 1 1.3-.6L12 5.8l3.6-1.4a1 1 0 0 1 1.3.6L19 11"/><circle cx="7" cy="17" r="3"/><circle cx="17" cy="17" r="3"/><path d="M10 16.4a3.2 3.2 0 0 1 4 0"/></svg>',
  micOff: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/><path d="M3 3l18 18"/></svg>',
  chevronDown: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg>',
  speaker: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M11 5 6 9H2v6h4l5 4V5Z"/><path d="M15.5 8.5a5 5 0 0 1 0 7"/><path d="M19 5a10 10 0 0 1 0 14"/></svg>',
  grid: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5"/></svg>',
  text: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/><path d="M8 8h8M12 8v6"/></svg>',
  close: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>',
  collapse: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M15 3v18M8 9l3 3-3 3"/></svg>',
  send: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 2 11 13M22 2l-7 20-4-9-9-4z"/></svg>',
  lock: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/></svg>',
  unlock: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 7.9-1"/></svg>',
  // Out and back: the round trip.
  selfView: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M21 9V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v10c0 1.1.9 2 2 2h4"/><rect x="12" y="13" width="10" height="7" rx="2"/></svg>',
  transfer: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m18 2 4 4-4 4"/><path d="M14 6h8"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>',
  keypad: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18"/></svg>',
  pause: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="6" y="4" width="4" height="16" rx="1"/><rect x="14" y="4" width="4" height="16" rx="1"/></svg>',
  play: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 4l14 8-14 8z"/></svg>',
  hangup: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7a2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-3.33-2.67m-2.67-3.34a19.79 19.79 0 0 1-3.07-8.63A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91"/><path d="M22 2 2 22"/></svg>',
};

export function log(text, fromHost = false) {
  if (!fromHost) post({ type: "log", text });
  const el = $("log");
  // Follows the newest line, unless you've scrolled up to read something.
  const atEnd = el.hidden || el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  el.textContent += text + "\n";
  if (el.textContent.length > 60000) el.textContent = el.textContent.slice(-40000);
  if (atEnd) el.scrollTop = el.scrollHeight;
}

// "Alice <sip:alice@example.com>" → "Alice"; "sip:bob@host" → "Bob".
export function shortName(remote) {
  const m = /^\s*(.+?)\s*</.exec(remote ?? "");
  if (m) return m[1];
  const u = /^(?:sips?:|tel:)?([^@;>]+)/i.exec(remote ?? "");
  const user = u ? u[1] : remote || "Them";
  return /^[+\d*#]+$/.test(user) ? user : user.charAt(0).toUpperCase() + user.slice(1);
}
// The address without its URI parameters and headers, which say nothing to a person:
// "Bob <sip:bob@host:5060;transport=tcp?X-Y=1>;tag=9" → "Bob <sip:bob@host:5060>",
// "sip:bob@host;transport=tcp" → "sip:bob@host". Call details has the full one.
export function plainAddress(remote) {
  if (!remote) return "";
  const strip = (u) => u.replace(/[;?].*$/, "");
  const m = /^(.*<)([^>]*)>/.exec(remote);
  return m ? `${m[1]}${strip(m[2])}>` : strip(remote.trim());
}
export const initial = (name) => (name ?? "?").replace(/^\+/, "").charAt(0).toUpperCase() || "#";

// The text, cut short with an ellipsis if it's wider than max on g's font.
export function fitText(g, text, max) {
  if (g.measureText(text).width <= max) return text;
  let n = text.length;
  while (n > 1 && g.measureText(`${text.slice(0, n)}…`).width > max) n--;
  return `${text.slice(0, n)}…`;
}
