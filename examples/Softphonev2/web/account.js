// The web softphone's SIP account: kept in this browser, created as a guest account on the
// server (behind a captcha, or by signing in with GitHub), or typed in to sign in. Also the account chip in the header and the
// sign-in dialog. host.js decides when to show them and does the registering.

const q = new URLSearchParams(location.search);

// The guest-account API, POST <api>/api/guest-accounts { captchaToken } ->
// { username, password, domain, wsUri, expiresAfterDays }. By default sipsorcery.com's; api=stub
// makes accounts up in this page instead, to try the flow against the desktop app.
const API = (q.get("api") || "https://www.sipsorcery.com").replace(/^stub$/, "");
// Cloudflare Turnstile's site key: sipsorcery.com's, or with the stub, Turnstile's test key, which
// always passes. (A local server takes the test key too: captcha=1x00000000000000000000AA.)
const CAPTCHA_SITE_KEY = q.get("captcha") || (API ? "0x4AAAAAAF6HyKfi5E74QvDa" : "1x00000000000000000000AA");
// Where the stub's accounts connect: by default the desktop app's WebSocket. The desktop isn't a
// registrar, so they don't register (calls out only), but the whole flow can be tried without the
// server.
const STUB_WS = q.get("stubws") || `ws://${location.hostname || "localhost"}:8081`;
const DEFAULT_WS = q.get("wsdefault") || "wss://sipsorcery.com";
// Testing against a local server: register guest accounts through this WebSocket instead of the
// one the server returns (its GuestAccounts:WebSocketUri, normally wss://sipsorcery.com).
const WS_OVERRIDE = q.get("ws") || "";

const KEY = "softphonev2.account";

export function loadAccount() {
  try {
    const a = JSON.parse(localStorage.getItem(KEY) || "null");
    if (!(a?.username && a?.password && a?.domain && a?.wsUri)) return null;
    // A test account belongs to the server that made it (or the stub): one from a local server
    // or the stub isn't used against sipsorcery.com. (Kept until another account replaces it.)
    return a.guest && (a.api ?? "") !== API ? null : a;
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
  const url = new URL("/api/guest-accounts", API);
  // A network failure, or the server not letting this page's origin in (CORS): fetch can't say which.
  const resp = await fetch(url, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ captchaToken }),
  }).catch(() => { throw new Error(`Couldn't reach ${url.host}, or it doesn't take requests from ${location.origin}.`); });
  const body = await resp.json().catch(() => ({}));
  if (resp.status === 429) throw new Error("Too many test accounts from here just now. Try again later.");
  if (!resp.ok) throw new Error(body.error || `The server said ${resp.status}.`);
  return WS_OVERRIDE ? { ...body, wsUri: WS_OVERRIDE } : body;
}

// A guest account from signing in with GitHub, named after the GitHub login (octocat ->
// sips:octocat@sipsorcery.com) and tied to the GitHub user: signing in again, from any browser,
// returns the same account and password. No captcha. The server's page does GitHub in a popup
// and posts the account (or an error sentence) back to this page; there's no cookie or CORS, as
// this page is on another origin. Must be called straight from the click, or the popup's blocked.
//   GET <api>/api/guest-accounts/github?origin=<this page's origin>   (the popup)
//   -> postMessage { type: "sipsorcery-guest-account", account: { username, password, domain,
//      wsUri, expiresAfterDays, github } } or { type: "sipsorcery-guest-account", error }
// The origin must be one of the server's GuestAccounts:AllowedOrigins, or the popup shows a 400.
// Rejects with .cancelled set when the popup is closed with nothing posted.
const GITHUB_MESSAGE = "sipsorcery-guest-account";
function githubGuest() {
  if (!API) {
    // The stub: no popup, a made-up account named after a made-up login (github=<login> to pick).
    const login = (q.get("github") || "octocat").toLowerCase();
    const pw = [...crypto.getRandomValues(new Uint8Array(18))].map((b) => b.toString(36).padStart(2, "0")).join("").slice(0, 24);
    return new Promise((r) => setTimeout(() => r({ username: login, password: pw, domain: "localhost", wsUri: STUB_WS,
      expiresAfterDays: 30, github: login, noRegister: true }), 400));
  }
  const url = new URL(`/api/guest-accounts/github?origin=${encodeURIComponent(location.origin)}`, API);
  const popup = window.open(url, "sipsorcery-github", "popup,width=600,height=720");
  if (!popup) return Promise.reject(new Error("Allow pop-ups for this page to sign in with GitHub."));
  const origin = new URL(API).origin;
  return new Promise((resolve, reject) => {
    let done = false, timer = 0;
    const finish = (fn, value) => {
      if (done) return;
      done = true;
      clearInterval(timer);
      window.removeEventListener("message", onMessage);
      fn(value);
    };
    // Only the server's page, in our popup, saying what we expect.
    function onMessage(e) {
      if (e.origin !== origin || e.source !== popup || e.data?.type !== GITHUB_MESSAGE) return;
      const a = e.data.account;
      if (e.data.error) finish(reject, new Error(String(e.data.error)));
      else if (a?.username && a?.password && a?.domain && a?.wsUri) finish(resolve, WS_OVERRIDE ? { ...a, wsUri: WS_OVERRIDE } : a);
      else finish(reject, new Error("GitHub sign-in didn't come back with an account. Try again."));
    }
    window.addEventListener("message", onMessage);
    // Closed with nothing posted: cancelled. (A moment's grace, as the page posts and then closes.)
    timer = setInterval(() => {
      if (!popup.closed) return;
      clearInterval(timer);
      setTimeout(() => finish(reject, Object.assign(new Error("GitHub sign-in was cancelled."), { cancelled: true })), 300);
    }, 500);
  });
}

