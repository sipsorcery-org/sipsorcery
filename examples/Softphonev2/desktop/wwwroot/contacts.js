// Contacts: names for SIP addresses, for the address field's suggestions (app.js, "Address field
// suggestions"), and the People view's Contacts tab that edits them.
//
// The desktop host keeps them per profile (contacts-{profile}.json beside the accounts) and sends
//   contacts {contacts: [{name, uri, account?, media?}]}
// on ready and after every change; the page sends contact-save {name, uri, account?, media?} and
// contact-remove {uri}. account is the SIP account (its id) to call them from, "direct" for
// no account (direct), or none for whichever Call from has. media (desktop only: the web
// softphone's calls are always WebRTC) is what to offer them when it's known what their phone
// takes, one of MEDIA's, so the call needn't try one kind after another; none works it out. Calling a contact switches Call from to it (accounts.js useAccount). A host that never sends any (the web softphone) leaves them in this
// browser's localStorage. Changes show here straight away; the host's reply then replaces them.
//
// The Contacts tab (history.js owns the view and its tabs, and calls mountContacts): search, Add
// contact, and the contacts like Recent's calls: a row (or its chevron) opens details with their
// recent calls, Call, Edit and Delete (Delete asks again on the button). Add and Edit share one
// form at the top: a name, the name or number, (desktop, with accounts) the account to call
// from, and (desktop) the media. Add to contacts / Edit contact from a call on Recent comes here, and goes back to Recent
// when the form is done with.
//
// The address follows the contact's Call from, as the address bar follows Call from (app.js,
// directMode): with an account it's just a name or number (no ":" or "@"), saved as that user at
// the account's server (sip:alice@sipsorcery.com), and a line under the fields says where calls
// go; with No account (direct) it's a full SIP address, as typed. Contacts are always kept as
// full addresses, so calls and history still find them.

import { historyEntries, describeCall } from "./history.js";
import { accountList, DIRECT } from "./accounts.js";
import { normAddress, contactMark } from "./contactmark.js";
import { avatarAttr } from "./avatar.js";

const KEY = "softphone.contacts";
const $ = (id) => document.getElementById(id);
const esc = (t) => String(t ?? "").replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
const svg = (p) => `<svg viewBox="0 0 24 24" aria-hidden="true">${p}</svg>`;
const ICON = {
  close: svg('<path d="M18 6 6 18M6 6l12 12"/>'),
  add: svg('<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/>'),
  phone: svg('<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/>'),
  edit: svg('<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>'),
  trash: svg('<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6M14 11v6"/>'),
  search: svg('<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>'),
  chevronDown: svg('<path d="m6 9 6 6 6-6"/>'),
};
// A contact's media (desktop, Contacts.cs ContactMedia): "" works it out on each call.
const MEDIA = [
  ["", "Automatic", "Automatic (plain RTP, then SRTP, then WebRTC if they turn it down)"],
  ["rtp", "Plain RTP", "Plain RTP (SRTP if their phone takes it up)"],
  ["srtp", "SRTP required", "SRTP required (RTP/SAVP, SDES keys)"],
  ["webrtc", "WebRTC", "WebRTC (ICE, DTLS-SRTP)"],
];
const mediaText = (m) => (MEDIA.find(([v]) => v === (m ?? "")) ?? MEDIA[0])[2];
const ARM_MS = 3000;   // how long Delete waits for the second click
const DETAIL_CALLS = 5;   // recent calls in a contact's details

// The same address, as calls are matched to contacts (contactmark.js normAddress).
export const sameUri = (a, b) => normAddress(a) === normAddress(b);
const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" });

let list = [];
let fromHost = false;   // true once the host sends its list: then it keeps them, not localStorage
try { list = JSON.parse(localStorage.getItem(KEY) ?? "[]").filter((c) => c?.uri); } catch { list = []; }

let app = null;   // { post, log, call(uri), shortName(uri), changed(), openPeople(tab, opts) }: see initContacts

