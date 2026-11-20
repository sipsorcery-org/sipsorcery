// Home: the stage when there's no call, from the devices mock (prototype/devices).
//
// At the left, New call, then Recent. New call is the address bar's list (address.js), drawn
// here instead of in a drop-down: with nothing typed, your most-called contacts as tiles, and
// Contacts…; as you type, the contacts and other addresses that match, as rows. ↑/↓ in the field
// move through it, Enter (or a click) fills the address field and Call from with the one picked,
// and calls no one: Call or Video call places the call. Each row has its own Call and Video call
// too, to call it straight away. Esc lets go of the highlight, then clears the field (putting Call
// from back), then leaves the field. Recent: the last few calls, each with Call and Video call,
// missed ones in red and, until you open People or next place a call, bold with an "N missed"
// pill; All history opens People. A call that ends goes to the top of Recent, highlighted for a
// moment, once its "Call ended" card has gone. Both fit the usual window without it scrolling.
//
// At the right, Status: Accounts (each account's registration; one that fails says why, with Retry
// and Edit; the address direct calls reach you at), Your devices (yourdevices.js; while the field
// has focus or text, it says what the call you're placing will do with each), and while it's on, a
// Privacy mode card with Turn off.

import { accountAor, accountList, accountStatus, accountUri, DIRECT, openAccounts, reRegister, useAccount } from "./accounts.js";
import { fieldText, fieldUri, dial, looksFull, offDomain, shownAddress, suggestions } from "./address.js";
import { placeCall, privacyOn, render, state, syncCallBtn, togglePrivacy } from "./app.js";
import { avatarAttr } from "./avatar.js";
import { contactMark } from "./contactmark.js";
import { contactFor, openContacts, sameUri } from "./contacts.js";
import { copyAddress, describeCall, historyEntries, isUnseen, markSeen, openPeople, renderSide, unseenMissed } from "./history.js";
import { openSnapshot } from "./snapshots.js";
import { $, escHtml, ICON, initial, post, shortName } from "./util.js";
import { closeDevices, renderYourDevices } from "./yourdevices.js";

const RECENT_ON_HOME = 5;      // with New call above, so both fit the usual window

export let dialing = false;     // you're placing a call: the address field has focus or text
let fromBefore;                 // Call from before a pick changed it (undefined: not changed), for Esc
let fresh = { id: null, until: 0 };   // the call just added to Recent, highlighted for a moment
let topId;                      // the newest history entry last seen

const looksEmpty = () => !$("address").value.trim();
/** Home is on the stage: no call there, nor Accounts or People over it. */
export const homeShown = () => state.call === "idle";
const dialShown = () => homeShown() && (dialing || !looksEmpty());

/** The address field took focus: with no call on the stage, Your devices says what a call will do. */
function enterDial() {
  if (dialing || !homeShown()) return;
  dialing = true;
  render();
}
/** The field let go: emptied, Call from as it was, the tests stopped. */
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

/** A call placed: placing it is done with (the field is the call's to clear, as before). */
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
function callBtns(uri, account, name, contactBtn = null, diagId = null) {
  const btn = (act, label, icon, quiet) => `<button type="button" class="icon-sq${quiet ? " icon-sq-quiet" : ""}" data-home="${act}" data-uri="${escHtml(uri)}" data-account="${escHtml(account ?? "")}"
      title="${label}" aria-label="${label}">${icon}</button>`;
  const contact = contactBtn === null ? ""
    : contactBtn ? btn("contact", `Edit contact ${escHtml(name)}`, CONTACT_ICON.edit, true)
    : btn("contact", `Add ${escHtml(name)} to contacts`, CONTACT_ICON.add, true);
  // A call's saved diagnostics (snapshots.js): its Call details, Streams and Call quality as it ended.
  const diag = diagId ? `<button type="button" class="icon-sq icon-sq-quiet" data-home="diag" data-id="${escHtml(diagId)}" data-name="${escHtml(name)}"
      title="Diagnostics for this call" aria-label="Diagnostics for the call with ${escHtml(name)}">${ICON.diag}</button>` : "";
  return `<span class="home-btns">${diag}${btn("copy", `Copy ${escHtml(name)}'s address`, ICON.copy, true)}${contact}${btn("call", `Call ${escHtml(name)}`, ICON.phone)}${state.videoAllowed !== false ? btn("video", `Video call ${escHtml(name)}`, ICON.video) : ""}</span>`;
}

