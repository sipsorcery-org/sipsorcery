// Settings: the gear in the top bar opens them, as in the UX prototype (prototype/web). Only
// the options with something behind them in these apps:
//  - Audio & video: the microphone, speaker and camera calls use (the same choice as the ▾ menus
//    on Mute and Camera), whether video calls start with your camera on (the Camera button
//    before a call), and the self view (the self-view button, V).
//  - Calls: answer automatically, with your camera off, and whether several calls show in a grid
//    (the Focus | Grid switch in the control bar, and G, set the same thing).
//  - Appearance: the theme, system, light or dark.
//  - Account: who you're signed in as. Managing SIP accounts will live here.
//  - Diagnostics: opens the panel F12 opens, handy where a browser keeps F12 for itself.
// Every change applies straight away. They're kept in the page's storage: on the desktop that's
// the WebView2 profile's (--profile), so each profile has its own; on the web, this browser's.
// The prototype's ringtone and saved-transcript options have nothing behind them here yet, so
// they're left out.

const KEY = "softphonev2.settings";
// theme: "system", "light" or "dark". camOn: video calls start with the camera on. view: "focus"
// shows only the selected call; "grid" shows every active call side by side, with two or more.
export const prefs = { theme: "system", camOn: true, autoAnswer: false, view: "focus" };
try { Object.assign(prefs, JSON.parse(localStorage.getItem(KEY) ?? "{}")); } catch { /* storage unavailable */ }

export function savePref(name, value) {
  prefs[name] = value;
  try { localStorage.setItem(KEY, JSON.stringify(prefs)); } catch { /* private window: this visit only */ }
}

function applyTheme() {
  if (prefs.theme === "light" || prefs.theme === "dark") document.documentElement.dataset.theme = prefs.theme;
  else delete document.documentElement.dataset.theme;
}
applyTheme();

const GEAR = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/></svg>';
const CLOSE = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M18 6 6 18M6 6l12 12"/></svg>';

