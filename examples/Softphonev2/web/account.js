// The web softphone's SIP account: kept in this browser, created as a guest account on the
// server (behind a captcha), or typed in to sign in. Also the account chip in the header and the
// sign-in dialog. host.js decides when to show them and does the registering.

const q = new URLSearchParams(location.search);

// The guest-account API, POST <api>/api/guest-accounts { captchaToken } ->
// { username, password, domain, wsUri, expiresAfterDays }. No api= parameter: a stub in this
// page, until the server has it.
const API = q.get("api") || "";
// Cloudflare Turnstile's site key. The default is Turnstile's test key, which always passes.
const CAPTCHA_SITE_KEY = q.get("captcha") || "1x00000000000000000000AA";
// Where the stub's accounts connect: by default the desktop app's WebSocket. The desktop isn't a
// registrar, so they don't register (calls out only), but the whole flow can be tried without the
// server.
const STUB_WS = q.get("stubws") || `ws://${location.hostname || "localhost"}:8081`;
const DEFAULT_WS = q.get("wsdefault") || "wss://sipsorcery.com";

const KEY = "softphonev2.account";

export function loadAccount() {
  try {
    const a = JSON.parse(localStorage.getItem(KEY) || "null");
    return a?.username && a?.password && a?.domain && a?.wsUri ? a : null;
  } catch { return null; }
}

export function saveAccount(a) {
  try { localStorage.setItem(KEY, JSON.stringify(a)); } catch { /* private window: this visit only */ }
}

export function forgetAccount() {
  try { localStorage.removeItem(KEY); } catch { }
}

// ---- Creating a guest account ----

const WORDS_A = ["calm", "brave", "quick", "bright", "gentle", "lucky", "quiet", "sunny", "swift", "merry"];
const WORDS_B = ["otter", "falcon", "badger", "heron", "lynx", "panda", "robin", "walrus", "koala", "finch"];
const pick = (list) => list[crypto.getRandomValues(new Uint32Array(1))[0] % list.length];

async function createGuest(captchaToken) {
  if (!API) {
    // The stub: what the server will return, made up here.
    await new Promise((r) => setTimeout(r, 400));
    const n = crypto.getRandomValues(new Uint32Array(1))[0] % 10000;
    const pw = [...crypto.getRandomValues(new Uint8Array(18))].map((b) => b.toString(36).padStart(2, "0")).join("").slice(0, 24);
    return { username: `${pick(WORDS_A)}-${pick(WORDS_B)}-${String(n).padStart(4, "0")}`, password: pw,
      domain: "localhost", wsUri: STUB_WS, expiresAfterDays: 30, noRegister: true };
  }
  const resp = await fetch(new URL("/api/guest-accounts", API), {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ captchaToken }),
  });
  const body = await resp.json().catch(() => ({}));
  if (resp.status === 429) throw new Error("Too many test accounts from here just now. Try again later.");
  if (!resp.ok) throw new Error(body.error || `The server said ${resp.status}.`);
  return body;
}

// ---- Turnstile ----

let turnstileLoading = null;
function loadTurnstile() {
  turnstileLoading ??= new Promise((resolve, reject) => {
    const s = document.createElement("script");
    s.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    s.async = true;
    s.onload = () => resolve(window.turnstile);
    s.onerror = () => { turnstileLoading = null; reject(new Error("Couldn't load the captcha.")); };
    document.head.append(s);
  });
  return turnstileLoading;
}

// ---- UI ----

const ICON = {
  copy: `<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/></svg>`,
  user: `<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>`,
};

const esc = (t) => String(t ?? "").replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));

