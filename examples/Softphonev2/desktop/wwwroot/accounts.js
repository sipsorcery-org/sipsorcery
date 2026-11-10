// SIP accounts, desktop only: "Call from" in the dial bar, and the Manage accounts view over the
// stage, from the UX prototype (prototype/web). The host keeps the accounts (passwords protected,
// never sent here) and registers them; this page only shows them and posts edits:
//   account-select {id}            the account calls go out from (null: direct calls, no account)
//   account-save {account}         add (no id) or update; an empty password keeps the saved one
//   account-enable {id, enabled}   account-remove {id}   account-register {id} (Re-register)
// and hears back account-saved {id} or account-error {text}, plus every state message's
// `accounts` and `accountId`.
//
// The web softphone loads this page too, but its state has no `accounts` (it has one account and
// its own sign-in), so none of this ever shows there.

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

const ICON = {
  users: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8"/></svg>',
  alert: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 8v4M12 16h.01"/></svg>',
  close: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>',
  plus: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14"/></svg>',
  back: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>',
  chevron: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 18 6-6-6-6"/></svg>',
};

// The form's fields, grouped as in the prototype, less what the host doesn't do yet (outbound
// proxy, STUN, SRTP, DTMF). [key, label, type, placeholder, hint]; a select's type is its
// options as [value, text] pairs, the first being the default.
const TRANSPORTS = [["udp", "UDP"], ["tcp", "TCP"], ["tls", "TLS"]];
const FIELDS = [
  ["Account", [["name", "Account name", "text", "Username@server"], ["displayName", "Display name", "text", "Shown to people you call"]]],
  ["Server", [["username", "Username", "text", "alice"], ["server", "Server", "text", "sip.example.com", "A host, or host:port"],
    ["transport", "Transport", TRANSPORTS, null, "How to reach the server. TLS encrypts the signalling"]]],
  ["Authentication", [["authUsername", "Auth username", "text", "Same as the username"], ["password", "Password", "password"]]],
  ["Registration", [["register", "Register with the server so this account can receive calls", "check"], ["expiry", "Expiry (seconds)", "number"]]],
];

let post = () => {};
let log = () => {};
let accounts = null;      // from the host; null until it sends some (never, in the web softphone)
let selectedId = null;
let callLive = false;
// The view: closed, the list, or one account's form ("new" for Add account). The form keeps what
// you've typed while it's open, even if the view is hidden by an incoming call.
const view = { open: false, page: "list", error: null, confirmRemove: false, saving: false };

const byId = (id) => accounts?.find((a) => a.id === id);

// How a registration state reads, and its colour: ok, warn, bad or off.
function status(a) {
  if (!a) return { tone: "off", short: "No account", long: "Direct calls only: to an IP address or host, with no account" };
  if (!a.enabled) return { tone: "off", short: "Disabled", long: "Disabled" };
  switch (a.reg) {
    case "registered": return { tone: "ok", short: "Registered", long: "Registered" };
    case "registering": return { tone: "warn", short: "Registering…", long: "Registering…" };
    case "failed": return { tone: "bad", short: "Not registered", long: `Not registered: ${a.regDetail || "no reason given"}` };
    default: return { tone: "off", short: a.register ? "Not registered" : "Calls out only", long: a.register ? "Not registered" : "Doesn't register: calls out only" };
  }
}

const aor = (a) => `${a.username}@${a.server.replace(/^sips?:/i, "").replace(/;.*$/, "")}`;

export function initAccounts(api) {
  post = api.post;
  log = api.log;
  $("manageAccounts").innerHTML = ICON.users;
  $("manageAccounts").onclick = () => (view.open && !$("accounts").hidden ? close() : open());
  $("fromSelect").onchange = () => {
    const id = $("fromSelect").value || null;
    if (id === "+add") { renderFrom(); open("new"); return; }
    post({ type: "account-select", id });
    log(`call from ${byId(id)?.name ?? "no account"}`);
  };
  // Esc steps back: form → list → closed. Not while a menu or the text popover has it.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Escape" || !view.open || $("accounts").hidden || e.defaultPrevented) return;
    if (document.querySelector(".device-menu:not([hidden])") || !($("rtt")?.hidden ?? true)) return;
    e.preventDefault();
    if (view.page !== "list") showList(); else close();
  });
}