// What the sign-in dialog says when a saved GitHub guest is turned down by the server.
export function githubGoneNote(a) {
  return `The server didn't take ${a.username}'s password. If you've renamed on GitHub, signing in with GitHub replaced this account with one under your new login: sign in with GitHub again to get it.`;
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
  github: `<svg viewBox="0 0 16 16" aria-hidden="true" class="acct-gh-mark"><path fill="currentColor" d="M8 0C3.58 0 0 3.58 0 8a8 8 0 0 0 5.47 7.59c.4.07.55-.17.55-.38v-1.33c-2.23.48-2.7-1.07-2.7-1.07-.36-.92-.89-1.17-.89-1.17-.73-.5.06-.49.06-.49.8.06 1.23.83 1.23.83.72 1.22 1.88.87 2.34.66.07-.52.28-.87.5-1.07-1.78-.2-3.65-.89-3.65-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.6 7.6 0 0 1 4 0c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48v2.2c0 .21.15.46.55.38A8 8 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/></svg>`,
  copy: `<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/></svg>`,
  user: `<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>`,
  eye: `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/></svg>`,
  eyeOff: `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9.9 5.2A10 10 0 0 1 12 5c6.5 0 10 7 10 7a17 17 0 0 1-2.6 3.6M6.6 6.6A17 17 0 0 0 2 12s3.5 7 10 7a9.7 9.7 0 0 0 5.4-1.6"/><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2"/><path d="M3 3l18 18"/></svg>`,
};