export const allContacts = () => list;
export const contactFor = (uri) => list.find((c) => sameUri(c.uri, uri));

export function onContacts(contacts) {
  fromHost = true;
  list = Array.isArray(contacts) ? contacts.filter((c) => c?.uri) : [];
  changed();
}

function changed() {
  if (!fromHost) try { localStorage.setItem(KEY, JSON.stringify(list)); } catch { /* private window */ }
  if ($("contactList")) renderList();
  app?.changed();
  document.dispatchEvent(new CustomEvent("contacts-changed"));   // history's names and buttons
}

// Adds a contact, or renames the one with that address. account: the SIP account (id) to call
// them from, or none for Call from's; media: what to offer them (MEDIA), or none to work it out.
// Saved without one, a contact keeps the media it had (Add to contacts from Recent, say).
export function saveContact(name, uri, account = null, media = undefined) {
  uri = uri.trim();
  name = (name ?? "").trim() || app.shortName(uri);
  account = account || null;
  if (media === undefined) media = contactFor(uri)?.media;
  media = fromHost && media ? media : null;
  const extra = { ...(account ? { account } : {}), ...(media ? { media } : {}) };
  list = [...list.filter((c) => !sameUri(c.uri, uri)), { name, uri, ...extra }].sort(byName);
  if (fromHost) app.post({ type: "contact-save", name, uri, ...extra });
  app.log(`contact saved: ${name} <${uri}>`);
  changed();
}

export function removeContact(uri) {
  list = list.filter((c) => !sameUri(c.uri, uri));
  if (fromHost) app.post({ type: "contact-remove", uri });
  app.log(`contact removed: ${uri}`);
  changed();
}

// ---- The Contacts tab ----

// editing: the address being edited, "" for a new one; draft: the form's text, so it survives the
// view being drawn again; back: the tab to return to when the form is done with.
const ui = { q: "", selected: null, editing: null, draft: null, back: null, armed: null, armTimer: 0 };

export function initContacts(a) {
  app = a;
  // Calls ending, or removed, change the recent calls on the tab.
  document.addEventListener("history-changed", () => { if ($("contactList")) renderList(); });
}

export const contactCount = () => list.length;

