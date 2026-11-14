// Real-time text (RFC 4103): the text panel and the toast for a line that arrives.

import { entries, inGrid, liveEntries, nameOf, render, sel, selectCall, selectedId, setView, state, testSay } from "./app.js";
import { openMenu } from "./menus.js";
import { $, ICON, post, shortName } from "./util.js";

// ---- Real-time text (RFC 4103) ----
// The host sends and receives the T.140 text; the page shows the conversation. What you type is
// sent when you press Send or Enter, as the line and a T.140 line separator (U+2028). Theirs
// may arrive letter by letter (a real-time text phone): their unfinished line shows as it comes,
// backspaces and all, dashed with a caret.
const LINE_SEP = "\u2028";
// Each call has its own conversation (entry.text): { lines: [{ me, text, open }], unread (they
// typed while you weren't reading it: a dot on the Text button and in the call list), bomSent
// (T.140 starts with a byte order mark), said (--say, testing, typed once per call) }. The
// text panel shows the selected call's.
export let textOpen = false;        // the text panel, in the dock
let toastTimer = 0;
export let toastCallId = null;      // the call the toast is about

export const openLine = (t, me, create = true) => {
  let l = t.lines.findLast((x) => x.me === me && x.open);
  if (!l && create) { l = { me, text: "", open: true }; t.lines.push(l); }
  return l;
};
function closeLine(t, me) {
  const l = openLine(t, me, false);
  if (!l) return;
  l.open = false;
  if (!l.text) t.lines.splice(t.lines.indexOf(l), 1);
}

export function onTextReceived(id, chars) {
  const e = entries.get(id);
  if (!e) return;
  const t = e.text;
  for (const ch of chars.replace(/\uFEFF/g, "").replace(/\r\n/g, "\n")) {
    if (ch === "\b") { const l = openLine(t, false, false); if (l) l.text = [...l.text].slice(0, -1).join(""); }
    else if (ch === LINE_SEP || ch === "\u2029" || ch === "\n" || ch === "\r") closeLine(t, false);
    else if (ch >= " ") openLine(t, false).text += ch;
  }
  if (!textOpen || id !== selectedId) { t.unread = true; showToast(e); }
  if (id === selectedId) renderText();
  render();
}

// With the text panel closed (or for another call), what they type shows in a toast for 6 s after
// the last letter. Reply opens that call's text.
function showToast(e) {
  const l = openLine(e.text, false, false) ?? e.text.lines.findLast((x) => !x.me);
  if (!l?.text) return;
  toastCallId = e.id;
  $("rttToastWho").textContent = nameOf(e) || "They";
  $("rttToastText").textContent = l.text;
  $("rttToast").hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(hideToast, 6000);
}
export function hideToast() {
  clearTimeout(toastTimer);
  $("rttToast").hidden = true;
}

// Sends what's in the box, with a line separator to finish it. Nothing goes until then.
function sendLine() {
  const e = sel();
  const line = $("rttInput").value;
  if (!e || !line.trim() || state.text !== "on") return;
  post({ type: "text", id: e.id, text: (e.text.bomSent ? "" : "\uFEFF") + line + LINE_SEP });
  e.text.bomSent = true;
  e.text.lines.push({ me: true, text: line, open: false });
  $("rttInput").value = "";
  $("rttSend").disabled = true;
  renderText();
}

// Run by app.js at this point in its start-up, in the order it always ran.
export function initRtt() {
  $("rttToastIcon").innerHTML = ICON.text;
  $("rttReply").onclick = () => {
    if (toastCallId && entries.has(toastCallId)) selectCall(toastCallId);
    setView("focus");   // the text panel is in Focus view only
    setTextOpen(true);
  };
  $("rttSend").innerHTML = ICON.send;
  $("rttSend").onclick = () => { sendLine(); $("rttInput").focus(); };
  $("rttInput").addEventListener("input", () => { $("rttSend").disabled = !$("rttInput").value.trim(); });
  $("rttInput").addEventListener("keydown", (e) => {
    if (e.key === "Enter") { e.preventDefault(); sendLine(); }
  });
  $("rttAdd").onclick = () => post({ type: "add-text", id: selectedId });
  $("rttClose").innerHTML = ICON.close;
  $("rttClose").onclick = () => setTextOpen(false);
  $("textBtn").onclick = () => setTextOpen(!textOpen);
  // Esc closes the text panel.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Escape" || !textOpen || openMenu) return;
    e.preventDefault();
    setTextOpen(false);
  });
  document.addEventListener("keydown", (e) => {
    if (e.key.toLowerCase() !== "t" || e.ctrlKey || e.altKey || e.metaKey || openMenu) return;
    if (e.target.closest?.("input, textarea, select, [contenteditable]")) return;
    if ($("textBtn").disabled) return;
    e.preventDefault();
    // From the grid: Focus on the selected call, with the text panel open.
    if (inGrid()) { setView("focus"); setTextOpen(true); return; }
    setTextOpen(!textOpen);
  });
}