// The address others call us on. sipsorcery.com's as sips: a desktop phone calling it over UDP
// sends an INVITE with video too big for one packet, and the fragments don't arrive; sips is TLS.
export function addressOf(a) {
  return `${/^sipsorcery\.com$/i.test(a.domain) ? "sips" : "sip"}:${a.username}@${a.domain}`;
}

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
      : status.state === "registered" ? addressOf(account)
      : status.state === "connected" ? `${addressOf(account)} (not registered: calls out only)`
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
      ${account.github && status.state === "failed" && status.auth ? `<button type="button" role="menuitem" data-act="github">Sign in with GitHub again</button>` : ""}
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
    if (act === "copy") navigator.clipboard?.writeText(addressOf(account)).catch(() => {});
    else if (act === "details") showDetails(account, false);
    else if (act === "github") showSignIn("", githubGoneNote(account));
    else if (act === "out") {
      // A GitHub guest gets the account back by signing in with GitHub again: no need to ask.
      if (account.guest && !account.github && !confirm("Sign out? Keep your user name and password if you want to use this test account again.")) return;
      on.signOut();
    }
  };

  // ---- The dialog: sign in, or create a test account ----

  // error: under the sign-in form (registering the typed account failed). note: under the test
  // account buttons (a saved GitHub guest the server turned down).
  function showSignIn(error = "", note = "") {
    dlg.innerHTML = `
      <form method="dialog" class="acct-form" id="acctCreate">
        <h2>Sign in to make calls</h2>
        <p class="acct-lead">A test account is free and takes a moment. It can call other SIPSorcery users and SIP addresses, but not phone numbers.</p>
        <button type="submit" class="btn btn-primary acct-wide" id="acctCreateBtn">Create a test account</button>
        <button type="button" class="btn acct-wide acct-github" id="acctGitHubBtn">${ICON.github}<span>Sign in with GitHub</span></button>
        <p class="acct-note">With GitHub, the account is named after you (your GitHub login), and signing in with GitHub again, in any browser, gets it back. No captcha.</p>
        <div class="acct-captcha" id="acctCaptcha" hidden></div>
        <p class="acct-error" id="acctCreateErr" ${note ? "" : "hidden"}>${esc(note)}</p>
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

    // A guest account, from either way: shown to keep, and signed in with. It's kept even if
    // registering fails; the header shows why.
    const useGuest = (created) => {
      const a = { username: created.username, password: created.password, domain: created.domain,
        wsUri: created.wsUri, guest: true, expiresAfterDays: created.expiresAfterDays ?? 30, api: API,
        ...(created.github ? { github: created.github } : {}), ...(created.noRegister ? { noRegister: true } : {}) };
      showDetails(a, true);
      on.signIn(a).catch(() => {});
    };
    const createBtn = dlg.querySelector("#acctCreateBtn");
    const githubBtn = dlg.querySelector("#acctGitHubBtn");
    const createErr = dlg.querySelector("#acctCreateErr");
    const showError = (message) => { createErr.textContent = message; createErr.hidden = !message; };

    createForm.onsubmit = async (e) => {
      e.preventDefault();
      showError("");
      createBtn.disabled = githubBtn.disabled = true;
      createBtn.textContent = "Checking you're not a robot…";
      try {
        const token = await captcha(dlg.querySelector("#acctCaptcha"));
        createBtn.textContent = "Creating your account…";
        useGuest(await createGuest(token));
      } catch (ex) {
        showError(ex.message);
        createBtn.disabled = githubBtn.disabled = false;
        createBtn.textContent = "Create a test account";
      }
    };

    // The popup opens in this click (githubGuest's window.open), so it isn't blocked.
    githubBtn.onclick = async () => {
      showError("");
      const pending = githubGuest();
      createBtn.disabled = githubBtn.disabled = true;
      githubBtn.querySelector("span").textContent = API ? "Waiting for GitHub…" : "Signing in…";
      try {
        useGuest(await pending);
      } catch (ex) {
        if (!ex.cancelled) showError(ex.message);
        createBtn.disabled = githubBtn.disabled = false;
        githubBtn.querySelector("span").textContent = "Sign in with GitHub";
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
    dlg.querySelector(note ? "#acctGitHubBtn" : "#acctCreateBtn").focus();
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
        // 110200: the site key's widget doesn't list this page's host name.
        "error-callback": (code) => reject(new Error(String(code) === "110200"
          ? `The captcha isn't set up for ${location.hostname} (Turnstile error 110200).`
          : `The captcha didn't work${code ? ` (Turnstile error ${code})` : ""}. Try again.`)),
        "expired-callback": () => reject(new Error("The captcha expired. Try again.")),
      });
    });
  }

  // The account's user name and password, to keep. `fresh`: just created, so say why.
  function showDetails(a, fresh) {
    const addr = addressOf(a);
    // The password is dots until the eye shows it; Copy copies it either way.
    const row = (label, value, secret) => `
      <div class="acct-cred"><span class="acct-cred-label">${label}</span>
        <code class="acct-cred-value${secret ? " secret" : ""}">${secret ? "••••••••••••" : esc(value)}</code>
        <span class="acct-cred-actions">
          ${secret ? `<button type="button" class="acct-copy acct-reveal" aria-label="Show password" aria-pressed="false" title="Show password">${ICON.eye}</button>` : ""}
          <button type="button" class="acct-copy" data-copy="${esc(value)}" aria-label="Copy ${label.toLowerCase()}">${ICON.copy}</button></span></div>`;
    dlg.innerHTML = `
      <div class="acct-form">
        <h2>${fresh && a.github ? "Your GitHub test account" : fresh ? "Your test account" : "Account details"}</h2>
        ${fresh && a.github ? `<p class="acct-lead">Signed in with GitHub as ${esc(a.github)}. This browser remembers the account, and signing in with GitHub again, here or in any other browser, gets it back. The user name and password below also work in any SIP phone.</p>`
          : fresh ? `<p class="acct-lead">Save these somewhere. This browser remembers them, but you'll need them to sign in anywhere else, or here after signing out.</p>` : ""}
        ${row("User name", a.username)}
        ${row("Password", a.password, true)}
        ${row("Address", addr)}
        <p class="acct-note">${a.guest ? `Others call you on ${esc(a.username)} or ${esc(addr)}. A test account that isn't used for ${a.expiresAfterDays ?? 30} days is deleted${a.github ? "; signing in with GitHub makes it again, if the name's still free" : ""}.` : `Others call you on ${esc(addr)}.`}</p>
        <button type="button" class="btn btn-primary acct-wide" id="acctDone">${fresh && !a.github ? "I've saved them" : fresh ? "Done" : "Close"}</button>
      </div>`;
    const reveal = dlg.querySelector(".acct-reveal");
    reveal.onclick = () => {
      const show = reveal.getAttribute("aria-pressed") !== "true";
      dlg.querySelector(".acct-cred-value.secret").textContent = show ? a.password : "••••••••••••";
      reveal.setAttribute("aria-pressed", String(show));
      reveal.setAttribute("aria-label", show ? "Hide password" : "Show password");
      reveal.title = show ? "Hide password" : "Show password";
      reveal.innerHTML = show ? ICON.eyeOff : ICON.eye;
    };
    dlg.querySelectorAll(".acct-copy[data-copy]").forEach((b) => b.onclick = () => {
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
