// Do not disturb, the button beside Privacy mode in the top bar: while it's on, the host answers
// every new incoming call 486 Busy Here, so nothing rings. Calls already ringing or in progress
// carry on. It's remembered (settings.js prefs), like the theme, and sent to the host once the
// page loads and with every change:
//   dnd {on}
// A call it turns away is in Recent as an incoming call noted "Do not disturb", not a missed one:
// you chose not to be disturbed, so it doesn't badge People.
import { prefs, savePref } from "./settings.js";
import { $, ICON, post } from "./util.js";

export const dndOn = () => prefs.dnd === true;

function render() {
  const btn = $("dndBtn"), on = dndOn();
  btn.classList.toggle("on", on);
  btn.setAttribute("aria-pressed", String(on));
  btn.title = on
    ? "Do not disturb is on: new calls are turned away (486 Busy Here) and show in Recent. Click to turn it off"
    : "Do not disturb: turn away new calls (486 Busy Here) until you turn it off. Calls you're on carry on";
}

export function setDnd(on) {
  savePref("dnd", !!on);
  post({ type: "dnd", on: !!on });
  render();
}

export function initDnd() {
  const btn = $("dndBtn");
  btn.innerHTML = `${ICON.dnd}<span class="privacy-text">Do not disturb</span>`;
  btn.onclick = () => setDnd(!dndOn());
  post({ type: "dnd", on: dndOn() });
  render();
}