// Fills the tab; history.js calls this whenever it draws the view on Contacts.
export function mountContacts(panel) {
  panel.innerHTML = `
    <div class="hv-toolbar">
      <label class="hv-search">${ICON.search}<input id="contactsSearch" type="search" placeholder="Search contacts" value="${esc(ui.q)}" spellcheck="false" autocomplete="off" aria-label="Search contacts"></label>
      <button type="button" class="btn am-small pv-add" data-act="add">${ICON.add}<span>Add contact</span></button>
    </div>
    <form class="contact-form" id="contactForm" autocomplete="off" novalidate hidden>
      <h3 id="contactFormTitle">New contact</h3>
      <div class="contact-fields">
        <label class="am-field"><span class="am-label">Name</span><input type="text" name="name" spellcheck="false" placeholder="e.g. Alice"></label>
        <label class="am-field"><span class="am-label" id="contactUriLabel">Name or number</span><input type="text" name="uri" spellcheck="false" placeholder="e.g. alice"></label>
        <label class="am-field contact-account" hidden><span class="am-label">Call from</span><select name="account"></select></label>
        <label class="am-field contact-media" hidden><span class="am-label">Media</span><select name="media">${MEDIA.map(([v, , text]) => `<option value="${v}">${esc(text)}</option>`).join("")}</select></label>
      </div>
      <p class="am-hint contact-via" id="contactVia"></p>
      <p class="contact-error" id="contactError" role="alert" hidden></p>
      <div class="contact-form-btns">
        <button type="button" class="btn am-small" data-act="cancel">Cancel</button>
        <button type="submit" class="btn btn-primary am-small">Save</button>
      </div>
    </form>
    <ol class="hv-list" id="contactList" aria-label="Contacts"></ol>
    <div class="hv-empty" id="contactsEmpty" hidden></div>`;

  // Its own buttons: kept from the view's handler, which has acts of the same names.
  panel.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-act]");
    if (!btn) return;
    e.stopPropagation();
    const uri = btn.closest("[data-uri]")?.dataset.uri;
    const c = uri != null ? contactFor(uri) : null;
    switch (btn.dataset.act) {
      case "add": editContact({ name: "", uri: "" }, true); break;
      case "cancel": closeForm(); break;
      case "select":
        if (!c) break;
        ui.selected = sameUri(ui.selected, c.uri) ? null : c.uri;
        renderList();
        rowEl(c.uri)?.querySelector(".hv-main")?.focus({ preventScroll: true });
        break;
      case "call": if (c) app.call(c.uri); break;
      case "edit": if (c) editContact(c, false); break;
      case "recent": if (c) app.openPeople("recent", { q: c.uri }); break;
      case "delete":
        if (!c) break;
        if (sameUri(ui.armed, c.uri)) {
          disarm();
          if (sameUri(ui.editing, c.uri)) closeForm();
          removeContact(c.uri);
          $("contactsSearch").focus();
        } else {
          disarm();
          ui.armed = c.uri;
          ui.armTimer = setTimeout(() => { disarm(); if ($("contactList")) renderList(); }, ARM_MS);
          renderList();
          rowEl(c.uri)?.querySelector("[data-act=delete]")?.focus();
        }
        break;
    }
  });
  panel.addEventListener("input", (e) => {
    if (e.target.id === "contactsSearch") { ui.q = e.target.value; renderList(); }
    else if (e.target.form?.id === "contactForm") ui.draft = draftOf(e.target.form);
  });
  panel.addEventListener("change", (e) => {
    if (e.target.form?.id !== "contactForm") return;
    // Another Call from: an address on its server goes back to just the name or number.
    if (e.target.name === "account") {
      const via = viaOf(e.target.value || null);
      if (!via.direct) e.target.form.uri.value = shortFor(e.target.form.uri.value, via);
    }
    ui.draft = draftOf(e.target.form);
    renderVia();
  });
  // The address field, with an account: no ":" or "@" typed (the field's bubble says why); a
  // pasted address on the account's server goes in as its name or number. As the address bar.
  const uriField = $("contactForm").uri;
  let hintTimer = 0;
  const clearHint = () => { clearTimeout(hintTimer); uriField.setCustomValidity(""); };
  uriField.addEventListener("beforeinput", (e) => {
    const via = viaOf($("contactForm").account.value || null);
    if (!e.data || !/[:@]/.test(e.data) || via.direct) return;
    e.preventDefault();
    const user = e.inputType !== "insertText" ? app.userOnDomain(e.data.trim(), via.aor) : null;
    if (user) {
      uriField.setRangeText(user, uriField.selectionStart ?? uriField.value.length, uriField.selectionEnd ?? uriField.value.length, "end");
      uriField.dispatchEvent(new Event("input", { bubbles: true }));
      return;
    }
    uriField.setCustomValidity(notJustName(via));
    uriField.reportValidity();
    clearTimeout(hintTimer);
    hintTimer = setTimeout(clearHint, 4000);
  });
  uriField.addEventListener("input", () => { clearHint(); renderVia(); });
  uriField.addEventListener("blur", clearHint);
  $("contactForm").addEventListener("submit", (e) => { e.preventDefault(); submitForm(); });
  // Esc closes the form, then clears the search; only then does it reach the view (which closes).
  panel.addEventListener("keydown", (e) => {
    if (e.key !== "Escape") return;
    if (!$("contactForm").hidden) closeForm();
    else if (e.target.id === "contactsSearch" && ui.q) { ui.q = ""; e.target.value = ""; renderList(); }
    else return;
    e.preventDefault();
    e.stopPropagation();
  });

  if (ui.editing !== null) showForm();
  renderList();
}

const draftOf = (form) => ({ name: form.name.value, uri: form.uri.value, account: form.account.value || null, media: form.media.value || null });

