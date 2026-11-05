// SIPSorcery Softphone v2 - UX prototype.
// Everything here is mock state: no SIP or media. The goal is to exercise the
// layout and interaction model (multiple calls, one audio focus, hold, transfer).

"use strict";

// ---------- Icons (Lucide-style strokes) ----------

const ICONS = {
  logo: '<path d="M2 10v3"/><path d="M6 6v11"/><path d="M10 3v18"/><path d="M14 8v7"/><path d="M18 5v13"/><path d="M22 10v3"/>',
  phone: '<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/>',
  incoming: '<path d="M16 2v6h6"/><path d="m22 2-6 6"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/>',
  transfer: '<path d="m18 2 4 4-4 4"/><path d="M14 6h8"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/>',
  hangup: '<path d="M10.68 13.31a16 16 0 0 0 3.41 2.6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7a2 2 0 0 1 1.72 2v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.42 19.42 0 0 1-3.33-2.67m-2.67-3.34a19.79 19.79 0 0 1-3.07-8.63A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91"/><path d="M22 2 2 22"/>',
  text: '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/><path d="M13 8H7"/><path d="M17 12H7"/>',
  mic: '<path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/>',
  micOff: '<path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/><path d="M3 3l18 18"/>',
  video: '<path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/>',
  videoOff: '<path d="m22 8-6 4 6 4V8Z"/><rect x="2" y="6" width="14" height="12" rx="2"/><path d="M2 2l20 20"/>',
  pause: '<rect x="6" y="4" width="4" height="16" rx="1"/><rect x="14" y="4" width="4" height="16" rx="1"/>',
  play: '<path d="M7 4v16l13-8z"/>',
  keypad: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18"/>',
  focus: '<rect x="3" y="5" width="18" height="14" rx="2"/>',
  grid: '<rect x="3" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="3" width="7.5" height="7.5" rx="1.5"/><rect x="3" y="13.5" width="7.5" height="7.5" rx="1.5"/><rect x="13.5" y="13.5" width="7.5" height="7.5" rx="1.5"/>',
  selfView: '<path d="M21 9V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v10c0 1.1.9 2 2 2h4"/><rect x="12" y="13" width="10" height="7" rx="2"/>',
  headphones: '<path d="M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3"/>',
  plus: '<path d="M5 12h14"/><path d="M12 5v14"/>',
  chevronDown: '<path d="m6 9 6 6 6-6"/>',
  check: '<path d="M20 6 9 17l-5-5"/>',
  outgoing: '<path d="M7 7h10v10"/><path d="M7 17 17 7"/>',
  inbound: '<path d="M17 7 7 17"/><path d="M17 17H7V7"/>',
  missed: '<path d="m16 2 6 6"/><path d="m22 2-6 6"/><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.36 1.9.7 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.34 1.85.57 2.81.7A2 2 0 0 1 22 16.92z"/>',
  copy: '<rect x="8" y="8" width="14" height="14" rx="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/>',
  search: '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
  trash: '<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6M14 11v6"/>',
  history: '<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>',
  alert: '<circle cx="12" cy="12" r="10"/><path d="M12 8v4"/><path d="M12 16h.01"/>',
  chevronRight: '<path d="m9 18 6-6-6-6"/>',
  arrowLeft: '<path d="m12 19-7-7 7-7"/><path d="M19 12H5"/>',
  users: '<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
  settings: '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>',
  x: '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
  lock: '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 8 0v4"/>',
  unlock: '<rect x="4" y="11" width="16" height="10" rx="2"/><path d="M8 11V7a4 4 0 0 1 7.75-1.4"/>',
};

const icon = (name) => `<svg class="i" viewBox="0 0 24 24" aria-hidden="true">${ICONS[name]}</svg>`;

// ---------- Mock state ----------

// Mock device lists for the Settings dialog. The first entry is the system default.
const DEVICES = {
  mic: ["System default", "Headset microphone (Jabra Evolve2)", "Laptop microphone array"],
  speaker: ["System default", "Headset (Jabra Evolve2)", "Laptop speakers", "Dell U2723QE (HDMI)"],
  ringer: ["Same as speaker", "Laptop speakers", "Dell U2723QE (HDMI)"],
  camera: ["System default", "Integrated webcam", "Logitech Brio 4K"],
};

const TILE_COLOURS =["var(--tile-a)", "var(--tile-b)", "var(--tile-c)", "var(--tile-d)"];
const MAX_CALLS = 8;
const now = () => Date.now();

// SIP accounts. Mutable: the Accounts view edits these in place.
// reg.state: registered | registering | failed | off (disabled or no registrar).
const ACCOUNTS = [
  { id: "sipsorcery", name: "SIPSorcery", displayName: "Aaron", user: "aaron", domain: "sipsorcery.cloud", registrar: "", proxy: "",
    transport: "TLS", authUser: "aaron", hasPassword: true, register: true, expiry: 3600, enabled: true,
    stun: "stun.sipsorcery.com", srtp: "Optional", dtmf: "RFC 4733", video: true, rtt: true, reg: { state: "registered", expiresAt: now() + 52 * 60_000 } },
  { id: "iptel", name: "iptel.org", displayName: "Aaron", user: "aaron", domain: "iptel.org", registrar: "", proxy: "",
    transport: "UDP", authUser: "aaron", hasPassword: true, register: true, expiry: 600, enabled: true,
    stun: "stun.iptel.org", srtp: "Off", dtmf: "RFC 4733", video: true, rtt: true, reg: { state: "registered", expiresAt: now() + 7 * 60_000 } },
  { id: "local", name: "Local (no registrar)", displayName: "Aaron", user: "aaron", domain: "192.168.0.50", registrar: "", proxy: "",
    transport: "UDP", authUser: "", hasPassword: false, register: false, expiry: 3600, enabled: true,
    stun: "", srtp: "Off", dtmf: "RFC 4733", video: true, rtt: false, reg: { state: "off" } },
];

// The editable fields, grouped as they appear in the Accounts form.
// [key, label, type | option list, placeholder]
const ACCOUNT_FIELDS = [
  ["Account", [["name", "Account name", "text"], ["displayName", "Display name", "text", "Shown to people you call"]]],
  ["Server", [["user", "Username", "text"], ["domain", "Domain", "text", "sip.example.com"],
    ["registrar", "Registrar", "text", "Same as domain"], ["proxy", "Outbound proxy", "text", "None"],
    ["transport", "Transport", ["UDP", "TCP", "TLS", "WebSocket"]]]],
  ["Authentication", [["authUser", "Auth username", "text", "Same as username"], ["password", "Password", "password"]]],
  ["Registration", [["register", "Register with the server so this account can receive calls", "check"], ["expiry", "Expiry (seconds)", "number"]]],
  ["Network & media", [["stun", "STUN server", "text", "None"], ["srtp", "Media encryption (SRTP)", ["Off", "Optional", "Required"]],
    ["dtmf", "Keypad tones (DTMF)", ["RFC 4733", "SIP INFO", "In-band"]],
    // Off leaves video out of every offer, for the odd server or gateway that rejects calls with it.
    ["video", "Offer video on calls (turn off for servers that can't handle it)", "check"],
    ["rtt", "Offer real-time text on calls (RFC 4103)", "check"]]],
];
const ACCOUNT_KEYS = ACCOUNT_FIELDS.flatMap(([, fields]) => fields.map(([k]) => k)).filter((k) => k !== "password");
// The Accounts view replaces the selected call in the main view; this is its value in state.selected.
const ACCOUNTS_TAB = "accounts";
// After the selected call ends, the main view shows what happened until it's dismissed.
const ENDED_VIEW = "ended";
// The full call history in the main view.
const HISTORY_VIEW = "history";

// Call history, newest first: the address-field suggestions, the sidebar and the History view
// all read this. dir is out | in | missed. at is when the call started. note is a short outcome
// for calls that didn't connect ("Busy", "Declined", "Cancelled"); sip is the failure response.
const CONTACTS = [
  { name: "Reception", uri: "sip:reception@example.com" },
  { name: "Echo test", uri: "sip:echo@sipsorcery.cloud" },
  { name: "Music on hold", uri: "sip:music@iptel.org" },
  { name: "Support line", uri: "tel:+35315550100" },
  { name: "Lobby desk phone", uri: "sip:lobby-phone@example.com" },
  { name: "Sam (camera off)", uri: "sip:sam-nocam@example.org" },
  { name: "Kiosk (can't see you)", uri: "sip:kiosk-noview@example.org" },
];
const H = 3_600_000, D = 24 * H;
const RECENTS = [
  { name: "Dana", uri: "sip:dana@example.org", dir: "missed", at: now() - 25 * 60_000, account: "sipsorcery" },
  { name: "Reception", uri: "sip:reception@example.com", dir: "out", at: now() - 2 * H, duration: 95_000, account: "sipsorcery" },
  { name: "+353 1 555 0199", uri: "tel:+35315550199", dir: "in", at: now() - 5 * H, duration: 312_000, account: "iptel" },
  { name: "Eve", uri: "sip:eve@biloxi.example.com", dir: "out", at: now() - 27 * H, note: "Busy", sip: "486 Busy Here", account: "sipsorcery" },
  { name: "Grace", uri: "sip:grace@example.net", dir: "in", at: now() - 28 * H, duration: 1_260_000, account: "sipsorcery",
    text: [{ me: false, text: "Hi, it's Grace. I'll type, my hearing aid battery just died.", at: now() - 28 * H + 20_000 },
      { me: true, text: "No problem. Is Thursday still OK for the review?", at: now() - 28 * H + 60_000 },
      { me: false, text: "Yes, 3pm works. Send me the link?", at: now() - 28 * H + 90_000 }] },
  { name: "Support line", uri: "tel:+35315550100", dir: "out", at: now() - 30 * H, duration: 600_000, account: "iptel" },
  { name: "Frank", uri: "sip:frank@atlanta.example.com", dir: "missed", at: now() - 50 * H, account: "sipsorcery" },
  { name: "Music", uri: "sip:music@iptel.org", dir: "out", at: now() - 3 * D, duration: 41_000, account: "iptel" },
  { name: "Echo test", uri: "sip:echo@sipsorcery.cloud", dir: "out", at: now() - 3 * D - H, duration: 20_000, account: "sipsorcery" },
  { name: "Nobody", uri: "sip:nobody@example.com", dir: "out", at: now() - 4 * D, note: "Number not found", sip: "404 Not Found", account: "sipsorcery" },
  { name: "Alice", uri: "sip:alice@example.com", dir: "in", at: now() - 5 * D, duration: 900_000, account: "sipsorcery" },
  { name: "Bob", uri: "sip:bob@example.com", dir: "out", at: now() - 6 * D, duration: 180_000, account: "sipsorcery" },
].map((e, i) => ({ id: i + 1, ...e }));
let nextHistoryId = RECENTS.length + 1;

// Plain-words versions of the SIP failures people actually hit. The code stays visible, smaller.
const SIP_FAILURES = {
  404: ["Number not found", "Check the number or address and try again."],
  408: ["No answer", "The other side didn't respond in time."],
  480: ["Not available right now", "They may be offline or have do-not-disturb on."],
  486: ["Busy", "They're on another call."],
  503: ["Service unavailable", "Their server couldn't take the call."],
  603: ["Declined", "They declined the call."],
};

// The mock decides how a placed call goes from words in the address.
function mockOutcome(uri) {
  if (/busy/i.test(uri)) return "486 Busy Here";
  if (/nobody|unknown/i.test(uri)) return "404 Not Found";
  if (/timeout/i.test(uri)) return "408 Request Timeout";
  if (/away|offline/i.test(uri)) return "480 Temporarily Unavailable";
  if (/decline/i.test(uri)) return "603 Decline";
  return null;
}

// Call states: dialing | incoming | connected | held | ended
// A call exists only while it's active; ended calls drop out of the list after a short beat.
// Connected calls that aren't the audio focus are still up, just locally silenced
// ("Audio off"). Hold is an explicit SIP re-INVITE and is shown separately.
// createdAt orders the Active calls list (newest first); startedAt drives the call timer.
const state = {
  accountId: "sipsorcery",
  selected: 1,
  audioFocus: 1,
  nextId: 4,
  calls: [
    { id: 1, name: "Alice", uri: "sip:alice@example.com", status: "connected", dir: "out", account: "sipsorcery", createdAt: now() - 280_000, startedAt: now() - 272_000, video: true, muted: false, camera: true,
      rtt: true, rttSupported: true, chat: [
        { me: false, text: "Hi! My audio is a bit patchy, so I'll type as well.", at: now() - 200_000 },
        { me: true, text: "No problem, I can see your text.", at: now() - 180_000 }] },
    { id: 2, name: "Bob", uri: "sip:bob@example.com", status: "connected", dir: "in", account: "sipsorcery", createdAt: now() - 100_000, startedAt: now() - 95_000, video: true, muted: false, camera: true, netProfile: "theirs", srtp: false,
      rtt: true, rttSupported: true },
    { id: 3, name: "Charlie", uri: "sip:charlie@example.com", status: "incoming", dir: "in", account: "sipsorcery", createdAt: now() - 6_000, startedAt: now() - 6_000, video: true,
      rtt: true, rttSupported: true },
  ],
  // "focus" shows only the selected call; "grid" shows every live call side by side.
  view: loadPref("view", "focus"),
  // Local camera preview ("You" box) in the selected call's tile. On by default.
  selfView: loadPref("selfView", "on") === "on",
  // The Audio | Video switch beside Call: what kind of call Call places. Audio by default, since
  // most SIP calls go to phones and an audio offer never trips anything up; remembered after that.
  callType: loadPref("callType", "audio"),
  // Emergency privacy toggles that apply to every call, including new ones, while on.
  muteAll: false,
  cameraAllOff: false,
  // Diagnostics: this computer has no camera (or it's in use or blocked). Calls still offer
  // video, receive-only, so you can see them.
  noCamera: false,
  // Accounts view. open stays true while you step away to a call, until it's closed.
  // drafts holds unsaved edits per account, so switching to a call loses nothing.
  acct: { open: false, selected: null, drafts: {}, error: null, confirmRemove: null, nextId: 1 },
  ended: null,     // What the ENDED_VIEW card shows: see endCall.
  toasts: [],      // Short notices at the bottom of the window.
  menu: null,      // Open device menu on the controls: "mic" | "camera".
  mutedAt: 0,      // When the mic was last muted, so the "you're muted" nudge isn't instant.
  dial: { open: false, active: -1, items: [] },
  // History view. seenAt: missed calls after this are "new" and counted in the sidebar.
  hist: { open: false, filter: "all", q: "", selectedId: null, seenAt: now() - H, undo: [], collapsed: loadPref("historyCollapsed", "no") === "yes" },
  sideTab: "calls", // Phones only: which sidebar tab is showing.
  bannerOpen: false, // Several calls ringing: is the stacked banner expanded?
  netBad: false,     // Diagnostics: pretend your own network is struggling.
  // Real-time text preferences (Settings). textFirst opens the text panel on every call.
  textFirst: loadPref("textFirst", "no") === "yes",
  saveText: loadPref("saveText", "no") === "yes",
  devices: { mic: DEVICES.mic[0], speaker: DEVICES.speaker[0], ringer: DEVICES.ringer[0], camera: DEVICES.camera[0] },
  modal: null,
  dtmf: "",
  diagOpen: false,
  log: [],
};

const timers = new Map();

function loadPref(key, fallback) {
  try { return localStorage.getItem("softphonev2." + key) ?? fallback; } catch { return fallback; }
}

function savePref(key, value) {
  try { localStorage.setItem("softphonev2." + key, value); } catch { /* storage unavailable */ }
}

function setView(view) {
  state.view = view;
  savePref("view", view);
  // Back in Focus view, an open text panel is on screen again, so its lines count as read.
  const sel = callById(state.selected);
  if (sel && textShowing(sel)) sel.unread = 0;
  render();
}

// Calls shown in the grid. It includes the selected call even while it's dialing or ending, so it doesn't vanish.
const gridCallsFor = (sel) => state.calls.filter((l) => isLive(l) || l.id === sel?.id);
// Is the stage showing the grid right now? (The preference only applies with two or more calls.)
function inGrid() {
  const sel = callById(state.selected);
  return !!sel && sel.status !== "incoming" && state.view === "grid" && gridCallsFor(sel).length > 1;
}

function toggleSelfView() {
  state.selfView = !state.selfView;
  savePref("selfView", state.selfView ? "on" : "off");
  renderStage();
}

const callById = (no) => state.calls.find((l) => l.id === no);
const isLive = (l) => l.status === "connected" || l.status === "held";
const callsNewestFirst = () => [...state.calls].sort((a, b) => b.createdAt - a.createdAt);
// Effective mic/camera state: the global "all calls" toggles override each call's own setting.
const micMuted = (l) => state.muteAll || !!l.muted;
const camOn = (l) => !state.cameraAllOff && l.camera !== false;
const accountById = (id) => ACCOUNTS.find((a) => a.id === id);
const accountName = (id) => accountById(id)?.name ?? id;
const aor = (a) => `sip:${a.user}@${a.domain}`;
const enabledAccounts = () => ACCOUNTS.filter((a) => a.enabled);
const needsAttention = (a) => a.enabled && a.register && a.reg.state === "failed";
const accountsOpen = () => state.acct.open;
const accountsActive = () => state.selected === ACCOUNTS_TAB;
const hasUnsavedAccounts = () => Object.keys(state.acct.drafts).some(isDirty);

// Registration state in a few words. tone drives the dot colour: ok | off | bad.
function regInfo(a) {
  if (!a.enabled) return { tone: "off", short: "Disabled", long: "Disabled · not used for calls" };
  if (!a.register) return { tone: "off", short: "Outbound only", long: "Outbound only · no registrar, so it can't receive calls" };
  if (a.reg.state === "registering") return { tone: "off", short: "Registering…", long: "Registering…" };
  if (a.reg.state === "failed") return { tone: "bad", short: "Not registered", long: `${a.reg.error} · retrying every 30s` };
  const mins = Math.max(1, Math.round((a.reg.expiresAt - now()) / 60_000));
  return { tone: "ok", short: "Registered", long: `Registered · renews in ${mins} min` };
}

