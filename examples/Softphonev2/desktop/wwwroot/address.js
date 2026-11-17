// Addresses: how they're typed and shown with an account and without, and the address fields
// (the address bar's and Transfer's) with their suggestions.

import { accountList, accountName } from "./accounts.js";
import { callBlocked, render, state, syncCallBtn } from "./app.js";
import { avatarAttr } from "./avatar.js";
import { contactMark, normAddress } from "./contactmark.js";
import { allContacts, contactFor, initContacts, openContacts, removeContact, sameUri, saveContact } from "./contacts.js";
import { closeHistory, historyEntries, openPeople } from "./history.js";
import { fill, followTyped, homeShown, leaveDial, renderHome, restoreFrom } from "./home.js";
import { openMenu } from "./menus.js";
import { xferField, xferOpen } from "./transfer.js";
import { $, ICON, escHtml, initial, log, plainAddress, post, shortName } from "./util.js";
// Addresses with an account and without. With an account a call goes to its server, which works
// out where it goes from the name or number, and an address on the account's domain shows as its
// user part. Nothing typed is refused: a full SIP address on another domain (user@host, or
// sip:...) is called directly, so on the desktop Call from switches to No account (direct) as you
// type it, and back if you take it out again (home.js, followTyped), as a contact set to call
// directly switches it. The web softphone sends no account list and always has its account, so it
// can't call directly: it takes the address, says the call goes through its server, and sends it.
const directMode = () => Array.isArray(state.accounts) && !state.account;
// "alice@sipsorcery.com" (an account's address of record) -> "sipsorcery.com".
const domainOf = (aor) => (aor ? aor.slice(aor.lastIndexOf("@") + 1).replace(/:\d+$/, "").toLowerCase() : null);
// "sip:alice@sipsorcery.com" (or "Alice <sip:alice@sipsorcery.com:5060;transport=tcp>") -> "alice",
// when it's on the domain of aor (by default the account calls go out from); otherwise null.
function userOnDomain(uri, aor = state.account) {
  const domain = domainOf(aor);
  const m = /^(?:[^<]*<)?\s*(?:sips?:)?([^@:;<>\s]+)@([^;?>\s]+)/i.exec(uri ?? "");
  return m && domain && m[2].replace(/:\d+$/, "").toLowerCase() === domain ? m[1] : null;
}
// An address as a call shows it (the stage's card, the call header, the suggestions). With an
// account: the user part on the account's domain, user@host on another, never "sip:".
// aor: the call's account; null for a direct call (the address as it is, less its parameters).
export function shownAddress(remote, aor = state.account) {
  const plain = plainAddress(remote);
  if (!aor) return plain;
  const m = /^(.*<)([^>]*)(>.*)$/.exec(plain);
  const uri = m ? m[2] : plain;
  const short = userOnDomain(uri, aor) ?? uri.replace(/^sips?:/i, "");
  return m ? `${m[1]}${short}${m[3]}` : short;
}
// What goes in an address field for a saved or recent address: its user part when that's all
// the account needs (an address elsewhere goes in whole: you saved it, so it's still callable).
export const fieldText = (uri) => (directMode() ? uri : userOnDomain(uri) ?? uri);
// What's in an address field as an address, to match against contacts and calls: a bare name or
// number, with an account, is that user at the account's domain (as the host dials it).
export function fieldUri(text) {
  text = (text ?? "").trim();
  const domain = domainOf(state.account);
  return !text || directMode() || !domain || /[:@]/.test(text) ? text : `sip:${text}@${domain}`;
}
// The address fields' hint (placeholder), by mode. Short enough for a phone's field: in the phone
// layout (500 px and under: the web softphone on a phone, or the desktop window made that narrow)
// the direct one leaves out its example, which the field's tooltip still gives.
const phoneWidth = matchMedia("(max-width: 500px)");
export const addressHint = () => (directMode() ? (phoneWidth.matches ? "Number or SIP address" : "Name, number or SIP address, e.g. sip:alice@192.168.0.20") : phoneWidth.matches ? "Name or number" : "Name, number or SIP address");
/** A full SIP address, not just a name or number: user@host, or sip:/sips: something. */
export const looksFull = (text) => /^sips?:\S/i.test(text ?? "") || /^[^@\s]+@[^@\s]+$/.test((text ?? "").replace(/^sips?:/i, ""));
/** A full address that isn't on the account's domain (so an account's server isn't its home). */
export const offDomain = (text) => looksFull(text) && !userOnDomain(text);
/** The web softphone's note for a full address elsewhere: it can't call directly. Null otherwise. */
export const webDirectNote = (text) => (!Array.isArray(state.accounts) && state.account && offDomain(text)
  ? `The web softphone can't call an address directly: this call goes through ${domainOf(state.account) ?? "your account's server"}, which may not reach it.` : null);