// Builds the header chip and the dialog. `on` has signIn(account) -> Promise (rejects with a
// message when registering fails) and signOut().
export function accountUi(on) {
  const chip = document.createElement("button");
  chip.type = "button";
  chip.className = "acct";
  chip.id = "acct";
  const menu = document.createElement("div");
  menu.className = "acct-menu";
  menu.hidden = true;
  menu.setAttribute("role", "menu");
  const wrap = document.createElement("div");
  wrap.className = "acct-wrap";
  wrap.append(chip, menu);
  document.querySelector(".topbar-right").prepend(wrap);

  const dlg = document.createElement("dialog");
  dlg.className = "acct-dialog";
  document.body.append(dlg);

  let account = null;
  let status = { state: "none", detail: null };

  function renderChip() {
    chip.className = `acct ${account ? status.state : "none"}`;
    chip.innerHTML = account ? `<span class="acct-dot"></span><span>${esc(account.username)}</span>` : `${ICON.user}<span>Sign in</span>`;
    chip.title = !account ? "Sign in or create a test account"
      : status.state === "registered" ? `sip:${account.username}@${account.domain}`
      : status.state === "connected" ? `sip:${account.username}@${account.domain} (not registered: calls out only)`
      : status.state === "failed" ? `Not registered: ${status.detail || "failed"}` : "Registering…";
  }

  function renderMenu() {
    const addr = `${account.username}@${account.domain}`;
    const line = status.state === "registered" ? "Ready for calls"
      : status.state === "connected" ? "Calls out only (not registered)"
      : status.state === "failed" ? (status.detail || "Not registered") : "Registering…";
    menu.innerHTML = `
      <div class="acct-menu-head"><div class="acct-menu-addr">${esc(addr)}</div><div class="acct-menu-state ${status.state}">${esc(line)}</div></div>
      <button type="button" role="menuitem" data-act="copy">Copy my address</button>
      <button type="button" role="menuitem" data-act="details">Show account details</button>
      <button type="button" role="menuitem" data-act="out">Sign out</button>`;
  }

  function closeMenu() { menu.hidden = true; chip.setAttribute("aria-expanded", "false"); }

  chip.onclick = () => {
    if (!account) return showSignIn();
    if (!menu.hidden) return closeMenu();
    renderMenu();
    menu.hidden = false;
    chip.setAttribute("aria-expanded", "true");
    menu.querySelector("button")?.focus();
  };
  document.addEventListener("pointerdown", (e) => { if (!menu.hidden && !wrap.contains(e.target)) closeMenu(); });
  menu.addEventListener("keydown", (e) => { if (e.key === "Escape") { closeMenu(); chip.focus(); } });
  menu.onclick = async (e) => {
    const act = e.target.closest("button")?.dataset.act;
    if (!act) return;
    closeMenu();
    if (act === "copy") navigator.clipboard?.writeText(`sip:${account.username}@${account.domain}`).catch(() => {});
    else if (act === "details") showDetails(account, false);
    else if (act === "out") {
      if (account.guest && !confirm("Sign out? Keep your user name and password if you want to use this test account again.")) return;
      on.signOut();
    }
  };

  // ---- The dialog: sign in, or create a test account ----

  function showSignIn(error = "") {
    dlg.innerHTML = `
      <form method="dialog" class="acct-form" id="acctCreate">
        <h2>Sign in to make calls</h2>
        <p class="acct-lead">A test account is free and takes a moment. It can call other SIPSorcery users and SIP addresses, but not phone numbers.</p>
        <button type="submit" class="btn btn-primary acct-wide" id="acctCreateBtn">Create a test account</button>
        <div class="acct-captcha" id="acctCaptcha" hidden></div>
        <p class="acct-error" id="acctCreateErr" hidden></p>
      </form>
      <div class="acct-or"><span>or sign in</span></div>
      <form class="acct-form" id="acctSignIn" autocomplete="on">
        <label>User name<input name="username" autocomplete="username" required spellcheck="false"></label>
        <label>Password<input name="password" type="password" autocomplete="current-password" required></label>
        <details class="acct-adv"><summary>Server</summary>
          <label>Domain<input name="domain" value="sipsorcery.com" spellcheck="false"></label>
          <label>WebSocket address<input name="wsUri" value="${esc(DEFAULT_WS)}" spellcheck="false"></label>
        </details>
        <p class="acct-error" id="acctSignInErr" ${error ? "" : "hidden"}>${esc(error)}</p>
        <button type="submit" class="btn acct-wide" id="acctSignInBtn">Sign in</button>
      </form>`;
    const createForm = dlg.querySelector("#acctCreate");
    const signInForm = dlg.querySelector("#acctSignIn");

    createForm.onsubmit = async (e) => {
      e.preventDefault();
      const btn = dlg.querySelector("#acctCreateBtn");
      const err = dlg.querySelector("#acctCreateErr");
      err.hidden = true;
      btn.disabled = true;
      btn.textContent = "Checking you're not a robot…";
      try {
        const token = await captcha(dlg.querySelector("#acctCaptcha"));
        btn.textContent = "Creating your account…";
        const created = await createGuest(token);
        const a = { username: created.username, password: created.password, domain: created.domain,
          wsUri: created.wsUri, guest: true, expiresAfterDays: created.expiresAfterDays ?? 30 };
        showDetails(a, true);
        // It's kept even if registering fails; the header shows why.
        on.signIn(a).catch(() => {});
      } catch (ex) {
        err.textContent = ex.message;
        err.hidden = false;
        btn.disabled = false;
        btn.textContent = "Create a test account";
      }
    };

    signInForm.onsubmit = async (e) => {
      e.preventDefault();
      const f = new FormData(signInForm);
      const a = { username: String(f.get("username")).trim(), password: String(f.get("password")),
        domain: String(f.get("domain")).trim() || "sipsorcery.com", wsUri: String(f.get("wsUri")).trim() || DEFAULT_WS, guest: false };
      const btn = dlg.querySelector("#acctSignInBtn");
      const err = dlg.querySelector("#acctSignInErr");
      err.hidden = true;
      btn.disabled = true;
      btn.textContent = "Signing in…";
      try {
        await on.signIn(a);
        dlg.close();
      } catch (ex) {
        err.textContent = ex.message;
        err.hidden = false;
        btn.disabled = false;
        btn.textContent = "Sign in";
      }
    };

    if (!dlg.open) dlg.showModal();
    dlg.querySelector("#acctCreateBtn").focus();
  }

  // The captcha, in the dialog; resolves to its token.
  async function captcha(box) {
    const ts = await loadTurnstile();
    box.hidden = false;
    box.innerHTML = "";
    return new Promise((resolve, reject) => {
      ts.render(box, {
        sitekey: CAPTCHA_SITE_KEY,
        callback: (token) => { box.hidden = true; resolve(token); },
        "error-callback": () => reject(new Error("The captcha didn't work. Try again.")),
        "expired-callback": () => reject(new Error("The captcha expired. Try again.")),
      });
    });
  }

  // The account's user name and password, to keep. `fresh`: just created, so say why.
  function showDetails(a, fresh) {
    const addr = `sip:${a.username}@${a.domain}`;
    const row = (label, value, secret) => `
      <div class="acct-cred"><span class="acct-cred-label">${label}</span>
        <code class="acct-cred-value${secret ? " secret" : ""}">${esc(value)}</code>
        <button type="button" class="acct-copy" data-copy="${esc(value)}" aria-label="Copy ${label.toLowerCase()}">${ICON.copy}</button></div>`;
    dlg.innerHTML = `
      <div class="acct-form">
        <h2>${fresh ? "Your test account" : "Account details"}</h2>
        ${fresh ? `<p class="acct-lead">Save these somewhere. This browser remembers them, but you'll need them to sign in anywhere else, or here after signing out.</p>` : ""}
        ${row("User name", a.username)}
        ${row("Password", a.password, true)}
        ${row("Address", addr)}
        <p class="acct-note">${a.guest ? `Others call you on ${esc(a.username)} or ${esc(addr)}. A test account that isn't used for ${a.expiresAfterDays ?? 30} days is deleted.` : `Others call you on ${esc(addr)}.`}</p>
        <button type="button" class="btn btn-primary acct-wide" id="acctDone">${fresh ? "I've saved them" : "Close"}</button>
      </div>`;
    dlg.querySelectorAll(".acct-copy").forEach((b) => b.onclick = () => {
      navigator.clipboard?.writeText(b.dataset.copy).catch(() => {});
      b.classList.add("copied");
      setTimeout(() => b.classList.remove("copied"), 1200);
    });
    dlg.querySelector("#acctDone").onclick = () => dlg.close();
    if (!dlg.open) dlg.showModal();
    dlg.querySelector("#acctDone").focus();
  }

  // Esc closes the details, but not the sign-in when there's no account yet.
  dlg.addEventListener("cancel", (e) => { if (!account) e.preventDefault(); });

  renderChip();
  return {
    set(a, s) {
      account = a;
      status = s;
      renderChip();
      if (!menu.hidden) renderMenu();
    },
    showSignIn,
  };
}
