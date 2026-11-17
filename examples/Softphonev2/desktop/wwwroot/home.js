// Home: the stage when there's no call, from the devices mock (prototype/devices), and the dial
// view it turns into while you place a call.
//
// Home: at the left, Recent: the last 8 calls, each with Call and Video call, missed ones in red
// and, until you open People or next place a call, bold with an "N missed" pill; All history opens
// People. At the right, Status: Accounts (each account's registration; one that fails says why,
// with Retry and Edit; the address direct calls reach you at), Your devices (yourdevices.js), and
// while it's on, a Privacy mode card with Turn off. A call that ends goes to the top of Recent,
// highlighted for a moment, once its "Call ended" card has gone.
//
// The dial view: focusing the address field (with no call on the stage) replaces Home with one
// column: Your devices at the top, then New call, all your contacts and the other addresses from
// recent calls, which stays put while you type (typing searches in the field's drop-down, as
// before). A pick from either fills the address field and Call from, and calls no one: Call or
// Video call places the call. Each row has its own Call and Video call too, as Recent's do, to
// call it straight away. Esc closes the drop-down, then clears the field (putting Call from
// back), then goes back to Home, as do Back to Home and leaving the field empty.

import { accountAor, accountList, accountStatus, accountUri, DIRECT, openAccounts, reRegister, useAccount } from "./accounts.js";
import { fieldText, fieldUri, dial, looksFull, offDomain, shownAddress } from "./address.js";
import { placeCall, privacyOn, render, state, syncCallBtn, togglePrivacy } from "./app.js";
import { avatarAttr } from "./avatar.js";
import { contactMark, normAddress } from "./contactmark.js";
import { allContacts, contactFor, openContacts, sameUri } from "./contacts.js";
import { copyAddress, describeCall, historyEntries, isUnseen, openPeople, unseenMissed } from "./history.js";
import { $, escHtml, ICON, initial, post, shortName } from "./util.js";
import { closeDevices, renderYourDevices } from "./yourdevices.js";

const RECENT_ON_HOME = 8;
const RECENT_TO_PICK = 20;   // New call: recent addresses after the contacts

export let dialing = false;     // the dial view: you're placing a call (the address field has focus or text)
let fromBefore;                 // Call from before a pick changed it (undefined: not changed), for Esc
let fresh = { id: null, until: 0 };   // the call just added to Recent, highlighted for a moment
let topId;                      // the newest history entry last seen

const looksEmpty = () => !$("address").value.trim();
/** Home (or the dial view) is on the stage: no call there, nor Accounts or People over it. */
export const homeShown = () => state.call === "idle";
const dialShown = () => homeShown() && (dialing || !looksEmpty());

/** The address field took focus: with no call on the stage, the dial view. */
function enterDial() {
  if (dialing || !homeShown()) return;
  dialing = true;
  render();
}
/** Back to Home: the field emptied, Call from as it was, the tests stopped. */
export function leaveDial() {
  if (!dialing && looksEmpty()) return;
  dialing = false;
  if (!looksEmpty()) { $("address").value = ""; dial.pick = null; dial.renderBadge(); syncCallBtn(); }
  restoreFrom();
  dial.close();
  closeDevices();
  render();
}
// A full SIP address typed (or pasted) with an account, on another domain, is called directly: Call
// from switches to No account (direct) as it becomes one, and back to the account if you take it
// out again (or Esc or × empties the field). Only as it changes: picking an account in Call from
// afterwards is yours to keep. A row picked from a list keeps the account it came with. Desktop
// only: the web softphone has no direct calls (address.js, webDirectNote).
let autoDirect = false;   // Call from was switched by followTyped
let wasFull = false;      // the field held a full address at the last change
export function followTyped() {
  if (!Array.isArray(state.accounts)) return;
  const text = $("address").value.trim();
  const picked = dial.pick && (fieldText(dial.pick.uri) === text);
  const full = looksFull(text) && !picked;
  if (full && !wasFull && state.account && offDomain(text)) {
    if (fromBefore === undefined) fromBefore = state.accountId ?? null;
    useAccount(DIRECT, null);
    autoDirect = true;
  } else if (!full && wasFull && autoDirect) {
    autoDirect = false;
    restoreFrom();
  }
  wasFull = full;
}

/** A call placed: the dial view's done with (the field is the call's to clear, as before). */
export function dialDone() {
  dialing = false;
  autoDirect = false;
  fromBefore = undefined;
  closeDevices();
}
// Esc (or ×) emptied the field: Call from goes back to what it was before a pick changed it.
export function restoreFrom() {
  autoDirect = false;
  if (fromBefore === undefined) return;
  const was = fromBefore;
  fromBefore = undefined;
  if (Array.isArray(state.accounts) && (state.accountId ?? null) !== was) post({ type: "account-select", id: was });
}