// Every state message. Returns at once in the web softphone, which sends no accounts.
export function onAccountsState(state) {
  if (!Array.isArray(state.accounts)) return;
  const first = accounts === null;
  accounts = state.accounts;
  selectedId = state.accountId ?? null;
  const live = ["calling", "ringing", "connected"].includes(state.call);
  // A ringing call takes over the stage; Manage accounts brings the view (and your edits) back.
  if (state.call === "ringing" && !callLive && view.open) $("accounts").hidden = true;
  callLive = live;
  $("from").hidden = false;
  renderFrom();
  if (view.open) renderView(false);
  if (first && accounts.length === 0) log("no SIP accounts yet: Manage accounts adds one (direct calls work without)");
}

// account-saved and account-error.
export function onAccountMessage(msg) {
  if (msg.type === "account-saved") {
    view.saving = false;
    view.error = null;
    view.page = msg.id;
    renderView(true);
  } else if (msg.type === "account-error") {
    view.saving = false;
    view.error = msg.text;
    renderView(false);
  }
}

// Call from: the picker, its status dot, and Manage accounts turning red when one fails.
function renderFrom() {
  const sel = $("fromSelect");
  const opts = accounts.filter((a) => a.enabled).map((a) => `<option value="${esc(a.id)}">${esc(a.name)}</option>`);
  opts.push(`<option value="">No account (direct)</option>`);
  if (accounts.length === 0) opts.push(`<option value="+add">Add an account…</option>`);
  const html = opts.join("");
  if (sel.dataset.html !== html) { sel.innerHTML = html; sel.dataset.html = html; }
  sel.value = byId(selectedId)?.enabled ? selectedId : "";
  sel.disabled = callLive;

  const cur = byId(selectedId);
  const st = status(cur?.enabled ? cur : null);
  $("fromDot").className = `from-dot ${st.tone}`;
  sel.title = cur?.enabled ? `Call from ${aor(cur)}. ${st.long}` : st.long;

  const failed = accounts.filter((a) => a.enabled && a.reg === "failed");
  const btn = $("manageAccounts");
  btn.classList.toggle("error", failed.length > 0);
  btn.classList.toggle("on", view.open && !$("accounts").hidden);
  btn.innerHTML = failed.length ? ICON.alert : ICON.users;
  btn.title = failed.length ? `Manage accounts: ${failed.map((a) => `${a.name} isn't registered (${a.regDetail || "failed"})`).join("; ")}` : "Manage accounts";
}

function open(page) {
  if (!view.open) Object.assign(view, { open: true, page: "list", error: null, confirmRemove: false });
  if (page) view.page = page;
  $("accounts").hidden = false;
  renderView(true);
  renderFrom();
}

function close() {
  Object.assign(view, { open: false, page: "list", error: null, confirmRemove: false });
  $("accounts").hidden = true;
  $("accounts").replaceChildren();
  renderFrom();
  $("manageAccounts").focus();
}

function showList() {
  Object.assign(view, { page: "list", error: null, confirmRemove: false });
  renderView(true);
}

function showForm(id) {
  Object.assign(view, { page: id, error: null, confirmRemove: false });
  renderView(true);
  $("accounts").querySelector('[data-field="name"]')?.focus();
}

// rebuild: draw the page afresh. Otherwise (a state update) only the status parts change, so
// nothing you're typing is lost.
function renderView(rebuild) {
  const root = $("accounts");
  if (!view.open) return;
  // The account being edited was removed (here, or by another copy of the profile's store).
  if (view.page !== "list" && view.page !== "new" && !byId(view.page)) { view.page = "list"; rebuild = true; }
  if (view.page === "list") { renderList(root); return; }
  if (rebuild || root.dataset.page !== view.page) renderForm(root);
  else updateForm(root);
}

