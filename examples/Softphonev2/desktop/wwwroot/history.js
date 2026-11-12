// Call history, from the UX prototype (prototype/web, "Call history"). The host keeps it (on the
// desktop, history-<profile>.json beside the accounts; in the web softphone, this browser's
// storage) and sends all of it, newest first, whenever it changes:
//   history {entries: [{id, dir: out|in|missed, name, uri, account, accountName, at, duration,
//            note, sip, reason}], max}
// and hears history-remove {ids} and history-restore {ids} (Undo) from here. A host that sends no
// history shows none of this.
//
// Two places show it:
//  - the History section in the Active calls panel: the latest calls, each with a call-back
//    button, a red badge for missed calls you haven't looked at, collapsible (remembered), and
//    See all history. On the rail, a History button with the badge.
//  - the People view over the stage, with two tabs: Recent (this list) and Contacts (contacts.js
//    fills that tab, through api.mountContacts). Like Accounts, it's not a call, so looking at it holds nothing;
//    Back to … and × close it, and so does Esc. Search, All / Missed / Outgoing / Incoming, day
//    groups; a row (or its chevron) opens its details, with Call back, Copy address, Remove and
//    Remove all N from this address, and (there and as an icon on the row) Add to contacts or
//    Edit contact, which open the Contacts dialog. Clear history (Clear N shown, when filtered) asks first.
//    Every removal has an Undo toast, and removals while it's up are undone together.
// A call ringing on the stage takes over from the view; unanswered, it hands back.

import { contactMark } from "./contactmark.js";
import { avatarAttr } from "./avatar.js";

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const svg = (p) => `<svg viewBox="0 0 24 24" aria-hidden="true">${p}</svg>`;
const PHONE = '<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/>';
const ICON = {
  phone: svg(PHONE),
  missed: svg(`<path d="m16 2 6 6"/><path d="m22 2-6 6"/>${PHONE}`),
  outgoing: svg('<path d="M7 7h10v10"/><path d="M7 17 17 7"/>'),
  inbound: svg('<path d="M17 7 7 17"/><path d="M17 17H7V7"/>'),
  history: svg('<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>'),
  search: svg('<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>'),
  trash: svg('<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6M14 11v6"/>'),
  copy: svg('<rect x="8" y="8" width="14" height="14" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>'),
  chevronDown: svg('<path d="m6 9 6 6 6-6"/>'),
  caret: svg('<path d="m6 9 6 6 6-6"/>'),
  close: svg('<path d="M18 6 6 18M6 6l12 12"/>'),
  back: svg('<path d="M19 12H5M12 19l-7-7 7-7"/>'),
  addContact: svg('<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/>'),
  contact: svg('<path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>'),
  people: svg('<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8"/>'),
};
const SIDE_COUNT = 6;          // calls in the sidebar section
const UNDO_MS = 10000;         // how long the Undo toast stays
const D = 24 * 3600_000;
const FILTERS = [["all", "All"], ["missed", "Missed"], ["out", "Outgoing"], ["in", "Incoming"]];

let api = null;
let entries = null;            // from the host; null until it sends some (never, if it keeps none)
let maxEntries = 500;
// The view: whether it's open, and (open but hidden) whether a ringing call took it over.
const view = { open: false, takenOver: false, tab: "recent", filter: "all", q: "", selectedId: null };
const TABS = [["recent", "Recent"], ["contacts", "Contacts"]];
let undo = { ids: [], timer: 0 };
const load = (k, d) => { try { return localStorage.getItem(k) ?? d; } catch { return d; } };
const store = (k, v) => { try { localStorage.setItem(k, v); } catch { /* storage unavailable */ } };
let seenAt = Number(load("historySeenAt", "0")) || 0;   // missed calls after this are "new"
let collapsed = load("historyCollapsed", "no") === "yes";

/** The history, newest first, or null if the host keeps none. */
export const historyEntries = () => entries;
export const historyOpen = () => view.open && !$("historyView").hidden;