/** A pick, from New call or the drop-down: its address in the field, its account in Call from. */
export function fill(it) {
  if (it.account && Array.isArray(state.accounts)) {
    if (fromBefore === undefined) fromBefore = state.accountId ?? null;
    useAccount(it.account, it.name);
  }
  $("address").value = fieldText(it.uri);
  dial.pick = { uri: it.uri, name: it.name ?? null, account: it.account ?? null };
  dial.close();
  dial.renderBadge();
  syncCallBtn();
  $("address").focus();
  const wrap = $("address").closest(".dest-wrap");
  wrap.classList.remove("filled");
  void wrap.offsetWidth;
  wrap.classList.add("filled");
  render();
}

// A row's Copy address, Call and Video call (Recent's, and New call's): the calls are placed
// straight away; Copy puts the address on the clipboard, to call it some other way. Recent's rows
// also have Edit contact (Add to contacts, if they aren't one), as in History: the Contacts form,
// then back to Home.
const CONTACT_ICON = {
  edit: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
  add: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/></svg>',
};
function callBtns(uri, account, name, contactBtn = null) {
  const btn = (act, label, icon, quiet) => `<button type="button" class="icon-sq${quiet ? " icon-sq-quiet" : ""}" data-home="${act}" data-uri="${escHtml(uri)}" data-account="${escHtml(account ?? "")}"
      title="${label}" aria-label="${label}">${icon}</button>`;
  const contact = contactBtn === null ? ""
    : contactBtn ? btn("contact", `Edit contact ${escHtml(name)}`, CONTACT_ICON.edit, true)
    : btn("contact", `Add ${escHtml(name)} to contacts`, CONTACT_ICON.add, true);
  return `<span class="home-btns">${btn("copy", `Copy ${escHtml(name)}'s address`, ICON.copy, true)}${contact}${btn("call", `Call ${escHtml(name)}`, ICON.phone)}${state.videoAllowed !== false ? btn("video", `Video call ${escHtml(name)}`, ICON.video) : ""}</span>`;
}

// ---- Recent (Home) ----
function recentHtml() {
  const h = historyEntries();
  const missed = unseenMissed();
  const head = `<div class="home-head"><h2>Recent</h2>${missed ? `<span class="missed-pill">${missed} missed</span>` : ""}
    ${Array.isArray(h) ? `<button type="button" class="link-btn" data-home="history">All history</button>` : ""}</div>`;
  if (!Array.isArray(h) || !h.length) {
    return `${head}<p class="home-empty">${Array.isArray(h) ? "No calls yet. Type a name or number above to make one." : "Type a name or number above to make a call."}</p>`;
  }
  const rows = h.slice(0, RECENT_ON_HOME).map((e) => {
    const c = contactFor(e.uri);
    const name = c?.name || e.name || shortName(e.uri);
    const d = describeCall(e);
    const cls = `home-row${e.dir === "missed" ? " missed" : ""}${isUnseen(e) ? " unseen" : ""}${e.id === fresh.id && Date.now() < fresh.until ? " fresh" : ""}`;
    return `<li class="${cls}"><span class="home-avatar" aria-hidden="true"${avatarAttr(e.uri)}>${escHtml(initial(name))}</span>
      <span class="home-main"><span class="home-name">${escHtml(name)}${c ? contactMark(c.name) : ""}</span>
        <span class="home-sub ${d.tone}">${escHtml(d.text)} · ${escHtml(d.when)}</span></span>
      ${callBtns(e.uri, e.account, name, !!c)}</li>`;
  }).join("");
  return `${head}<ol class="home-rows">${rows}</ol>`;
}

// ---- Status: Accounts, and privacy mode ----
// Each account's Copy address, and Edit (straight to its form in Accounts); this device's address
// (direct calls) has Copy too.
const copyBtn = (text, what) => `<button type="button" class="icon-sq icon-sq-small icon-sq-quiet" data-home="copy" data-uri="${escHtml(text)}"
    title="Copy ${what}" aria-label="Copy ${what}">${ICON.copy}</button>`;
const editBtn = (a) => `<button type="button" class="icon-sq icon-sq-small icon-sq-quiet" data-home="edit" data-id="${escHtml(a.id)}"
    title="Edit ${escHtml(a.name)}" aria-label="Edit ${escHtml(a.name)}">${ICON.edit}</button>`;
