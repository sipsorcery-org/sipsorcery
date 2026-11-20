// Transfer: to another of your calls (attended), or to an address (blind).

import { addressField, fieldText, fieldUri } from "./address.js";
import { entries, holdState, hostState, inGrid, listed, nameOf, render, sel, selectedId } from "./app.js";
import { avatarAttr } from "./avatar.js";
import { contactFor, sameUri } from "./contacts.js";
import { openMenu } from "./menus.js";
import { $, ICON, escHtml, initial, log, post } from "./util.js";
import { syncCamera } from "./video.js";

// ---- Transfer ----
// As in the prototype: "Attended to another active call, or blind to an address". The Transfer
// button (Focus view, a connected call, a host that can: not the web softphone's) opens a
// popover above it: your other connected calls first (Join: they're asked to call that person,
// replacing your call with them, and both your calls end), then an address field that is the
// address bar's (addressField, below: the same contacts and recent calls, keys, mark and ×).
// Enter or a click puts a row in the field; Transfer, or Enter again, sends the call there. A
// bare name or number goes to the call's account's server (the host resolves it). A note on
// the stage says how it's going, from the host's transfer state (see MainForm). Blind, the call
// isn't held (they may be deciding): failed, you're still talking. Attended, both calls go on
// hold as it starts: failed, Resume takes it back (or Transfer again). A transfer done to you (they send you elsewhere, or someone takes over from them)
// shows in the same note, for a few seconds.
export let xferOpen = false;
let xferFor = null;        // the call it was opened on: it closes if another is selected
export const xferBusy = (e) => ["asking", "calling", "offered"].includes(e?.h.transfer?.status);
// Why the selected call can't be transferred now, or null if it can.
function transferBlocked() {
  const e = sel();
  if (!e || e.h.call !== "connected") return "Transfer, during a call";
  if (inGrid()) return "Transfer: pick the call to open it in Focus";
  if (xferBusy(e)) return "A transfer is under way on this call";
  return null;
}
function setTransferOpen(open) {
  if (open && transferBlocked()) return;
  if (open && !xferOpen) {
    $("xferAddr").value = "";
    xferField.pick = null;
  }
  xferOpen = open;
  xferFor = open ? selectedId : null;
  render();
  if (open) {
    xferField.refresh();
    ($("xferCalls").querySelector("button") ?? $("xferAddr")).focus();
  } else if (document.activeElement && $("xferPop").contains(document.activeElement)) $("transferBtn").focus();
}
// Your other calls it can be joined with: connected, and not being transferred themselves.
const xferCandidates = () => listed().filter((x) => x.id !== selectedId && x.h.call === "connected" && !xferBusy(x));
export function renderTransfer() {
  const btn = $("transferBtn");
  const can = !!hostState.canTransfer;
  btn.hidden = !can || inGrid();
  const blocked = can ? transferBlocked() : "Transfer";
  if (xferOpen && (blocked || selectedId !== xferFor)) {
    xferOpen = false;
    if (document.activeElement && $("xferPop").contains(document.activeElement)) btn.focus();
  }
  btn.disabled = !!blocked;
  btn.setAttribute("aria-pressed", String(xferOpen));
  btn.setAttribute("aria-expanded", String(xferOpen));
  if (!btn.innerHTML) btn.innerHTML = ICON.transfer;
  btn.title = blocked ?? (xferOpen ? "Close transfer (Esc)" : "Transfer this call: to another of your calls, or an address");
  $("xferPop").hidden = !xferOpen;
  if (!xferOpen) return;
  const e = sel();
  $("xferTitle").textContent = `Transfer ${nameOf(e)}`;
  // Rebuilt only when it changes: render runs every second while a call's held.
  const others = xferCandidates();
  const html = others.length ? `<div class="xfer-label">To another of your calls</div>${others.map((o) => `
    <button type="button" class="xfer-call" data-call="${o.id}" title="${escHtml(`${nameOf(e)} will be put through to ${nameOf(o)}, and you'll leave both calls`)}">
      <span class="suggest-avatar" aria-hidden="true"${avatarAttr(o.h.remote)}>${escHtml(initial(nameOf(o)))}</span>
      <span class="suggest-main"><span class="suggest-name">${escHtml(nameOf(o))}</span><span class="suggest-uri">${escHtml(o.h.remote)}</span></span>
      <span class="xfer-call-go">Join</span></button>`).join("")}` : "";
  if ($("xferCalls").dataset.html !== html) { $("xferCalls").dataset.html = html; $("xferCalls").innerHTML = html; }
  $("xferAddrLabel").textContent = others.length ? "Or to an address" : "To an address";
  renderXferEntry();
}
// The address: the address bar's field (addressField), its list always showing, without the
// person being transferred and without the Contacts… and Add to contacts rows.
export let xferField;
// The Transfer button: enabled with something typed or highlighted.
function renderXferEntry() {
  $("xferGo").disabled = !$("xferAddr").value.trim() && !xferField.items[xferField.active];
}
// Starts a transfer of the selected call: { target } another call's id, or { address, label }.
// Attended, the host holds both calls as it sends the REFER; here they count as held at once, so
// the audio moves off them now. Blind, the call isn't held: they may be deciding, and can talk
// to you meanwhile (Phone.cs).
function startTransfer(how) {
  const e = sel();
  if (!e || transferBlocked()) return;
  for (const x of how.target ? [e, entries.get(how.target)] : []) {
    if (!x || holdState(x)) continue;
    x.holdWanted = true;
    x.holdAskedAt = Date.now();
  }
  log(how.target ? `call ${e.id}: transfer to call ${how.target} (${nameOf(entries.get(how.target))}), attended`
    : `call ${e.id}: transfer to ${how.address}, blind`);
  post({ type: "transfer", id: e.id, ...how });
  setTransferOpen(false);
  syncCamera();   // the On hold card
}
// it: the row picked; without one, what's typed.
function transferToAddress(it = null) {
  const typed = $("xferAddr").value.trim();
  // A row picked into the field (and not edited since) goes with its name.
  if (!it && xferField.pick && (sameUri(xferField.pick.uri, typed) || fieldText(xferField.pick.uri) === typed)) it = xferField.pick;
  if (it) startTransfer({ address: it.uri, label: it.name });
  else if (typed) startTransfer({ address: typed, label: contactFor(fieldUri(typed))?.name ?? null });
}
// A transfer, in a few words, for the call header's one line and the call's row: what's
// happening now. Who it's with is already in the header (whose call it is), so it names the other
// party. The host's sentence in full (t.detail) goes in the tooltip. Failed: why, from t.reason.
export function transferShort(t) {
  const busy = t.status === "asking" || t.status === "calling";
  if (t.role === "out") {
    if (busy) return t.rang ? `${t.target} is ringing…` : t.attended ? `Joining with ${t.target}…` : `Transferring to ${t.target}…`;
    if (t.status === "done") return t.attended ? `Joined with ${t.target}` : `Transferred to ${t.target}`;
    if (t.status === "cancelled") return "Transfer cancelled";
    return t.reason ? `Transfer failed: ${t.reason}` : `Transfer to ${t.target} failed`;
  }
  if (t.role === "in") {
    if (t.status === "offered") return `${t.by} wants to transfer you to ${t.target}`;
    if (t.status === "declined") return t.reason === "you declined it" ? `Transfer declined · still with ${t.by}`
      : t.reason === "you didn't answer it" ? `Transfer not answered · still with ${t.by}` : "Transfer declined";
    if (busy) return t.transferorGone ? `${t.by} hung up · calling ${t.target}…`
      : `Transferring you to ${t.target}${t.rang ? " · ringing" : ""}…`;
    if (t.status === "done") return `Transferred by ${t.by}`;
    return t.reason === "you cancelled it" ? `Transfer cancelled · still with ${t.by}`
      : `Transfer to ${t.target} failed${t.reason ? `: ${t.reason}` : ""}`;
  }
  // "replace": someone taking over the call from the person you were talking to.
  return busy ? `${t.by} is handing you over…` : `Handed over by ${t.by}`;
}
// A transfer, where it shows. Under way (asking, calling): the state card in the middle of the
// stage, on its call (transferState): ours with × to close it, theirs with Cancel. Theirs, offered (they've asked to
// transfer you): a decision in the notices, Decline and Accept; no answer in 20 s declines it
// (the host). Over: an outcome in the notices with × (done, cancelled, declined: the host lets
// it go by itself after a few seconds); ours failed, until you close it or Resume (attended, the
// call stays on hold, and the state card says so). No Try again: Transfer is on the bar. open(id) opens that call (the name's click).
export function transferNotices(open) {
  const decisions = [], outcomes = [];
  for (const e of [...entries.values()].sort((a, b) => Number(a.id) - Number(b.id))) {
    const t = e.h.call === "connected" ? e.h.transfer : null;
    if (!t || t.status === "asking" || t.status === "calling") continue;
    const id = e.id, name = nameOf(e);
    if (t.canAnswer) {
      const answer = (accept) => { log(`call ${id}: transfer ${accept ? "accepted" : "declined"}`); post({ type: "transfer-answer", id, accept }); };
      decisions.push({
        key: `xfer-${id}`, icon: ICON.transfer, who: t.by || name, whoTitle: `${e.h.remote}: open this call`, onWho: open(id),
        what: `wants to transfer you to ${t.target}`, title: t.detail || "", tone: "warn",
        actions: [
          { label: "Decline", title: `Stay with ${t.by}`, cls: "btn", fn: () => answer(false) },
          { label: "Accept", title: `Go to ${t.target}: ${t.by} hangs up once ${t.target} answers`, cls: "btn btn-primary", fn: () => answer(true) },
        ],
        bulk: () => answer(false),
        // Where it's asked: the call on the stage (Focus), in its call bar
        // (app.js renderHeadEnded); a call in the background, or any in Grid, by its row and
        // tile blinking (calllist.js, app.js renderGrid): you switch to it to answer.
        stage: !inGrid() && id === selectedId,
        row: inGrid() || id !== selectedId ? id : null,
      });
      continue;
    }
    if (t.status === "offered") continue;
    // Attended, ours: both calls carry the transfer's outcome (the host), but it's one transfer, so
    // one notice, on the call the REFER went on (t.referred); × on it clears both. The other call
    // says it only if that one's gone.
    if (t.role === "out" && t.attended && !t.referred && entries.get(t.partner)?.h.transfer?.referred) continue;
    // Theirs, and you declined it: nothing to say (you know, and the call goes on as it was).
    if (t.role === "in" && t.status === "declined" && t.reason === "you declined it") continue;
    const dismiss = () => post({ type: "transfer-dismiss", id });
    const failed = t.status === "failed";
    outcomes.push({
      key: `xfer-${id}-${t.status}`, icon: ICON.transfer, who: name, whoTitle: `${e.h.remote}: open this call`, onWho: open(id),
      what: transferShort(t), title: t.detail || "", tone: failed ? "bad" : "info",
      actions: [],
      dismiss, bulk: dismiss,
      // A call in the background, or any in Grid, with the calls panel showing: on
      // its row there (calllist.js), not in the notices. The call on the stage: the notices.
      row: inGrid() || id !== selectedId ? id : null,
      // The call on the stage (Focus): in its bar (app.js renderHeadEnded).
      stage: !inGrid() && id === selectedId,
    });
  }
  return { decisions, outcomes };
}
// The selected call's transfer while it's under way, for the state card: { text, title, cancel
// (Cancel's tooltip, if it can), close (×'s tooltip), key (which transfer, for × to remember) },
// or null. Ours has no Cancel: SIP can't call back a REFER once it's sent (RFC 3515), so a
// Cancel here only forgot it while their phone carried on. × closes the card instead, and the
// outcome still comes as a notice. Theirs keeps Cancel: that one is real (a CANCEL of the call
// to the target).
export function transferState(e) {
  const t = e?.h.call === "connected" ? e.h.transfer : null;
  if (!t || !(t.status === "asking" || t.status === "calling")) return null;
  const ours = t.role === "out";
  return {
    text: transferShort(t), title: t.detail || "",
    cancel: t.canCancel && !ours ? `Stay with ${t.by}: cancels the call to ${t.target}` : null,
    close: ours ? `Close: the transfer to ${t.target} carries on, and how it ends will show here` : null,
    key: `${t.role}|${t.target}|${t.attended ? "a" : "b"}`,
  };
}
export function cancelTransfer(id) { log(`call ${id}: transfer cancelled`); post({ type: "transfer-cancel", id }); }