// Run by app.js at this point in its start-up, in the order it always ran.
export function initAddress() {
  phoneWidth.addEventListener("change", () => render());
}
// What a full SIP address does, with an account: on the desktop it's called directly (Call from
// switches to No account); the web softphone sends it through its account.
const directNote = () => (Array.isArray(state.accounts) ? "A full SIP address elsewhere is called directly: Call from switches to No account (direct)."
  : "A full SIP address goes through it too: the web softphone can't call directly.");
// The address fields' tooltip: the hint, and with an account, what a full address does.
export const addressTip = () => (directMode() ? (phoneWidth.matches ? "Number or SIP address, e.g. sip:alice@192.168.0.20" : "") : [`A name or number goes through ${domainOf(state.account) ?? "the account's server"}.`, directNote()].filter(Boolean).join(" "));

// ---- Address fields: the address bar's, and Transfer's ----
// As in the prototype: focusing the address (or /) lists contacts and recent calls; typing
// filters them (see suggestions). Contacts come first, in their own group: with nothing typed the
// ones you call most, then your latest calls to anyone else; typed, the contacts that match, then
// other addresses you've called that match. Typing part of a name highlights the first match, so
// Enter calls it; a full address or number doesn't, so Enter calls what you typed. ↑/↓ move
// through the list, Enter or a click puts the highlighted entry in the field (Call, or Enter
// again, calls it), and Esc closes it. Calls that
// didn't connect show their outcome, and missed calls are red. Recents come from the call history
// (see recentCalls); contacts from contacts.js. Each entry's person button adds it to contacts, or
// removes it. The last row opens the People view's Contacts tab: "Contacts…" with nothing typed,
// or "Add … to contacts" for a typed address that isn't one yet.
//
// addressField puts all that on an input: the address bar's (dial, a dropdown) and Transfer's
// (xferField, its list always showing), so the two can't drift apart.

const SUGGEST_ICON = {
  outgoing: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7h10v10"/><path d="M7 17 17 7"/></svg>',
  inbound: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M17 7 7 17"/><path d="M17 17H7V7"/></svg>',
  missed: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m16 2 6 6"/><path d="m22 2-6 6"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/></svg>',
  addContact: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/></svg>',
  removeContact: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 11h-6"/></svg>',
};
const MAX_CONTACT_SUGGESTIONS = 5;   // nothing typed: your most-called contacts
const MAX_RECENT_SUGGESTIONS = 6;
const MAX_TYPED_CONTACTS = 5;
const MAX_TYPED_RECENT = 4;
const RECENCY_DAYS = 14;   // a call this long ago counts half as much as one just now

// The call history (history.js), newest first: { name, uri, dir: "out" | "in" | "missed", at
// (ms), note? ("Busy", "Declined": why a call didn't connect), sip? (the failure response) }.
// None from a host that keeps no history.
function recentCalls() {
  const h = historyEntries();
  return Array.isArray(h) ? h : [];
}

function relTime(at) {
  const mins = Math.round((Date.now() - at) / 60_000);
  if (mins < 1) return "Just now";
  if (mins < 60) return `${mins} min ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours} h ago`;
  if (hours < 48) return "Yesterday";
  return `${Math.round(hours / 24)} days ago`;
}

// The user part of an address: "sip:alice@example.com" → "alice", "tel:+353…" → "+353…".
const userPart = (uri) => String(uri ?? "").toLowerCase().replace(/^(sips?|tel):/, "").split(/[@;]/)[0];

// How well what's typed matches: 0 when it starts a word of the name, the address's user part
// or the address (less sip:); 1 when it's anywhere in the name or address; -1 not at all.
function matchTier(x, q) {
  const name = String(x.name ?? "").toLowerCase();
  const uri = String(x.uri ?? "").toLowerCase();
  const bare = q.replace(/^(sips?|tel):/, "");
  if (bare && (name.startsWith(q) || name.split(/[^\p{L}\p{N}+]+/u).some((w) => w && w.startsWith(q))
    || userPart(uri).startsWith(bare) || uri.replace(/^(sips?|tel):/, "").startsWith(bare))) return 0;
  return name.includes(q) || uri.includes(q) ? 1 : -1;
}