function accountsHtml() {
  const list = accountList();
  const direct = state.localAddress ? `<p class="home-foot home-local"><span>Direct calls reach you at <b>${escHtml(state.localAddress)}</b></span>${copyBtn(state.localAddress, "this device's address")}</p>` : "";
  if (Array.isArray(list)) {
    const shown = list.filter((a) => a.enabled);
    const rows = shown.map((a) => {
      const st = accountStatus(a);
      const failed = a.reg === "failed";
      return `<div class="ha ${st.tone}"><span class="ha-dot"></span><div class="ha-main">
        <div class="ha-name"><b>${escHtml(a.name)}</b> <span class="home-dim">${escHtml(accountAor(a))}</span></div>
        <div class="ha-state">${escHtml(st.short)}</div>
        ${failed ? `<div class="ha-why">${escHtml(a.regDetail || "No reason given")}</div>
          <div class="ha-btns"><button type="button" class="btn btn-small" data-home="retry" data-id="${escHtml(a.id)}">Retry</button></div>` : ""}
      </div><span class="ha-icons">${copyBtn(accountUri(a), `${escHtml(a.name)}'s address`)}${editBtn(a)}</span></div>`;
    }).join("");
    return `<section class="scard"><div class="home-head"><h3>Accounts</h3><button type="button" class="link-btn" data-home="add">Add</button></div>
      ${rows || `<p class="home-empty">No accounts: direct calls only.</p>`}${direct}</section>`;
  }
  // The web softphone: its one account, signed in from its own chip.
  if (!state.account) return "";
  const reg = state.registration;
  const tone = reg === "registered" ? "ok" : reg === "failed" ? "bad" : reg === "none" ? "off" : "warn";
  const text = reg === "registered" ? "Registered" : reg === "failed" ? "Not registered" : reg === "none" ? "Calls out only" : "Registering…";
  return `<section class="scard"><div class="home-head"><h3>Account</h3></div>
    <div class="ha ${tone}"><span class="ha-dot"></span><div class="ha-main">
      <div class="ha-name"><b>${escHtml(state.account)}</b></div><div class="ha-state">${text}</div>
      ${reg === "failed" && state.registrationDetail ? `<div class="ha-why">${escHtml(state.registrationDetail)}</div>` : ""}
    </div><span class="ha-icons">${copyBtn(state.account, "your address")}</span></div></section>`;
}
const privacyHtml = () => (privacyOn() ? `<section class="scard privacy-card">${ICON.incognito}<div><b>Privacy mode is on</b>
  <p>Your microphone and camera are off on every call: they hear silence and see your camera-off card.</p>
  <button type="button" class="btn btn-small" data-home="privacy">Turn off</button></div></section>` : "");

// ---- New call (the dial view) ----
// All your contacts (by name), then the other addresses from recent calls, newest first.
function pickItems() {
  const h = historyEntries() ?? [];
  const last = new Map();
  for (const e of h) { const k = normAddress(e.uri); if (!last.has(k)) last.set(k, e); }
  const items = [...allContacts()].sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }))
    .map((c) => ({ group: "Contacts", name: c.name, uri: c.uri, contact: true, account: c.account ?? last.get(normAddress(c.uri))?.account ?? null, last: last.get(normAddress(c.uri)) }));
  const seen = new Set(items.map((x) => normAddress(x.uri)));
  for (const e of h) {
    const k = normAddress(e.uri);
    if (seen.has(k)) continue;
    seen.add(k);
    items.push({ group: "Recent", name: e.name || shortName(e.uri), uri: e.uri, contact: false, account: e.account ?? null, last: e });
    if (items.filter((x) => x.group === "Recent").length >= RECENT_TO_PICK) break;
  }
  return items;
}
let picks = [];
function newCallHtml() {
  picks = pickItems();
  const typed = $("address").value.trim();
  const inBar = (it) => typed && (sameUri(it.uri, fieldUri(typed)) || fieldText(it.uri) === typed);
  let group = "";
  const rows = picks.map((it, i) => {
    const head = it.group !== group ? `<li class="home-group" role="presentation">${(group = it.group)}</li>` : "";
    const used = inBar(it);
    const sub = [shownAddress(it.uri), it.last ? `${describeCall(it.last).text} · ${describeCall(it.last).when}` : ""].filter(Boolean).join(" · ");
    return `${head}<li class="home-row pickable${used ? " used" : ""}" data-pick="${i}" title="${used ? "In the address bar" : `Fill in ${escHtml(shownAddress(it.uri))}`}">
      <span class="home-avatar" aria-hidden="true"${avatarAttr(it.uri)}>${escHtml(initial(it.name))}</span>
      <span class="home-main"><span class="home-name">${escHtml(it.name)}${it.contact ? contactMark(it.name) : ""}</span><span class="home-sub">${escHtml(sub)}</span></span>
      ${used ? `<span class="home-note">${ICON.check} In the address bar</span>` : `<span class="home-hint" aria-hidden="true">Fill in</span>`}
      ${callBtns(it.uri, it.account, it.name)}</li>`;
  }).join("");
  return `<div class="home-head"><h2>New call</h2><span class="home-dim">Contacts and recent calls</span>
      <button type="button" class="link-btn" data-home="back">Back to Home</button></div>
    ${picks.length ? `<ol class="home-rows">${rows}</ol>` : `<p class="home-empty">No contacts or recent calls yet: type a name or number above.</p>`}
    <p class="home-foot">Type to search, or pick one here: it fills the address bar${Array.isArray(state.accounts) ? " and Call from" : ""}, and Call or Video call places the call. A row's own buttons call it straight away.</p>`;
}