function topbar(title, sub) {
  return `<div class="am-top">
      <div><h2 class="am-title">${esc(title)}</h2>${sub ? `<div class="am-sub">${sub}</div>` : ""}</div>
      <button type="button" class="am-close" data-act="close" aria-label="Close accounts" title="Close (Esc)">${ICON.close}</button>
    </div>`;
}

function renderList(root) {
  root.dataset.page = "list";
  const rows = accounts.map((a) => {
    const st = status(a);
    return `<tr data-id="${esc(a.id)}" class="${a.enabled ? "" : "disabled"}">
        <td><button type="button" class="am-name" data-act="edit" data-id="${esc(a.id)}">${esc(a.name)}</button>${a.id === selectedId && a.enabled ? '<span class="am-tag">Calls from</span>' : ""}</td>
        <td class="am-mono">${esc(aor(a))}</td>
        <td><span class="am-status ${st.tone}"><span class="am-dot ${st.tone}"></span>${esc(st.long)}</span></td>
        <td class="am-chev">${ICON.chevron}</td>
      </tr>`;
  }).join("");
  root.innerHTML = `${topbar("Accounts", "The SIP accounts this phone registers with and calls from.")}
    <div class="am-actions"><button type="button" class="btn" data-act="add">${ICON.plus}Add account</button></div>
    ${accounts.length ? `<div class="am-table-wrap"><table class="am-table">
      <thead><tr><th>Account</th><th>Address</th><th>Status</th><th></th></tr></thead>
      <tbody>${rows}</tbody></table></div>`
      : `<p class="am-empty">No accounts yet. Add one to register with a SIP server and call from it. Calls straight to an IP address or host work without one.</p>`}`;
  root.onclick = onClick;
  root.onsubmit = null;
}

function renderForm(root) {
  const isNew = view.page === "new";
  const a = isNew ? { name: "", displayName: "", username: "", server: "", transport: "udp", authUsername: "", register: true, expiry: 300, hasPassword: false } : byId(view.page);
  root.dataset.page = view.page;
  const field = ([key, label, type, placeholder, hint]) => {
    const id = `am-${key}`;
    if (type === "check") {
      return `<label class="am-check span-2"><input type="checkbox" id="${id}" data-field="${key}" ${a[key] ? "checked" : ""}>${esc(label)}</label>`;
    }
    if (Array.isArray(type)) {
      const current = type.some(([v]) => v === a[key]) ? a[key] : type[0][0];
      return `<label class="am-field" for="${id}"><span class="am-label">${esc(label)}</span>
        <select id="${id}" data-field="${key}">${type.map(([v, text]) => `<option value="${esc(v)}" ${v === current ? "selected" : ""}>${esc(text)}</option>`).join("")}</select>
        ${hint ? `<span class="am-hint">${esc(hint)}</span>` : ""}</label>`;
    }
    const value = key === "password" ? "" : a[key] ?? "";
    const ph = key === "password" ? (a.hasPassword ? "Saved: leave blank to keep it" : "None") : placeholder ?? "";
    return `<label class="am-field" for="${id}"><span class="am-label">${esc(label)}</span>
        <input id="${id}" data-field="${key}" type="${type}" value="${esc(value)}" placeholder="${esc(ph)}" spellcheck="false" autocomplete="off"
          ${type === "number" ? 'min="60" max="7200" step="1"' : ""}>
        ${hint ? `<span class="am-hint">${esc(hint)}</span>` : ""}</label>`;
  };
  root.innerHTML = `${topbar(isNew ? "Add account" : a.name, null)}
    <button type="button" class="am-back" data-act="list">${ICON.back}All accounts</button>
    ${isNew ? "" : `<div class="am-head">
        <span class="am-status" id="amStatus"></span>
        <span class="am-spacer"></span>
        <button type="button" class="btn am-small" data-act="register" id="amRegister">Re-register</button>
        <button type="button" class="am-switch" role="switch" data-act="enable" id="amEnable"><span class="am-track"><span class="am-knob"></span></span>Enabled</button>
      </div>`}
    <form class="am-form" id="amForm" novalidate>
      ${FIELDS.map(([title, fields]) => `<section class="am-section"><h3>${esc(title)}</h3>
        <div class="am-grid">${fields.map(field).join("")}</div></section>`).join("")}
      <p class="am-error" id="amError" role="alert" hidden></p>
      <div class="am-foot">
        ${isNew ? "" : `<button type="button" class="btn am-small am-remove" data-act="remove" id="amRemove"></button>`}
        <span class="am-spacer"></span>
        <button type="button" class="btn am-small" data-act="list">Cancel</button>
        <button type="submit" class="btn btn-primary am-small" id="amSave">${isNew ? "Add account" : "Save"}</button>
      </div>
    </form>`;
  root.onclick = onClick;
  root.onsubmit = (e) => { e.preventDefault(); save(); };
  updateForm(root);
}