// The host part of an account's server, as the desktop dials it (Accounts.cs Host): no scheme,
// nothing before an "@", no parameters. "sip:sipsorcery.com;transport=tcp" -> "sipsorcery.com".
const hostOf = (server) => String(server ?? "").trim().replace(/^sips?:/i, "").replace(/^.*@/, "").replace(/;.*$/, "");

// Where a contact's calls go, by its Call from (an account id, DIRECT, or null for Any):
// { direct: true } with no account, else { aor: "user@host" of the account, host, name, any }.
// A removed or disabled account calls from Call from's, as fromText says.
function viaOf(account) {
  const mode = app.addressMode();   // Call from's: { direct, aor, name }
  if (account === DIRECT) return { direct: true };
  const a = account ? accountList()?.find((x) => x.id === account && x.enabled) : null;
  if (a) return { aor: `${a.username}@${hostOf(a.server)}`, host: hostOf(a.server), name: a.name };
  if (mode.direct) return { direct: true, any: true };
  const host = mode.aor ? mode.aor.slice(mode.aor.lastIndexOf("@") + 1) : null;
  return { aor: mode.aor, host, name: mode.name, any: true };
}
// The address field's text for a saved address: its name or number when it's on via's server.
const shortFor = (uri, via) => (via.direct ? uri : app.userOnDomain(uri, via.aor) ?? uri);
// Why a full address isn't taken here, and (on the desktop) where it is.
const notJustName = (via) => `Just the name or number: calls go through ${via.host ?? "the account's server"}.${accountList()?.length ? " For a full SIP address, set Call from to No account (direct)." : ""}`;

// The line under the fields: where the contact's calls will go, from what's typed and Call from.
function renderVia() {
  const form = $("contactForm");
  if (!form || form.hidden) return;
  const via = viaOf(form.account.value || null);
  const typed = form.uri.value.trim();
  $("contactUriLabel").textContent = via.direct ? "Number or SIP address" : "Name or number";
  form.uri.placeholder = via.direct ? "e.g. sip:alice@192.168.0.20" : "e.g. alice";
  const who = esc(typed || "…");
  const from = via.name ? `${esc(via.name)}'s server` : "your account's server";
  $("contactVia").innerHTML = via.direct
    ? `${via.any ? "Call from is No account (direct) now, so calls" : "Calls"} go straight to the address, with no server: type a full SIP address (<span class="am-mono">sip:alice@192.168.0.20</span>).`
    : via.host
      ? `Calls go to <span class="am-mono">${/[:@]/.test(typed) ? esc(typed) : `${who}@${esc(via.host)}`}</span> through ${from}${via.any && accountList()?.length ? " (Any: the account in Call from when you call, now this one)" : ""}.`
      : "Calls go through your account's server once you've signed in.";
}

const rowEl = (uri) => $("contactList")?.querySelector(`.hv-row[data-uri="${CSS.escape(uri)}"]`);

/**
 * Opens the Contacts tab; with prefill ({name, uri, account?}), on its form: Edit for a contact's address,
 * else New contact. back: the tab to return to when the form is saved or cancelled.
 */
export function openContacts(prefill = null, back = null) {
  ui.back = null;
  if (!prefill) Object.assign(ui, { q: "", editing: null, draft: null });
  if (!app.openPeople("contacts", { focus: !prefill })) return;
  if (prefill) {
    const c = contactFor(prefill.uri);
    editContact(c ?? prefill, !c);
    ui.back = back;
  }
}

function editContact(c, isNew) {
  ui.editing = isNew ? "" : c.uri;
  ui.draft = { name: c.name ?? "", uri: c.uri ?? "", account: c.account ?? null, media: c.media ?? null };
  ui.back = null;
  showForm();
  renderList();
  $("contactForm").name.focus();
  $("contactForm").name.select();
}