// Per address: how often and how lately you've called or been called (each call counts 1, half
// that after RECENCY_DAYS, a third after twice that…), and its latest call.
function callStats() {
  const now = Date.now();
  const stats = new Map();
  for (const r of recentCalls()) {
    if (!r?.uri) continue;
    const k = normAddress(r.uri);
    const st = stats.get(k) ?? { score: 0, count: 0, last: r };
    st.score += 1 / (1 + Math.max(0, now - (r.at ?? now)) / (RECENCY_DAYS * 86_400_000));
    st.count++;
    stats.set(k, st);
  }
  return stats;
}

// The list for what's typed: { group: "Contacts" | "Recent", name, uri, contact, account?, dir?,
// note?, sip?, at? } entries, then the foot row ({ action: "manage" | "add" }) when foot is set.
// Contacts first: nothing typed, the ones you call most (by callStats: how often and how lately),
// then by name; typed, those that match, a prefix match (a word of the name, or the address's
// user part) before one inside, then the most called. Then Recent: one per address, newest
// first, less the contacts already listed (a contact further down shows here, by its name);
// typed, the other addresses that match, ranked the same way. skip: an address to leave out.
function suggestions(typed, { skip = null, foot = true } = {}) {
  typed = typed.trim();
  const q = typed.toLowerCase();
  const stats = callStats();
  const skipKey = skip ? normAddress(skip) : null;
  const score = (x) => stats.get(normAddress(x.uri))?.score ?? 0;
  const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" });
  const contacts = [];
  const seen = new Set();
  for (const c of allContacts()) {
    const k = normAddress(c.uri);
    if (k === skipKey || seen.has(k)) continue;
    seen.add(k);
    const last = stats.get(k)?.last;
    contacts.push({ name: c.name, uri: c.uri, account: c.account ?? null, contact: true,
      ...(last ? { dir: last.dir, note: last.note, sip: last.sip, at: last.at } : {}) });
  }
  const recent = [];
  const seenRecent = new Set();
  for (const r of recentCalls()) {
    const k = r?.uri ? normAddress(r.uri) : "";
    if (!k || k === skipKey || seenRecent.has(k)) continue;
    seenRecent.add(k);
    const c = contactFor(r.uri);
    recent.push({ ...r, name: c?.name || r.name || shortName(r.uri), contact: !!c, ...(c ? { account: c.account ?? null } : {}) });
  }
  let top, rest;
  if (!q) {
    top = contacts.sort((a, b) => score(b) - score(a) || byName(a, b)).slice(0, MAX_CONTACT_SUGGESTIONS);
    const shown = new Set(top.map((x) => normAddress(x.uri)));
    rest = recent.filter((x) => !shown.has(normAddress(x.uri))).slice(0, MAX_RECENT_SUGGESTIONS);
  } else {
    const rank = (list, max) => list.map((x) => ({ x, tier: matchTier(x, q) })).filter((m) => m.tier >= 0)
      .sort((a, b) => a.tier - b.tier || score(b.x) - score(a.x) || byName(a.x, b.x)).slice(0, max).map((m) => m.x);
    top = rank(contacts, MAX_TYPED_CONTACTS);
    rest = rank(recent.filter((x) => !x.contact), MAX_TYPED_RECENT);
  }
  const items = [...top.map((x) => ({ ...x, group: "Contacts" })), ...rest.map((x) => ({ ...x, group: "Recent" }))];
  if (foot) {
    // The way to the Contacts tab; not while filtering by name, so a name that matches nothing
    // closes the list, as in the prototype.
    if (!typed) items.push({ group: "", action: "manage" });
    else if (looksLikeAddress(typed) && !contactFor(fieldUri(typed))) items.push({ group: "", action: "add", uri: fieldUri(typed) });
  }
  return items;
}