// ---- Recent (Home) ----
function recentHtml() {
  const h = historyEntries();
  const missed = unseenMissed();
  const head = `<div class="home-head"><h2>Recent</h2>${missed ? `<span class="missed-pill">${missed} missed<button type="button" class="missed-x" data-home="seen"
      aria-label="Mark ${missed === 1 ? "it" : "them"} as seen" title="Mark ${missed === 1 ? "it" : "them"} as seen: clears this and the count on People">${ICON.close}</button></span>` : ""}
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
      ${callBtns(e.uri, e.account, name, !!c, e.diag ? e.id : null)}</li>`;
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

// ---- New call (top of Home) ----
// The address bar's list (address.js suggestions), drawn here rather than in a drop-down, so ↑/↓,
// Enter and Esc in the field work on it. Nothing typed: your most-called contacts, as a strip of
// tiles, and Contacts…; typed, the contacts and other addresses that match, as rows with their own
// Call and Video call, and Add … to contacts for an address that isn't one. A pick fills the
// address field and Call from, and calls no one.
let picks = [];
const FOOT_ICON = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/></svg>';
function newCallHtml() {
  const typed = $("address").value.trim();
  picks = dial?.open ? dial.items : suggestions(typed, { foot: true, emptyRecent: false });
  const active = dial?.open ? dial.active : -1;
  const inBar = (it) => typed && (sameUri(it.uri, fieldUri(typed)) || fieldText(it.uri) === typed);
  const opt = (i) => `id="sug-${i}" role="option" aria-selected="${i === active}" data-pick="${i}"`;
  const footText = (it) => (it.action === "add" ? `Add <b>${escHtml(shownAddress(it.uri))}</b> to contacts…` : "Contacts…");
  let body;
  if (!typed) {
    const tiles = picks.map((it, i) => (it.action
      ? `<li class="nc-tile nc-more${i === active ? " active" : ""}" ${opt(i)} title="Open your contacts">
          <span class="home-avatar nc-more-icon" aria-hidden="true">${FOOT_ICON}</span><span class="nc-name">${footText(it)}</span></li>`
      : `<li class="nc-tile${i === active ? " active" : ""}" ${opt(i)} title="Fill in ${escHtml(it.name)} (${escHtml(shownAddress(it.uri))})">
          <span class="home-avatar" aria-hidden="true"${avatarAttr(it.uri)}>${escHtml(initial(it.name))}</span>
          <span class="nc-name">${escHtml(it.name.split(/\s+/)[0])}</span></li>`)).join("");
    const none = picks.some((it) => !it.action) ? "" : `<li class="nc-none home-empty" role="presentation">No contacts yet: type a name, number or SIP address above.</li>`;
    body = `<ol class="nc-tiles" id="homePicks" role="listbox" aria-label="Contacts">${none}${tiles}</ol>`;
  } else {
    let group = "";
    const rows = picks.map((it, i) => {
      if (it.action) {
        return `<li class="home-row pickable nc-foot${i === active ? " active" : ""}" ${opt(i)}>
          <span class="home-avatar nc-more-icon" aria-hidden="true">${FOOT_ICON}</span><span class="home-main"><span class="home-name">${footText(it)}</span></span></li>`;
      }
      const head = it.group !== group ? `<li class="home-group" role="presentation">${(group = it.group)}</li>` : "";
      const used = inBar(it);
      const last = it.at ? describeCall(it) : null;
      const sub = [shownAddress(it.uri), last ? `${last.text} · ${last.when}` : ""].filter(Boolean).join(" · ");
      return `${head}<li class="home-row pickable${used ? " used" : ""}${i === active ? " active" : ""}" ${opt(i)} title="${used ? "In the address bar" : `Fill in ${escHtml(shownAddress(it.uri))}`}">
        <span class="home-avatar" aria-hidden="true"${avatarAttr(it.uri)}>${escHtml(initial(it.name))}</span>
        <span class="home-main"><span class="home-name">${escHtml(it.name)}${it.contact ? contactMark(it.name) : ""}</span><span class="home-sub">${escHtml(sub)}</span></span>
        ${used ? `<span class="home-note">${ICON.check} In the address bar</span>` : `<span class="home-hint" aria-hidden="true">Fill in</span>`}
        ${callBtns(it.uri, it.account, it.name)}</li>`;
    }).join("");
    body = picks.some((it) => !it.action)
      ? `<ol class="home-rows nc-rows" id="homePicks" role="listbox" aria-label="Contacts and recent calls">${rows}</ol>`
      : `<p class="home-empty">Nothing you've saved or called matches: Call or Enter calls <b>${escHtml(typed)}</b> as typed.</p>${rows ? `<ol class="home-rows nc-rows" id="homePicks" role="listbox" aria-label="Contacts">${rows}</ol>` : ""}`;
  }
  const hint = typed ? "Matching contacts and calls" : `Type in the bar above, or pick one to fill it${Array.isArray(state.accounts) ? " and Call from" : ""}`;
  return `<div class="home-head"><h2>New call</h2><span class="home-dim">${hint}</span></div>${body}`;
}

const setHtml = (el, html) => { if (el.dataset.html !== html) { el.innerHTML = html; el.dataset.html = html; } };

/** Draws Home while the stage has no call; hides it otherwise. From app.js render(). */
export function renderHome() {
  const home = $("home");
  const shown = homeShown();
  home.hidden = !shown;
  if (!shown) return;
  setHtml($("homeNew"), newCallHtml());
  setHtml($("homeLeft"), recentHtml());
  setHtml($("homeAccounts"), accountsHtml());
  setHtml($("homePrivacy"), privacyHtml());
  renderYourDevices($("ydHome"), dialShown() ? "dial" : "home");
  // The highlighted pick in view, as the drop-down's is (New call's rows scroll inside it).
  const on = dial?.open && dial.active >= 0 ? $(`sug-${dial.active}`) : null;
  if (on) { const box = on.closest(".nc-rows"); if (box) box.scrollTop = Math.min(Math.max(box.scrollTop, on.offsetTop + on.offsetHeight - box.clientHeight), on.offsetTop); }
}

// Run by app.js once the page is up.
export function initHome() {
  const input = $("address");
  input.addEventListener("focus", enterDial);
  // With the field empty, clicking or tabbing to something else lets go of it (Your devices back
  // to Home's, its tests stopped): anything but the dial bar (the address, Call from, Call), Home
  // itself (Your devices' pickers and buttons, New call) and what opens over them (the
  // indicator's popover, a ▾ menu, a dialog). Focus merely dropping to the page doesn't count:
  // Your devices redraws itself after a click or a pick, which takes the focused control away.
  const inside = (el) => !!el?.closest?.("#home, #dialForm, #devInd, #devPop, .device-menu, dialog");
  const leaveFor = (el) => { if (dialing && looksEmpty() && el && el !== document.body && !inside(el)) leaveDial(); };
  document.addEventListener("pointerdown", (e) => leaveFor(e.target), true);
  document.addEventListener("focusin", (e) => leaveFor(e.target));
  // New call's rows keep focus in the field, as the drop-down's do.
  $("home").addEventListener("pointerdown", (e) => { if (e.target.closest("[data-pick]")) e.preventDefault(); });
  $("home").addEventListener("click", (e) => {
    const t = e.target.closest("[data-home]");
    const row = !t && e.target.closest("[data-pick]");
    if (row) {
      const it = picks[Number(row.dataset.pick)];
      if (it?.action) { dial.close(); openContacts(it.action === "add" ? { name: "", uri: it.uri } : null); }
      else if (it) fill(it);
      return;
    }
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
      case "seen": markSeen(); renderSide(); renderHome(); break;
      case "diag": openSnapshot(t.dataset.id, t.dataset.name); break;
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