// The parts of the form that follow the host: status, Enabled, and the error line.
function updateForm(root) {
  const a = byId(view.page);
  if (a) {
    const st = status(a);
    const s = $("amStatus");
    s.className = `am-status ${st.tone}`;
    s.innerHTML = `<span class="am-dot ${st.tone}"></span>${esc(st.long)}`;
    $("amRegister").disabled = !a.enabled || !a.register;
    $("amRegister").title = !a.enabled ? "Enable the account to register it" : !a.register ? "This account doesn't register" : "Register again now";
    const en = $("amEnable");
    en.classList.toggle("on", a.enabled);
    en.setAttribute("aria-checked", String(a.enabled));
    const rm = $("amRemove");
    rm.textContent = view.confirmRemove ? "Click again to remove" : "Remove account";
    rm.classList.toggle("confirm", view.confirmRemove);
  }
  const err = root.querySelector("#amError");
  err.hidden = !view.error;
  err.textContent = view.error ?? "";
  root.querySelector("#amSave").disabled = view.saving;
}

function onClick(e) {
  const el = e.target.closest("[data-act], tr[data-id]");
  if (!el) return;
  const act = el.dataset.act ?? "edit";
  const id = el.dataset.id ?? view.page;
  switch (act) {
    case "close": close(); break;
    case "list": showList(); break;
    case "add": showForm("new"); break;
    case "edit": showForm(id); break;
    case "register": post({ type: "account-register", id }); log(`${byId(id)?.name}: re-register`); break;
    case "enable": post({ type: "account-enable", id, enabled: !byId(id)?.enabled }); break;
    case "remove":
      if (!view.confirmRemove) { view.confirmRemove = true; renderView(false); break; }
      post({ type: "account-remove", id });
      showList();
      break;
  }
}

function save() {
  const root = $("accounts");
  const value = (k) => root.querySelector(`[data-field="${k}"]`);
  const account = { id: view.page === "new" ? null : view.page };
  for (const [, fields] of FIELDS) {
    for (const [k, , type] of fields) {
      const el = value(k);
      account[k] = type === "check" ? el.checked : type === "number" ? Number(el.value) || 300 : el.value.trim();   // a select's value too
    }
  }
  account.password = value("password").value;   // as typed, spaces and all
  const missing = [["username", "username"], ["server", "server"]].filter(([k]) => !account[k]).map(([, l]) => l);
  if (missing.length) {
    view.error = `Fill in the ${missing.join(" and ")}.`;
    renderView(false);
    value(missing[0])?.focus();
    return;
  }
  view.saving = true;
  view.error = null;
  renderView(false);
  post({ type: "account-save", account });
}