function displayName(uri) {
  const m = /^(?:sips?:)?([^@;]+)/i.exec(uri.trim());
  const user = m ? m[1] : uri;
  return /^[+\d*#]+$/.test(user) ? user : user.charAt(0).toUpperCase() + user.slice(1);
}

function fmtDuration(ms) {
  const s = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
  const mm = String(m).padStart(h ? 2 : 2, "0"), ss = String(sec).padStart(2, "0");
  return h ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

function callStatusText(l) {
  switch (l.status) {
    case "dialing": return "Calling…";
    case "incoming": return "Incoming call";
    case "held": return "On hold";
    case "ended": return l.endReason ?? "Call ended";
    case "connected": {
      if (l.id !== state.audioFocus) return (l.video ? "Live video · " : "") + "Audio off";
      if (micMuted(l)) return state.muteAll ? "Muted (all calls)" : "Muted";
      return "Mic + speaker on";
    }
  }
  return "";
}

// Like callStatusText, but as HTML: a held call shows how long it's been held, and the timer ticks.
function statusHtml(l) {
  // Text from the other side wins while you can't see the text panel.
  if (l.status === "connected" && !textShowing(l)) {
    if (theirLive(l)) return `<span class="st-typing">Typing…</span>`;
    const last = l.unread ? [...l.chat].reverse().find((m) => !m.me && !m.open) : null;
    if (last) return `<span class="st-unread">“${esc(last.text)}”</span>`;
  }
  if (l.status !== "held") return esc(callStatusText(l));
  return `On hold · <span data-since="${l.heldAt}">${fmtDuration(now() - l.heldAt)}</span>`;
}
const heldLong = (l) => l.status === "held" && now() - l.heldAt >= 60_000;

function log(msg) {
  const t = new Date().toTimeString().slice(0, 8);
  state.log.push(`${t}  ${msg}`);
  if (state.log.length > 300) state.log.shift();
  renderLog();
}

// ---------- Actions ----------

function addCall(fields) {
  if (state.calls.length >= MAX_CALLS) return null;
  const call = { id: state.nextId++, account: state.accountId, createdAt: now(), startedAt: now(), muted: false, camera: true, ...fields };
  state.calls.push(call);
  return call;
}

function holdCurrent(reason) {
  const cur = callById(state.audioFocus);
  if (cur && cur.status === "connected") {
    cur.status = "held";
    cur.heldAt = now();
    cur.holdNudges = 0;
    log(`Call ${cur.id}: re-INVITE a=sendonly → <b>${cur.name}</b> held (${reason})`);
  }
}

// The Audio | Video switch decides whether the call offers video. A phone number, or an account
// with video turned off, is always audio. An audio call can add video later (a re-INVITE).
function placeCall(dest, name) {
  dest = dest.trim();
  if (!dest) { document.getElementById("destInput").focus(); return; }
  if (state.calls.length >= MAX_CALLS) { log(`Can't place call: already ${MAX_CALLS} active calls`); return; }
  if (!accountById(state.accountId)?.enabled) { log("Can't place call: no enabled account to call from"); return; }

  holdCurrent("new call");
  const uri = /^(sips?:|tel:)/i.test(dest) ? dest : (/^[+\d*#]+$/.test(dest) ? `tel:${dest}` : `sip:${dest}`);
  // Text is offered on every call unless the account turns it off. tel: gateways (and any address
  // containing "notext") decline the m=text line.
  const offerText = accountById(state.accountId)?.rtt !== false;
  const textOk = !/^tel:/.test(uri) && !/notext/i.test(uri);
  const tel = /^tel:/.test(uri);
  const videoOk = accountById(state.accountId)?.video !== false;
  const wantVideo = state.callType === "video";
  const line = addCall({ name: name || displayName(uri), uri, status: "dialing", dir: "out", video: !tel && videoOk && wantVideo,
    noVideo: tel ? "tel" : !videoOk ? "account" : !wantVideo ? "chosen" : undefined, rtt: offerText && textOk, rttSupported: textOk });
  state.selected = line.id;
  state.audioFocus = line.id;
  log(`Call ${line.id}: INVITE ${uri} via ${accountName(state.accountId)} (${line.video ? `audio + video${state.noCamera ? ", video recvonly: no camera" : !camOn(line) ? ", video recvonly: camera off" : ""}` : "audio only, no m=video"})`);
  const input = document.getElementById("destInput");
  input.value = "";
  input.blur(); // So shortcuts like M work straight away.
  closeDialSuggest();
  render();

  const id = setTimeout(() => {
    log(`Call ${line.id}: 180 Ringing`);
    const id2 = setTimeout(() => {
      if (line.status !== "dialing") return;
      const failure = mockOutcome(uri);
      if (failure) {
        endCall(line, { kind: "failed", sip: failure });
      } else {
        line.status = "connected";
        line.startedAt = now();
        if (offerText && !line.rtt) line.rttDeclined = true;
        applyRemoteVideo(line);
        if (line.rtt && state.textFirst) line.textOpen = true;
        log(`Call ${line.id}: 200 OK, ACK — media flowing (${[videoDesc(line), line.rtt ? "text" : ""].filter(Boolean).join(" + ")}${line.rttDeclined ? "; text declined, m=text 0" : ""})`);
        render();
      }
    }, 1600);
    timers.set(line.id, id2);
  }, 900);
  timers.set(line.id, id);
}

// How the far end's answer (or their offer) treats video, from words in the mock address:
// "phone" = a phone with no video (m=video 0), "nocam" = they won't send video (recvonly),
// "noview" = they send but won't take ours (sendonly).
function applyRemoteVideo(l) {
  if (!l.video) return;
  if (/phone/i.test(l.uri)) { l.video = false; l.noVideo = "rejected"; return; }
  if (/nocam/i.test(l.uri)) l.theirCam = false;
  if (/noview/i.test(l.uri)) l.theySee = false;
}

const videoDesc = (l) => !l.video ? (l.noVideo === "rejected" ? "audio; video declined, m=video 0" : "audio only")
  : l.theirCam === false ? "audio+video; video recvonly, they aren't sending" : l.theySee === false ? "audio+video; video sendonly, they don't take ours" : "audio+video";

// Why a call has no video, in words for the tile and the icons.
const noVideoText = (l) => l.noVideo === "rejected" ? "Their phone doesn't do video"
  : l.noVideo === "tel" ? "Phone number · audio only"
  : l.noVideo === "account" ? "Video is off for this account" : "Audio call";

// An audio call can't simply switch the camera on: video has to be added with a re-INVITE carrying
// an m=video line. Plenty of phones and gateways refuse it (m=video 0), so say so plainly.
const canAddVideo = (l) => !l.video && l.noVideo !== "tel" && l.noVideo !== "account" && l.noVideo !== "rejected"
  && accountById(l.account)?.video !== false;

function addVideo(l) {
  if (!l || !canAddVideo(l) || l.videoAdding || l.status !== "connected") return;
  l.videoAdding = true;
  log(`Call ${l.id}: re-INVITE adding m=video (VP8, H264)${state.noCamera ? ", recvonly: no camera" : state.cameraAllOff ? ", recvonly: camera off" : ""} → ${l.name}`);
  render();
  setTimeout(() => {
    l.videoAdding = false;
    if (l.status === "ended") return;
    l.video = true;
    l.noVideo = undefined;
    l.camera = true;
    applyRemoteVideo(l);
    if (l.video) {
      log(`Call ${l.id}: 200 OK — video on (${videoDesc(l)})`);
    } else {
      log(`Call ${l.id}: 200 OK with m=video 0 — ${l.name}'s phone declined video`);
      toast({ key: `video-${l.id}`, tone: "warn", icon: "videoOff", text: `<b>${esc(l.name)}</b>'s phone doesn't do video. The call carries on as audio.` });
    }
    render();
  }, 900);
}

// Diagnostics: their camera stops (or starts). Video is still agreed; frames just stop arriving,
// so the tile waits a couple of seconds before saying so, rather than flickering on a short gap.
function toggleTheirCamera() {
  const l = callById(state.selected);
  if (!l || l.status !== "connected" || !l.video) { log("Select a connected video call first"); return; }
  if (l.theirCam === false) {
    l.theirCam = true;
    log(`Call ${l.id}: video frames from ${l.name} again`);
    render();
    return;
  }
  log(`Call ${l.id}: ${l.name} turned their camera off (frames stop)`);
  setTimeout(() => {
    if (l.status !== "connected" && l.status !== "held") return;
    l.theirCam = false;
    log(`Call ${l.id}: no video from ${l.name} for 2 s: showing "Their camera is off"`);
    render();
  }, 2000);
}

// withCamera false answers with your camera off: video is still agreed, so you see them.
function answer(no, holdOthers = true, withCamera = true) {
  const l = callById(no);
  if (!l || l.status !== "incoming") return;
  if (holdOthers) holdCurrent("answering " + l.name);
  l.status = "connected";
  l.startedAt = now();
  l.muted = false;
  l.camera = withCamera;
  if (l.rtt && state.textFirst) l.textOpen = true;
  delete l.returnTo; // Once answered, it's a normal call; the Accounts view is one click away.
  state.selected = no;
  state.audioFocus = no;
  log(`Call ${no}: 200 OK → <b>${l.name}</b> answered (${videoDesc(l)}${l.video && !camOn(l) ? ", your camera off: video recvonly" : ""})`);
  render();
}

// How a call ended, in words.
// kind: local (you hung up or cancelled) | remote (they hung up) | failed (a SIP error) |
//       declined (you declined an incoming call) | transferred
function endSummary(l, kind, sip, target) {
  if (kind === "failed") {
    const [title, sub] = SIP_FAILURES[parseInt(sip, 10)] ?? ["Call failed", ""];
    return { title, sub };
  }
  if (kind === "remote") return { title: `${l.name} hung up` };
  if (kind === "transferred") return { title: `Transferred to ${target}` };
  if (kind === "declined") return { title: "Declined" };
  if (kind === "missed") return { title: "Missed call" };
  return { title: l.status === "dialing" ? "Cancelled" : "You hung up" };
}

function endCall(l, { kind = "local", sip = "BYE", target = "", quiet = false } = {}) {
  clearTimeout(timers.get(l.id));
  const { title, sub = "" } = endSummary(l, kind, sip, target);
  const duration = isLive(l) ? now() - l.startedAt : 0;
  log(`Call ${l.id}: ${esc(sip)} — ${esc(l.name ?? "call")}: ${esc(title)}`);
  const name = l.name;
  l.status = "ended";
  l.endReason = title;
  dismissToast(`hold-${l.id}`);
  if (state.audioFocus === l.id) state.audioFocus = null;
  // The transcript is kept only if Settings says so.
  const said = (l.chat ?? []).filter((m) => m.text.trim()).map(({ me, text, at }) => ({ me, text, at }));
  addRecent({ name: l.name, uri: l.uri, dir: kind === "missed" ? "missed" : l.dir ?? "out", at: l.createdAt, duration, account: l.account,
    text: state.saveText && said.length ? said : undefined,
    note: duration || kind === "missed" ? undefined : kind === "failed" ? title : kind === "declined" ? "Declined" : kind === "local" ? "Cancelled" : undefined,
    sip: kind === "failed" ? sip : undefined });

  if (!quiet && kind !== "declined") {
    const ended = { id: l.id, name: l.name, uri: l.uri, video: l.video, noVideo: l.noVideo, account: l.account, kind, title, sub, sip, duration };
    // An unanswered call that interrupted Accounts or History hands straight back to that view.
    const handBack = state.selected === l.id && l.returnTo != null && nextSelection(l.returnTo) === l.returnTo;
    if (handBack) state.selected = l.returnTo;
    if (state.selected === l.id) {
      // The selected call: replace it with a card saying what happened, with Redial.
      state.ended = ended;
      state.selected = ENDED_VIEW;
    } else {
      // A call in the background: a toast is enough.
      toast({
        tone: kind === "failed" || kind === "missed" ? "bad" : "",
        icon: kind === "failed" ? "alert" : kind === "missed" ? "missed" : "hangup",
        text: kind === "failed" ? `Couldn't reach <b>${esc(l.name)}</b>: ${esc(title)}`
          : kind === "missed" ? `Missed call from <b>${esc(l.name)}</b>`
          : `${esc(title)}${duration ? ` · ${fmtDuration(duration)}` : ""}`,
        actions: kind === "transferred" ? []
          : [{ label: kind === "missed" ? "Call back" : "Redial", act: "redial", data: { uri: l.uri, name: l.name, account: l.account } }],
      });
    }
  }
  render();
  // Leave a short "Call ended" beat before the call drops out of the list.
  setTimeout(() => {
    if (l.status !== "ended") return;
    state.calls = state.calls.filter((x) => x !== l);
    if (state.audioFocus === l.id) state.audioFocus = null;
    if (state.selected === l.id) state.selected = nextSelection(l.returnTo);
    render();
  }, 1400);
  return name;
}

// What to show once the selected call goes away. A call that interrupted the
// Accounts view (and was never answered) hands back to it.
function nextSelection(prefer) {
  if (prefer === ACCOUNTS_TAB && accountsOpen()) return ACCOUNTS_TAB;
  if (prefer === HISTORY_VIEW && state.hist.open) return HISTORY_VIEW;
  const recent = callsNewestFirst().filter((x) => x.status !== "ended");
  const next = recent.find((x) => x.status === "connected") ?? recent[0];
  return next?.id ?? (accountsOpen() ? ACCOUNTS_TAB : null);
}

function decline(no) {
  const l = callById(no);
  if (l?.status === "incoming") endCall(l, { kind: "declined", sip: "486 Busy Here" });
}

// Picking a call from the grid, by its tile, its entry in the call list or its number key, opens
// it in Focus view. The grid has no controls, so a click there is only ever a step towards acting
// on a call. A ringing call keeps the grid preference: answering it goes back to the grid.
function focusCall(no) {
  const fromGrid = inGrid();
  select(no);
  if (fromGrid && callById(no) && callById(no).status !== "incoming") setView("focus");
}

function select(no) {
  if (no === ACCOUNTS_TAB) {
    // Viewing accounts doesn't touch audio: the current call keeps the mic and speaker.
    if (accountsOpen()) { state.selected = no; render(); }
    return;
  }
  if (no === HISTORY_VIEW) {
    if (state.hist.open) { state.selected = no; render(); }
    return;
  }
  const l = callById(no);
  if (!l) return;
  state.selected = no;
  if (textShowing(l)) l.unread = 0;
  // Selecting a live, un-held call moves audio to it; the previous one keeps its video.
  if (l.status === "connected" && state.audioFocus !== no) {
    state.audioFocus = no;
    log(`Audio focus → call ${no} (${l.name})`);
  }
  render();
}

function toggleHold(l) {
  if (l.status === "connected") {
    l.status = "held";
    l.heldAt = now();
    l.holdNudges = 0;
    if (state.audioFocus === l.id) state.audioFocus = null;
    log(`Call ${l.id}: re-INVITE a=sendonly → ${l.name} on hold`);
  } else if (l.status === "held") {
    holdCurrent("resuming " + l.name);
    l.status = "connected";
    l.holdNudges = 0;
    dismissToast(`hold-${l.id}`);
    state.audioFocus = l.id;
    log(`Call ${l.id}: re-INVITE a=sendrecv → ${l.name} resumed`);
  }
  render();
}

function sendDtmf(digit) {
  const l = callById(state.selected);
  if (!l || l.status !== "connected") return;
  state.dtmf = (state.dtmf + digit).slice(-16);
  log(`Call ${l.id}: DTMF '${digit}' (RFC 4733) → ${l.name}`);
  const d = document.getElementById("dtmfDisplay");
  if (d) d.textContent = state.dtmf;
}

function transfer(l, target) {
  if (typeof target === "number") {
    const other = callById(target);
    log(`Call ${l.id}: REFER Refer-To: ${other.uri};Replaces=… (attended → ${other.name})`);
    closeModal();
    endCall(l, { kind: "transferred", target: other.name, sip: "202 Accepted, NOTIFY 200" });
    endCall(other, { kind: "transferred", target: l.name, quiet: true });
  } else {
    if (!target.trim()) return;
    log(`Call ${l.id}: REFER Refer-To: ${target} (blind)`);
    closeModal();
    endCall(l, { kind: "transferred", target: displayName(target), sip: "202 Accepted, NOTIFY 200" });
  }
}

function simulateIncoming() {
  const callers = [
    ["Dana", "sip:dana@example.org"], ["Eve", "sip:eve@biloxi.example.com"],
    ["Frank", "sip:frank@atlanta.example.com"], ["+353 1 555 0199", "tel:+35315550199"],
    ["Grace", "sip:grace@example.net"], ["Lobby desk phone", "sip:lobby-phone@example.com"],
    ["Sam", "sip:sam-nocam@example.org"],
  ];
  const free = callers.filter(([, u]) => !state.calls.some((l) => l.uri === u));
  const pool = free.length ? free : callers;
  const [name, uri] = pool[Math.floor(Math.random() * pool.length)];
  // Only a registered account can be reached.
  const acc = ACCOUNTS.find((a) => a.enabled && a.register && a.reg.state === "registered");
  if (!acc) { log(`No registered account, so the INVITE from ${uri} never arrives`); return; }
  // A desk phone ("phone" in the address) offers no video at all.
  const line = addCall({ name, uri, status: "incoming", dir: "in", video: uri.startsWith("sip:") && !/phone/i.test(uri),
    noVideo: uri.startsWith("tel:") ? "tel" : /phone/i.test(uri) ? "rejected" : undefined,
    theirCam: /nocam/i.test(uri) ? false : undefined, account: acc.id,
    rtt: acc.rtt !== false && uri.startsWith("sip:"), rttSupported: uri.startsWith("sip:") });
  if (!line) { log(`Incoming INVITE from ${uri} rejected: 486 Busy Here (${MAX_CALLS} active calls)`); return; }
  log(`Call ${line.id}: INVITE from ${uri} → 180 Ringing`);
  state.sideTab = "calls"; // Phones: show the ringing call in the strip.
  if (state.selected === ACCOUNTS_TAB || state.selected === HISTORY_VIEW) {
    // A ringing call takes over from the Accounts or History view, and hands back if it's not answered.
    line.returnTo = state.selected;
    state.selected = line.id;
  } else if (state.selected === ENDED_VIEW
    || (!state.calls.some((l) => isLive(l) || l.status === "dialing") && callById(state.selected)?.status !== "incoming")) {
    // Nothing else going on: show the answer screen. If a call is already ringing there, it keeps
    // its place and this one queues behind it in the banner.
    state.selected = line.id;
  }
  render();
  return line;
}

// The caller gives up after a few rings: a missed call.
function simulateMissed() {
  const line = simulateIncoming();
  if (!line) return;
  setTimeout(() => {
    if (line.status === "incoming") endCall(line, { kind: "missed", sip: "CANCEL (caller hung up)" });
  }, 3500);
}

function addRecent(entry) {
  RECENTS.unshift({ id: nextHistoryId++, at: now(), ...entry });
  RECENTS.sort((a, b) => b.at - a.at);
  if (RECENTS.length > 200) RECENTS.pop();
}

function dismissEnded() {
  state.ended = null;
  state.selected = returnCall()?.id ?? (accountsOpen() ? ACCOUNTS_TAB : null);
  render();
}

function redial({ uri, name, account }) {
  if (accountById(account)?.enabled) state.accountId = account;
  state.ended = null;
  placeCall(uri, name);
}

// ---------- Call history ----------

const historyColour = (name) => TILE_COLOURS[[...name].reduce((h, c) => h + c.charCodeAt(0), 0) % TILE_COLOURS.length];
const unseenMissed = () => RECENTS.filter((e) => e.dir === "missed" && e.at > state.hist.seenAt).length;

// "Outgoing · 1:35", "Busy", "Missed", … with the matching icon.
function historyOutcome(e) {
  const ico = icon(e.dir === "missed" ? "missed" : e.dir === "in" ? "inbound" : "outgoing");
  const text = e.dir === "missed" ? "Missed"
    : e.note ?? `${e.dir === "in" ? "Incoming" : "Outgoing"}${e.duration ? ` · ${fmtDuration(e.duration)}` : ""}`;
  const tone = e.dir === "missed" ? "bad" : e.note ? "warn" : "";
  return { ico, text, tone };
}

function clockTime(at) { return new Date(at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); }

function dayLabel(at) {
  const d = new Date(at), today = new Date();
  const startOf = (x) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const days = Math.round((startOf(today) - startOf(d)) / D);
  if (days === 0) return "Today";
  if (days === 1) return "Yesterday";
  if (days < 7) return d.toLocaleDateString([], { weekday: "long" });
  return d.toLocaleDateString([], { day: "numeric", month: "short" });
}

function openHistory(id = null) {
  Object.assign(state.hist, { open: true, selectedId: id, seenAt: now() });
  if (id) Object.assign(state.hist, { filter: "all", q: "" }); // Make sure the chosen entry is visible.
  select(HISTORY_VIEW);
  if (id) document.querySelector(`.hist-row[data-hist="${id}"]`)?.scrollIntoView({ block: "center" });
}

function closeHistory() {
  state.hist.open = false;
  if (state.selected === HISTORY_VIEW) state.selected = returnCall()?.id ?? null;
  render();
}

async function copyAddress(uri) {
  try {
    await navigator.clipboard.writeText(uri);
    toast({ key: "copy", icon: "copy", ms: 2500, text: `Copied <b>${esc(uri)}</b>` });
  } catch {
    toast({ key: "copy", tone: "warn", icon: "copy", ms: 4000, text: `Couldn't copy. The address is <b>${esc(uri)}</b>` });
  }
}

// Removing calls from history. Removals made while the Undo toast is still up join the
// same batch, so clicking × down a list of spam calls can be undone in one go.
function removeHistory(ids, label) {
  const gone = RECENTS.filter((e) => ids.includes(e.id));
  if (!gone.length) return;
  for (const e of gone) RECENTS.splice(RECENTS.indexOf(e), 1);
  if (ids.includes(state.hist.selectedId)) state.hist.selectedId = null;
  const live = state.toasts.some((t) => t.key === "hist-removed");
  state.hist.undo = live ? [...state.hist.undo, ...gone] : gone;
  const n = state.hist.undo.length;
  toast({
    key: "hist-removed", icon: "trash", ms: 10000,
    text: label ?? (n === 1 ? `Removed <b>${esc(gone[0].name)}</b> from history` : `Removed ${n} calls from history`),
    actions: [{ label: "Undo", act: "hist-undo" }],
  });
  log(`History: removed ${gone.length} entr${gone.length === 1 ? "y" : "ies"}`);
  render();
}

function undoRemoveHistory() {
  RECENTS.push(...state.hist.undo);
  RECENTS.sort((a, b) => b.at - a.at);
  toast({ key: "hist-removed", icon: "trash", ms: 2500, text: `Restored ${state.hist.undo.length === 1 ? "1 call" : `${state.hist.undo.length} calls`}` });
  state.hist.undo = [];
  render();
}

// Clear acts on what the view is showing: everything, or just the filtered/searched calls.
function historyFiltered() { return state.hist.filter !== "all" || state.hist.q.trim() !== ""; }

function clearHistory() {
  const ids = historyMatches().map((e) => e.id);
  const n = ids.length;
  closeModal();
  removeHistory(ids, `Cleared ${n === 1 ? "1 call" : `${n} calls`} from history`);
}

function sidebarHistoryHtml() {
  return RECENTS.slice(0, 6).map((e) => {
    const o = historyOutcome(e);
    const sel = state.selected === HISTORY_VIEW && state.hist.selectedId === e.id;
    return `<li class="hist-side">
      <button class="line hist-line ${sel ? "selected" : ""}" data-act="hist-open" data-hist="${e.id}" title="Details for this call">
        <span class="line-avatar" style="--tile-bg:${historyColour(e.name)}">${esc(initial(e.name))}</span>
        <span><span class="line-name">${esc(e.name)}</span>
          <span class="line-status hist-outcome ${o.tone}">${o.ico}${esc(e.dir === "missed" ? "Missed · " : e.note ? `${e.note} · ` : "")}${relTime(e.at)}</span></span>
        <span></span>
      </button>
      <button class="icon-btn hist-side-call" data-act="redial" data-uri="${esc(e.uri)}" data-name="${esc(e.name)}" data-account="${esc(e.account ?? "")}"
        aria-label="Call ${esc(e.name)}" title="Call ${esc(e.name)}">${icon("phone")}</button>
    </li>`;
  }).join("") || `<li class="call-list-empty">No calls yet</li>`;
}

function historyMatches() {
  const { filter, q } = state.hist;
  const needle = q.trim().toLowerCase();
  return RECENTS.filter((e) => (filter === "all" || e.dir === filter)
    && (!needle || e.name.toLowerCase().includes(needle) || e.uri.toLowerCase().includes(needle)));
}

function historyResultsHtml() {
  const list = historyMatches();
  if (!list.length) {
    const what = { all: "calls", missed: "missed calls", out: "outgoing calls", in: "incoming calls" }[state.hist.filter];
    return `<div class="hist-empty">${state.hist.q.trim() ? `No ${what} match “${esc(state.hist.q.trim())}”.` : state.hist.filter === "all" ? "History is empty." : `No ${what}.`}</div>`;
  }
  let day = "";
  return list.map((e) => {
    const d = dayLabel(e.at);
    const head = d !== day ? `${day ? "</ol>" : ""}<h3 class="hist-day">${(day = d)}</h3><ol class="hist-list">` : "";
    const o = historyOutcome(e);
    const sel = e.id === state.hist.selectedId;
    const sameAddress = sel ? RECENTS.filter((x) => x.uri === e.uri).length : 0;
    const detail = sel ? `<div class="hist-detail" id="hist-detail-${e.id}">
        <dl>
          <div><dt>Direction</dt><dd>${{ out: "Outgoing", in: "Incoming", missed: "Incoming, missed" }[e.dir]}</dd></div>
          <div><dt>Started</dt><dd>${dayLabel(e.at)}, ${clockTime(e.at)}</dd></div>
          <div><dt>Duration</dt><dd>${e.duration ? fmtDuration(e.duration) : "Didn't connect"}</dd></div>
          <div><dt>Account</dt><dd>${esc(accountName(e.account) ?? "Unknown")}</dd></div>
          <div class="wide"><dt>Outcome</dt><dd>${esc(e.dir === "missed" ? "Caller hung up before you answered" : e.note ?? "Completed")}${e.sip ? ` <span class="mono">SIP ${esc(e.sip)}</span>` : ""}</dd></div>
          ${e.text ? `<div class="wide"><dt>Text conversation</dt><dd class="hist-text">${e.text.map((m) =>
            `<p class="${m.me ? "me" : ""}"><b>${m.me ? "You" : esc(e.name)}</b><span>${esc(m.text)}</span><time>${clockTime(m.at)}</time></p>`).join("")}</dd></div>` : ""}
        </dl>
        <div class="hist-detail-actions">
          <button class="btn btn-small btn-primary" data-act="redial" data-uri="${esc(e.uri)}" data-name="${esc(e.name)}" data-account="${esc(e.account ?? "")}">${icon("phone")}Call back</button>
          <button class="btn btn-small" data-act="hist-copy" data-uri="${esc(e.uri)}">${icon("copy")}Copy address</button>
          <span class="hist-detail-spacer"></span>
          <button class="btn btn-small" data-act="hist-remove" data-hist="${e.id}">${icon("trash")}Remove</button>
          ${sameAddress > 1 ? `<button class="btn btn-small" data-act="hist-remove-uri" data-uri="${esc(e.uri)}">${icon("trash")}Remove all ${sameAddress} from this address</button>` : ""}
        </div>
      </div>` : "";
    return `${head}<li class="hist-row ${sel ? "selected" : ""}" data-hist="${e.id}">
      <button class="hist-main" data-act="hist-select" data-hist="${e.id}" aria-expanded="${sel}" ${sel ? `aria-controls="hist-detail-${e.id}"` : ""}>
        <span class="line-avatar" style="--tile-bg:${historyColour(e.name)}">${esc(initial(e.name))}</span>
        <span class="hist-who"><span class="hist-name">${esc(e.name)}</span><span class="hist-uri">${esc(e.uri)}</span></span>
        <span class="hist-outcome ${o.tone}">${o.ico}${esc(o.text)}${e.text ? `<span class="hist-has-text" title="Text conversation saved">${icon("text")}</span>` : ""}</span>
        <span class="hist-time">${clockTime(e.at)}</span>
      </button>
      <button class="icon-btn hist-call" data-act="redial" data-uri="${esc(e.uri)}" data-name="${esc(e.name)}" data-account="${esc(e.account ?? "")}"
        aria-label="Call ${esc(e.name)}" title="Call ${esc(e.name)}">${icon("phone")}</button>
      <button class="icon-btn hist-toggle" data-act="hist-select" data-hist="${e.id}" aria-expanded="${sel}"
        aria-label="${sel ? "Collapse" : "Show"} details for ${esc(e.name)}" title="${sel ? "Collapse" : "Show details"}">${icon("chevronDown")}</button>
      ${detail}
    </li>`;
  }).join("") + "</ol>";
}

// Re-render just the results (and the Clear label), so the search box keeps focus.
function refreshHistResults() {
  document.getElementById("histResults").innerHTML = historyResultsHtml();
  const btn = document.getElementById("histClear");
  if (btn) { btn.querySelector("span").textContent = clearLabel(); btn.disabled = !historyMatches().length; }
}

function clearLabel() {
  const n = historyMatches().length;
  return historyFiltered() ? `Clear ${n} shown` : "Clear history";
}

function historyViewHtml() {
  const missed = RECENTS.filter((e) => e.dir === "missed").length;
  const filters = [["all", "All"], ["missed", "Missed"], ["out", "Outgoing"], ["in", "Incoming"]];
  return `${viewTopbarHtml("history-close", "Close history")}
    <div class="call-head">
      <div>
        <div class="eyebrow">Call history</div>
        <h1 class="call-name">History</h1>
        <div class="call-uri">${RECENTS.length} calls · ${missed} missed</div>
      </div>
    </div>
    <div class="hist-toolbar">
      <label class="hist-search">${icon("search")}<input id="histSearch" type="search" placeholder="Search names and addresses" value="${esc(state.hist.q)}" spellcheck="false" aria-label="Search call history"></label>
      <div class="view-toggle" role="group" aria-label="Show">
        ${filters.map(([f, label]) => `<button class="${state.hist.filter === f ? "on" : ""}" data-act="hist-filter" data-filter="${f}" aria-pressed="${state.hist.filter === f}">${label}</button>`).join("")}
      </div>
      <button class="btn btn-small hist-clear" id="histClear" data-act="hist-clear" ${historyMatches().length ? "" : "disabled"}>${icon("trash")}<span>${clearLabel()}</span></button>
    </div>
    <div id="histResults">${historyResultsHtml()}</div>`;
}

// ---------- Toasts ----------

let toastSeq = 0;

// t: { key?, text (HTML), tone?: "" | "warn" | "bad", icon?, actions?: [{ label, act, data }], ms? }
// A toast with a key replaces any other with the same key.
function toast(t) {
  state.toasts = state.toasts.filter((x) => !t.key || x.key !== t.key);
  const item = { id: ++toastSeq, ...t };
  state.toasts.push(item);
  if (state.toasts.length > 3) state.toasts.shift();
  renderToasts();
  if (t.ms !== 0) setTimeout(() => dismissToast(item.id), t.ms ?? 7000);
}

function dismissToast(id) {
  const before = state.toasts.length;
  state.toasts = state.toasts.filter((x) => x.id !== id && x.key !== id);
  if (state.toasts.length !== before) renderToasts();
}

function renderToasts() {
  document.getElementById("toasts").innerHTML = state.toasts.map((t) => `<div class="toast ${t.tone ?? ""}" role="status">
      ${t.icon ? icon(t.icon) : ""}<span class="toast-text">${t.text}</span>
      ${(t.actions ?? []).map((a) => `<button class="btn btn-small" data-act="${a.act}" data-toast="${t.id}"
        ${Object.entries(a.data ?? {}).map(([k, v]) => `data-${k}="${esc(v)}"`).join(" ")} ${a.title ? `title="${esc(a.title)}"` : ""}>${a.label}</button>`).join("")}
      <button class="icon-btn toast-x" data-act="toast-close" data-toast="${t.id}" aria-label="Dismiss">${icon("x")}</button>
    </div>`).join("");
}

// ---------- Simulated audio levels ----------
// There's no real media, so speech is faked as turns: you talk, a pause, they talk.
// Only the call with the audio focus is heard, so only it gets meters.

const talk = { who: "pause", until: 0 };
let lastNudge = 0;

// ---------- Call audio, drawn ----------
// An audio-only call has no picture, so its card shows the conversation instead, as two rows of
// frequency bars that stay put and only change height: what you hear from them grows up from
// the centre line, what you send grows down from it. Bars rise quickly and fall slowly, so the
// picture settles rather than flickers. Quiet and broken look different on purpose: quiet is a
// row of small resting pills; no packets at all (one-way audio, the classic SIP/NAT fault) makes
// the bars vanish, leaving one thin line, and after 2 s a note says which way it's broken. Only the call with the audio
// focus is heard, so other calls are flat and dimmed, and a held call freezes. With reduced
// motion on, each lane is a still level bar instead.
//
// In the real app the bands come from an AnalyserNode (getByteFrequencyData) on the received
// track and on the microphone; here they're shaped from the simulated speech level.

const VIZ_BANDS = 28;
const VIZ_TARGET_MS = 70;        // new band targets ~14 times a second; bars ease between them
const VIZ_FALL_MS = 260;         // how slowly a bar sinks once the sound behind it stops
const vizState = new Map();      // call id -> { them, me, themTarget, meTarget, level }
let vizLastFrame = 0, vizLastTarget = 0;
const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");

// Speech comes in syllables: a level that rises and falls a few times a second.
const speech = (seed) => Math.min(1, (0.25 + Math.random() * 0.75) * (0.35 + 0.65 * Math.abs(Math.sin(now() / 110 + seed))));

function vizSample(l) {
  const heard = l.id === state.audioFocus && l.status === "connected";
  const them = l.rxDead ? null : !heard ? 0 : talk.who === "them" ? speech(l.id) : Math.random() * 0.04;
  // Your lane is what your mic sends; muted is flat (a choice), not dashed (a fault).
  const me = !heard || micMuted(l) ? 0 : talk.who === "me" ? speech(l.id + 7) : Math.random() * 0.04;
  return { them, me };
}

// A voice's energy sits low: a strong band around the fundamental and first formant, a weaker
// one higher up, little at the top. The formants drift a little so it doesn't look stamped.
function speechSpectrum(level, seed, out) {
  const drift = 0.04 * Math.sin(now() / 420 + seed);
  for (let b = 0; b < VIZ_BANDS; b++) {
    const x = b / (VIZ_BANDS - 1);
    const shape = Math.exp(-(((x - 0.18 - drift) / 0.16) ** 2)) + 0.55 * Math.exp(-(((x - 0.48 + drift) / 0.13) ** 2)) + 0.12 * (1 - x);
    out[b] = level === null ? 0 : Math.min(1, level * shape * (0.7 + Math.random() * 0.45));
  }
}

function vizFrame(t) {
  const dt = Math.min(250, t - (vizLastFrame || t));
  vizLastFrame = t;
  const newTargets = t - vizLastTarget >= VIZ_TARGET_MS;
  if (newTargets) vizLastTarget = t;
  const fall = Math.exp(-dt / VIZ_FALL_MS);
  for (const l of state.calls) {
    if (l.video || !isLive(l)) { vizState.delete(l.id); continue; }
    let v = vizState.get(l.id);
    if (!v) {
      v = { them: new Float32Array(VIZ_BANDS), me: new Float32Array(VIZ_BANDS), themTarget: new Float32Array(VIZ_BANDS), meTarget: new Float32Array(VIZ_BANDS), level: { them: 0, me: 0 } };
      vizState.set(l.id, v);
    }
    if (l.status === "held") continue;   // frozen
    if (newTargets) {
      const s = vizSample(l);
      v.level = s;
      speechSpectrum(s.them, l.id, v.themTarget);
      speechSpectrum(s.me, l.id + 7, v.meTarget);
    }
    for (const k of ["them", "me"]) {
      const bars = v[k], target = v[`${k}Target`];
      for (let b = 0; b < VIZ_BANDS; b++) {
        // Rise most of the way at once; sink slowly.
        bars[b] = target[b] > bars[b] ? bars[b] + (target[b] - bars[b]) * 0.6 : Math.max(target[b], bars[b] * fall);
      }
    }
  }
  document.querySelectorAll("canvas[data-viz]").forEach((c) => {
    const l = callById(Number(c.dataset.viz));
    const v = l && vizState.get(l.id);
    if (v) drawViz(c, l, v);
  });
  requestAnimationFrame(vizFrame);
}

function drawViz(c, l, v) {
  const dpr = devicePixelRatio || 1;
  const w = Math.round(c.clientWidth * dpr), ht = Math.round(c.clientHeight * dpr);
  if (!w || !ht) return;
  if (c.width !== w || c.height !== ht) { c.width = w; c.height = ht; }
  const g = c.getContext("2d");
  g.clearRect(0, 0, w, ht);
  const css = getComputedStyle(c);
  const colour = { them: css.color, me: (l.txDead ? css.getPropertyValue("--viz-warn") : css.getPropertyValue("--viz-me")).trim() };
  const dead = { them: !!l.rxDead, me: false };
  const mid = ht / 2, gap = 3 * dpr, room = mid - gap - 2 * dpr;
  const lanes = [["them", -1], ["me", 1]];   // them grows up from the centre, you grow down

  for (const [k, dir] of lanes) {
    const base = mid + dir * gap;
    g.fillStyle = colour[k];
    if (dead[k]) {
      // No packets: no bars at all, just one thin line, unlike the resting pills of a quiet line.
      g.globalAlpha = 0.45;
      g.fillRect(0, base - dpr / 2, w, dpr);
      continue;
    }
    if (reducedMotion.matches) {
      // Still: one level bar per lane, its length the loudness.
      const level = v.level[k] ?? 0;
      g.globalAlpha = 0.9;
      g.fillRect(0, mid + dir * room / 2 - 3 * dpr, Math.max(2 * dpr, Math.min(1, level * 1.6) * w), 6 * dpr);
      continue;
    }
    const slot = w / VIZ_BANDS, bar = Math.max(2 * dpr, slot * 0.6), pad = (slot - bar) / 2;
    g.globalAlpha = 0.9;
    for (let b = 0; b < VIZ_BANDS; b++) {
      const len = Math.max(2 * dpr, v[k][b] * room);
      const x = b * slot + pad, y = dir < 0 ? base - len : base;
      g.beginPath();
      g.roundRect ? g.roundRect(x, y, bar, len, Math.min(bar, len) / 2) : g.rect(x, y, bar, len);
      g.fill();
    }
  }
  g.globalAlpha = 1;
}

// Diagnostics: one-way audio. "rx": their packets stop arriving. "tx": their receiver reports
// show none of yours arriving. The note waits 2 s, so a short gap isn't called a fault.
function toggleAudioFault(kind) {
  const l = callById(state.selected);
  if (!l || !isLive(l)) { log("Select a live call first"); return; }
  const dead = kind === "rx" ? "rxDead" : "txDead", shown = `${dead}Shown`;
  if (l[dead]) {
    l[dead] = false;
    l[shown] = false;
    log(`Call ${l.id}: ${kind === "rx" ? `audio from ${l.name} is arriving again` : `${l.name} is getting your audio again`}`);
    render();
    return;
  }
  l[dead] = true;
  log(`Call ${l.id}: ${kind === "rx" ? `RTP from ${l.name} stopped` : `RTCP from ${l.name}: none of your packets arriving`}${l.video ? " (the waveform shows on audio-only calls)" : ""}`);
  setTimeout(() => {
    if (!l[dead] || !isLive(l)) return;
    l[shown] = true;
    log(`Call ${l.id}: no audio ${kind === "rx" ? `from ${l.name}` : `reaching ${l.name}`} for 2 s: showing a note`);
    render();
  }, 2000);
}

function speechTick() {
  const t = now();
  if (t > talk.until) {
    talk.who = talk.who !== "pause" ? "pause" : Math.random() < 0.5 ? "me" : "them";
    talk.until = t + (talk.who === "pause" ? 400 + Math.random() * 1200 : 1500 + Math.random() * 3000);
  }
  const level = (who) => (talk.who === who ? 0.3 + Math.random() * 0.7 : Math.random() * 0.05);
  const focus = callById(state.audioFocus);
  const live = focus?.status === "connected";
  const muted = live && micMuted(focus);
  const me = live && !muted ? level("me") : 0;
  const them = live ? level("them") : 0;
  document.querySelectorAll("[data-meter]").forEach((el) =>
    el.style.setProperty("--lvl", Number(el.dataset.meter) === focus?.id ? me.toFixed(2) : 0));
  document.querySelectorAll("[data-speak]").forEach((el) =>
    el.style.setProperty("--spk", Number(el.dataset.speak) === focus?.id ? them.toFixed(2) : 0));

  // Talking while muted: a gentle nudge, at most every 30 seconds.
  if (muted && talk.who === "me" && t - lastNudge > 30_000 && t - state.mutedAt > 1500) {
    lastNudge = t;
    toast({
      key: "muted", tone: "warn", icon: "micOff", ms: 6000,
      text: state.muteAll ? "You're talking, but <b>all calls are muted</b>."
        : `You're talking, but you're muted. <b>${esc(focus.name)}</b> can't hear you.`,
      actions: [state.muteAll
        ? { label: "Unmute all", act: "mute-all", title: "Shift+M" }
        : { label: "Unmute", act: "unmute-focus", title: "M" }],
    });
  }
  if (!muted && state.toasts.some((x) => x.key === "muted")) dismissToast("muted");
}

// ---------- Call quality and encryption ----------
// There's no real media, so RTCP-style stats are simulated per call. "From them" is what we
// measure on the stream we receive; "To them" is what their receiver reports say about ours.
// netProfile: good | theirs (their connection is poor). state.netBad makes *your* side poor.

const rand = (a, b) => a + Math.random() * (b - a);

function netOf(l) {
  if (!l.net) {
    const acc = accountById(l.account);
    l.netProfile ??= Math.random() < 0.2 ? "theirs" : "good";
    // Optional SRTP is used when the other side accepts it; tel: calls go via a gateway that doesn't.
    l.srtp ??= acc?.srtp !== "Off" && !/^tel:/.test(l.uri);
    l.net = { rx: { loss: 0.3, jitter: 8 }, tx: { loss: 0.2, jitter: 7 }, rtt: 40 };
    stepNet(l, 1);
  }
  return l.net;
}

function stepNet(l, weight = 0.4) {
  const n = l.net;
  const theirs = l.netProfile === "theirs", mine = state.netBad;
  const target = {
    rx: { loss: theirs ? rand(3, 9) : mine ? rand(2, 6) : rand(0, 0.8), jitter: theirs ? rand(35, 80) : mine ? rand(30, 60) : rand(4, 14) },
    tx: { loss: mine ? rand(4, 10) : rand(0, 0.6), jitter: mine ? rand(40, 90) : rand(3, 12) },
    rtt: (mine ? rand(140, 260) : rand(30, 60)) + (theirs ? rand(40, 90) : 0),
  };
  const mix = (a, b) => a * (1 - weight) + b * weight;
  for (const d of ["rx", "tx"]) for (const k of ["loss", "jitter"]) n[d][k] = mix(n[d][k], target[d][k]);
  n.rtt = mix(n.rtt, target.rtt);
}

// 0–4 bars for one direction.
function dirLevel({ loss, jitter }) {
  if (loss < 1 && jitter < 20) return 4;
  if (loss < 2.5 && jitter < 40) return 3;
  if (loss < 5 && jitter < 70) return 2;
  return loss < 10 ? 1 : 0;
}

function netSummary(l) {
  const n = netOf(l);
  const rx = dirLevel(n.rx), tx = dirLevel(n.tx), level = Math.min(rx, tx);
  const word = level >= 4 ? "Good" : level === 3 ? "OK" : level === 2 ? "Fair" : "Poor";
  const tone = level >= 3 ? "good" : level === 2 ? "fair" : "poor";
  const rxBad = rx < 3, txBad = tx < 3;
  const others = state.calls.filter((x) => x !== l && x.status === "connected");
  const othersBad = others.some((x) => Math.min(dirLevel(netOf(x).rx), dirLevel(netOf(x).tx)) < 3);
  let verdict;
  if (!rxBad && !txBad) verdict = "Good both ways.";
  else if (rxBad && !txBad) verdict = `Probably <b>their</b> connection: you're receiving ${esc(l.name)} poorly, but they're receiving you fine${others.length && !othersBad ? ", and your other calls are fine" : ""}.`;
  else if (txBad && !rxBad) verdict = `Probably <b>your</b> upload: ${esc(l.name)} is receiving you poorly, so they may hear you breaking up.`;
  else verdict = `Probably <b>your</b> connection: it's poor both ways${othersBad ? ", and your other calls are affected too" : ""}.`;
  return { n, level, word, tone, verdict, yours: txBad };
}

function barsSvg(level) {
  return `<svg class="net-bars" viewBox="0 0 16 13" aria-hidden="true">${[4, 7, 10, 13].map((h, i) =>
    `<rect x="${i * 4}" y="${13 - h}" width="3" height="${h}" rx="0.8" class="${i < level ? "on" : ""}"/>`).join("")}</svg>`;
}

function netLabel(l) {
  const s = netSummary(l);
  return `Call quality: ${s.word}. ${l.srtp ? "Encrypted" : "Not encrypted"}.`;
}

function netInnerHtml(l) {
  const s = netSummary(l);
  return `${barsSvg(s.level)}${l.srtp ? `<svg class="net-lock" viewBox="0 0 24 24" aria-hidden="true">${ICONS.lock}</svg>` : ""}`;
}

// Small badge for the sidebar and grid tiles; only live, un-held calls have media to measure.
function netBadgeHtml(l) {
  if (l.status !== "connected") return "";
  return `<span class="net q-${netSummary(l).tone}" data-quality="${l.id}" title="">${netInnerHtml(l)}</span>`;
}

// The selected call's version is a real button, so the stats can be reached by keyboard and by tap.
function netButtonHtml(l) {
  return `<button class="net net-btn q-${netSummary(l).tone}" data-quality="${l.id}" data-act="net-pop" aria-label="${netLabel(l)}" aria-describedby="netPop">${netInnerHtml(l)}</button>`;
}

function netPopHtml(l) {
  const s = netSummary(l);
  const pct = (v) => `${v.toFixed(1)}%`, ms = (v) => `${Math.round(v)} ms`;
  const cell = (v, bad, fmt) => `<td class="${bad ? "bad" : ""}">${fmt(v)}</td>`;
  const acc = accountById(l.account);
  return `<div class="net-pop-head"><span class="net q-${s.tone}">${barsSvg(s.level)}</span><b>${s.word} quality</b><span class="net-pop-name">${esc(l.name)}</span></div>
    <table class="net-table">
      <thead><tr><th></th><th>From them</th><th>To them</th></tr></thead>
      <tbody>
        <tr><th>Packet loss</th>${cell(s.n.rx.loss, s.n.rx.loss >= 2.5, pct)}${cell(s.n.tx.loss, s.n.tx.loss >= 2.5, pct)}</tr>
        <tr><th>Jitter</th>${cell(s.n.rx.jitter, s.n.rx.jitter >= 40, ms)}${cell(s.n.tx.jitter, s.n.tx.jitter >= 40, ms)}</tr>
        <tr><th>Round trip</th><td colspan="2">${ms(s.n.rtt)}</td></tr>
      </tbody>
    </table>
    <p class="net-verdict ${s.level >= 3 ? "" : "bad"}">${s.verdict}</p>
    <div class="net-enc ${l.srtp ? "on" : ""}">
      <svg viewBox="0 0 24 24" aria-hidden="true">${l.srtp ? ICONS.lock : ICONS.unlock}</svg>
      <span>${l.srtp ? "<b>Encrypted</b> · SRTP, keys exchanged with DTLS"
        : `<b>Not encrypted</b> · ${acc?.srtp === "Off" ? `SRTP is off for ${esc(acc.name)}` : "the other side didn't accept SRTP"}`}</span>
    </div>
    <div class="net-codec">${l.video ? "Opus 48 kHz · VP8" : "Opus 48 kHz"}</div>`;
}

const netPop = Object.assign(document.createElement("div"), { id: "netPop", className: "net-pop", hidden: true });
netPop.setAttribute("role", "tooltip");
document.body.append(netPop);
let netPopFor = null; // { id, anchor }

function showNetPop(anchor) {
  const l = callById(Number(anchor.dataset.quality));
  if (!l || l.status !== "connected") return hideNetPop();
  netPopFor = { id: l.id, anchor };
  netPop.innerHTML = netPopHtml(l);
  netPop.hidden = false;
  const r = anchor.getBoundingClientRect(), w = netPop.offsetWidth, h = netPop.offsetHeight;
  // Sidebar badges open to the right, into the main area; everything else drops down, right-aligned.
  if (anchor.closest("#sidebar") && r.right + 12 + w < innerWidth) {
    netPop.style.left = `${r.right + 12}px`;
    netPop.style.top = `${Math.min(Math.max(8, r.top - 12), innerHeight - h - 8)}px`;
    return;
  }
  const left = Math.min(Math.max(8, r.right - w), innerWidth - w - 8);
  const below = r.bottom + 8 + h < innerHeight;
  netPop.style.left = `${left}px`;
  netPop.style.top = `${below ? r.bottom + 8 : Math.max(8, r.top - h - 8)}px`;
}

function hideNetPop() { netPop.hidden = true; netPopFor = null; }

function netTick() {
  for (const l of state.calls) if (l.status === "connected") { netOf(l); stepNet(l); }
  document.querySelectorAll("[data-quality]").forEach((el) => {
    const l = callById(Number(el.dataset.quality));
    if (!l || l.status !== "connected") return;
    const s = netSummary(l);
    el.classList.remove("q-good", "q-fair", "q-poor");
    el.classList.add(`q-${s.tone}`);
    el.innerHTML = netInnerHtml(l);
    if (el.matches("button")) el.setAttribute("aria-label", netLabel(l));
  });
  if (netPopFor) {
    const anchor = document.querySelector(`[data-quality="${netPopFor.id}"]`);
    if (anchor && document.body.contains(netPopFor.anchor)) netPop.innerHTML = netPopHtml(callById(netPopFor.id));
    else hideNetPop();
  }
}

// ---------- Hold reminders ----------
// A gentle toast after a minute on hold, then every two minutes, so nobody is forgotten.

function holdTick() {
  for (const l of state.calls) {
    if (l.status !== "held") continue;
    const held = now() - l.heldAt;
    const due = 60_000 + 120_000 * (l.holdNudges ?? 0);
    if (held < due) continue;
    l.holdNudges = (l.holdNudges ?? 0) + 1;
    const mins = Math.floor(held / 60_000);
    toast({
      key: `hold-${l.id}`, icon: "pause", ms: 12000,
      text: `<b>${esc(l.name)}</b> has been on hold for ${mins === 1 ? "a minute" : `${mins} minutes`}.`,
      actions: [{ label: "Resume", act: "resume", data: { call: l.id } }],
    });
    if (l.holdNudges === 1) renderCallList(); // Turns its hold time amber.
    if (l.id === state.selected && state.selected !== null) renderStage();
  }
}

// ---------- Real-time text (RFC 4103, T.140) ----------
// Text belongs to a call, like its audio and video. Each character goes out as it's typed,
// backspaces included, and a line ends on Enter. l.chat holds the conversation in the order each
// line was started; open:true marks a line still being typed (at most one per side).

const pick = (list) => list[Math.floor(Math.random() * list.length)];
const theirLive = (l) => l.chat?.find((m) => !m.me && m.open);
const myLive = (l) => l.chat?.find((m) => m.me && m.open);
// Is this call's text panel on screen right now? It's only shown in Focus view; in the grid, the
// tiles' status lines carry the typing and unread cues instead.
const textShowing = (l) => l.id === state.selected && !!l.textOpen && !inGrid();
const lastTheirs = (l) => l.chat?.findLast((m) => !m.me && !m.open);
const PEEK_MS = 6000;

const REMOTE_LINES = [
  "Can you send me the meeting link afterwards?",
  "Sorry, the audio cut out for a second there.",
  "I'm on a train, so typing is easier for me right now.",
  "Thursday at 3 works for me.",
  "Could you spell the reference number for me?",
];
const REMOTE_REPLIES = ["Ok, thanks!", "Got it.", "Sure, one moment.", "Sounds good.", "Yes, that's right."];

// Opening text from the grid switches to Focus view, the only place the panel is shown.
function toggleTextPanel(l, open = !textShowing(l)) {
  if (!l) return;
  if (open && inGrid()) { state.view = "focus"; savePref("view", "focus"); }
  l.textOpen = open;
  if (open) { l.unread = 0; l.textAuto = true; l.peekUntil = 0; }
  render();
  if (open && l.rtt && l.status === "connected") document.getElementById("rttInput")?.focus();
}

// Adding text to a call that didn't start with it: a re-INVITE with an m=text line.
function addTextToCall(l) {
  if (!l || l.rtt || l.rttAdding) return;
  l.rttAdding = true;
  log(`Call ${l.id}: re-INVITE adding m=text (T.140 + RED) → ${l.name}`);
  render();
  setTimeout(() => {
    l.rttAdding = false;
    if (l.status === "ended") return;
    if (l.rttSupported) {
      l.rtt = true;
      log(`Call ${l.id}: 200 OK — real-time text on`);
      render();
      if (textShowing(l)) document.getElementById("rttInput")?.focus();
    } else {
      l.rttDeclined = true;
      log(`Call ${l.id}: 200 OK with m=text 0 — ${l.name}'s phone declined text`);
      toast({ key: `rtt-${l.id}`, tone: "warn", icon: "text", text: `<b>${esc(l.name)}</b>'s phone doesn't support real-time text.` });
      render();
    }
  }, 900);
}

// The text field is your live line. Work out what changed since the last keystroke and send it the
// way T.140 can: backspaces for anything removed from the end, then the new characters.
function onTextInput(l, value) {
  if (!l?.rtt) return;
  l.chat ??= [];
  const old = l.draft ?? "";
  let same = 0;
  while (same < old.length && same < value.length && old[same] === value[same]) same++;
  l.sent = (l.sent ?? 0) + (value.length - same) + (old.length - same);
  l.draft = value;
  let m = myLive(l);
  if (!m && value) { m = { me: true, text: "", at: now(), open: true }; l.chat.push(m); }
  if (m) {
    m.text = value;
    if (!value) l.chat = l.chat.filter((x) => x !== m); // Erased back to nothing.
  }
  refreshTranscript(l);
}

function sendLine(l) {
  const m = l && myLive(l);
  if (!m || !m.text.trim()) return;
  m.open = false;
  l.draft = "";
  log(`Call ${l.id}: T.140 → ${esc(l.name)}: “${esc(m.text)}” + line end (${l.sent ?? m.text.length} keystrokes sent live)`);
  l.sent = 0;
  document.getElementById("rttInput").value = "";
  refreshTranscript(l);
  if (Math.random() < 0.7) setTimeout(() => remoteType(l, pick(REMOTE_REPLIES)), 1200 + Math.random() * 1500);
}

// A line typed the way people type: uneven gaps, and now and then a slip that gets backspaced.
function typingSteps(text) {
  const out = [];
  let slipped = false;
  text.split(" ").forEach((w, i) => {
    if (i) out.push(" ");
    if (!slipped && w.length >= 5 && Math.random() < 0.3) {
      slipped = true;
      const k = Math.floor(w.length / 2);
      out.push(...w.slice(0, k), w[k + 1], w[k], "pause", "\b", "\b", w[k], w[k + 1], ...w.slice(k + 2));
    } else out.push(...w);
  });
  return out;
}

// The other side types a line. Returns false if they're already typing on this call.
function remoteType(l, text) {
  if (!l.rtt || l.typingJob) return false;
  l.chat ??= [];
  const steps = typingSteps(text);
  let i = 0;
  l.typingJob = true;
  const step = () => {
    if (l.status === "ended" || !l.rtt) { l.typingJob = false; return; }
    if (l.status !== "connected") { setTimeout(step, 500); return; } // Text pauses while on hold.
    let m = theirLive(l);
    if (!m) {
      m = { me: false, text: "", at: now(), open: true };
      l.chat.push(m);
      // The first time they type on the call you're looking at, the panel opens by itself.
      if (l.id === state.selected && !inGrid() && !l.textOpen && !l.textAuto) { l.textOpen = true; l.textAuto = true; l.unread = 0; }
      textStateChanged(l);
    }
    const s = steps[i++];
    if (s === "\b") m.text = m.text.slice(0, -1);
    else if (s !== "pause") m.text += s;
    refreshTranscript(l);
    if (i < steps.length) {
      setTimeout(step, s === "pause" ? 700 : s === "\b" ? 140 : s === " " ? 120 + Math.random() * 160 : 60 + Math.random() * 120);
      return;
    }
    setTimeout(() => {
      m.open = false;
      l.typingJob = false;
      if (l.status === "ended") return;
      if (!textShowing(l)) {
        l.unread = (l.unread ?? 0) + 1;
        // Collapsed on the call you're looking at: the new line pops up over the video for a moment.
        if (l.id === state.selected && !inGrid()) {
          l.peekUntil = now() + PEEK_MS;
          setTimeout(() => { if (now() >= l.peekUntil) document.querySelector(`.rtt-peek[data-peek="${l.id}"]`)?.remove(); }, PEEK_MS + 50);
        }
      }
      log(`Call ${l.id}: T.140 ← ${esc(l.name)}: “${esc(m.text)}” + line end`);
      textStateChanged(l);
    }, 350);
  };
  step();
  return true;
}

// Typing started or a line finished: redraw the call list and stage. Single characters only
// patch the transcript (refreshTranscript), so clicks elsewhere aren't lost mid-render.
function textStateChanged(l) {
  renderCallList();
  renderStage();
}

function refreshTranscript(l) {
  const el = document.getElementById("rttLog");
  if (!el || !textShowing(l)) return;
  const atEnd = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
  el.innerHTML = transcriptHtml(l);
  if (atEnd) el.scrollTop = el.scrollHeight;
}

function transcriptHtml(l) {
  const chat = l.chat ?? [];
  if (!chat.length) return `<p class="rtt-empty">Nothing typed yet.</p>`;
  let who = null;
  return chat.map((m) => {
    const head = m.me !== who ? `<div class="rtt-who ${m.me ? "me" : ""}">${m.me ? "You" : esc(l.name)}</div>` : "";
    who = m.me;
    // Lines still being typed change with every key, so screen readers wait for the finished line.
    return `${head}<div class="rtt-msg ${m.me ? "me" : "them"} ${m.open ? "live" : ""}" ${m.open ? 'aria-live="off"' : ""} title="${clockTime(m.at)}">${esc(m.text)}${m.open ? `<span class="rtt-caret" aria-hidden="true"></span>` : ""}</div>`;
  }).join("");
}

function textPanelHtml(l) {
  const connected = l.status === "connected";
  let compose;
  if (l.rtt) {
    const why = l.status === "held" ? `Text is paused while ${l.name} is on hold`
      : l.status === "ended" ? "The call has ended"
      : l.status !== "connected" ? "Text starts when the call connects" : "";
    compose = `<input id="rttInput" class="rtt-input" type="text" value="${esc(l.draft ?? "")}" autocomplete="off" spellcheck="true"
        placeholder="${esc(why || `Type to ${l.name}, Enter for a new line`)}" aria-label="Text to ${esc(l.name)}" ${connected ? "" : "disabled"}>
      <div class="rtt-hint ${connected ? "" : "off"}">${connected ? `${esc(l.name)} sees each letter as you type.` : esc(why)}</div>`;
  } else if (l.rttAdding) {
    compose = `<div class="rtt-note">Asking ${esc(l.name)}'s phone to add text…</div>`;
  } else if (l.rttDeclined && !l.rttSupported) {
    compose = `<div class="rtt-note">${esc(l.name)}'s phone doesn't support real-time text.</div>`;
  } else {
    compose = `<div class="rtt-note">Text isn't set up on this call.
      <button class="btn btn-small" data-act="text-add" ${connected ? "" : "disabled"}>${icon("text")}Add text to this call</button></div>`;
  }
  return `<section class="rtt" aria-label="Real-time text with ${esc(l.name)}">
    <div class="rtt-head">
      <span class="rtt-title">${icon("text")}Text</span><span class="rtt-sub">Real-time · RFC 4103</span>
      <button class="icon-btn rtt-collapse" data-act="text-close" aria-expanded="true" aria-label="Collapse text" title="Collapse text (T)">${icon("chevronRight")}</button>
    </div>
    <div class="rtt-log" id="rttLog" role="log" aria-label="Conversation">${transcriptHtml(l)}</div>
    <div class="rtt-compose">${compose}</div>
  </section>`;
}

// The collapsed panel: a slim rail beside the video (a bar under it on narrow screens) that still
// says when they're typing and how many lines you haven't read. Clicking it expands the panel.
function textRailHtml(l) {
  const typing = !!theirLive(l) && l.status === "connected";
  const n = l.unread ?? 0;
  const last = lastTheirs(l);
  const preview = typing ? `<span class="st-typing">${esc(l.name)} is typing</span>`
    : n && last ? `“${esc(last.text)}”` : "Show the conversation";
  const label = `Show text${n ? `, ${n} new` : ""}${typing ? `, ${l.name} is typing` : ""}`;
  return `<button class="rtt-rail ${n ? "has-new" : ""} ${typing ? "is-typing" : ""}" data-act="text" aria-expanded="false" aria-label="${esc(label)}" title="Show text (T)">
    <span class="rtt-rail-expand">${icon("chevronRight")}</span>
    <span class="rtt-rail-icon">${icon("text")}${n ? `<span class="rtt-rail-count">${n}</span>` : ""}</span>
    <span class="rtt-rail-label">Text</span>
    ${typing ? `<span class="rtt-rail-dots" aria-hidden="true"><i></i><i></i><i></i></span>` : ""}
    <span class="rtt-rail-preview">${preview}</span>
  </button>`;
}

// A just-finished line from them, shown briefly over the video while the panel is collapsed.
function textPeekHtml(l) {
  const last = lastTheirs(l);
  if (!last || !(l.peekUntil > now())) return "";
  // A re-render mustn't restart the fade, so it's timed from what's left.
  const fadeIn = Math.max(0, l.peekUntil - now() - 300);
  return `<button class="rtt-peek" data-peek="${l.id}" data-act="text" title="Show text (T)" style="animation-delay: 0s, ${fadeIn}ms">
    <span class="rtt-peek-who">${icon("text")}${esc(l.name)}</span>${esc(last.text)}</button>`;
}

// ---------- Device menus (on the Mute and Camera buttons) ----------

function deviceMenuHtml(kind) {
  const groups = kind === "mic" ? [["mic", "Microphone"], ["speaker", "Speaker"]] : [["camera", "Camera"]];
  return `<div class="device-menu" role="menu" aria-label="${kind === "mic" ? "Audio devices" : "Camera"}">
    ${groups.map(([key, label]) => `<div class="device-menu-label">${label}</div>
      ${DEVICES[key].map((d) => {
        const on = state.devices[key] === d;
        return `<button class="device-option" role="menuitemradio" aria-checked="${on}" data-act="device-pick" data-key="${key}" data-dev="${esc(d)}">
          <span class="device-check">${on ? icon("check") : ""}</span>${esc(d)}</button>`;
      }).join("")}`).join("")}
    <div class="device-menu-sep"></div>
    <button class="device-option" role="menuitem" data-act="settings"><span class="device-check">${icon("settings")}</span>All audio and video settings</button>
  </div>`;
}

function toggleDeviceMenu(kind) {
  state.menu = state.menu === kind ? null : kind;
  renderStage();
  document.querySelector('.device-option[aria-checked="true"]')?.focus();
}

function pickDevice(key, dev) {
  state.devices[key] = dev;
  state.menu = null;
  const label = { mic: "Microphone", speaker: "Speaker", camera: "Camera" }[key];
  log(`Devices: ${label.toLowerCase()} → ${esc(dev)}`);
  toast({ key: "device", icon: key === "camera" ? "video" : key === "speaker" ? "headphones" : "mic", ms: 3000, text: `${label}: <b>${esc(dev)}</b>` });
  render();
}

// ---------- Recent calls and contacts under the address field ----------

function relTime(at) {
  const mins = Math.round((now() - at) / 60_000);
  if (mins < 1) return "Just now";
  if (mins < 60) return `${mins} min ago`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `${hours} h ago`;
  if (hours < 48) return "Yesterday";
  return `${Math.round(hours / 24)} days ago`;
}

function dialSuggestions(q) {
  q = q.trim().toLowerCase();
  const match = (x) => !q || x.name.toLowerCase().includes(q) || x.uri.toLowerCase().includes(q);
  const seen = new Set();
  const recent = RECENTS.filter((r) => !seen.has(r.uri) && seen.add(r.uri) && match(r)).slice(0, 6);
  const contacts = CONTACTS.filter((c) => match(c) && !seen.has(c.uri)).slice(0, 4);
  return [...recent.map((r) => ({ ...r, group: "Recent" })), ...contacts.map((c) => ({ ...c, group: "Contacts" }))];
}

function openDialSuggest() {
  const q = document.getElementById("destInput").value;
  const items = dialSuggestions(q);
  // Typing part of a name highlights the first match so Enter calls it; a full address or number doesn't.
  const looksComplete = /[@:]/.test(q) || /^[+\d*#\s]+$/.test(q.trim());
  state.dial = { open: items.length > 0, items, active: q.trim() && !looksComplete && items.length ? 0 : -1 };
  renderDialSuggest();
}

function closeDialSuggest() {
  if (!state.dial.open) return;
  state.dial = { open: false, active: -1, items: [] };
  renderDialSuggest();
}

function renderDialSuggest() {
  const el = document.getElementById("dialSuggest");
  const input = document.getElementById("destInput");
  const { open, items, active } = state.dial;
  el.hidden = !open;
  input.setAttribute("aria-expanded", open);
  if (active >= 0) input.setAttribute("aria-activedescendant", `sug-${active}`); else input.removeAttribute("aria-activedescendant");
  if (!open) { el.innerHTML = ""; return; }
  let group = "";
  el.innerHTML = items.map((it, i) => {
    const head = it.group !== group ? `<div class="suggest-group" role="presentation">${(group = it.group)}</div>` : "";
    const meta = it.group === "Contacts" ? ""
      : `<span class="suggest-meta ${it.dir === "missed" ? "bad" : ""}">${icon(it.dir === "missed" ? "missed" : it.dir === "in" ? "inbound" : "outgoing")}
          ${it.dir === "missed" ? "Missed · " : it.note ? `${esc(it.note)} · ` : ""}${relTime(it.at)}</span>`;
    return `${head}<div class="suggest-item ${i === active ? "active" : ""}" role="option" id="sug-${i}" aria-selected="${i === active}" data-idx="${i}">
      <span class="line-avatar" style="--tile-bg:${TILE_COLOURS[i % TILE_COLOURS.length]}">${esc(initial(it.name))}</span>
      <span class="suggest-main"><span class="suggest-name">${esc(it.name)}</span><span class="suggest-uri">${esc(it.uri)}</span></span>
      ${meta}
      <span class="suggest-call" aria-hidden="true">${icon("phone")}</span>
    </div>`;
  }).join("");
  el.querySelector(".suggest-item.active")?.scrollIntoView({ block: "nearest" });
}

function callSuggestion(i) {
  const it = state.dial.items[i];
  if (it) placeCall(it.uri, it.name);
}

// ---------- Account actions ----------

function openAccounts() {
  // A fresh open starts on the overview table; coming back from a call keeps the page you left.
  if (!accountsOpen()) Object.assign(state.acct, { open: true, selected: null });
  select(ACCOUNTS_TAB);
}

// Manage accounts opens the view, or closes it if it's already showing.
function toggleAccounts() {
  if (accountsActive()) closeAccounts(); else openAccounts();
}

// Back to the table. Any unsaved edits stay as drafts and show in the table.
function showAccountsOverview() {
  Object.assign(state.acct, { selected: null, error: null, confirmRemove: null });
  render();
}

// Closing throws away drafts, so unsaved edits get a warning first.
function closeAccounts(force = false) {
  if (!force && hasUnsavedAccounts()) { state.modal = "discard"; renderModal(); return; }
  Object.assign(state.acct, { open: false, selected: null, drafts: {}, error: null, confirmRemove: null });
  state.modal = null;
  // Land on the same call the "Back to …" button offered.
  if (accountsActive()) state.selected = returnCall()?.id ?? null;
  render();
}

// The call to offer a way back to: the one with audio, else the newest still going.
function returnCall() {
  const calls = callsNewestFirst().filter((x) => x.status !== "ended");
  return callById(state.audioFocus) ?? calls.find((x) => isLive(x)) ?? calls[0];
}

function register(a, why = "") {
  clearTimeout(a.reg.timer);
  if (!a.enabled || !a.register) { a.reg = { state: "off" }; render(); return; }
  a.reg = { state: "registering" };
  log(`${esc(a.name)}: REGISTER ${esc(aor(a))} via ${a.transport} to ${esc(a.registrar || a.domain)}${why ? ` (${why})` : ""}`);
  render();
  a.reg.timer = setTimeout(() => {
    // Any domain containing "fail" times out, so the error state can be tried from the form.
    if (/fail/i.test(a.registrar || a.domain)) { failRegistration(a, "408 Request Timeout"); return; }
    a.reg = { state: "registered", expiresAt: now() + a.expiry * 1000 };
    log(`${esc(a.name)}: 200 OK, registered (expires ${a.expiry})`);
    render();
  }, 900);
}

function failRegistration(a, error) {
  clearTimeout(a.reg.timer);
  a.reg = { state: "failed", error };
  log(`${esc(a.name)}: <b>${error}</b>, not registered`);
  render();
}

function unregister(a) {
  clearTimeout(a.reg.timer);
  if (a.reg.state === "registered") log(`${esc(a.name)}: REGISTER Expires: 0, unregistered`);
  a.reg = { state: "off" };
}

function toggleAccountEnabled(a) {
  a.enabled = !a.enabled;
  if (a.enabled) { register(a, "enabled"); return; }
  unregister(a);
  log(`${esc(a.name)}: disabled`);
  render();
}

function fromAccount(a) {
  const d = { password: "" };
  ACCOUNT_KEYS.forEach((k) => (d[k] = a[k]));
  return d;
}

const isNewAccount = (id) => typeof id === "string" && id.startsWith("new");

function isDirty(id) {
  const d = state.acct.drafts[id];
  if (!d) return false;
  if (isNewAccount(id)) return true;
  const a = accountById(id);
  return !!d.password || ACCOUNT_KEYS.some((k) => d[k] !== a[k]);
}

function addAccount() {
  const id = "new";
  if (!state.acct.drafts[id]) {
    state.acct.drafts[id] = { name: "", displayName: "", user: "", domain: "", registrar: "", proxy: "", transport: "UDP",
      authUser: "", password: "", register: true, expiry: 3600, stun: "", srtp: "Optional", dtmf: "RFC 4733", video: true, rtt: true };
  }
  state.acct.selected = id;
  state.acct.error = null;
  render();
  document.querySelector('#acctForm [data-field="name"]')?.focus();
}

function saveAccount(id) {
  const d = state.acct.drafts[id];
  if (!d) return;
  const missing = [["name", "account name"], ["user", "username"], ["domain", "domain"]].filter(([k]) => !String(d[k]).trim());
  if (missing.length) {
    state.acct.error = `Fill in the ${missing.map(([, label]) => label).join(", ")}.`;
    renderStage();
    return;
  }
  let a = accountById(id);
  const regKeys = ["user", "domain", "registrar", "proxy", "transport", "authUser", "register", "expiry"];
  const regChanged = !a || !!d.password || regKeys.some((k) => a[k] !== d[k]);
  if (!a) {
    a = { id: `acct${state.acct.nextId++}`, enabled: true, hasPassword: false, reg: { state: "off" } };
    ACCOUNTS.push(a);
  }
  const { password, ...fields } = d;
  Object.assign(a, fields);
  if (password) a.hasPassword = true; // The prototype never keeps the password itself.
  delete state.acct.drafts[id];
  Object.assign(state.acct, { selected: a.id, error: null, confirmRemove: null });
  log(`${esc(a.name)}: account ${isNewAccount(id) ? "added" : "saved"}`);
  if (regChanged && a.enabled) register(a, "settings changed"); else render();
}

function cancelAccountEdit(id) {
  delete state.acct.drafts[id];
  state.acct.error = null;
  if (isNewAccount(id)) state.acct.selected = null;
  render();
}

function removeAccount(a) {
  if (ACCOUNTS.length === 1) return;
  if (state.acct.confirmRemove !== a.id) { state.acct.confirmRemove = a.id; renderStage(); return; }
  unregister(a);
  ACCOUNTS.splice(ACCOUNTS.indexOf(a), 1);
  delete state.acct.drafts[a.id];
  Object.assign(state.acct, { selected: null, confirmRemove: null, error: null });
  log(`${esc(a.name)}: account removed`);
  render();
}

// ---------- Rendering ----------

function renderAccount() {
  const sel = document.getElementById("accountSelect");
  const usable = enabledAccounts();
  // Rebuild only when the list changes, so an open dropdown isn't reset by unrelated renders.
  const sig = usable.map((a) => a.id + "\u0001" + a.name).join("\u0002");
  if (sel.dataset.sig !== sig) {
    sel.innerHTML = usable.map((a) => `<option value="${a.id}">${esc(a.name)}</option>`).join("");
    sel.dataset.sig = sig;
  }
  if (!usable.some((a) => a.id === state.accountId)) state.accountId = usable[0]?.id ?? null;
  sel.value = state.accountId ?? "";

  // The chosen account's registration state, quietly, next to its label.
  const acc = accountById(state.accountId);
  const info = acc ? regInfo(acc) : { tone: "bad", short: "No enabled accounts" };
  const status = document.getElementById("regStatus");
  status.className = `reg-status ${info.tone}`;
  status.innerHTML = `<span class="dot ${info.tone}"></span>${info.short}`;
  status.title = acc ? info.long ?? "" : "";

  // Errors turn the button red with an alert icon. Unsaved edits left behind
  // (e.g. to answer a call) get a small amber dot.
  const attn = ACCOUNTS.filter(needsAttention).length;
  const active = accountsActive();
  const pending = !active && hasUnsavedAccounts();
  const manage = document.getElementById("manageAccounts");
  manage.classList.toggle("on", active && !attn);
  manage.classList.toggle("error", !!attn);
  manage.setAttribute("aria-pressed", active);
  document.getElementById("manageIcon").innerHTML = icon(attn ? "alert" : "users");
  document.getElementById("accountsPending").hidden = !pending;
  manage.title = [
    attn ? `${attn} account${attn === 1 ? " isn't" : "s aren't"} registered` : "",
    pending ? "Unsaved account changes" : "",
    active ? "Close SIP accounts" : "Manage SIP accounts",
  ].filter(Boolean).join(" · ");
}

function renderCallList() {
  renderSidebarChrome();
  const list = document.getElementById("callList");
  document.getElementById("callCount").textContent = state.calls.length || "";
  if (!state.calls.length) {
    list.innerHTML = `<li class="call-list-empty">No active calls</li>`;
    return;
  }
  list.innerHTML = callsNewestFirst().map((l) => {
    const classes = ["line", l.status];
    if (l.id === state.selected) classes.push("selected");
    if (l.id === state.audioFocus && l.status === "connected") classes.push("talking");
    if (heldLong(l)) classes.push("held-long");
    const time = isLive(l) ? `<span class="line-time" data-since="${l.startedAt}">${fmtDuration(now() - l.startedAt)}</span>` : "";
    return `<li><button class="${classes.join(" ")}" data-call="${l.id}" aria-current="${l.id === state.selected}">
      <span class="line-avatar" data-speak="${l.id}" style="--tile-bg:${tileColour(l)}">${esc(initial(l.name))}</span>
      <span><span class="line-top"><span class="line-name">${esc(l.name)}</span>${mediaIconsHtml(l)}</span><span class="line-status">${statusHtml(l)}</span></span>
      <span class="line-side">${time}${netBadgeHtml(l)}</span>
    </button></li>`;
  }).join("");
}

// What you're sending on a call, at a glance: mic and camera. Red means you turned it off;
// faint means it isn't flowing for another reason (audio is on another call, hold, audio-only).
function mediaIconsHtml(l) {
  if (l.status !== "connected" && l.status !== "held") return "";
  const held = l.status === "held", focus = l.id === state.audioFocus && !held;
  const mic = micMuted(l) ? ["micOff", "off", state.muteAll ? "Muted (all calls)" : "Muted"]
    : held ? ["mic", "idle", "On hold: mic paused"]
    : focus ? ["mic", "live", "Your mic is live"]
    : ["mic", "idle", "Mic unmuted, but audio is on another call"];
  const cam = !l.video ? ["videoOff", "idle", noVideoText(l)]
    : state.noCamera ? ["videoOff", "idle", "No camera: you can see them, they can't see you"]
    : !camOn(l) ? ["videoOff", "off", state.cameraAllOff ? "Camera off (all calls)" : "Camera off"]
    : held ? ["video", "idle", "On hold: camera paused"]
    : l.theySee === false ? ["video", "idle", "They aren't receiving your video"]
    : ["video", "live", "Sending your camera"];
  // Text only appears once it's set up on the call: typing pulses, unread gets a dot.
  const txt = !l.rtt ? null
    : theirLive(l) && !held ? ["text", "typing", `${l.name} is typing`]
    : l.unread ? ["text", "live unread", `${l.unread} new text message${l.unread === 1 ? "" : "s"}`]
    : ["text", held ? "idle" : "live", held ? "On hold: text paused" : "Real-time text is on"];
  const one = ([ico, cls, label]) =>
    `<span class="media-ico ${cls}" title="${label}" role="img" aria-label="${label}"><svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[ico]}</svg></span>`;
  return `<span class="line-media">${one(mic)}${one(cam)}${txt ? one(txt) : ""}</span>`;
}

// Sections, History list, missed badge and (on phones) the tabs.
function renderSidebarChrome() {
  const side = document.getElementById("sidebar");
  side.classList.toggle("has-calls", state.calls.length > 0);
  side.dataset.tab = state.sideTab;
  const missed = unseenMissed();
  for (const id of ["missedCount", "tabMissed"]) {
    const el = document.getElementById(id);
    el.textContent = missed || "";
    el.setAttribute("aria-label", missed ? `${missed} new missed call${missed === 1 ? "" : "s"}` : "");
  }
  document.getElementById("tabCallCount").textContent = state.calls.length || "";
  for (const [id, tab] of [["tabCalls", "calls"], ["tabHistory", "history"]]) {
    document.getElementById(id).setAttribute("aria-selected", state.sideTab === tab);
  }
  const collapsed = state.hist.collapsed;
  document.getElementById("historyToggle").setAttribute("aria-expanded", !collapsed);
  document.getElementById("historyBody").hidden = collapsed;
  document.getElementById("historyList").innerHTML = sidebarHistoryHtml();
  const seeAll = document.getElementById("seeAllHistory");
  seeAll.classList.toggle("on", state.selected === HISTORY_VIEW);
  seeAll.textContent = `See all history (${RECENTS.length})`;
}

const tileColour = (l) => TILE_COLOURS[(l.id - 1) % TILE_COLOURS.length];

// Ringing calls in the order they arrived: the one waiting longest is answered first.
const ringingQueue = () => state.calls.filter((l) => l.status === "incoming").sort((a, b) => a.createdAt - b.createdAt);

function renderBanners(sel) {
  const queue = ringingQueue().filter((l) => l.id !== sel?.id);
  if (queue.length > 1) return stackedBannerHtml(queue, sel?.status === "incoming");
  return queue
    .map((l) => {
      const hasCurrent = callById(state.audioFocus)?.status === "connected";
      return `<div class="banner" role="alert">
        <span class="banner-icon">${icon("incoming")}</span>
        <div class="banner-text">
          <div class="banner-title">${esc(l.name)} is calling</div>
          <div class="banner-sub">${l.video ? "Video call" : "Audio call"} · ${esc(l.uri)} · ${esc(accountName(l.account))}</div>
        </div>
        <button class="btn btn-ghost" data-act="decline" data-call="${l.id}">Decline</button>
        ${l.video ? `<button class="btn" data-act="answer-nocam" data-call="${l.id}" title="Answer with your camera off. You'll still see them, and can turn it on any time.">${icon("videoOff")}Camera off</button>` : ""}
        <button class="btn btn-primary" data-act="answer" data-call="${l.id}">${icon(l.video ? "video" : "phone")}${hasCurrent ? "Answer &amp; hold current" : "Answer"}</button>
      </div>`;
    }).join("");
}

// Several calls ringing at once: one card, not a pile. The longest-waiting caller is on top with
// the usual buttons; the rest fold away under "Show all".
function stackedBannerHtml(queue, alsoOnScreen) {
  const hasCurrent = callById(state.audioFocus)?.status === "connected";
  const row = (l, first) => `<li class="stack-row">
      <span class="line-avatar" style="--tile-bg:${tileColour(l)}">${esc(initial(l.name))}</span>
      <span class="stack-who"><span class="stack-name">${esc(l.name)}</span>
        <span class="stack-sub">${esc(l.uri)} · waiting <span data-since="${l.createdAt}">${fmtDuration(now() - l.createdAt)}</span></span></span>
      <span class="stack-actions">
        <button class="btn btn-small btn-ghost" data-act="decline" data-call="${l.id}">Decline</button>
        <button class="btn btn-small ${first ? "btn-primary" : ""}" data-act="answer" data-call="${l.id}">${hasCurrent ? "Answer &amp; hold" : "Answer"}</button>
      </span>
    </li>`;
  const open = state.bannerOpen;
  return `<div class="banner stack" role="alert">
    <div class="stack-head">
      <span class="banner-icon">${icon("incoming")}</span>
      <span class="banner-title">${alsoOnScreen ? `${queue.length} more calls waiting` : `${queue.length} calls waiting`}</span>
      <span class="stack-head-actions">
        <button class="btn btn-small btn-ghost" data-act="decline-all" title="Decline every ringing call">Decline all</button>
        <button class="btn btn-small btn-ghost stack-toggle" data-act="banner-toggle" aria-expanded="${open}">
          ${open ? "Show less" : `+${queue.length - 1} more`}${icon("chevronDown")}</button>
      </span>
    </div>
    <ol class="stack-list">${queue.slice(0, open ? queue.length : 1).map((l, i) => row(l, i === 0)).join("")}</ol>
  </div>`;
}

// An audio-only call's picture: two lanes, them above and you below, with who's who at the start
// of each lane and a note when audio isn't flowing one way.
function audioVizHtml(l) {
  const heard = l.id === state.audioFocus && l.status === "connected";
  const note = (lane, text, tone) => `<span class="viz-note ${lane} ${tone}"><span>${text}</span></span>`;
  return `<div class="audio-viz-wrap ${heard ? "" : "dim"}">
      <span class="viz-who them" title="${esc(l.name)}">${esc(initial(l.name))}</span>
      <span class="viz-who me" title="You">You</span>
      <canvas class="audio-viz" data-viz="${l.id}" role="img" aria-label="Audio from ${esc(l.name)} above, your audio below"></canvas>
      ${l.rxDeadShown ? note("them", `No audio arriving from ${esc(l.name)}`, "bad") : ""}
      ${l.txDeadShown ? note("me", `${esc(l.name)} isn't getting your audio`, "bad")
        : l.status === "connected" && micMuted(l) ? note("me", "You're muted", "muted") : ""}
    </div>`;
}

// side: the text panel, shown beside the video inside the main card.
function tileHtml(l, kind, footer = "", side = "") {
  const bg = tileColour(l);
  // Their video shows only when it's agreed and actually arriving; otherwise their initials
  // stay up with a plain note saying why, and never a "waiting for video" spinner.
  const theirVideo = l.video && l.theirCam !== false;
  const showVideo = theirVideo && l.status !== "held";
  const note = l.status !== "connected" && l.status !== "held" ? ""
    : l.video && !theirVideo ? "Their camera is off" : "";
  // An audio call keeps the same tile; a small label says so and the waveform of what you hear
  // takes the picture's place.
  const audioViz = !l.video && isLive(l);
  const overlay = l.status === "held" ? `<div class="tile-overlay"><div>On hold<span class="overlay-time" data-since="${l.heldAt}">${fmtDuration(now() - l.heldAt)}</span></div></div>`
    : l.status === "dialing" ? `<div class="tile-overlay">Calling…</div>`
    : l.status === "ended" ? `<div class="tile-overlay">${esc(l.endReason)}</div>` : "";
  const label = !l.video
    ? `<span class="audio-chip" title="${esc(noVideoText(l))}">${icon("phone")}Audio only${l.noVideo === "rejected" ? " · their phone has no video" : l.noVideo === "tel" ? " · phone number" : l.noVideo === "account" ? " · video off for this account" : ""}</span>`
    : theirVideo ? "Test feed" : "Video";
  const isSel = l.id === state.selected;
  const canSelfView = isSel && l.status === "connected" && l.video;
  const selfView = !canSelfView || !state.selfView ? ""
    : state.noCamera ? `<div class="self-view none">${icon("videoOff")}<span>No camera</span></div>`
    : !camOn(l) ? `<div class="self-view none" title="They can't see you. You can still see them.">${icon("videoOff")}<span>Camera off</span></div>`
    : l.theySee === false ? `<div class="self-view unseen" title="They accepted video but aren't receiving yours">You<span>They can't see you</span></div>`
    : `<div class="self-view">You</div>`;
  const time = isLive(l) ? `<span class="tile-time" data-since="${l.startedAt}">${fmtDuration(now() - l.startedAt)}</span>` : "";
  const action = kind === "main"
    ? (canSelfView ? `<button class="btn btn-small meta-toggle ${state.selfView ? "on" : ""}" data-act="selfview" aria-pressed="${state.selfView}"
        title="${state.selfView ? "Hide" : "Show"} your camera preview (V)">${icon("selfView")}Self view</button>` : "")
    : kind !== "grid" ? ""
    : isSel ? `<span class="tile-badge">Selected</span>` : "";
  const feed = `<div class="tile-feed ${showVideo ? "live" : "off"} ${!l.video ? "audio" : ""}" style="--tile-bg:${bg}">
      <span class="tile-label">${label}</span>
      ${audioViz ? audioVizHtml(l)
        : `<div class="tile-who"><span class="avatar" data-speak="${l.id}">${esc(initial(l.name))}</span>${note ? `<span class="tile-note">${esc(note)}</span>` : ""}</div>`}
      ${time}${overlay}${selfView}${kind === "main" && !l.textOpen && l.rtt ? textPeekHtml(l) : ""}
    </div>`;
  // In the grid a click anywhere on a tile opens that call in Focus view.
  return `<div class="tile ${kind} ${kind === "grid" && isSel ? "is-selected" : ""}" ${kind === "grid" ? `data-act="tile" data-call="${l.id}"` : ""}>
    ${side ? `<div class="tile-row">${feed}${side}</div>` : feed}
    <div class="tile-meta">
      <div><div class="tile-meta-name">${esc(l.name)}${kind === "grid" ? ` ${netBadgeHtml(l)}` : ""}</div><div class="tile-meta-sub ${(l.status === "connected" && micMuted(l)) || heldLong(l) ? "warn" : ""}">${statusHtml(l)}</div></div>
      ${action}
    </div>
    ${footer}
  </div>`;
}

function renderStage() {
  const stage = document.getElementById("stage");
  const sel = callById(state.selected);
  let html = renderBanners(sel);
  // Text arriving or a timer re-rendering mustn't take the caret out of the text field.
  const typing = document.activeElement?.id === "rttInput"
    ? { start: document.activeElement.selectionStart, end: document.activeElement.selectionEnd } : null;

  if (state.selected === ACCOUNTS_TAB) {
    // Keep the caret where it was if something re-renders while an account field has focus.
    const f = document.activeElement?.closest?.("#acctForm") ? document.activeElement : null;
    const keep = f && { field: f.dataset.field, start: f.selectionStart, end: f.selectionEnd };
    stage.innerHTML = html + accountsViewHtml();
    if (keep?.field) {
      const el = stage.querySelector(`#acctForm [data-field="${keep.field}"]`);
      el?.focus();
      try { if (keep.start != null) el.setSelectionRange(keep.start, keep.end); } catch { /* not a text field */ }
    }
    return;
  }

  if (state.selected === HISTORY_VIEW) {
    const searching = document.activeElement?.id === "histSearch";
    stage.innerHTML = html + historyViewHtml();
    if (searching) { const el = document.getElementById("histSearch"); el.focus(); el.setSelectionRange(el.value.length, el.value.length); }
    return;
  }

  if (state.selected === ENDED_VIEW && state.ended) {
    stage.innerHTML = html + endedViewHtml(state.ended);
    return;
  }

  if (!sel) {
    // Calls are started from the bar at the top; the stage only ever shows existing calls.
    html += `<div class="empty"><div>
      <h2>No active calls</h2>
      <p>Enter a number or SIP address above and press <b>Call</b>. The switch beside it picks an audio or a video call.</p>
    </div></div>`;
  } else if (sel.status === "incoming") {
    const hasCurrent = callById(state.audioFocus)?.status === "connected";
    html += `<div class="ringing"><div>
      <div class="avatar">${esc(initial(sel.name))}</div>
      <div class="eyebrow">Incoming call · ${esc(accountName(sel.account))}</div>
      <h2>${esc(sel.name)}</h2>
      <p>${esc(sel.uri)} · ${sel.video ? "video call" : "audio call"}</p>
      <div class="actions">
        <button class="btn btn-danger" data-act="decline" data-call="${sel.id}">${icon("hangup")}Decline</button>
        ${hasCurrent ? `<button class="btn" data-act="answer-only" data-call="${sel.id}">Answer, keep current</button>` : ""}
        ${sel.video ? `<button class="btn" data-act="answer-nocam" data-call="${sel.id}" title="Answer with your camera off. You'll still see them, and can turn it on any time.">${icon("videoOff")}Answer, camera off</button>` : ""}
        <button class="btn btn-primary" data-act="answer" data-call="${sel.id}">${icon(sel.video ? "video" : "phone")}${hasCurrent ? "Answer &amp; hold current" : "Answer"}</button>
      </div>
    </div></div>`;
  } else {
    const gridCalls = gridCallsFor(sel);
    const grid = inGrid();
    const live = isLive(sel);
    const toggle = `<div class="view-toggle" role="group" aria-label="Call view">
        <button class="${grid ? "" : "on"}" data-act="view" data-view="focus" aria-pressed="${!grid}" title="Show only the selected call (G)">${icon("focus")}Focus</button>
        <button class="${grid ? "on" : ""}" data-act="view" data-view="grid" aria-pressed="${grid}" ${gridCalls.length > 1 ? "" : "disabled"}
          title="${gridCalls.length > 1 ? "Show all active calls (G)" : "Only one active call"}">${icon("grid")}Grid${gridCalls.length > 1 ? ` <span class="count">${gridCalls.length}</span>` : ""}</button>
      </div>`;
    html += `<div class="call-head">
        <div>
          <div class="eyebrow">Selected call · ${esc(accountName(sel.account))}</div>
          <h1 class="call-name">${esc(sel.name)}</h1>
          <div class="call-uri">${esc(sel.uri)}</div>
        </div>
        <div class="call-head-side">
          ${toggle}
          ${live ? `<div class="call-head-stats">${sel.status === "connected" ? netButtonHtml(sel) : ""}
            <div class="call-timer ${sel.status === "held" ? "paused" : ""}" data-since="${sel.startedAt}">${fmtDuration(now() - sel.startedAt)}</div></div>` : ""}
        </div>
      </div>
      ${grid
        // The grid is an overview only: there's no control bar, so it's never in doubt which call a
        // button acts on. Focus on a call to mute, hold, transfer or end it.
        ? `<div class="tiles grid">${gridCalls.map((l) => tileHtml(l, "grid")).join("")}</div>`
        : `<div class="tiles focus">${tileHtml(sel, "main", `<div class="tile-controls">${controlButtons(sel)}</div>`,
            sel.textOpen ? textPanelHtml(sel) : sel.rtt ? textRailHtml(sel) : "")}</div>`}`;
  }
  stage.innerHTML = html;
  const rttLog = document.getElementById("rttLog");
  if (rttLog) rttLog.scrollTop = rttLog.scrollHeight;
  if (typing) {
    const el = document.getElementById("rttInput");
    if (el && !el.disabled) { el.focus(); try { el.setSelectionRange(typing.start, typing.end); } catch { /* ignore */ } }
  }
}

// What happened to the call you were looking at: who, how it ended, and Redial.
function endedViewHtml(e) {
  const failed = e.kind === "failed";
  const back = returnCall();
  const canRedial = e.kind !== "transferred";
  return `<div class="ended-view ${failed ? "failed" : ""}"><div>
    <div class="avatar" style="--tile-bg:${TILE_COLOURS[(e.id - 1) % TILE_COLOURS.length]}">${esc(initial(e.name))}</div>
    <div class="eyebrow">${failed ? "Call failed" : e.kind === "missed" ? "Missed call" : "Call ended"} · ${esc(accountName(e.account))}</div>
    <h2>${failed ? `Couldn't reach ${esc(e.name)}` : esc(e.name)}</h2>
    <p class="ended-uri">${esc(e.uri)}</p>
    <div class="ended-reason">${failed ? icon("alert") : ""}<b>${esc(e.title)}</b>${e.duration ? `<span>· ${fmtDuration(e.duration)}</span>` : ""}</div>
    ${e.sub ? `<p class="ended-sub">${esc(e.sub)}</p>` : ""}
    ${failed ? `<p class="ended-code">SIP ${esc(e.sip)}</p>` : ""}
    <div class="actions">
      <button class="btn" data-act="dismiss-ended">${back ? `${icon("arrowLeft")}Back to ${esc(back.name)}` : "Dismiss"}</button>
      ${canRedial ? `<button class="btn btn-primary" data-act="redial" data-uri="${esc(e.uri)}" data-name="${esc(e.name)}" data-account="${esc(e.account)}">${icon("phone")}${e.kind === "missed" ? "Call back" : "Redial"}</button>` : ""}
    </div>
  </div></div>`;
}

// ---------- Accounts view ----------
// Two pages: an overview table of every account, and a full-page form for one
// account. state.acct.selected is the account being edited; null is the overview.

// The strip above the Accounts and History views: a way back to a call, and close.
function viewTopbarHtml(closeAct, closeLabel) {
  const back = returnCall();
  const backHtml = back
    ? `<button type="button" class="return-call" data-act="select" data-call="${back.id}" title="Go back to this call">
        ${icon("arrowLeft")}<span class="line-avatar" style="--tile-bg:${tileColour(back)}">${esc(initial(back.name))}</span>
        <span>Back to <b>${esc(back.name)}</b></span>
        ${isLive(back) ? `<span class="return-time" data-since="${back.startedAt}">${fmtDuration(now() - back.startedAt)}</span>` : `<span class="return-time">${esc(callStatusText(back))}</span>`}
      </button>`
    : "<span></span>";
  return `<div class="acct-topbar">${backHtml}
      <button type="button" class="icon-btn acct-close" data-act="${closeAct}" aria-label="${closeLabel}" title="${closeLabel} (Esc)">${icon("x")}</button>
    </div>`;
}

function accountsViewHtml() {
  return `${viewTopbarHtml("accounts-close", "Close SIP accounts")}
    ${state.acct.selected ? accountDetailHtml() : accountsOverviewHtml()}`;
}

function accountsOverviewHtml() {
  const registered = ACCOUNTS.filter((a) => a.enabled && a.register && a.reg.state === "registered").length;
  const attn = ACCOUNTS.filter(needsAttention).length;
  const ids = [...ACCOUNTS.map((a) => a.id), ...Object.keys(state.acct.drafts).filter(isNewAccount)];
  const rows = ids.map((id) => {
    const a = accountById(id);
    const info = a ? regInfo(a) : { tone: "off", long: "Not added yet" };
    const name = a?.name || state.acct.drafts[id]?.name || "New account";
    const tags = (id === state.accountId ? `<span class="tag">Calls from</span>` : "")
      + (isDirty(id) ? `<span class="tag warn">${a ? "Unsaved changes" : "Draft"}</span>` : "");
    return `<tr data-act="acct-select" data-acct="${id}" class="${a && !a.enabled ? "disabled" : ""}">
      <td><div class="acct-name-cell"><button type="button" class="acct-name-btn" data-act="acct-select" data-acct="${id}">${esc(name)}</button>${tags}</div></td>
      <td class="mono hide-narrow">${a ? esc(aor(a)) : ""}</td>
      <td class="hide-narrow">${a ? a.transport : ""}</td>
      <td><span class="acct-status ${info.tone}"><span class="dot ${info.tone}"></span>${esc(info.long)}</span></td>
      <td class="acct-chevron">${icon("chevronRight")}</td>
    </tr>`;
  }).join("");
  return `<div class="call-head">
      <div>
        <div class="eyebrow">SIP accounts</div>
        <h1 class="call-name">Accounts</h1>
        <div class="call-uri">${ACCOUNTS.length} account${ACCOUNTS.length === 1 ? "" : "s"} · ${registered} registered${attn ? ` · <span class="attn-text">${attn} not registered</span>` : ""}</div>
      </div>
      <div class="call-head-side"><button class="btn" data-act="acct-add">${icon("plus")}Add account</button></div>
    </div>
    <div class="acct-table-wrap"><table class="acct-table">
      <thead><tr><th>Account</th><th class="hide-narrow">Address</th><th class="hide-narrow">Transport</th><th>Status</th><th aria-label="Open"></th></tr></thead>
      <tbody>${rows}</tbody>
    </table></div>`;
}

function accountDetailHtml() {
  const id = state.acct.selected;
  const a = accountById(id);
  const vals = state.acct.drafts[id] ?? (a ? fromAccount(a) : null);
  if (!vals) { state.acct.selected = null; return accountsOverviewHtml(); }
  const isNew = !a;
  const dirty = isDirty(id);
  const info = a ? regInfo(a) : null;

  const field = ([key, label, type, ph = ""]) => {
    const v = vals[key];
    if (type === "check") {
      return `<label class="check-row span-2"><input type="checkbox" data-field="${key}" ${v ? "checked" : ""}>${label}</label>`;
    }
    if (Array.isArray(type)) {
      return `<label class="field"><span class="field-label">${label}</span>
        <select data-field="${key}">${type.map((o) => `<option ${o === v ? "selected" : ""}>${o}</option>`).join("")}</select></label>`;
    }
    if (key === "password") ph = a?.hasPassword ? "Saved · leave blank to keep" : "";
    const disabled = key === "expiry" && !vals.register ? "disabled" : "";
    return `<label class="field"><span class="field-label">${label}</span>
      <input type="${type}" data-field="${key}" value="${esc(v ?? "")}" placeholder="${esc(ph)}" spellcheck="false"
        ${type === "password" ? 'autocomplete="new-password"' : ""} ${type === "number" ? 'min="60" step="60"' : ""} ${disabled}></label>`;
  };

  const onlyEnabled = a?.enabled && enabledAccounts().length === 1;
  const actions = a
    ? `<div class="acct-form-actions">
        <button type="button" class="btn btn-small" data-act="acct-register" ${a.enabled && a.register ? "" : "disabled"}>Re-register</button>
        <button type="button" class="switch ${a.enabled ? "on" : ""}" role="switch" aria-checked="${a.enabled}" data-act="acct-enable"
          ${onlyEnabled ? 'disabled title="You need at least one enabled account"' : ""}><span class="switch-track"><span class="switch-knob"></span></span>Enabled</button>
      </div>`
    : "";
  const confirmRemove = state.acct.confirmRemove === id;

  return `<button type="button" class="back-link" data-act="acct-overview">${icon("arrowLeft")}All accounts</button>
    <div class="call-head">
      <div>
        <div class="eyebrow">SIP account${a && id === state.accountId ? " · used for new calls" : ""}</div>
        <h1 class="call-name">${esc(isNew ? (vals.name || "New account") : a.name)}</h1>
        <div class="acct-head-status">
          ${info ? `<span class="acct-status ${info.tone}"><span class="dot ${info.tone}"></span>${esc(info.long)}</span>`
            : `<span class="acct-status off">Fill in the details, then add the account to register it.</span>`}
          <span class="tag warn" id="acctDirtyTag" ${dirty && !isNew ? "" : "hidden"}>Unsaved changes</span>
        </div>
      </div>
      ${actions}
    </div>
    <form class="acct-form" id="acctForm" data-acct="${id}" novalidate>
      ${ACCOUNT_FIELDS.map(([title, fields]) => `<section class="acct-section"><h3>${title}</h3>
        <div class="acct-grid">${fields.map(field).join("")}</div></section>`).join("")}
      ${state.acct.error ? `<div class="form-error" role="alert">${esc(state.acct.error)}</div>` : ""}
      <div class="acct-form-foot">
        ${a && ACCOUNTS.length > 1 ? `<button type="button" class="btn btn-ghost danger-text ${confirmRemove ? "confirm" : ""}" data-act="acct-remove">${confirmRemove ? "Click again to remove" : "Remove account"}</button>` : ""}
        <span class="spacer"></span>
        <button type="button" class="btn" data-act="acct-cancel" ${dirty ? "" : "disabled"}>${isNew ? "Discard" : "Cancel"}</button>
        <button type="submit" class="btn btn-primary" ${dirty ? "" : "disabled"}>${isNew ? "Add account" : "Save"}</button>
      </div>
    </form>`;
}

// Typing in the form updates the draft only; just the parts that show "unsaved" are refreshed.
function onAccountField(e) {
  const key = e.target.dataset.field;
  const form = e.target.closest("#acctForm");
  if (!key || !form) return;
  const id = form.dataset.acct;
  const a = accountById(id);
  const d = (state.acct.drafts[id] ??= fromAccount(a));
  d[key] = e.target.type === "checkbox" ? e.target.checked : e.target.type === "number" ? Number(e.target.value) : e.target.value;
  if (key === "register") form.querySelector('[data-field="expiry"]').disabled = !d.register;
  if (!isDirty(id)) delete state.acct.drafts[id];
  const dirty = isDirty(id);
  form.querySelectorAll('[data-act="acct-cancel"], [type="submit"]').forEach((b) => (b.disabled = !dirty));
  const tag = document.getElementById("acctDirtyTag");
  if (tag) tag.hidden = !dirty || isNewAccount(id);
  renderCallList();
}

function controlButtons(l) {
  const connected = l.status === "connected";
  const held = l.status === "held";
  const canAct = connected || held;
  const muted = micMuted(l);
  const camOff = l.video && !camOn(l);
  const dis = (on) => (on ? "" : "disabled");
  // Mute and Camera are split buttons: the ▾ opens a quick device switcher.
  const caret = (menu, label, warn) => `<button class="btn split-caret ${warn ? "on warn" : ""}" data-act="device-menu" data-menu="${menu}"
      aria-haspopup="menu" aria-expanded="${state.menu === menu}" aria-label="${label}" title="${label}">${icon("chevronDown")}</button>`;
  // Live mic level, only while you can actually be heard on this call.
  const meter = connected && !muted && l.id === state.audioFocus ? `<span class="mic-meter" data-meter="${l.id}" aria-hidden="true"><span></span></span>` : "";
  return `<div class="control-row">
      <div class="split">
        <button class="btn ${muted ? "on warn" : ""}" data-act="mute" ${dis(connected)} aria-pressed="${muted}"
          ${state.muteAll ? 'title="All calls are muted. Click to unmute just this call."' : 'title="Mute (M)"'}>${icon(muted ? "micOff" : "mic")}${muted ? "Unmute" : "Mute"}${meter}</button>
        ${caret("mic", "Choose microphone and speaker", muted)}
        ${state.menu === "mic" ? deviceMenuHtml("mic") : ""}
      </div>
      <div class="split">
        ${!l.video && canAddVideo(l)
          // An audio call: video can be added (a re-INVITE), though their phone may say no.
          ? `<button class="btn" data-act="add-video" ${dis(connected && !l.videoAdding)}
              title="Add video to this call. They'll be asked; some phones can't.">${icon("video")}${l.videoAdding ? "Adding video…" : "Add video"}</button>`
          : !l.video
          // No video possible (their phone, a phone number, or the account), so nothing to turn on.
          ? `<button class="btn" disabled title="${esc(noVideoText(l))}">${icon("videoOff")}No video</button>`
          : state.noCamera
          ? `<button class="btn" disabled title="No camera found. You can still see them.">${icon("videoOff")}No camera</button>`
          : `<button class="btn ${camOff ? "on warn" : ""}" data-act="camera" ${dis(connected)} aria-pressed="${camOff}"
          ${state.cameraAllOff ? 'title="Camera is off for all calls. Click to turn it on for just this call."' : ""}>${icon(camOff ? "videoOff" : "video")}${camOff ? "Camera off" : "Camera"}</button>`}
        ${caret("camera", "Choose camera", camOff)}
        ${state.menu === "camera" ? deviceMenuHtml("camera") : ""}
      </div>
      <button class="btn ${textShowing(l) ? "on" : ""}" data-act="text" ${dis(canAct)} aria-pressed="${textShowing(l)}"
        title="${inGrid() ? "Real-time text: opens in Focus view (T)" : "Real-time text (T)"}">
        ${icon("text")}Text${l.unread ? `<span class="btn-dot" role="img" aria-label="${l.unread} new"></span>` : ""}</button>
      <button class="btn ${held ? "on" : ""}" data-act="hold" ${dis(canAct)} aria-pressed="${held}">${icon(held ? "play" : "pause")}${held ? "Resume" : "Hold"}</button>
      <button class="btn" data-act="keypad" ${dis(connected)}>${icon("keypad")}Keypad</button>
      <button class="btn" data-act="transfer" ${dis(canAct)}>${icon("transfer")}Transfer</button>
      <button class="btn btn-danger" data-act="end" ${dis(canAct || l.status === "dialing")}>${icon("hangup")}${l.status === "dialing" ? "Cancel" : "End"}</button>
    </div>`;
}

function renderPrivacy() {
  const el = document.getElementById("privacy");
  el.innerHTML = `
    <button class="privacy-btn ${state.muteAll ? "active" : ""}" data-act="mute-all" aria-pressed="${state.muteAll}"
      title="${state.muteAll ? "Unmute" : "Mute"} microphone on all calls (Shift+M)">
      ${icon(state.muteAll ? "micOff" : "mic")}<span>${state.muteAll ? "All muted" : "Mute all"}</span></button>
    <button class="privacy-btn ${state.cameraAllOff ? "active" : ""}" data-act="camera-all" aria-pressed="${state.cameraAllOff}"
      title="Turn camera ${state.cameraAllOff ? "back on" : "off"} for all calls (Shift+C)">
      ${icon(state.cameraAllOff ? "videoOff" : "video")}<span>${state.cameraAllOff ? "Cameras off" : "Camera off"}</span></button>`;
}

// Per-call mute while "Mute all" is on lifts the global mute for just this call:
// every other live call becomes individually muted so nothing else changes.
function toggleCallMute(l) {
  if (state.muteAll) {
    state.muteAll = false;
    state.calls.forEach((x) => { if (isLive(x) || x.status === "dialing") x.muted = x.id !== l.id; });
    log(`Mute all lifted for ${l.name} only; other calls stay muted`);
  } else {
    l.muted = !l.muted;
    if (l.muted) state.mutedAt = now();
    log(`Call ${l.id}: mic ${l.muted ? "muted" : "unmuted"}`);
  }
  render();
}

function toggleCallCamera(l) {
  if (state.cameraAllOff) {
    state.cameraAllOff = false;
    state.calls.forEach((x) => { if (isLive(x) || x.status === "dialing") x.camera = x.id === l.id; });
    log(`Camera on for ${l.name} only; other calls stay off`);
  } else {
    l.camera = !l.camera;
    log(`Call ${l.id}: camera ${l.camera ? "on" : "off"}`);
  }
  render();
}

function toggleMuteAll() {
  state.muteAll = !state.muteAll;
  if (state.muteAll) state.mutedAt = now();
  // Turning it off restores each call's own setting, so clear those too: "unmute all" means all.
  if (!state.muteAll) state.calls.forEach((x) => (x.muted = false));
  log(state.muteAll ? "<b>Mute all</b>: microphone muted on every call" : "Mute all off: microphone live");
  render();
}

function toggleCameraAll() {
  state.cameraAllOff = !state.cameraAllOff;
  if (!state.cameraAllOff) state.calls.forEach((x) => (x.camera = true));
  log(state.cameraAllOff ? "<b>Camera off</b>: no video sent on any call" : "Camera back on for all calls");
  render();
}

function renderLog() {
  const el = document.getElementById("diagLog");
  if (!el) return;
  el.innerHTML = state.log.join("\n");
  el.scrollTop = el.scrollHeight;
}

function renderModal() {
  const modal = document.getElementById("modal");
  const l = callById(state.selected);
  // Settings works with or without a call; the call dialogs need a selected call.
  if (!state.modal || (!l && !["settings", "discard", "clear-history"].includes(state.modal))) { modal.hidden = true; modal.innerHTML = ""; return; }
  modal.hidden = false;

  if (state.modal === "discard") {
    const names = Object.keys(state.acct.drafts).filter(isDirty)
      .map((id) => accountById(id)?.name || state.acct.drafts[id].name || "a new account");
    const list = names.map((n) => `<b>${esc(n)}</b>`).join(names.length === 2 ? " and " : ", ");
    modal.innerHTML = `<div class="modal" role="alertdialog" aria-labelledby="discardTitle" aria-describedby="discardText">
      <div class="modal-head"><h3 id="discardTitle">Discard unsaved changes?</h3><button class="icon-btn" data-act="close" aria-label="Keep editing">${icon("x")}</button></div>
      <p class="modal-text" id="discardText">Closing SIP accounts throws away your changes to ${list}.</p>
      <div class="modal-actions">
        <button class="btn" data-act="close" id="keepEditing">Keep editing</button>
        <button class="btn btn-danger" data-act="acct-discard">Discard changes</button>
      </div>
    </div>`;
    setTimeout(() => document.getElementById("keepEditing")?.focus(), 0);
    return;
  }

  if (state.modal === "clear-history") {
    const n = historyMatches().length;
    const what = n === 1 ? "1 call" : `${n} calls`;
    const kind = { all: "", missed: "missed ", out: "outgoing ", in: "incoming " }[state.hist.filter];
    const q = state.hist.q.trim();
    const text = historyFiltered()
      ? `This removes the ${n === 1 ? `1 ${kind}call` : `${n} ${kind}calls`}${q ? ` matching <b>“${esc(q)}”</b>` : ""}. Other calls stay in history.`
      : `This removes all ${what} from history.`;
    modal.innerHTML = `<div class="modal" role="alertdialog" aria-labelledby="clearTitle" aria-describedby="clearText">
      <div class="modal-head"><h3 id="clearTitle">${historyFiltered() ? `Clear ${what}?` : "Clear call history?"}</h3><button class="icon-btn" data-act="close" aria-label="Cancel">${icon("x")}</button></div>
      <p class="modal-text" id="clearText">${text} You can undo this for a few seconds afterwards.</p>
      <div class="modal-actions">
        <button class="btn" data-act="close" id="clearCancel">Cancel</button>
        <button class="btn btn-danger" data-act="hist-clear-confirm">${icon("trash")}Clear ${what}</button>
      </div>
    </div>`;
    setTimeout(() => document.getElementById("clearCancel")?.focus(), 0);
    return;
  }

  if (state.modal === "settings") {
    // Only rebuild when first opened, so a change to one select doesn't reset focus.
    if (modal.querySelector(".settings")) return;
    const deviceField = (key, label) => `<label class="field"><span class="field-label">${label}</span>
      <select data-device="${key}">${DEVICES[key].map((d) => `<option ${d === state.devices[key] ? "selected" : ""}>${esc(d)}</option>`).join("")}</select></label>`;
    modal.innerHTML = `<div class="modal settings" role="dialog" aria-label="Settings">
      <div class="modal-head"><h3>Settings</h3><button class="icon-btn" data-act="close" aria-label="Close">${icon("x")}</button></div>
      <section class="settings-section"><h4>Audio &amp; video</h4>
        ${deviceField("mic", "Microphone")}${deviceField("speaker", "Speaker")}${deviceField("ringer", "Ringtone")}${deviceField("camera", "Camera")}
      </section>
      <section class="settings-section"><h4>Display</h4>
        <label class="check-row"><input type="checkbox" data-pref="selfView" ${state.selfView ? "checked" : ""}>Show self view on the selected call</label>
        <label class="check-row"><input type="checkbox" data-pref="grid" ${state.view === "grid" ? "checked" : ""}>Show all calls in a grid</label>
      </section>
      <section class="settings-section"><h4>Real-time text</h4>
        <label class="check-row"><input type="checkbox" data-pref="textFirst" ${state.textFirst ? "checked" : ""}>Open the text panel on every call</label>
        <p class="settings-note">For people who mainly talk by text, such as deaf and hard-of-hearing users.</p>
        <label class="check-row"><input type="checkbox" data-pref="saveText" ${state.saveText ? "checked" : ""}>Save text conversations in call history</label>
        <p class="settings-note">Off by default. A transcript is more personal than a record of the call.</p>
        <p class="settings-note">Whether calls offer text is set per account, under SIP accounts.</p>
      </section>
    </div>`;
    setTimeout(() => modal.querySelector("select")?.focus(), 0);
  } else if (state.modal === "keypad") {
    const keys = [["1", ""], ["2", "ABC"], ["3", "DEF"], ["4", "GHI"], ["5", "JKL"], ["6", "MNO"], ["7", "PQRS"], ["8", "TUV"], ["9", "WXYZ"], ["*", ""], ["0", "+"], ["#", ""]];
    modal.innerHTML = `<div class="modal" role="dialog" aria-label="Keypad">
      <div class="modal-head"><h3>Keypad · ${esc(l.name)}</h3><button class="icon-btn" data-act="close" aria-label="Close">${icon("x")}</button></div>
      <div class="dtmf-display" id="dtmfDisplay">${esc(state.dtmf)}</div>
      <div class="keypad">${keys.map(([d, s]) => `<button class="key" data-act="dtmf" data-digit="${d}"><span><b>${d}</b><br><small>${s || "&nbsp;"}</small></span></button>`).join("")}</div>
    </div>`;
  } else if (state.modal === "transfer") {
    const others = callsNewestFirst().filter((o) => o.id !== l.id && isLive(o));
    modal.innerHTML = `<form class="modal" role="dialog" aria-label="Transfer" id="transferForm">
      <div class="modal-head"><h3>Transfer ${esc(l.name)}</h3><button type="button" class="icon-btn" data-act="close" aria-label="Close">${icon("x")}</button></div>
      ${others.length ? `<div class="section-label">Connect to another call (attended)</div>
        <div class="option-list">${others.map((o) => `<button type="button" class="option" data-act="xfer-line" data-call="${o.id}">
          <span><b>${esc(o.name)}</b><br><small class="line-status">${esc(o.uri)} · ${esc(callStatusText(o))}</small></span>${icon("transfer")}</button>`).join("")}</div>` : ""}
      <label class="field"><span class="field-label">Or send to number / SIP address (blind)</span>
        <input id="xferDest" type="text" placeholder="sip:reception@example.com" spellcheck="false"></label>
      <div class="modal-actions">
        <button type="button" class="btn" data-act="close">Cancel</button>
        <button type="submit" class="btn btn-primary">Transfer</button>
      </div>
    </form>`;
    setTimeout(() => document.getElementById("xferDest")?.focus(), 0);
  }
}

// The Audio | Video switch beside Call. Locked to Audio when the account has video turned off.
function renderCallType() {
  const locked = accountById(state.accountId)?.video === false;
  const type = locked ? "audio" : state.callType;
  document.querySelectorAll("#callType [data-type]").forEach((el) => {
    const on = el.dataset.type === type;
    el.classList.toggle("on", on);
    el.setAttribute("aria-checked", String(on));
    el.tabIndex = on ? 0 : -1;
    el.disabled = locked && el.dataset.type === "video";
  });
  document.getElementById("callType").title = locked ? "Video is turned off for this account"
    : type === "video" ? "Video call: you'll see each other. You can turn your camera off once it connects."
    : "Audio call. You can add video once it connects.";
  const call = document.getElementById("callBtn");
  call.querySelector("[data-icon]").innerHTML = icon(type === "video" ? "video" : "phone");
  call.querySelector(".label").textContent = type === "video" ? "Video call" : "Call";
}

function setCallType(type) {
  if (accountById(state.accountId)?.video === false) return;
  state.callType = type;
  savePref("callType", type);
  renderCallType();
}

function render() {
  renderCallType();
  renderAccount();
  renderPrivacy();
  renderCallList();
  renderStage();
  renderModal();
}

function initial(name) { return (name ?? "?").replace(/^\+/, "").charAt(0).toUpperCase() || "#"; }
function esc(s) { return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c])); }
function closeModal() { state.modal = null; state.dtmf = ""; renderModal(); }

// ---------- Events ----------

document.querySelectorAll("[data-icon]").forEach((el) => (el.innerHTML = icon(el.dataset.icon)));

document.getElementById("dialForm").addEventListener("submit", (e) => {
  e.preventDefault();
  placeCall(document.getElementById("destInput").value);
});
document.getElementById("callType").addEventListener("click", (e) => {
  const btn = e.target.closest("[data-type]");
  if (btn && !btn.disabled) setCallType(btn.dataset.type);
});
// A radio group: the arrow keys move between Audio and Video.
document.getElementById("callType").addEventListener("keydown", (e) => {
  if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(e.key)) return;
  e.preventDefault();
  setCallType(state.callType === "video" ? "audio" : "video");
  document.querySelector(`#callType [data-type="${state.callType}"]`)?.focus();
});

document.getElementById("accountSelect").addEventListener("change", (e) => {
  state.accountId = e.target.value;
  render();
});

document.getElementById("callList").addEventListener("click", (e) => {
  const btn = e.target.closest("[data-call]");
  if (btn) focusCall(Number(btn.dataset.call));
});

const stageEl = document.getElementById("stage");
stageEl.addEventListener("input", onAccountField);
// History search filters as you type; only the results are redrawn so the field keeps focus.
stageEl.addEventListener("input", (e) => {
  if (e.target.id !== "histSearch") return;
  state.hist.q = e.target.value;
  refreshHistResults();
});
stageEl.addEventListener("keydown", (e) => {
  if (e.target.id !== "histSearch" || e.key !== "Escape") return;
  e.stopPropagation();
  // Esc clears the search first, then a second Esc closes the view.
  if (e.target.value) { e.target.value = ""; state.hist.q = ""; refreshHistResults(); }
  else closeHistory();
});
stageEl.addEventListener("input", (e) => {
  if (e.target.id === "rttInput") onTextInput(callById(state.selected), e.target.value);
});
stageEl.addEventListener("keydown", (e) => {
  if (e.target.id !== "rttInput") return;
  if (e.key === "Enter") { e.preventDefault(); sendLine(callById(state.selected)); }
  // Esc leaves the field, so the single-key shortcuts work again.
  if (e.key === "Escape") { e.stopPropagation(); e.target.blur(); }
});
stageEl.addEventListener("submit", (e) => {
  if (e.target.id !== "acctForm") return;
  e.preventDefault();
  saveAccount(e.target.dataset.acct);
});

document.addEventListener("click", (e) => {
  // Any click outside an open device menu closes it.
  if (state.menu && !e.target.closest(".device-menu, [data-act='device-menu']")) { state.menu = null; renderStage(); }
  const btn = e.target.closest("[data-act]");
  if (!btn || btn.disabled) return;
  const l = callById(state.selected);
  const no = Number(btn.dataset.call);
  switch (btn.dataset.act) {
    case "answer": answer(no, true); break;
    case "resume": { const h = callById(no); if (h?.status === "held") { state.selected = no; toggleHold(h); } break; }
    case "decline-all": for (const r of ringingQueue().filter((x) => x.id !== state.selected)) decline(r.id); state.bannerOpen = false; break;
    case "banner-toggle": state.bannerOpen = !state.bannerOpen; renderStage(); break;
    case "net-pop": showNetPop(btn); break; // Tap shows it; tapping anywhere else hides it.
    case "answer-only": answer(no, false); break;
    case "answer-nocam": answer(no, true, false); break;
    case "decline": decline(no); break;
    case "select": select(no); break;
    case "tile": focusCall(no); break;
    case "view": setView(btn.dataset.view); break;
    case "selfview": toggleSelfView(); break;
    case "mute": toggleCallMute(l); break;
    case "camera": toggleCallCamera(l); break;
    case "add-video": addVideo(l); break;
    case "mute-all": toggleMuteAll(); break;
    case "camera-all": toggleCameraAll(); break;
    case "hold": toggleHold(l); break;
    case "text": toggleTextPanel(l); break;
    case "text-close": toggleTextPanel(l, false); break;
    case "text-add": addTextToCall(l); break;
    case "settings": state.modal = "settings"; renderModal(); break;
    case "accounts": toggleAccounts(); break;
    case "accounts-close": closeAccounts(); break;
    case "acct-discard": closeAccounts(true); break;
    case "acct-select":
      Object.assign(state.acct, { selected: btn.dataset.acct, error: null, confirmRemove: null });
      render();
      document.getElementById("stage").scrollTop = 0;
      break;
    case "acct-overview": showAccountsOverview(); break;
    case "acct-add": addAccount(); break;
    case "acct-cancel": cancelAccountEdit(state.acct.selected); break;
    case "acct-register": register(accountById(state.acct.selected), "manual"); break;
    case "acct-enable": toggleAccountEnabled(accountById(state.acct.selected)); break;
    case "acct-remove": removeAccount(accountById(state.acct.selected)); break;
    case "keypad": state.modal = "keypad"; renderModal(); break;
    case "transfer": state.modal = "transfer"; renderModal(); break;
    case "end": endCall(l, { kind: "local", sip: l.status === "dialing" ? "CANCEL" : "BYE" }); break;
    case "close": closeModal(); break;
    case "dtmf": sendDtmf(btn.dataset.digit); break;
    case "xfer-line": transfer(l, no); break;
    case "dismiss-ended": dismissEnded(); break;
    case "redial": redial(btn.dataset); break;
    case "toast-close": break; // handled below
    case "unmute-focus": { const f = callById(state.audioFocus); if (f && micMuted(f)) toggleCallMute(f); break; }
    case "device-menu": toggleDeviceMenu(btn.dataset.menu); break;
    case "device-pick": pickDevice(btn.dataset.key, btn.dataset.dev); break;
    case "history": state.selected === HISTORY_VIEW ? closeHistory() : openHistory(); break;
    case "history-close": closeHistory(); break;
    case "hist-open": openHistory(Number(btn.dataset.hist)); break;
    case "hist-select": {
      const id = Number(btn.dataset.hist);
      state.hist.selectedId = state.hist.selectedId === id ? null : id;
      refreshHistResults();
      renderSidebarChrome();
      document.querySelector(`.hist-row[data-hist="${id}"] .hist-main`)?.focus();
      break;
    }
    case "hist-filter":
      state.hist.filter = btn.dataset.filter;
      renderStage();
      break;
    case "hist-copy": copyAddress(btn.dataset.uri); break;
    case "hist-remove": removeHistory([Number(btn.dataset.hist)]); break;
    case "hist-remove-uri": {
      const same = RECENTS.filter((e) => e.uri === btn.dataset.uri);
      removeHistory(same.map((e) => e.id), `Removed ${same.length} calls from <b>${esc(same[0]?.name ?? btn.dataset.uri)}</b>`);
      break;
    }
    case "hist-undo": undoRemoveHistory(); break;
    case "hist-clear":
      if (historyMatches().length) { state.modal = "clear-history"; renderModal(); }
      break;
    case "hist-clear-confirm": clearHistory(); break;
    case "history-toggle":
      state.hist.collapsed = !state.hist.collapsed;
      savePref("historyCollapsed", state.hist.collapsed ? "yes" : "no");
      renderSidebarChrome();
      break;
    case "side-tab": state.sideTab = btn.dataset.tab; renderSidebarChrome(); break;
  }
  // A toast's buttons all close the toast once they've done their thing.
  if (btn.dataset.toast) dismissToast(Number(btn.dataset.toast));
});

// ---------- Address field: recent calls and contacts ----------

const destInput = document.getElementById("destInput");
destInput.addEventListener("focus", openDialSuggest);
destInput.addEventListener("input", openDialSuggest);
destInput.addEventListener("blur", () => setTimeout(closeDialSuggest, 0));
destInput.addEventListener("keydown", (e) => {
  const d = state.dial;
  if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    e.preventDefault();
    if (!d.open) { openDialSuggest(); return; }
    const n = d.items.length;
    d.active = e.key === "ArrowDown" ? (d.active + 1) % n : (d.active <= 0 ? n - 1 : d.active - 1);
    renderDialSuggest();
  } else if (e.key === "Enter" && d.open && d.active >= 0) {
    e.preventDefault();
    callSuggestion(d.active);
  } else if (e.key === "Escape" && d.open) {
    e.preventDefault();
    e.stopPropagation(); // Don't let Esc also close the Accounts view or a card.
    closeDialSuggest();
  }
});
const dialSuggestEl = document.getElementById("dialSuggest");
dialSuggestEl.addEventListener("mousedown", (e) => e.preventDefault()); // Keep focus in the field.
dialSuggestEl.addEventListener("click", (e) => {
  const item = e.target.closest("[data-idx]");
  if (item) callSuggestion(Number(item.dataset.idx));
});

document.getElementById("modal").addEventListener("click", (e) => {
  if (e.target.id === "modal") closeModal();
});

document.getElementById("modal").addEventListener("change", (e) => {
  const device = e.target.dataset.device;
  if (device) {
    state.devices[device] = e.target.value;
    log(`Settings: ${device} → ${esc(e.target.value)}`);
  }
  const pref = e.target.dataset.pref;
  if (pref === "selfView" && e.target.checked !== state.selfView) toggleSelfView();
  if (pref === "grid") setView(e.target.checked ? "grid" : "focus");
  if (pref === "textFirst" || pref === "saveText") {
    state[pref] = e.target.checked;
    savePref(pref, e.target.checked ? "yes" : "no");
    log(`Settings: ${pref === "textFirst" ? "open text on every call" : "save text conversations"} → ${e.target.checked ? "on" : "off"}`);
  }
});

document.getElementById("modal").addEventListener("submit", (e) => {
  e.preventDefault();
  transfer(callById(state.selected), document.getElementById("xferDest").value);
});

document.getElementById("diagToggle").addEventListener("click", (e) => {
  state.diagOpen = !state.diagOpen;
  e.currentTarget.setAttribute("aria-expanded", state.diagOpen);
  document.getElementById("diagnostics").hidden = !state.diagOpen;
  renderLog();
});

document.querySelector(".diag-actions").addEventListener("click", (e) => {
  const sim = e.target.closest("[data-sim]")?.dataset.sim;
  if (sim === "incoming") simulateIncoming();
  if (sim === "missed") simulateMissed();
  if (sim === "burst") { for (let i = 0; i < 3; i++) simulateIncoming(); }
  if (sim === "net-bad") {
    state.netBad = !state.netBad;
    e.target.closest("[data-sim]").classList.toggle("on", state.netBad);
    log(state.netBad ? "Network: your connection is now poor (simulated)" : "Network: your connection is back to normal");
  }
  if (sim === "hold-skip") {
    const held = state.calls.filter((l) => l.status === "held");
    for (const l of held) l.heldAt -= 60_000;
    log(held.length ? `Skipped ${held.length} held call${held.length === 1 ? "" : "s"} ahead a minute` : "No held calls to skip ahead");
    render();
  }
  if (sim === "clear") { state.log = []; renderLog(); }
  if (sim === "their-camera") toggleTheirCamera();
  if (sim === "rx-dead") toggleAudioFault("rx");
  if (sim === "tx-dead") toggleAudioFault("tx");
  if (sim === "no-camera") {
    state.noCamera = !state.noCamera;
    e.target.closest("[data-sim]").classList.toggle("on", state.noCamera);
    log(state.noCamera ? "Camera: none found (new calls offer video recvonly)" : "Camera: found again");
    render();
  }
  if (sim === "text-remote") {
    const sel = callById(state.selected);
    const l = sel?.status === "connected" && sel.rtt ? sel : callsNewestFirst().find((x) => x.status === "connected" && x.rtt);
    if (!l) log("No connected call with text set up");
    else if (!remoteType(l, pick(REMOTE_LINES))) log(`${l.name} is already typing`);
  }
  if (sim === "text-unsupported") {
    const l = callById(state.selected);
    if (!l || !isLive(l)) log("Select a live call first");
    else {
      l.chat = (l.chat ?? []).filter((m) => !m.open);
      Object.assign(l, { rtt: false, rttSupported: false, rttDeclined: true, draft: "" });
      log(`Call ${l.id}: re-INVITE from ${l.name}: m=text 0 — their phone dropped real-time text`);
      render();
    }
  }
  if (sim === "reg-fail") {
    const a = ACCOUNTS.find((x) => x.enabled && x.register && x.reg.state === "registered");
    if (a) failRegistration(a, "408 Request Timeout"); else log("No registered account to break");
  }
  if (sim === "remote-hangup") {
    const l = callById(state.selected);
    if (l && (isLive(l) || l.status === "dialing")) {
      endCall(l, l.status === "dialing" ? { kind: "failed", sip: "480 Temporarily Unavailable" } : { kind: "remote", sip: "BYE (remote)" });
    }
  }
  if (sim === "remote-hangup-other") {
    const other = callsNewestFirst().find((x) => isLive(x) && x.id !== state.selected);
    if (other) endCall(other, { kind: "remote", sip: "BYE (remote)" }); else log("No other live call to hang up");
  }
});

// Keyboard: digits go to the keypad when it's open; otherwise a few shortcuts
// when focus isn't in a text field.
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && !netPop.hidden) { hideNetPop(); return; }
  if (e.key === "Escape" && state.menu) { state.menu = null; renderStage(); return; }
  if (e.key === "Escape" && state.modal) { closeModal(); return; }
  // Esc steps back: account page, then the table, then closes (with the unsaved-changes warning).
  if (e.key === "Escape" && accountsActive()) { if (state.acct.selected) showAccountsOverview(); else closeAccounts(); return; }
  if (state.modal === "keypad" && /^[\d*#]$/.test(e.key)) { sendDtmf(e.key); return; }
  if (e.target.matches("input, select, textarea") || state.modal || e.ctrlKey || e.metaKey || e.altKey) return;
  if (e.key === "Escape" && state.selected === ENDED_VIEW) { dismissEnded(); return; }
  if (e.key === "Escape" && state.selected === HISTORY_VIEW) { closeHistory(); return; }
  const l = callById(state.selected);
  const k = e.key.toLowerCase();
  if (e.shiftKey && k === "m") { toggleMuteAll(); return; }
  if (e.shiftKey && k === "c") { toggleCameraAll(); return; }
  if (k === "m" && l?.status === "connected") document.querySelector('[data-act="mute"]')?.click();
  // Like the buttons, M and H only act in Focus view, where it's clear which call they apply to.
  else if (k === "h" && l && isLive(l) && !inGrid()) toggleHold(l);
  else if (k === "g") setView(state.view === "grid" ? "focus" : "grid");
  else if (k === "v") toggleSelfView();
  // preventDefault: the panel focuses its input, which would otherwise receive this "t".
  else if (k === "t" && l && (isLive(l) || l.status === "ended")) { e.preventDefault(); toggleTextPanel(l); }
  else if (k === "a") { const inc = callById(state.selected)?.status === "incoming" ? callById(state.selected) : ringingQueue()[0]; if (inc) answer(inc.id); }
  else if (k === "/" ) { e.preventDefault(); document.getElementById("destInput").focus(); }
  // 1–8 pick by position in the Active calls list (1 = newest).
  else if (/^[1-8]$/.test(k)) { const c = callsNewestFirst()[Number(k) - 1]; if (c) focusCall(c.id); }
});

setInterval(speechTick, 100);
requestAnimationFrame(vizFrame);
setInterval(netTick, 2000);
setInterval(holdTick, 1000);

// Quality popover: hover or focus shows it; the call-head button also toggles it on click/tap.
document.addEventListener("mouseover", (e) => {
  const a = e.target.closest?.("[data-quality]");
  if (a && netPopFor?.anchor !== a) showNetPop(a);
});
document.addEventListener("mouseout", (e) => {
  const a = e.target.closest?.("[data-quality]");
  if (a && !a.contains(e.relatedTarget) && netPopFor?.anchor === a && document.activeElement !== a) hideNetPop();
});
document.addEventListener("focusin", (e) => { if (e.target.matches?.("[data-quality]")) showNetPop(e.target); });
document.addEventListener("focusout", (e) => { if (e.target.matches?.("[data-quality]")) hideNetPop(); });
addEventListener("scroll", hideNetPop, true);
document.addEventListener("click", (e) => { if (!e.target.closest("[data-quality]")) hideNetPop(); });

// Tick call timers without re-rendering everything.
setInterval(() => {
  document.querySelectorAll("[data-since]").forEach((el) => {
    el.textContent = fmtDuration(now() - Number(el.dataset.since));
  });
}, 1000);

log("Registered sip:aaron@sipsorcery.cloud (expires 3600)");
log("Call 1: 200 OK → <b>Alice</b> (audio+video)");
log("Call 2: 200 OK → <b>Bob</b> (audio+video)");
log("Call 3: INVITE from sip:charlie@example.com → 180 Ringing");
render();
// Demo: Bob starts typing on his call in the background, so the list shows it.
setTimeout(() => { const bob = callById(2); if (bob?.status === "connected") remoteType(bob, "Are you still there? No rush, I'll wait."); }, 5000);