const looksLikeAddress = (q) => /[@:]/.test(q) || /^[+\d*#\s]+$/.test(q.trim());

/**
 * Suggestions on an address input, for the address bar and Transfer. popup: the list drops down
 * while the field has focus (the address bar's); otherwise it's always there (Transfer's). badge:
 * the saved-contact pill inside the field; clear: its × button (both inside a .dest-wrap). foot:
 * the Contacts… and Add to contacts rows. goIcon: the icon on the highlighted row (what Enter
 * does). skip(): an address to leave out. pick(entry): a row was chosen: its address is in the
 * field (f.pick is the entry, for its name and account), and nothing is dialled or sent. changed(): after every refresh.
 * listEmpty(): whether an empty field lists Contacts and Recent (the dial view has its own list).
 * cleared(): Esc or × emptied the field. escEmpty(): Esc with the field already empty; true if it
 * dealt with it. Returns its state: { open, items, active, pick,
 * refresh(keepActive), close(), renderBadge() }.
 */
// rowButtons: each row's Add/Remove contact button and its Call (or Transfer) icon. The address
// bar's list has neither: a row fills the field, and the bar's own buttons call.
// warn(text): a note on what's typed (the web softphone's, for an address it can't call directly),
// shown in the field's bubble when you pause; the text is kept either way.
export function addressField({ input, list, clear, badge, idPrefix, popup, foot, goIcon, goLabel, skip = () => null, pick, changed = () => {},
  listEmpty = () => true, cleared = () => {}, escEmpty = () => false, rowButtons = true, warn = () => null }) {
  const wrap = input.closest(".dest-wrap");
  const f = { open: false, items: [], active: -1, pick: null };

  // keepActive: a refresh (contacts or history changed) keeps the highlighted entry where it can.
  f.refresh = (keepActive = false) => {
    if (input.disabled) return;
    const q = input.value;
    if (popup && !q.trim() && !listEmpty()) {
      f.close();
      changed();
      return;
    }
    const items = suggestions(q, { skip: skip(), foot });
    let active = q.trim() && !looksLikeAddress(q) && items.length && !items[0].action ? 0 : -1;
    if (keepActive && f.active >= 0) {
      const was = f.items[f.active];
      const i = items.findIndex((x) => x.group === was?.group && sameUri(x.uri, was?.uri));
      active = i >= 0 ? i : Math.min(f.active, items.length - 1);
    }
    Object.assign(f, { open: popup ? items.length > 0 : true, items, active });
    if (!f.open) f.active = -1;
    render();
    f.renderBadge();
    changed();
  };

  f.close = () => {
    f.renderBadge();
    if (!popup || !f.open) return;
    Object.assign(f, { open: false, items: [], active: -1 });
    render();
  };

  // The badge: what's in the field is a saved contact's address (typed, pasted or picked). Also
  // the clear (×) button, shown while there's any text.
  f.renderBadge = () => {
    const value = input.value;
    const c = contactFor(fieldUri(value));
    badge.hidden = !c;
    clear.hidden = !value;
    wrap.classList.toggle("has-text", !!value);
    wrap.classList.toggle("has-contact", !!c);
    if (c) {
      badge.innerHTML = `${contactMark(c.name)}<span class="dest-contact-name">${escHtml(c.name)}</span>`;
      badge.title = `Saved contact: ${c.name}`;
    }
  };

  function render() {
    const { open, items, active } = f;
    list.hidden = !open;
    input.setAttribute("aria-expanded", String(open && items.length > 0));
    if (open && active >= 0) input.setAttribute("aria-activedescendant", `${idPrefix}-${active}`); else input.removeAttribute("aria-activedescendant");
    if (!open) { list.innerHTML = ""; return; }
    let group = "";
    list.innerHTML = items.map((it, i) => {
      const cls = `suggest-item${i === active ? " active" : ""}`;
      const attrs = `role="option" id="${idPrefix}-${i}" aria-selected="${i === active}" data-idx="${i}"`;
      if (it.action) {
        const text = it.action === "add" ? `Add <b>${escHtml(shownAddress(it.uri))}</b> to contacts…` : "Contacts…";
        return `<div class="${cls} suggest-foot" ${attrs}>${SUGGEST_ICON.addContact}<span class="suggest-foot-text">${text}</span></div>`;
      }
      const head = it.group !== group
        ? `<div class="suggest-group${it.group === "Contacts" ? " contacts" : ""}" role="presentation">${(group = it.group)}</div>` : "";
      const missed = it.dir === "missed";
      const meta = !it.at ? "<span></span>"
        : `<span class="suggest-meta ${missed ? "bad" : ""}" ${it.sip ? `title="${escHtml(it.sip)}"` : ""}>${SUGGEST_ICON[missed ? "missed" : it.dir === "in" ? "inbound" : "outgoing"]}
            ${missed ? "Missed · " : it.note ? `${escHtml(it.note)} · ` : ""}${relTime(it.at)}</span>`;
      const label = it.contact ? `Remove ${it.name} from contacts` : `Add ${it.name} to contacts`;
      return `${head}<div class="${cls}${it.contact ? " is-contact" : ""}${rowButtons ? "" : " plain"}" ${attrs}>
        <span class="suggest-avatar" aria-hidden="true"${avatarAttr(it.uri)}>${escHtml(initial(it.name))}</span>
        <span class="suggest-main"><span class="suggest-name">${escHtml(it.name)}${it.contact ? contactMark(it.name) : ""}</span><span class="suggest-uri" title="${escHtml(plainAddress(it.uri))}">${escHtml(shownAddress(it.uri))}${fromLabel(it)}</span></span>
        ${meta}
        ${rowButtons ? `<button type="button" class="suggest-contact" tabindex="-1" data-contact="${it.contact ? "remove" : "add"}" data-idx="${i}"
          title="${escHtml(label)}" aria-label="${escHtml(label)}">${it.contact ? SUGGEST_ICON.removeContact : SUGGEST_ICON.addContact}</button>
        <span class="suggest-call" title="${escHtml(goLabel)}" aria-hidden="true">${goIcon()}</span>` : ""}
      </div>`;
    }).join("");
    list.querySelector(".suggest-item.active")?.scrollIntoView({ block: "nearest" });
  }

  // A row chosen: the foot opens the Contacts tab; an entry goes into the field, and nothing
  // more: the call (or transfer) waits for its button, or Enter with no row highlighted.
  function choose(i) {
    const it = f.items[i];
    if (!it) return;
    if (it.action) {
      f.close();
      openContacts(it.action === "add" ? { name: "", uri: it.uri } : null);
      return;
    }
    input.value = fieldText(it.uri);
    f.pick = it;
    if (popup) f.close(); else f.refresh();
    input.focus();
    pick(it);
  }

  if (popup) {
    input.addEventListener("focus", () => f.refresh());
    input.addEventListener("blur", () => setTimeout(f.close, 0));
  }
  // Nothing typed is refused (see directMode). A pasted or dropped address on the account's domain
  // goes in as its user part, which is all the account needs.
  let hintTimer = 0;
  const clearHint = () => { clearTimeout(hintTimer); input.setCustomValidity(""); };
  // (The forms are novalidate: the bubble explains, and never stops Call or Transfer.)
  input.addEventListener("beforeinput", (e) => {
    if (!e.data || e.inputType === "insertText" || directMode() || !state.account) return;
    const user = userOnDomain(e.data.trim());
    if (!user) return;
    e.preventDefault();
    input.setRangeText(user, input.selectionStart ?? input.value.length, input.selectionEnd ?? input.value.length, "end");
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
  // warn's note: set on the field while it applies, and its bubble shown once you pause typing.
  function checkWarn() {
    const msg = warn(input.value.trim()) ?? "";
    input.setCustomValidity(msg);
    if (!msg) return;
    hintTimer = setTimeout(() => { if (input.validationMessage === msg && document.activeElement === input) input.reportValidity(); }, 600);
  }
  input.addEventListener("blur", clearHint);
  input.addEventListener("input", () => { clearHint(); checkWarn(); f.refresh(); });
  input.addEventListener("keydown", (e) => {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      if (!f.open) { f.refresh(); return; }
      const n = f.items.length;
      if (!n) return;
      f.active = e.key === "ArrowDown" ? (f.active + 1) % n : (f.active <= 0 ? n - 1 : f.active - 1);
      render();
      changed();
    } else if (e.key === "Enter" && f.open && f.active >= 0 && !e.ctrlKey && !e.metaKey) {   // Ctrl+Enter: Video call
      e.preventDefault();
      choose(f.active);
    } else if (e.key === "Escape" && popup && f.open) {
      e.preventDefault();
      e.stopPropagation();   // Esc closes the list, and nothing else (not a menu, panel or ended call)
      f.close();
    } else if (e.key === "Escape" && popup && input.value) {
      // A second Esc, with the list closed, empties the field (leaving the list closed).
      e.preventDefault();
      e.stopPropagation();
      input.value = "";
      f.pick = null;
      f.renderBadge();
      cleared();
      changed();
    } else if (e.key === "Escape" && popup && escEmpty()) {
      e.stopPropagation();   // it was the dial view's Esc: back to Home, and nothing else
    }
  });
  // The clear (×) button: empties the field and lists Contacts and Recent, as deleting the text
  // would. mousedown's preventDefault keeps focus in the field (and the list open) until the click.
  clear.innerHTML = ICON.close;
  clear.addEventListener("mousedown", (e) => e.preventDefault());
  clear.addEventListener("click", () => {
    if (input.disabled) return;
    input.value = "";
    f.pick = null;
    input.focus();
    cleared();
    f.refresh();
  });
  list.addEventListener("mousedown", (e) => e.preventDefault());   // keep focus in the field
  list.addEventListener("click", (e) => {
    const btn = e.target.closest(".suggest-contact");
    if (btn) {
      const it = f.items[Number(btn.dataset.idx)];
      if (!it) return;
      if (btn.dataset.contact === "remove") removeContact(it.uri); else saveContact(it.name, it.uri, it.account);
      f.refresh(true);
      return;
    }
    const item = e.target.closest(".suggest-item");
    if (item) choose(Number(item.dataset.idx));
  });
  return f;
}

// A contact's own Call from account, after its address (not a recent call's: that's in History).
function fromLabel(it) {
  const name = it.contact && it.account ? accountName(it.account) : null;
  return name ? ` · from ${escHtml(name)}` : "";
}

// The address bar's. dial.pick: the entry last picked from the list, so Call knows its account.
export let dial;
// Either list, when what it reads changes: the history store says when a call's ended, and
// contacts.js when a contact's saved or removed.
function refreshAddressFields() {
  if (dial.open) dial.refresh(true);
  dial.renderBadge();
  if (xferOpen) xferField.refresh(true);
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initAddress2() {
  // The address bar's. dial.pick: the entry last picked from the list, so Call knows its account.
  dial = addressField({
    input: $("address"), list: $("dialSuggest"), clear: $("destClear"), badge: $("destContact"),
    idPrefix: "sug", popup: true, foot: true, goIcon: () => ICON.phone, goLabel: "Call", rowButtons: false,
    // A pick goes in as New call's do: its account becomes Call from (Esc or × puts it back).
    pick: fill, changed() { followTyped(); syncCallBtn(); renderHome(); },
    warn: webDirectNote,
    // With no call on the stage, the dial view lists your contacts and recent calls itself, so the
    // drop-down only opens once you type.
    listEmpty: () => !homeShown(), cleared: restoreFrom,
    escEmpty() {
      if (!homeShown()) return false;
      leaveDial();
      $("address").blur();
      return true;
    },
  });
  $("dialForm").addEventListener("submit", () => dial.close());
  document.addEventListener("history-changed", () => { if (dial.open) dial.refresh(true); if (xferOpen) xferField.refresh(true); });
  // Contacts (contacts.js): the store the suggestions read, and the People view's Contacts tab
  // that edits it.
  initContacts({
    post,
    log,
    shortName,
    // Call, from the tab: the way Call does, with the contact's address in the field, which closes
    // the view (placeCall) so the stage shows how the call's going. Not while a call can't be
    // placed (callBlocked): then nothing happens, and the address bar keeps what's in it.
    call(uri, video = false) {
      const blocked = callBlocked();
      if (blocked) { log(`not calling ${uri}: ${blocked}`); return; }
      $("address").value = fieldText(uri);
      dial.pick = { uri, name: contactFor(uri)?.name ?? null, account: contactFor(uri)?.account ?? null };
      syncCallBtn();
      if (video) $("videoCallBtn").click();   // Video call, as its button
      else $("dialForm").requestSubmit();
    },
    videoAllowed: () => state.videoAllowed !== false,
    changed() { refreshAddressFields(); render(); },
    openPeople,
    closePeople: () => closeHistory(),
    // The contact form's address follows the same rules as the address bar (see directMode).
    addressMode: () => ({ direct: directMode(), aor: state.account ?? null,
      name: accountList()?.find((a) => a.id === state.accountId)?.name ?? null }),
    userOnDomain,
    shownAddress,
  });
  // / anywhere but a text field goes to the address field, which opens the list.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "/" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
    if (e.target.closest?.("input, textarea, select, [contenteditable], dialog")) return;
    if ($("address").disabled) return;
    e.preventDefault();
    $("address").focus();
  });
}
