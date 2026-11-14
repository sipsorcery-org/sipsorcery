// The keypad (DTMF).

import { entries, holdState, inGrid, nameOf, render, sel, selectedId } from "./app.js";
import { openMenu } from "./menus.js";
import { $, ICON, log, post } from "./util.js";

// ---- The keypad (DTMF), from the prototype ----
// The Keypad button (Focus view; the grid hides the controls) opens a 12-key pad above it for the
// selected call. Each key sends a tone on that call: the host sends it as an RFC 4733
// telephone-event, or by SIP INFO if the far end didn't agree telephone-event (the web build:
// RTCDTMFSender). While it's open the number keys, * and # send tones rather than picking calls,
// and Esc closes it. It closes when the call is held (either way), ends, or another is selected.
export let keypadOpen = false;    // the keypad, in the dock: see renderKeypad
let keypadFor = null;      // the call it was opened on: it closes if another is selected
let keypadDigits = "";     // the tones sent since it opened
const KEYPAD_KEYS = [["1", ""], ["2", "ABC"], ["3", "DEF"], ["4", "GHI"], ["5", "JKL"], ["6", "MNO"],
  ["7", "PQRS"], ["8", "TUV"], ["9", "WXYZ"], ["*", ""], ["0", "+"], ["#", ""]];
// Why the selected call can't take tones now, or null if it can.
function keypadBlocked() {
  const e = sel();
  if (!e || e.h.call !== "connected") return "Keypad, during a call";
  if (inGrid()) return "Keypad: pick the call to open it in Focus";
  if (e.pc?.connectionState !== "connected") return "Keypad: waiting for the call's audio";
  if (holdState(e)) return "Keypad: resume the call to send tones";
  if (e.h.heldByThem) return `Keypad: ${nameOf(e)} has put you on hold`;
  if (e.h.dtmf === false) return "This call can't send keypad tones";
  return null;
}
export function renderKeypad() {
  const btn = $("keypadBtn");
  const blocked = keypadBlocked();
  if (keypadOpen && (blocked || selectedId !== keypadFor)) {
    keypadOpen = false;
    if (document.activeElement && $("keypad").contains(document.activeElement)) btn.focus();
  }
  if (!keypadOpen) keypadDigits = "";
  btn.disabled = !!blocked;
  btn.setAttribute("aria-pressed", String(keypadOpen));
  btn.setAttribute("aria-expanded", String(keypadOpen));
  if (!btn.innerHTML) btn.innerHTML = ICON.keypad;
  btn.title = blocked ?? (keypadOpen ? "Close keypad (Esc)" : "Keypad: send tones (DTMF)");
  // --dtmf <keys> (testing): once a call is up, open the keypad and press them, as typed keys
  // would, 2 s in (so audio is flowing to put the tones in), one every 400 ms.
  const testKeys = new URLSearchParams(location.search).get("dtmf");
  const e = sel();
  if (testKeys && !blocked && e && !e.dtmfTested) {
    e.dtmfTested = true;
    setTimeout(async () => {
      if (selectedId !== e.id) return;
      setKeypadOpen(true);
      for (const k of testKeys) {
        document.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true }));
        await new Promise((r) => setTimeout(r, 400));
      }
    }, 2000);
  }
  $("keypad").hidden = !keypadOpen;
  if (!keypadOpen) { $("keypadIn").textContent = ""; return; }
  $("keypadTitle").textContent = `Keypad · ${nameOf(sel())}`;
  $("keypadDigits").textContent = keypadDigits;
}
export function setKeypadOpen(open) {
  if (open && keypadBlocked()) return;
  keypadOpen = open;
  keypadFor = open ? selectedId : null;
  if (open && sel()) sel().dtmfIn = "";
  render();
}
function pressKey(digit) {
  const e = sel();
  if (!keypadOpen || keypadBlocked() || !e) return;
  keypadDigits = (keypadDigits + digit).slice(-20);
  $("keypadDigits").textContent = keypadDigits;
  const key = $("keypadKeys").querySelector(`[data-digit="${CSS.escape(digit)}"]`);
  if (key) { key.classList.add("hit"); setTimeout(() => key.classList.remove("hit"), 150); }
  log(`call ${e.id}: keypad '${digit}' → ${nameOf(e)}`);
  post({ type: "dtmf", id: e.id, digit });
}
// Tones they send: in the log (the host logs them), and under the keys while it's open on that call.
export function onDtmfIn(id, digit) {
  const e = entries.get(id);
  if (!e || !keypadOpen || id !== keypadFor) return;
  e.dtmfIn = ((e.dtmfIn ?? "") + digit).slice(-20);
  $("keypadIn").textContent = `${nameOf(e)} sent ${e.dtmfIn}`;
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initKeypad() {
  $("keypadKeys").innerHTML = KEYPAD_KEYS.map(([d, s]) => `<button type="button" class="key" data-digit="${d}" aria-label="${d}${s ? ` ${s}` : ""}"><span><b>${d}</b><br><small>${s || "&nbsp;"}</small></span></button>`).join("");
  $("keypadKeys").onclick = (ev) => { const k = ev.target.closest(".key"); if (k) pressKey(k.dataset.digit); };
  $("keypadBtn").onclick = () => setKeypadOpen(!keypadOpen);
  $("keypadClose").innerHTML = ICON.close;
  $("keypadClose").onclick = () => { setKeypadOpen(false); $("keypadBtn").focus(); };
  // Before the shortcuts (capture): while it's open, 0–9, * and # are tones (1–8 would pick calls),
  // and Esc closes it. Not while typing in a field (the address bar, say).
  document.addEventListener("keydown", (ev) => {
    if (!keypadOpen || ev.ctrlKey || ev.altKey || ev.metaKey || openMenu) return;
    if (ev.target.closest?.("input, textarea, select, [contenteditable]")) return;
    if (ev.key === "Escape") { ev.preventDefault(); ev.stopImmediatePropagation(); setKeypadOpen(false); $("keypadBtn").focus(); return; }
    if (!/^[0-9*#]$/.test(ev.key)) return;
    ev.preventDefault();
    ev.stopImmediatePropagation();
    if (!ev.repeat) pressKey(ev.key);
  }, true);
}
