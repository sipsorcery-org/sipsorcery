// Gravatar pictures in place of the initials: in the address field's suggestions, People's
// Recent and Contacts rows, the sidebar's History, Active calls rows, grid tiles and the stage.
//
// An avatar is an element with the initial as its text and data-av set to an email-like form of
// the call's or contact's address (sip:alice@example.com → alice@example.com). Its picture is
// https://www.gravatar.com/avatar/<SHA-256 of that>?d=identicon: their picture if they have one,
// otherwise Gravatar's identicon for that address (the same pattern every time), which covers the
// initial. The initial stays only if gravatar.com can't be reached. Each address is looked up once per run. Addresses with no
// user part, a host that's an IP address or has no dot (localhost), or tel: numbers aren't looked
// up at all. Settings' "Show Gravatar pictures" (prefs.gravatar, on by default) turns it off, as
// it sends gravatar.com a hash of each address shown.
//
// Renders just set data-av (avatarAttr in HTML, setAvatar on an element); a MutationObserver
// adds the <img> whenever such an element appears or its text is redrawn.

import { prefs } from "./settings.js";

const cache = new Map();   // email -> { state: "loading" | "ok" | "none", url }

export function emailOf(address) {
  let s = String(address ?? "").trim();
  const m = /<([^>]*)>/.exec(s);
  if (m) s = m[1];
  s = s.replace(/[;?].*$/, "").toLowerCase();
  if (/^tel:/.test(s)) return null;
  s = s.replace(/^sips?:/, "");
  const at = s.lastIndexOf("@");
  if (at <= 0) return null;
  const user = s.slice(0, at);
  const host = s.slice(at + 1).replace(/:\d+$/, "");
  if (!/^[a-z0-9-]+(\.[a-z0-9-]+)+$/.test(host) || /^[\d.]+$/.test(host)) return null;
  return `${user}@${host}`;
}

const esc = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));

// For HTML templates: ` data-av="…"`, or nothing for an address with no lookup.
export function avatarAttr(address) {
  const email = emailOf(address);
  return email ? ` data-av="${esc(email)}"` : "";
}

// For built elements: sets (or clears) data-av; the observer does the rest.
export function setAvatar(el, address) {
  const email = emailOf(address);
  if (email) { if (el.dataset.av !== email) el.dataset.av = email; }
  else if (el.dataset.av) delete el.dataset.av;
  else hydrate(el);   // no lookup: drop a picture left from a previous address
}

async function lookup(email) {
  cache.set(email, { state: "loading" });
  try {
    const bytes = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(email));
    const hash = [...new Uint8Array(bytes)].map((b) => b.toString(16).padStart(2, "0")).join("");
    const url = `https://www.gravatar.com/avatar/${hash}?s=160&d=identicon`;
    await new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = resolve;
      img.onerror = reject;
      img.src = url;
    });
    cache.set(email, { state: "ok", url });
  } catch {
    cache.set(email, { state: "none" });
  }
  for (const el of document.querySelectorAll(`[data-av="${CSS.escape(email)}"]`)) hydrate(el);
}

function hydrate(el) {
  if (!el) return;
  const email = prefs.gravatar === false ? null : el.dataset?.av;
  const got = email ? cache.get(email) : null;
  if (email && !got) lookup(email);
  const img = el.querySelector?.(":scope > img.av-img");
  if (got?.state === "ok") {
    if (img?.getAttribute("src") === got.url) return;
    const pic = img ?? Object.assign(document.createElement("img"), { className: "av-img", alt: "", draggable: false });
    pic.src = got.url;
    if (!img) el.append(pic);
  } else if (img) img.remove();
}

function scan(root) {
  if (root.nodeType !== 1) return;
  if (root.dataset.av !== undefined) hydrate(root);
  for (const el of root.querySelectorAll("[data-av]")) hydrate(el);
}

new MutationObserver((records) => {
  for (const r of records) {
    if (r.type === "attributes") { hydrate(r.target); continue; }
    // A redrawn avatar (its text set again drops the picture), or new rows with avatars.
    const t = r.target.nodeType === 1 ? r.target : r.target.parentElement;
    if (t?.dataset?.av !== undefined && !r.addedNodes[0]?.classList?.contains("av-img")) hydrate(t);
    for (const n of r.addedNodes) if (!n.classList?.contains("av-img")) scan(n);
  }
}).observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ["data-av"] });

// Settings turned it on or off: every avatar again.
document.addEventListener("gravatar-changed", () => {
  for (const el of document.querySelectorAll("[data-av]")) hydrate(el);
});