// A contact's name wins over the one the call carried.
const contactOf = (e) => api.contactFor?.(e.uri);
// The saved-contact mark (contactmark.js) after a contact's name.
const markOf = (e) => { const c = contactOf(e); return c ? contactMark(c.name) : ""; };
const nameOf = (e) => contactOf(e)?.name || e.name || api.shortName(e.uri);
const initial = (name) => (name ?? "?").replace(/^\+/, "").charAt(0).toUpperCase() || "#";
const unseenMissed = () => (entries ?? []).filter((e) => e.dir === "missed" && e.at > seenAt).length;

function fmtDuration(ms) {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = String(s % 60).padStart(2, "0");
  return h ? `${h}:${String(m).padStart(2, "0")}:${sec}` : `${m}:${sec}`;
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
const clockTime = (at) => new Date(at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
function dayLabel(at) {
  const startOf = (x) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const d = new Date(at);
  const days = Math.round((startOf(new Date()) - startOf(d)) / D);
  if (days === 0) return "Today";
  if (days === 1) return "Yesterday";
  if (days < 7) return d.toLocaleDateString([], { weekday: "long" });
  return d.toLocaleDateString([], { day: "numeric", month: "short", year: d.getFullYear() === new Date().getFullYear() ? undefined : "numeric" });
}

// "Outgoing · 1:35", "Busy", "Missed", … with its icon and colour.
function outcome(e) {
  const ico = e.dir === "missed" ? ICON.missed : e.dir === "in" ? ICON.inbound : ICON.outgoing;
  const text = e.dir === "missed" ? "Missed"
    : e.note ?? `${e.dir === "in" ? "Incoming" : "Outgoing"}${e.duration ? ` · ${fmtDuration(e.duration)}` : ""}`;
  return { ico, text, tone: e.dir === "missed" ? "bad" : e.note ? "warn" : "" };
}
/** A call as the lists show it: {ico, text, tone, when ("5 min ago"), at ("Today, 08:09")}. */
export const describeCall = (e) => ({ ...outcome(e), when: relTime(e.at), at: `${dayLabel(e.at)}, ${clockTime(e.at)}` });

export function initHistory(a) {
  api = a;
  $("histToggle").onclick = (ev) => {
    ev.stopPropagation();
    collapsed = !collapsed;
    store("historyCollapsed", collapsed ? "yes" : "no");
    renderSide();
  };
  // History (See all history, the rail's button) opens the view on Recent and People (the dial
  // bar) on Contacts. Each closes it again if it's on its own tab, and switches tab if it's on
  // the other.
  const onTab = (tab) => historyOpen() && view.tab === tab;
  const toggleRecent = (ev) => { ev.stopPropagation(); onTab("recent") ? closeHistory() : openHistory(); };
  $("seeAllHistory").onclick = toggleRecent;
  $("histRail").onclick = toggleRecent;
  $("peopleBtn").insertAdjacentHTML("afterbegin", ICON.people);
  $("peopleBtn").onclick = (ev) => { ev.stopPropagation(); onTab("contacts") ? closeHistory() : openPeople("contacts"); };
  $("histList").onclick = (ev) => {
    ev.stopPropagation();
    const btn = ev.target.closest("button[data-act]");
    if (!btn) return;
    if (btn.dataset.act === "call") callBack(byId(btn.dataset.id));
    else openHistory(btn.dataset.id);
  };
  $("historyView").onclick = onViewClick;
  // ←/→ on the tabs move between them.
  $("historyView").addEventListener("keydown", (ev) => {
    if (!ev.target.matches?.(".pv-tabs [role=tab]") || (ev.key !== "ArrowLeft" && ev.key !== "ArrowRight")) return;
    ev.preventDefault();
    const i = TABS.findIndex(([t]) => t === view.tab);
    switchTab(TABS[(i + (ev.key === "ArrowRight" ? 1 : TABS.length - 1)) % TABS.length][0], true);
  });
  $("historyView").oninput = (ev) => {
    if (ev.target.id !== "histSearch") return;
    view.q = ev.target.value;
    view.selectedId = null;
    renderResults();
  };
  $("histToastUndo").onclick = () => undoRemove();
  $("histToastClose").onclick = () => hideToast();
  // Esc: clears the search first, then closes the view. Not while a menu, dialog or the text
  // popover has it.
  document.addEventListener("keydown", (ev) => {
    if (ev.key !== "Escape" || !historyOpen() || ev.defaultPrevented) return;
    if (document.querySelector(".device-menu:not([hidden]), dialog[open]") || !($("rtt")?.hidden ?? true)) return;
    ev.preventDefault();
    if (ev.target.id === "histSearch" && view.q) { view.q = ""; ev.target.value = ""; renderResults(); return; }
    closeHistory();
  });
  // Relative times ("5 min ago") move on.
  setInterval(() => { if (entries) renderSide(); }, 30_000);
  // Contact names, and Add to contacts / Edit contact, follow the contacts.
  document.addEventListener("contacts-changed", () => {
    if (!entries) return;
    renderSide();
    renderResults();
    if ($("hvCount")) $("hvCount").textContent = subText();
  });
}

// history: the whole list again.
export function onHistoryMessage(msg) {
  const first = entries === null;
  entries = Array.isArray(msg.entries) ? msg.entries : [];
  maxEntries = msg.max ?? maxEntries;
  // Looking at the list counts as seeing what's new on it.
  if (historyOpen()) markSeen();
  if (view.selectedId && !byId(view.selectedId)) view.selectedId = null;
  $("histSide").hidden = false;
  $("peopleBtn").hidden = false;
  renderSide();
  if (historyOpen()) renderResults();
  if (first) api.log(`call history: ${entries.length} call${entries.length === 1 ? "" : "s"}`);
  document.dispatchEvent(new CustomEvent("history-changed"));
}

// Every state message (state.call is the selected call's phase). A call ringing on the stage
// takes over from the view; answered, the view closes; unanswered, it comes back.
export function onHistoryState(state) {
  if (!entries) return;
  const ringing = state.call === "ringing";
  if (ringing && historyOpen()) {
    $("historyView").hidden = true;
    view.takenOver = true;
  } else if (!ringing && view.takenOver) {
    view.takenOver = false;
    if (state.call === "connected") closeHistory(false);
    else if (view.open) { $("historyView").hidden = false; markSeen(); renderView(); }
  }
  renderSide();
}

const byId = (id) => entries?.find((e) => e.id === id);

function markSeen() {
  const newest = Math.max(Date.now(), ...(entries ?? []).map((e) => e.at));
  if (newest <= seenAt) return;
  seenAt = newest;
  store("historySeenAt", String(seenAt));
}

export function openHistory(id = null) {
  if (!entries) return;
  api.closeAccounts();
  Object.assign(view, { open: true, takenOver: false, tab: "recent" });
  if (id) Object.assign(view, { selectedId: id, filter: "all", q: "" });   // so the chosen call shows
  $("historyView").hidden = false;
  markSeen();
  renderView();
  renderSide();
  if (id) $("historyView").querySelector(`.hv-row[data-id="${CSS.escape(id)}"]`)?.scrollIntoView({ block: "center" });
  else $("histSearch")?.focus();
}

/**
 * Opens the People view on a tab ("recent" or "contacts"), or switches to it. q: a search for
 * Recent (all calls). Returns false if there's no view (a host that keeps no history).
 */
export function openPeople(tab, { q = null, focus = true } = {}) {
  if (!entries) return false;
  if (!view.open || $("historyView").hidden) {
    api.closeAccounts();
    Object.assign(view, { open: true, takenOver: false });
    $("historyView").hidden = false;
  }
  view.tab = tab;
  if (q != null) Object.assign(view, { q, filter: "all", selectedId: null });
  if (tab === "recent") markSeen();
  renderView();
  renderSide();
  if (focus) (tab === "recent" ? $("histSearch") : $("contactsSearch"))?.focus();
  return true;
}

function switchTab(tab, focusTab = false) {
  if (tab === view.tab) return;
  openPeople(tab, { focus: !focusTab });
  if (focusTab) $("historyView").querySelector(`.pv-tabs [data-tab="${tab}"]`)?.focus();
}

export function closeHistory(focus = true) {
  if (!view.open) return;
  Object.assign(view, { open: false, takenOver: false, selectedId: null });
  $("historyView").hidden = true;
  $("historyView").replaceChildren();
  $("histConfirm")?.close();
  renderSide();
  if (focus) $("seeAllHistory").focus({ preventScroll: true });
}

// The view stays open until the call's answered (app.js onPhase closes it then).
function callBack(e) {
  if (!e) return;
  api.call(e.uri, e.account);
}

// ---- The section in the Active calls panel ----

export function renderSide() {
  if (!entries) return;
  const n = unseenMissed();
  const label = n ? `${n} new missed call${n === 1 ? "" : "s"}` : "";
  for (const id of ["missedCount", "histRailCount", "peopleCount", "callsTabMissed"]) {
    $(id).textContent = n || "";
    $(id).title = label;
    $(id).setAttribute("aria-label", label);
  }
  const t = $("histToggle");
  t.setAttribute("aria-expanded", String(!collapsed));
  t.title = collapsed ? "Show recent calls" : "Hide recent calls";
  if (!t.querySelector("svg")) t.insertAdjacentHTML("afterbegin", ICON.caret);
  $("histBody").hidden = collapsed;
  const open = historyOpen();
  const html = entries.slice(0, SIDE_COUNT).map((e) => {
    const o = outcome(e), name = nameOf(e);
    const sel = open && view.selectedId === e.id;
    return `<li class="hs-item">
        <button type="button" class="hs-line${sel ? " selected" : ""}" data-act="open" data-id="${esc(e.id)}" title="${esc(`${e.uri} · details`)}">
          <span class="call-avatar" aria-hidden="true"${avatarAttr(e.uri)}>${esc(initial(name))}</span>
          <span class="hs-main"><span class="hs-name">${esc(name)}${markOf(e)}</span>
            <span class="hs-status ${o.tone}">${o.ico}<span>${esc(e.dir === "missed" ? "Missed · " : e.note ? `${e.note} · ` : "")}${esc(relTime(e.at))}</span></span></span>
        </button>
        <button type="button" class="hs-call" data-act="call" data-id="${esc(e.id)}" aria-label="Call ${esc(name)}" title="Call ${esc(name)}">${ICON.phone}</button>
      </li>`;
  }).join("") || `<li class="call-empty">No calls yet</li>`;
  if ($("histList").dataset.html !== html) { $("histList").innerHTML = html; $("histList").dataset.html = html; }
  // Pressed: History's buttons while the view is on Recent, People's while it's on Contacts.
  const onRecent = open && view.tab === "recent";
  const onContacts = open && view.tab === "contacts";
  const all = $("seeAllHistory");
  all.textContent = `See all history${entries.length ? ` (${entries.length})` : ""}`;
  all.classList.toggle("on", onRecent);
  all.setAttribute("aria-pressed", String(onRecent));
  const rail = $("histRail");
  rail.classList.toggle("on", onRecent);
  rail.setAttribute("aria-pressed", String(onRecent));
  rail.title = `Call history${n ? ` · ${label}` : ""}`;
  const people = $("peopleBtn");
  people.classList.toggle("on", onContacts);
  people.setAttribute("aria-pressed", String(onContacts));
  people.title = `Contacts${n ? ` · ${label}` : ""}`;
  if (!rail.querySelector("svg")) rail.insertAdjacentHTML("afterbegin", ICON.history);
}

// ---- The History view ----

function matches() {
  const needle = view.q.trim().toLowerCase();
  return entries.filter((e) => (view.filter === "all" || e.dir === view.filter)
    && (!needle || nameOf(e).toLowerCase().includes(needle) || e.uri.toLowerCase().includes(needle)));
}
const filtered = () => view.filter !== "all" || view.q.trim() !== "";
const clearLabel = () => (filtered() ? `Clear ${matches().length} shown` : "Clear history");

function subText() {
  if (view.tab === "contacts") { const n = api.contactCount?.() ?? 0; return `${n} contact${n === 1 ? "" : "s"}`; }
  const missed = entries.filter((e) => e.dir === "missed").length;
  return `${entries.length} call${entries.length === 1 ? "" : "s"} · ${missed} missed`;
}

function renderView() {
  const root = $("historyView");
  const back = api.backTo();
  const top = `<div class="am-top">
      <div><h2 class="am-title">People</h2>
        <div class="am-sub" id="hvCount">${subText()}</div></div>
      <button type="button" class="am-close" data-act="close" aria-label="Close people" title="Close (Esc)">${ICON.close}</button>
    </div>
    ${back ? `<button type="button" class="am-back" data-act="close">${ICON.back}Back to ${esc(back)}</button>` : ""}
    <div class="pv-tabs" role="tablist" aria-label="People">
      ${TABS.map(([t, label]) => `<button type="button" role="tab" id="pvTab-${t}" data-act="tab" data-tab="${t}" aria-selected="${view.tab === t}" aria-controls="pvPanel" tabindex="${view.tab === t ? 0 : -1}" class="${view.tab === t ? "on" : ""}">${label}</button>`).join("")}
    </div>`;
  if (view.tab === "contacts") {
    root.innerHTML = `${top}<div class="pv-panel pv-contacts" id="pvPanel" role="tabpanel" aria-labelledby="pvTab-contacts"></div>`;
    api.mountContacts($("pvPanel"));
    return;
  }
  root.innerHTML = `${top}
    <div class="pv-panel" id="pvPanel" role="tabpanel" aria-labelledby="pvTab-recent">
    <div class="hv-toolbar">
      <label class="hv-search">${ICON.search}<input id="histSearch" type="search" placeholder="Search names and addresses" value="${esc(view.q)}" spellcheck="false" autocomplete="off" aria-label="Search call history"></label>
      <div class="hv-filters" role="group" aria-label="Show">
        ${FILTERS.map(([f, label]) => `<button type="button" class="${view.filter === f ? "on" : ""}" data-act="filter" data-filter="${f}" aria-pressed="${view.filter === f}">${label}</button>`).join("")}
      </div>
      <button type="button" class="btn am-small hv-clear" id="hvClear" data-act="clear">${ICON.trash}<span></span></button>
    </div>
    <div id="hvResults"></div>
    </div>
    <p class="hv-foot">The last ${maxEntries} calls are kept; older ones drop off.</p>`;
  renderResults();
}

// Just the list (and the counts and Clear label), so the search box keeps focus as you type.
function renderResults() {
  const root = $("historyView");
  if (!root || root.hidden || !$("hvResults")) return;
  $("hvCount").textContent = subText();
  const list = matches();
  const clear = $("hvClear");
  clear.querySelector("span").textContent = clearLabel();
  clear.disabled = !list.length;
  if (!list.length) {
    const what = { all: "calls", missed: "missed calls", out: "outgoing calls", in: "incoming calls" }[view.filter];
    $("hvResults").innerHTML = `<div class="hv-empty">${view.q.trim() ? `No ${what} match “${esc(view.q.trim())}”.` : view.filter === "all" ? "No calls yet. Calls you make and get show up here." : `No ${what}.`}</div>`;
    return;
  }
  let day = "";
  $("hvResults").innerHTML = list.map((e) => {
    const d = dayLabel(e.at);
    const head = d !== day ? `${day ? "</ol>" : ""}<h3 class="hv-day">${esc((day = d))}</h3><ol class="hv-list">` : "";
    const o = outcome(e), name = nameOf(e);
    const sel = e.id === view.selectedId;
    const same = sel ? entries.filter((x) => x.uri === e.uri).length : 0;
    const outcomeText = e.dir === "missed" ? "They hung up before you answered"
      : e.note ? (e.reason && e.reason !== e.note ? `${e.note}: ${e.reason}` : e.note)
      : e.reason ? `Completed · ${e.reason}` : "Completed";
    const detail = sel ? `<div class="hv-detail" id="hv-detail-${esc(e.id)}">
        <dl>
          <div><dt>Direction</dt><dd>${{ out: "Outgoing", in: "Incoming", missed: "Incoming, missed" }[e.dir]}</dd></div>
          <div><dt>Started</dt><dd>${esc(dayLabel(e.at))}, ${esc(clockTime(e.at))}</dd></div>
          <div><dt>Duration</dt><dd>${e.duration ? fmtDuration(e.duration) : "Didn't connect"}</dd></div>
          <div><dt>Account</dt><dd>${esc(e.accountName ?? "No account (direct)")}</dd></div>
          <div class="wide"><dt>Outcome</dt><dd>${esc(outcomeText)}${e.sip ? `<span class="hv-mono">SIP ${esc(e.sip)}</span>` : ""}</dd></div>
          <div class="wide"><dt>Address</dt><dd class="hv-mono-dd">${esc(e.uri)}</dd></div>
          ${contactOf(e) ? `<div class="wide"><dt>Contact</dt><dd class="hv-contact-dd">${markOf(e)}Saved contact: ${esc(contactOf(e).name)}</dd></div>` : ""}
        </dl>
        <div class="hv-actions">
          <button type="button" class="btn btn-primary am-small" data-act="call" data-id="${esc(e.id)}">${ICON.phone}Call back</button>
          <button type="button" class="btn am-small" data-act="copy" data-id="${esc(e.id)}">${ICON.copy}Copy address</button>
          <button type="button" class="btn am-small" data-act="contact" data-id="${esc(e.id)}">${contactOf(e) ? `${ICON.contact}Edit contact` : `${ICON.addContact}Add to contacts`}</button>
          <span class="am-spacer"></span>
          <button type="button" class="btn am-small" data-act="remove" data-id="${esc(e.id)}">${ICON.trash}Remove</button>
          ${same > 1 ? `<button type="button" class="btn am-small" data-act="remove-uri" data-id="${esc(e.id)}">${ICON.trash}Remove all ${same} from this address</button>` : ""}
        </div>
      </div>` : "";
    return `${head}<li class="hv-row${sel ? " selected" : ""}" data-id="${esc(e.id)}">
        <button type="button" class="hv-main" data-act="select" data-id="${esc(e.id)}" aria-expanded="${sel}"${sel ? ` aria-controls="hv-detail-${esc(e.id)}"` : ""}>
          <span class="call-avatar" aria-hidden="true"${avatarAttr(e.uri)}>${esc(initial(name))}</span>
          <span class="hv-who"><span class="hv-name">${esc(name)}${markOf(e)}</span><span class="hv-uri">${esc(e.uri)}</span></span>
          <span class="hv-outcome ${o.tone}">${o.ico}<span>${esc(o.text)}</span></span>
          <span class="hv-time">${esc(clockTime(e.at))}</span>
        </button>
        <button type="button" class="hv-icon hv-toggle" data-act="select" data-id="${esc(e.id)}" aria-expanded="${sel}"
          aria-label="${sel ? "Hide" : "Show"} details for ${esc(name)}" title="${sel ? "Hide details" : "Show details"}">${ICON.chevronDown}</button>
        ${contactOf(e)
          ? `<button type="button" class="hv-icon hv-contact is-contact" data-act="contact" data-id="${esc(e.id)}" aria-label="Edit contact ${esc(name)}" title="Edit contact">${ICON.contact}</button>`
          : `<button type="button" class="hv-icon hv-contact" data-act="contact" data-id="${esc(e.id)}" aria-label="Add ${esc(name)} to contacts" title="Add to contacts">${ICON.addContact}</button>`}
        <button type="button" class="hv-icon hv-call" data-act="call" data-id="${esc(e.id)}" aria-label="Call ${esc(name)}" title="Call ${esc(name)}">${ICON.phone}</button>
        ${detail}
      </li>`;
  }).join("") + "</ol>";
}

function onViewClick(ev) {
  const el = ev.target.closest("[data-act]");
  if (!el) return;
  const e = byId(el.dataset.id);
  switch (el.dataset.act) {
    case "close": closeHistory(); break;
    case "tab": switchTab(el.dataset.tab); break;
    case "filter": view.filter = el.dataset.filter; view.selectedId = null; renderView(); break;
    case "select":
      view.selectedId = view.selectedId === el.dataset.id ? null : el.dataset.id;
      renderResults();
      renderSide();
      $("historyView").querySelector(`.hv-row[data-id="${CSS.escape(el.dataset.id)}"] .hv-main`)?.focus({ preventScroll: true });
      break;
    case "call": callBack(e); break;
    case "contact": if (e) api.editContact(e); break;
    case "copy":
      if (!e) break;
      api.post({ type: "copy", text: e.uri });
      showToast(`Copied <b>${esc(e.uri)}</b>`, false, 2500);
      break;
    case "remove": if (e) remove([e.id], `Removed <b>${esc(nameOf(e))}</b> from history`); break;
    case "remove-uri": {
      if (!e) break;
      const ids = entries.filter((x) => x.uri === e.uri).map((x) => x.id);
      remove(ids, `Removed ${ids.length} calls from <b>${esc(nameOf(e))}</b>`);
      break;
    }
    case "clear": confirmClear(); break;
  }
}

// Clear asks first: everything, or only what the search and filter show.
function confirmClear() {
  const list = matches();
  if (!list.length) return;
  const n = list.length, what = n === 1 ? "1 call" : `${n} calls`;
  const kind = { all: "", missed: "missed ", out: "outgoing ", in: "incoming " }[view.filter];
  const q = view.q.trim();
  let dlg = $("histConfirm");
  if (!dlg) {
    dlg = Object.assign(document.createElement("dialog"), { id: "histConfirm", className: "hv-confirm" });
    document.body.append(dlg);
  }
  dlg.innerHTML = `<form method="dialog">
      <h2>${filtered() ? `Clear ${what}?` : "Clear call history?"}</h2>
      <p>${filtered() ? `This removes the ${n === 1 ? `1 ${kind}call` : `${n} ${kind}calls`}${q ? ` matching <b>“${esc(q)}”</b>` : ""}. Other calls stay in history.`
        : `This removes all ${what} from history.`} You can undo it for a few seconds afterwards.</p>
      <div class="hv-confirm-actions">
        <button class="btn am-small" value="cancel" autofocus>Cancel</button>
        <button class="btn btn-danger am-small" value="clear">${ICON.trash}Clear ${what}</button>
      </div>
    </form>`;
  dlg.onclose = () => {
    if (dlg.returnValue === "clear") remove(list.map((x) => x.id), `Cleared ${what} from history`);
    dlg.returnValue = "";
  };
  dlg.showModal();
}

// Removing: straight away here, and the host is told. Removals while the toast is up join its
// batch, so Undo puts them all back.
function remove(ids, text) {
  if (!ids.length) return;
  const set = new Set(ids);
  entries = entries.filter((x) => !set.has(x.id));
  if (set.has(view.selectedId)) view.selectedId = null;
  api.post({ type: "history-remove", ids });
  undo.ids = $("histToast").hidden || !undo.ids.length ? ids : [...undo.ids, ...ids];
  const n = undo.ids.length;
  showToast(n > ids.length ? `Removed ${n} calls from history` : text, true, UNDO_MS);
  api.log(`history: removed ${ids.length}`);
  renderSide();
  renderResults();
}

function undoRemove() {
  if (!undo.ids.length) return;
  const n = undo.ids.length;
  api.post({ type: "history-restore", ids: undo.ids });
  undo.ids = [];
  showToast(`Restored ${n === 1 ? "1 call" : `${n} calls`}`, false, 2500);
}

function showToast(html, canUndo, ms) {
  if (!canUndo) undo.ids = [];
  $("histToastText").innerHTML = html;
  $("histToastUndo").hidden = !canUndo;
  $("histToast").hidden = false;
  clearTimeout(undo.timer);
  undo.timer = setTimeout(hideToast, ms);
}

function hideToast() {
  clearTimeout(undo.timer);
  $("histToast").hidden = true;
  undo.ids = [];
}