function showForm() {
  const form = $("contactForm");
  form.hidden = false;
  form.name.value = ui.draft?.name ?? "";
  // Call from: "Any" for Call from's, the accounts (none in the web softphone, which hides it),
  // and No account (direct). One that's since been removed stays listed, so saving doesn't quietly drop it.
  const accounts = accountList();
  const want = ui.draft?.account ?? "";
  form.querySelector(".contact-account").hidden = !accounts?.length;
  if (accounts?.length) {
    const opts = [`<option value="">Any (the current Call from account)</option>`,
      ...accounts.map((a) => `<option value="${esc(a.id)}">${esc(a.name)}${a.enabled ? "" : " (disabled)"}</option>`),
      `<option value="${DIRECT}">No account (direct)</option>`];
    if (want && want !== DIRECT && !accounts.some((a) => a.id === want)) opts.push(`<option value="${esc(want)}">A removed account</option>`);
    form.account.innerHTML = opts.join("");
    form.account.value = want;
  }
  // Media: the desktop's alone (fromHost); the web softphone's calls are always WebRTC.
  form.querySelector(".contact-media").hidden = !fromHost;
  form.media.value = ui.draft?.media ?? "";
  // The address as its name or number, when it's on the server the contact calls through.
  form.uri.value = shortFor(ui.draft?.uri ?? "", viaOf(ui.draft?.account ?? null));
  const c = ui.editing ? contactFor(ui.editing) : null;
  $("contactFormTitle").textContent = c ? `Edit ${c.name}` : "New contact";
  $("contactError").hidden = true;
  renderVia();
}

// Done with the form: back to Recent if it came from there, else to the search box.
function closeForm() {
  const back = ui.back;
  Object.assign(ui, { editing: null, draft: null, back: null });
  if (back && app.openPeople(back)) return;
  if (!$("contactForm")) return;
  $("contactForm").hidden = true;
  renderList();
  $("contactsSearch").focus();
}

function submitForm() {
  const form = $("contactForm");
  const name = form.name.value.trim();
  const typed = form.uri.value.trim();
  const via = viaOf(form.account.value || null);
  const err = (text, field) => { $("contactError").textContent = text; $("contactError").hidden = false; field.focus(); };
  if (!typed) return err(via.direct ? "Type a number or SIP address." : "Type a name or number.", form.uri);
  if (/\s/.test(typed) && !/^(tel:)?[+\d\s()-]+$/i.test(typed)) return err(via.direct ? "A SIP address can't have spaces in it." : "A name can't have spaces in it.", form.uri);
  // With an account, a name or number, kept as that user at the account's server. A full address
  // left from before (an edited contact, or one added from Recent) goes if it's on that server.
  let uri = typed;
  if (!via.direct) {
    const user = /[:@]/.test(typed) ? app.userOnDomain(typed, via.aor) : typed;
    if (!user) return err(notJustName(via), form.uri);
    if (!via.host) return err("Sign in first: a contact's address is on your account's server.", form.uri);
    uri = `sip:${user.replace(/\s+/g, "")}@${via.host}`;
  }
  const clash = contactFor(uri);
  if (clash && !sameUri(clash.uri, ui.editing)) return err(`${clash.name} already has that address.`, form.uri);
  // A changed address replaces the old entry (contacts are kept by address).
  const old = ui.editing;
  if (old && !sameUri(old, uri)) {
    removeContact(old);
    if (sameUri(ui.selected, old)) ui.selected = uri;
  }
  saveContact(name, uri, form.account.value || null, form.media.value || null);
  closeForm();
}

function disarm() {
  clearTimeout(ui.armTimer);
  ui.armed = null;
}

// A contact's Call from account, for its details.
function fromText(c) {
  if (!c.account) return "Any: the current Call from account";
  if (c.account === DIRECT) return "No account (direct)";
  const a = accountList()?.find((x) => x.id === c.account);
  if (!a) return "A removed account (calls use Call from's)";
  return a.enabled ? a.name : `${a.name} (disabled: calls use Call from's)`;
}

