// The "saved contact" mark: one small icon (a person with a tick, in the accent colour) shown
// wherever a call or an address is one of your contacts: the address field, its suggestions, the
// People view's Recent tab and its details, the sidebar's History, the Active calls rows, the grid
// tiles and the call's header. Addresses you haven't saved show nothing extra: they're still
// normal calls. Its tooltip says "Saved contact".
//
// normAddress is how a call's address is matched to a contact's: the URI out of a
// "Name <sip:…>" header, without parameters or headers, case aside, and sip: optional.

export function normAddress(a) {
  let s = String(a ?? "").trim();
  const m = /<([^>]*)>/.exec(s);
  if (m) s = m[1].trim();
  return s.replace(/[;?].*$/, "").toLowerCase().replace(/^sip:/, "");
}

const SVG = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="m16 11 2 2 4-4"/></svg>';
const esc = (t) => String(t ?? "").replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));

// name: the contact's, for the tooltip ("Saved contact: Hannah Desk").
export function contactMark(name = null) {
  const label = name ? `Saved contact: ${name}` : "Saved contact";
  return `<span class="contact-mark" role="img" title="${esc(label)}" aria-label="${esc(label)}">${SVG}</span>`;
}

// The same, as an element, for code that builds nodes.
export function contactMarkEl(name = null) {
  const t = document.createElement("template");
  t.innerHTML = contactMark(name);
  return t.content.firstChild;
}