const setHtml = (el, html) => { if (el.dataset.html !== html) { el.innerHTML = html; el.dataset.html = html; } };

/** Draws Home or the dial view while the stage has no call; hides it otherwise. From app.js render(). */
export function renderHome() {
  const home = $("home");
  const shown = homeShown();
  home.hidden = !shown;
  if (!shown) return;
  const dialView = dialShown();
  home.classList.toggle("dialing", dialView);
  home.setAttribute("aria-label", dialView ? "New call" : "Home");
  setHtml($("homeLeft"), dialView ? newCallHtml() : recentHtml());
  setHtml($("homeAccounts"), dialView ? "" : accountsHtml());
  setHtml($("homePrivacy"), dialView ? "" : privacyHtml());
  renderYourDevices($("ydHome"), dialView ? "dial" : "home");
}

// Run by app.js once the page is up.
export function initHome() {
  const input = $("address");
  input.addEventListener("focus", enterDial);
  // With the field empty, clicking or tabbing to something else goes back to Home: anything but
  // the dial bar (the address, Call from, Call), the dial view itself (Your devices' pickers and
  // buttons, New call) and what opens over them (the indicator's popover, a ▾ menu, a dialog).
  // Focus merely dropping to the page doesn't count: Your devices redraws itself after a click
  // or a pick, which takes the focused control away, and that's still the dial view.
  const inside = (el) => !!el?.closest?.("#home, #dialForm, #devInd, #devPop, .device-menu, dialog");
  const leaveFor = (el) => { if (dialing && looksEmpty() && el && el !== document.body && !inside(el)) leaveDial(); };
  document.addEventListener("pointerdown", (e) => leaveFor(e.target), true);
  document.addEventListener("focusin", (e) => leaveFor(e.target));
  // New call's rows keep focus in the field, as the drop-down's do.
  $("home").addEventListener("pointerdown", (e) => { if (e.target.closest("[data-pick]")) e.preventDefault(); });
  $("home").addEventListener("click", (e) => {
    const t = e.target.closest("[data-home]");
    const row = !t && e.target.closest("[data-pick]");
    if (row) { const it = picks[Number(row.dataset.pick)]; if (it) fill(it); return; }
    if (!t) return;
    switch (t.dataset.home) {
      case "call": case "video": {
        // New call's rows: the pick's own account (a contact's, or the one it was last called from).
        const it = t.closest("[data-pick]") ? picks[Number(t.closest("[data-pick]").dataset.pick)] : null;
        const c = contactFor(t.dataset.uri);
        placeCall(t.dataset.uri, it ? it.account : c ? c.account : t.dataset.account || null, it?.name ?? c?.name ?? null, t.dataset.home === "video");
        break;
      }
      case "copy": copyAddress(t.dataset.uri); break;
      case "contact": {
        // Edit contact, or Add to contacts with the name the row shows (the call's, else one from
        // the address) and the call's account (its Call from).
        const e = historyEntries()?.find((x) => x.uri === t.dataset.uri);
        openContacts(contactFor(t.dataset.uri) ?? { name: e?.name || shortName(t.dataset.uri), uri: t.dataset.uri, account: t.dataset.account || null }, "home");
        break;
      }
      case "history": openPeople("recent"); break;
      case "back": leaveDial(); break;
      case "retry": reRegister(t.dataset.id); break;
      case "edit": openAccounts(t.dataset.id); break;
      case "add": openAccounts("new"); break;
      case "privacy": togglePrivacy(); break;
    }
  });
  // A call that ends goes to the top of Recent, highlighted for a moment.
  document.addEventListener("history-changed", () => {
    const top = historyEntries()?.[0]?.id ?? null;
    if (topId !== undefined && top && top !== topId) fresh = { id: top, until: Date.now() + 3000 };
    topId = top;
    render();
  });
  document.addEventListener("contacts-changed", () => render());
  setInterval(() => { if (homeShown()) render(); }, 30_000);   // "5 min ago" moves on
}