function renderList() {
  const q = ui.q.trim().toLowerCase();
  const shown = list.filter((c) => !q || c.name.toLowerCase().includes(q) || c.uri.toLowerCase().includes(q));
  const calls = historyEntries() ?? [];
  $("contactList").hidden = !shown.length;
  $("contactList").innerHTML = shown.map((c) => {
    const mine = calls.filter((e) => sameUri(e.uri, c.uri));
    const last = mine[0] ? describeCall(mine[0]) : null;
    const sel = sameUri(ui.selected, c.uri);
    const editing = ui.editing && sameUri(ui.editing, c.uri);
    const armed = sameUri(ui.armed, c.uri);
    const n = esc(c.name), u = esc(c.uri);
    const via = viaOf(c.account ?? null);
    const short = esc(via.direct ? c.uri : app.shownAddress(c.uri, via.aor));
    const callList = mine.slice(0, DETAIL_CALLS).map((e) => {
      const d = describeCall(e);
      return `<li><span class="hv-outcome ${d.tone}">${d.ico}<span>${esc(d.text)}</span></span><span class="pv-call-at">${esc(d.at)}</span></li>`;
    }).join("");
    const detail = sel ? `<div class="hv-detail">
        <dl>
          <div class="wide"><dt>Address</dt><dd class="hv-mono-dd">${u}</dd></div>
          ${accountList()?.length ? `<div class="wide"><dt>Call from</dt><dd>${esc(fromText(c))}</dd></div>` : ""}
          ${fromHost ? `<div class="wide"><dt>Media</dt><dd>${esc(mediaText(c.media))}</dd></div>` : ""}
          <div class="wide"><dt>Recent calls</dt><dd>${mine.length ? `<ul class="pv-calls">${callList}</ul>` : "None yet."}</dd></div>
        </dl>
        <div class="hv-actions">
          <button type="button" class="btn btn-primary am-small" data-act="call">${ICON.phone}Call</button>
          <button type="button" class="btn am-small" data-act="edit">${ICON.edit}Edit</button>
          ${mine.length ? `<button type="button" class="btn am-small" data-act="recent">${mine.length === 1 ? "Show the call in Recent" : `Show all ${mine.length} calls in Recent`}</button>` : ""}
          <span class="am-spacer"></span>
          <button type="button" class="btn am-small am-remove${armed ? " confirm" : ""}" data-act="delete"
            aria-label="${armed ? `Delete ${n}? Click again` : `Delete ${n}`}">${ICON.trash}${armed ? "Delete? Click again" : "Delete"}</button>
        </div>
      </div>` : "";
    return `<li class="hv-row${sel ? " selected" : ""}${editing ? " editing" : ""}" data-uri="${u}">
        <button type="button" class="hv-main" data-act="select" aria-expanded="${sel}" title="${u} · details">
          <span class="call-avatar" aria-hidden="true"${avatarAttr(c.uri)}>${esc((c.name.replace(/^\+/, "").charAt(0) || "#").toUpperCase())}</span>
          <span class="hv-who"><span class="hv-name">${n}${contactMark(c.name)}</span><span class="hv-uri">${short}</span></span>
          <span class="hv-outcome ${last?.tone ?? ""}">${last ? `${last.ico}<span>${esc(last.text)}</span>` : ""}</span>
          <span class="hv-time">${last ? esc(last.when) : ""}</span>
        </button>
        <button type="button" class="hv-icon hv-toggle" data-act="select" aria-expanded="${sel}"
          aria-label="${sel ? "Hide" : "Show"} details for ${n}" title="${sel ? "Hide details" : "Show details"}">${ICON.chevronDown}</button>
        <button type="button" class="hv-icon hv-call" data-act="call" aria-label="Call ${n}" title="Call ${n}">${ICON.phone}</button>
        ${detail}
      </li>`;
  }).join("");
  const empty = $("contactsEmpty");
  empty.hidden = shown.length > 0;
  empty.textContent = list.length ? `No contacts match “${ui.q.trim()}”.`
    : "No contacts yet. Add one here, or with the person button on a call in Recent.";
}