// The text panel is in Focus view only: going to the grid closes it (render does, so nothing more).
export function closeTextForGrid() { textOpen = false; }
export function setTextOpen(open, focus = true) {
  textOpen = open;
  if (open) { const e = sel(); if (e) e.text.unread = false; hideToast(); renderText(); }
  render();
  if (open && focus && !$("rttEntry").hidden) $("rttInput").focus();
  else if (!open && focus && document.activeElement && $("rtt").contains(document.activeElement)) $("textBtn").focus();
}

export function renderText() {
  const el = $("rttLog");
  const atEnd = el.scrollHeight - el.scrollTop - el.clientHeight < 24;
  const esc = (t) => t.replace(/[&<>]/g, (ch) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[ch]));
  const name = shortName(state.remote) || "Them";
  const lines = sel()?.text.lines ?? [];
  let who = null;
  el.innerHTML = !lines.length ? `<p class="rtt-empty">Nothing typed yet.</p>`
    : lines.map((l) => {
      const head = l.me !== who ? `<div class="rtt-who ${l.me ? "me" : ""}">${l.me ? "You" : esc(name)}</div>` : "";
      who = l.me;
      return `${head}<div class="rtt-msg ${l.me ? "me" : "them"}${l.open ? " live" : ""}">${esc(l.text)}${l.open ? `<span class="rtt-caret" aria-hidden="true"></span>` : ""}</div>`;
    }).join("");
  if (atEnd) el.scrollTop = el.scrollHeight;
}

// The panel and the Text button, from the selected call's state (render calls this).
export function renderTextControls(phase) {
  const live = phase === "connected";
  const e = sel();
  const unread = live && !!e?.text.unread;
  const btn = $("textBtn");
  btn.disabled = !live;
  btn.setAttribute("aria-pressed", String(live && textOpen));
  const typing = !!(e && openLine(e.text, false, false));
  const dot = unread ? `<span class="btn-dot" aria-hidden="true"></span>` : "";
  const html = `${ICON.text}${dot}`;
  if (btn.innerHTML !== html) btn.innerHTML = html;
  btn.title = !live ? "Real-time text, during a call"
    : unread ? `Text: ${typing ? "they're typing" : "new message"} (T)` : textOpen ? "Close text (T)" : "Text (T)";
  btn.setAttribute("aria-label", unread ? "Text, new message" : "Text");
  $("rtt").hidden = !(live && textOpen);
  if (!liveEntries().some((x) => x.h.call === "connected")) hideToast();
  // The compose area follows the text state: on, adding, refused, off or disabled.
  const t = state.text ?? "off";
  const name = shortName(state.remote) || "They";
  $("rttEntry").hidden = t !== "on";
  $("rttAdd").hidden = t !== "off";
  $("rttHint").textContent = t === "on" ? "Enter or Send sends the message."
    : t === "adding" ? `Asking ${name}'s phone to add text…`
    : t === "refused" ? `${name}'s phone doesn't support real-time text.`
    : t === "disabled" ? "Real-time text is turned off (--no-text)."
    : t === "unavailable" ? "Text isn't available on calls with browser phones yet."
    : "Text isn't set up on this call.";
  // --say (testing): once text is up on a call, type it into the box, then send it.
  if (testSay && t === "on" && e && !e.text.said) {
    e.text.said = true;
    setTimeout(() => { if (selectedId !== e.id) return; $("rttInput").value = testSay; sendLine(); }, 1500);
  }
}