// Run by app.js at this point in its start-up, in the order it always ran.
export function initTransfer() {
  // The address: the address bar's field (addressField), its list always showing, without the
  // person being transferred and without the Contacts… and Add to contacts rows.
  xferField = addressField({
    input: $("xferAddr"), list: $("xferSug"), clear: $("xferClear"), badge: $("xferContact"),
    idPrefix: "xsug", popup: false, foot: false, goIcon: () => ICON.transfer, goLabel: "Transfer here",
    skip: () => sel()?.h.remote ?? null,
    pick: () => renderXferEntry(),
    changed: () => renderXferEntry(),
  });
  $("transferBtn").onclick = () => setTransferOpen(!xferOpen);
  $("xferClose").innerHTML = ICON.close;
  $("xferClose").onclick = () => setTransferOpen(false);
  $("xferCalls").onclick = (ev) => { const b = ev.target.closest("[data-call]"); if (b) startTransfer({ target: b.dataset.call }); };
  $("xferPop").onsubmit = (ev) => {
    ev.preventDefault();
    const it = xferField.items[xferField.active];
    transferToAddress(it?.action ? null : it);
  };
  // Esc closes it (before the other shortcuts), and so does a click away from it.
  document.addEventListener("keydown", (ev) => {
    if (!xferOpen || ev.key !== "Escape" || openMenu) return;
    ev.preventDefault();
    ev.stopImmediatePropagation();
    setTransferOpen(false);
  }, true);
  document.addEventListener("click", (ev) => {
    if (xferOpen && !$("xferPop").contains(ev.target) && !$("transferBtn").contains(ev.target) && !$("moreMenu").contains(ev.target)) setTransferOpen(false);
  }, true);
}
