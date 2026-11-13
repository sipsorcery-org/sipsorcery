// Settings: the gear in the top bar opens them, as in the UX prototype (prototype/web). Only
// the options with something behind them in these apps:
//  - Audio & video: the microphone, speaker and camera calls use (the same choice as the ▾ menus
//    on Mute and Camera).
//  - Appearance: the theme, system, light or dark, and Gravatar pictures for people (avatar.js).
// Nothing here repeats a button you already have: whether video calls start with your camera on
// (the Camera button before a call), the self view (Self view, V) and the grid (Grid, G) are each
// kept by their own button, so they used to be settings too and aren't any more. Gravatar stays,
// though it has no button, because it's a choice about what's sent to someone else's server.
//  - Diagnostics: opens the panel F12 opens, handy where a browser keeps F12 for itself.
// Every change applies straight away. They're kept in the page's storage: on the desktop that's
// the WebView2 profile's (--profile), so each profile has its own; on the web, this browser's.
// The prototype's ringtone and saved-transcript options have nothing behind them here yet, so
// they're left out.

const KEY = "softphonev2.settings";
// theme: "system", "light" or "dark". camOn: video calls start with the camera on. view: "focus"
// shows only the selected call; "grid" shows every active call side by side, with two or more.
// gravatar: show Gravatar pictures in place of initials (avatar.js).
export const prefs = { theme: "system", camOn: true, view: "focus", gravatar: true };
try { Object.assign(prefs, JSON.parse(localStorage.getItem(KEY) ?? "{}")); } catch { /* storage unavailable */ }
delete prefs.autoAnswer;   // "Answer calls automatically" was a setting once; --auto-answer is the testing switch

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

// The foot of Settings: which build this is. The desktop host sets window.softphoneVersion
// (MainForm): the app's version (the .csproj's <Version>), the SIPSorcery library's and the
// commit it was built from. The browser build has no SIPSorcery in it, so it just says what it is.
function versionText(web) {
  const v = window.softphoneVersion;
  if (!v?.sipsorcery) return web ? "Softphone v2, browser build" : "Softphone v2";
  return `Softphone ${v.app || "v2"} · SIPSorcery ${v.sipsorcery}${v.commit ? ` · build ${v.commit}` : ""}`;
}

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
      </section>
      <section class="settings-section" aria-labelledby="setThemeHead">
        <h3 id="setThemeHead">Appearance</h3>
        <div class="settings-seg" role="radiogroup" aria-labelledby="setThemeHead">
          ${["system", "light", "dark"].map((t) => `<label><input type="radio" name="theme" value="${t}">${t === "system" ? "System" : t === "light" ? "Light" : "Dark"}</label>`).join("")}
        </div>
        <label class="check-row"><input type="checkbox" data-pref="gravatar">Show Gravatar pictures for people</label>
        <p class="settings-note">In place of their initial: their picture, or a pattern made from their address if they have none. It asks gravatar.com, sending a hash of each address shown.</p>
      </section>
      <section class="settings-section" aria-labelledby="setDiagHead">
        <h3 id="setDiagHead">Diagnostics</h3>
        <button type="button" class="btn" id="setDiag"></button>
        <p class="settings-note">The SIP log and simulated network loss, with call quality and the test video sources. F12 opens and closes it too${web ? ", where the browser doesn't keep F12 for its own tools" : ""}.</p>
      </section>
    </div>
    <p class="settings-foot">${versionText(web)}</p>`;

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
    q('[data-pref="gravatar"]').checked = prefs.gravatar !== false;
    for (const r of dlg.querySelectorAll('input[name="theme"]')) r.checked = r.value === (prefs.theme || "system");
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
    } else if (t.dataset.pref === "gravatar") {
      savePref("gravatar", t.checked);
      document.dispatchEvent(new CustomEvent("gravatar-changed"));
      app.log(`settings: Gravatar pictures ${t.checked ? "on" : "off"}`);
    } else if (t.name === "theme") {
      savePref("theme", t.value);
      applyTheme();
      app.log(`settings: theme ${t.value}`);
    }
  });

  q("#setDiag").onclick = () => {
    const on = !app.diagnostics();
    dlg.close();
    app.setDiagnostics(on);
  };

  return { refresh: () => { if (dlg.open) fill(); } };
}
