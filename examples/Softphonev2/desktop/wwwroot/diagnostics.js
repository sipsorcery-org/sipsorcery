// Diagnostics (F12): the panel with the log, call quality and test sources, and the security popover.

import { entries, nextVideo, params, render, sel, setCamera } from "./app.js";
import { closeMenu, openMenu, showLoss } from "./menus.js";
import { renderDetails, renderQuality } from "./quality.js";
import { $, ICON, post } from "./util.js";
import { SOURCE_LABEL, setVideoSource, sourceOf } from "./video.js";

// ---- Diagnostics (F12) ----
// A panel docked at the bottom, like a browser's developer tools: the SIP log, Call details,
// Call quality and Network (the loss sliders). While it's open the app is in diagnostics mode, which also shows
// your SIP address in the header, and the test video sources. Closed at every start
// (--diagnostics opens it, for testing). Ctrl+Shift+I is left to WebView2's own DevTools.
export function setDiagnostics(on) {
  document.body.classList.toggle("diag", on);
  $("devPanel").hidden = !on;
  fitDevTabs();
  if (on) { const el = $("log"); el.scrollTop = el.scrollHeight; }
  // The test pattern and audio bars are diagnostics things, so the camera takes over again.
  // But it isn't sent until you turn it on: the camera never starts by itself. (With no camera
  // the placeholder goes, as it would anyway.)
  if (!on) {
    const onTest = [...entries.values()].filter((e) => e.m && sourceOf(e) !== "camera");
    const sending = onTest.filter((e) => e.m.hasVideo && e.m.camOn);
    for (const e of sending) setCamera(false, e);
    for (const e of onTest) e.source = "camera";
    if (onTest.length || nextVideo.source !== "camera") setVideoSource("camera");
  }
  // Simulated loss is a diagnostics thing too: leaving diagnostics turns it off, so it can't
  // quietly carry on spoiling calls with the sliders out of sight.
  if (!on && (Number($("lossAudio").value) > 0 || Number($("lossVideo").value) > 0)) {
    $("lossAudio").value = 0;
    $("lossVideo").value = 0;
    showLoss();
    post({ type: "loss", audio: 0, video: 0 });
  }
  closeMenu();
  renderQuality();
  render();
}

// The padlock in the call info: green with SIP and the media both encrypted, yellow with one of
// them, and grey and open with neither. A click shows the call's security under it.
export function renderInfoLock() {
  const d = sel()?.details;
  const b = $("infoLock");
  b.hidden = !d;
  if (!d) return;
  const n = Number(!!d.mediaEncrypted) + Number(!!d.signallingEncrypted);
  const icon = n ? ICON.lock : ICON.unlock;
  if (b.innerHTML !== icon) b.innerHTML = icon;
  b.classList.toggle("partial", n === 1);
  b.classList.toggle("none", n === 0);
  const sip = (d.signalling ?? "UDP").split(" ")[0];
  const how = d.mediaSecurity ?? "SRTP";
  const what = n === 2 ? `Encrypted: ${how} media, SIP over ${sip === "WSS" ? "WSS (TLS)" : "TLS"}`
    : n === 0 ? `Not encrypted: plain RTP, SIP over ${sip}`
    : d.mediaEncrypted ? `Media encrypted (${how}); SIP unencrypted (${sip})` : "SIP encrypted (TLS); media unencrypted (plain RTP)";
  b.title = `${what}. Click for more.`;
  b.setAttribute("aria-label", b.title);
  renderSecurity();
}