// app: what the call page lends Settings (see the end of app.js).
export function initSettings(app) {
  const btn = document.getElementById("settingsBtn");
  const dlg = document.getElementById("settings");
  const web = document.documentElement.dataset.host === "web";
  const canPickSpeaker = "setSinkId" in HTMLMediaElement.prototype;
  btn.innerHTML = GEAR;

  dlg.innerHTML = `
    <div class="settings-head">
      <h2 id="settingsTitle">Settings</h2>
      <button type="button" class="icon-btn settings-close" data-act="close" aria-label="Close settings" title="Close (Esc)">${CLOSE}</button>
    </div>
    <div class="settings-body">
      <section class="settings-section" aria-labelledby="setAvHead">
        <h3 id="setAvHead">Audio &amp; video</h3>
        <label class="settings-field"><span>Microphone</span><select data-device="mic"></select></label>
        <label class="settings-field" ${canPickSpeaker ? "" : "hidden"}><span>Speaker</span><select data-device="speaker"></select></label>
        <label class="settings-field"><span>Camera</span><select data-device="camera"></select></label>
        <p class="settings-note" id="setDevNote" hidden></p>
        <label class="check-row"><input type="checkbox" data-pref="camOn">Start video calls with my camera on</label>
        <p class="settings-note">Off, video calls start with your camera off, and you still see them. The Camera button sets this too, before a call.</p>
        <label class="check-row"><input type="checkbox" data-pref="selfView">Show my self view on video calls</label>
      </section>
      <section class="settings-section" aria-labelledby="setCallsHead">
        <h3 id="setCallsHead">Calls</h3>
        <label class="check-row"><input type="checkbox" data-pref="autoAnswer">Answer calls automatically</label>
        <p class="settings-note">After a second, with your camera off. Your microphone is on as soon as it answers.</p>
        <label class="check-row"><input type="checkbox" data-pref="grid">Show all calls in a grid</label>
        <p class="settings-note">With two or more calls. Picking one opens it on its own, with its controls. Focus | Grid in the control bar, or G, switches too.</p>
      </section>
      <section class="settings-section" aria-labelledby="setThemeHead">
        <h3 id="setThemeHead">Appearance</h3>
        <div class="settings-seg" role="radiogroup" aria-labelledby="setThemeHead">
          ${["system", "light", "dark"].map((t) => `<label><input type="radio" name="theme" value="${t}">${t === "system" ? "System" : t === "light" ? "Light" : "Dark"}</label>`).join("")}
        </div>
      </section>
      <section class="settings-section" aria-labelledby="setAcctHead">
        <h3 id="setAcctHead">Account</h3>
        <p class="settings-account" id="setAccount"></p>
        ${web
          ? `<p class="settings-note">The account button at the top shows its details, signs out, or signs in as someone else.</p>`
          : `<button type="button" class="btn" id="setManageAccounts">Manage accounts</button>
        <p class="settings-note">Add, edit or remove your SIP accounts, and see whether each is registered.</p>`}
      </section>
      <section class="settings-section" aria-labelledby="setDiagHead">
        <h3 id="setDiagHead">Diagnostics</h3>
        <button type="button" class="btn" id="setDiag"></button>
        <p class="settings-note">The SIP log and simulated network loss, with call quality and the test video sources. F12 opens and closes it too${web ? ", where the browser doesn't keep F12 for its own tools" : ""}.</p>
      </section>
    </div>`;

  const q = (sel) => dlg.querySelector(sel);

  // The device lists: "System default" first, then what's plugged in. A remembered device
  // that's unplugged stays listed, so you can see it's chosen but gone.
  async function fillDevices() {
    let list = [];
    try { list = await navigator.mediaDevices.enumerateDevices(); } catch { /* none to show */ }
    const kinds = { mic: "audioinput", speaker: "audiooutput", camera: "videoinput" };
    let unnamed = false;
    for (const [key, kind] of Object.entries(kinds)) {
      const sel = q(`select[data-device="${key}"]`);
      const all = list.filter((d) => d.kind === kind);
      const sys = all.find((d) => d.deviceId === "default");
      const named = all.filter((d) => d.deviceId && d.deviceId !== "default" && d.deviceId !== "communications");
      if (all.length && !named.some((d) => d.label)) unnamed = true;
      const opts = [["", sys?.label ? `System default (${sys.label.replace(/^Default\s*-\s*/i, "")})` : "System default"],
        ...named.map((d, i) => [d.deviceId, d.label || `${key === "mic" ? "Microphone" : key === "speaker" ? "Speaker" : "Camera"} ${i + 1}`])];
      const current = app.devices[key] === "default" ? "" : app.devices[key];
      if (current && !opts.some(([id]) => id === current)) opts.push([current, "Chosen device (not connected)"]);
      if (sel.dataset.sig === JSON.stringify(opts) && sel.value === current) continue;   // don't disturb an open list
      sel.replaceChildren(...opts.map(([id, label]) => Object.assign(document.createElement("option"), { value: id, textContent: label })));
      sel.value = current;
      sel.dataset.sig = JSON.stringify(opts);
    }
    const note = q("#setDevNote");
    note.hidden = !unnamed;
    note.textContent = "Device names show once a call has used your microphone or camera.";
  }

  function fill() {
    q('[data-pref="camOn"]').checked = app.camPref();
    q('[data-pref="selfView"]').checked = app.selfView();
    q('[data-pref="autoAnswer"]').checked = prefs.autoAnswer;
    q('[data-pref="grid"]').checked = prefs.view === "grid";
    for (const r of dlg.querySelectorAll('input[name="theme"]')) r.checked = r.value === (prefs.theme || "system");
    const acct = app.account();
    q("#setAccount").textContent = acct || "Not signed in";
    q("#setAccount").classList.toggle("none", !acct);
    q("#setDiag").textContent = app.diagnostics() ? "Close diagnostics" : "Open diagnostics";
    return fillDevices();
  }

  // The device lists are filled first (it's quick), so they don't open empty.
  async function open() {
    await fill();
    if (!dlg.open) dlg.showModal();
    btn.setAttribute("aria-expanded", "true");
    q("select").focus();   // the first setting, as in the prototype, not the close button
  }
  btn.onclick = open;
  dlg.addEventListener("close", () => { btn.setAttribute("aria-expanded", "false"); btn.focus(); });
  q('[data-act="close"]').onclick = () => dlg.close();
  // A click on the backdrop (outside the box) closes it, like the prototype's.
  dlg.addEventListener("click", (e) => { if (e.target === dlg) dlg.close(); });
  // Keys typed here stay here: V, T and the like are the call's shortcuts. (F12 is caught
  // before this, on the way down, and Esc's closing is the dialog's own.)
  dlg.addEventListener("keydown", (e) => e.stopPropagation());
  navigator.mediaDevices?.addEventListener?.("devicechange", () => { if (dlg.open) fillDevices(); });

  dlg.addEventListener("change", async (e) => {
    const t = e.target;
    if (t.dataset.device) {
      await app.pickDevice(t.dataset.device, t.value);
      fillDevices();
    } else if (t.dataset.pref === "camOn") {
      app.setCamPref(t.checked);
    } else if (t.dataset.pref === "selfView") {
      app.setSelfView(t.checked);
    } else if (t.dataset.pref === "autoAnswer") {
      savePref("autoAnswer", t.checked);
      app.log(`settings: answer calls automatically ${t.checked ? "on" : "off"}`);
    } else if (t.dataset.pref === "grid") {
      app.setView(t.checked ? "grid" : "focus");
    } else if (t.name === "theme") {
      savePref("theme", t.value);
      applyTheme();
      app.log(`settings: theme ${t.value}`);
    }
  });

  // Desktop: Manage accounts (accounts.js) is the view the dial bar's people button opens.
  const manage = q("#setManageAccounts");
  if (manage) manage.onclick = () => {
    dlg.close();
    // After the dialog's close event has put focus back on the gear.
    setTimeout(() => document.getElementById("manageAccounts")?.click(), 0);
  };
  q("#setDiag").onclick = () => {
    const on = !app.diagnostics();
    dlg.close();
    app.setDiagnostics(on);
  };

  return { refresh: () => { if (dlg.open) fill(); } };
}