// The call's security, for anyone, under the padlock: is the conversation (voice and video)
// encrypted, and the call setup (SIP: who's calling whom)? Each with the technical name small
// (DTLS + SRTP, SIP over TLS), so it can be recognised, then what that means in a sentence.
// (The rest is Call details, in Diagnostics.) Media encrypted with plain SRTP but SIP not is only partly
// protected: its keys travel in the SIP, in the clear. Esc, a click away or the padlock closes it.
let secHtml = "";
function renderSecurity() {
  const pop = $("secPop");
  if (pop.hidden) return;
  const d = sel()?.details;
  if (!d || $("info").hidden) { setSecurityOpen(false); return; }
  const esc = (t) => String(t).replace(/[&<>"]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[ch]));
  const media = !!d.mediaEncrypted, sip = !!d.signallingEncrypted;
  const how = d.mediaSecurity ?? "SRTP";
  const keysInSip = media && !sip && how !== "DTLS + SRTP";   // SDES: the keys are in the SIP
  const transport = (d.signalling ?? "UDP").split(" ")[0];
  const [title, cls, means] =
    media && sip ? ["This call is encrypted", "ok", "Others on the network can't listen in or see who you're calling."]
    : keysInSip ? ["This call is only partly protected", "part", "Your conversation is encrypted, but the key to it is sent in the unencrypted call setup, so someone watching the network could still listen in."]
    : media ? ["Your conversation is encrypted", "part", "Others on the network can't listen in, but could see who you're calling."]
    : sip ? ["Your conversation isn't encrypted", "part", "Others on the network can't see who you're calling, but could listen in."]
    : ["This call isn't encrypted", "off", "Someone on the network between you could listen in and see who you're calling."];
  const row = (on, what, sub, tech) => `<li class="sec-row"><span class="${on ? "ok" : "off"}">${on ? ICON.lock : ICON.unlock}</span>`
    + `<span class="sec-what">${what}</span><span class="sec-state${on ? "" : " no"}">${on ? "Encrypted" : "Not encrypted"}</span>`
    + `<span class="sec-sub">${sub} · ${esc(tech)}</span></li>`;
  const html = `<div class="sec-title" id="secTitle"><span class="${cls}">${media || sip ? ICON.lock : ICON.unlock}</span>${title}</div>`
    + `<ul class="sec-rows">`
    + row(media, "Your conversation", "Voice and video", media ? how : "plain RTP")
    + row(sip, "Call setup", "Who's calling whom", sip ? (transport === "WSS" ? "SIP over WSS (TLS)" : "SIP over TLS") : `SIP over ${transport}`)
    + `</ul><p class="sec-means">${means}</p>`;
  if (html === secHtml) return;
  pop.innerHTML = secHtml = html;
}
function setSecurityOpen(on) {
  $("secPop").hidden = !on;
  $("infoLock").setAttribute("aria-expanded", String(on));
  secHtml = "";
  if (on) renderSecurity();
}

function setDevTab(tab) {
  for (const b of document.querySelectorAll(".dev-tab")) b.setAttribute("aria-selected", String(b.dataset.tab === tab));
  $("log").hidden = tab !== "log";
  $("paneDetails").hidden = tab !== "details";
  $("paneQuality").hidden = tab !== "quality";
  $("paneNet").hidden = tab !== "net";
  $("logCopy").hidden = $("logClear").hidden = tab !== "log";
  fitDevTabs();
  if (tab === "log") $("log").scrollTop = $("log").scrollHeight;
  if (tab === "details") renderDetails();
  if (tab === "quality") renderQuality();
}

// ---- The bar at phone width: a ⋯ for what doesn't fit ----
// The open tab always shows; the others from the left as fit, the rest (from the right) go into
// the ⋯ menu, with Copy log and Clear while the SIP log is open. Wider, everything's on the bar.
const compactBar = () => matchMedia("(max-width: 500px)").matches;
export function fitDevTabs() {
  const tabs = [...document.querySelectorAll(".dev-tab")];
  for (const t of tabs) t.hidden = false;
  const compact = compactBar() && !$("devPanel").hidden;
  if (compact) {
    const box = document.querySelector(".dev-tabs");
    for (const t of [...tabs].reverse()) {
      if (box.scrollWidth <= box.clientWidth) break;
      if (t.getAttribute("aria-selected") !== "true") t.hidden = true;
    }
  }
  const hidden = tabs.filter((t) => t.hidden);
  $("devMore").hidden = !compact || (!hidden.length && $("logCopy").hidden);
  const dot = hidden.some((t) => t.querySelector(".dev-dot"));
  if (!!$("devMoreBtn").querySelector(".dev-dot") !== dot) {
    if (dot) $("devMoreBtn").append(Object.assign(document.createElement("span"), { className: "dev-dot" }));
    else $("devMoreBtn").querySelector(".dev-dot").remove();
  }
  if ($("devMore").hidden && openMenu === "dev") closeMenu();
}
// The ⋯ menu (opened by menus.js, showMenu("dev")): each item does what its tab or button does.
export function fillDevMenu() {
  const menu = $("devMenu");
  menu.replaceChildren();
  const item = (label, onclick, dot) => {
    const opt = Object.assign(document.createElement("button"), { type: "button", className: "device-option", onclick });
    opt.setAttribute("role", "menuitem");
    opt.append(Object.assign(document.createElement("span"), { textContent: label }));
    if (dot) opt.append(Object.assign(document.createElement("span"), { className: "dev-dot", title: "Loss is on" }));
    menu.append(opt);
    return opt;
  };
  const hiddenTabs = [...document.querySelectorAll(".dev-tab")].filter((t) => t.hidden);
  for (const t of hiddenTabs) item(t.textContent, () => { closeMenu(); setDevTab(t.dataset.tab); $("devMoreBtn").focus(); }, !!t.querySelector(".dev-dot"));
  if ($("logCopy").hidden) return;
  if (hiddenTabs.length) menu.append(Object.assign(document.createElement("div"), { className: "device-menu-sep" }));
  // Copy log says it's done in the menu, as the button would, then the menu closes.
  const copy = item("Copy log", () => {
    $("logCopy").click();
    copy.firstChild.textContent = "Copied";
    setTimeout(closeMenu, 700);
  });
  item("Clear log", () => { closeMenu(); $("logClear").click(); $("devMoreBtn").focus(); });
}

// Dragging the panel's top edge resizes it, between 120 px and 70% of the window. The height you
// leave it at is remembered (localStorage, per profile on the desktop) and used next time, kept
// within the same limits in case the window is smaller now.
const devHeight = (h) => Math.round(Math.max(120, Math.min(window.innerHeight * 0.7, h)));

// Run by app.js at this point in its start-up, in the order it always ran.
export function initDiagnostics() {
  document.body.classList.toggle("diag", params.get("diag") === "1");
  $("devPanel").hidden = params.get("diag") !== "1";
  // F12, caught before WebView2 would open its DevTools with it.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "F12" || e.ctrlKey || e.altKey || e.shiftKey || e.metaKey) return;
    e.preventDefault();
    e.stopPropagation();
    setDiagnostics($("devPanel").hidden);
  }, true);
  $("devClose").innerHTML = ICON.close;
  $("devClose").onclick = () => setDiagnostics(false);
  $("infoLock").setAttribute("aria-haspopup", "dialog");
  $("infoLock").onclick = () => setSecurityOpen($("secPop").hidden);
  document.addEventListener("keydown", (ev) => {
    if ($("secPop").hidden || ev.key !== "Escape") return;
    ev.preventDefault();
    ev.stopImmediatePropagation();
    setSecurityOpen(false);
    $("infoLock").focus();
  }, true);
  document.addEventListener("click", (ev) => {
    if (!$("secPop").hidden && !$("secPop").contains(ev.target) && !$("infoLock").contains(ev.target)) setSecurityOpen(false);
  }, true);
  for (const b of document.querySelectorAll(".dev-tab")) b.onclick = () => setDevTab(b.dataset.tab);
  $("logCopy").onclick = () => {
    post({ type: "copy", text: $("log").textContent });
    $("logCopy").textContent = "Copied";
    setTimeout(() => { $("logCopy").textContent = "Copy log"; }, 1500);
  };
  $("logClear").onclick = () => { $("log").textContent = ""; };
  addEventListener("resize", fitDevTabs);
  fitDevTabs();
  try {
    const saved = Number(localStorage.getItem("devPanelHeight"));
    if (saved > 0) $("devPanel").style.setProperty("--dev-h", `${devHeight(saved)}px`);
  } catch { /* storage unavailable */ }
  $("devResize").addEventListener("pointerdown", (e) => {
    e.preventDefault();
    const handle = e.currentTarget;
    handle.setPointerCapture(e.pointerId);
    document.body.classList.add("dev-resizing");
    const move = (ev) => {
      $("devPanel").style.setProperty("--dev-h", `${devHeight(window.innerHeight - ev.clientY)}px`);
    };
    const up = () => {
      handle.removeEventListener("pointermove", move);
      document.body.classList.remove("dev-resizing");
      const h = parseInt($("devPanel").style.getPropertyValue("--dev-h"), 10);
      if (h > 0) try { localStorage.setItem("devPanelHeight", String(h)); } catch { /* storage unavailable */ }
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", up, { once: true });
    handle.addEventListener("pointercancel", up, { once: true });
  });
  if (SOURCE_LABEL[params.get("video")]) setVideoSource(params.get("video"));   // --test-pattern, --audio-bars (testing)
}
