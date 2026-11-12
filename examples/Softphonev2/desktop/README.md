# Softphone v2 – desktop prototype

A minimal Windows softphone that makes and takes **SIP video calls, several at once**. It is the first build of
the desktop app, using the media design the video spike
([../spikes/VideoPipeline](../spikes/VideoPipeline/RESULTS.md)) picked: the **relay**.

- **C# (this project)** does SIP with SIPSorcery: the transport, a registration for each SIP
  account (see [SIP accounts](#sip-accounts)), and up to 8 calls at once (dial, answer, decline,
  cancel, hang up, hold; see [Several calls](#several-calls)).
- **The page** (`wwwroot`, shown in WebView2) does all the media: it captures the camera and
  microphone, does echo cancellation, encodes, decodes (in hardware where it can) and renders.
- **`MediaRelay`** joins the two, one per call. Once a call connects, it creates a local `RTCPeerConnection` to
  the page that offers the codecs the SIP call negotiated, and passes the media between the two
  without decoding it:
  - audio packet by packet, keeping the timestamps and sequence numbers;
  - video frame by frame;
  - keyframe requests (PLI/FIR) in both directions, plus one request each way when the relay
    comes up, so video starts at once.

## The control bar

Under the stage, from the UX prototype (`prototype/web`), and there the whole time, not just
during a call, so the microphone and camera can be set up first:

- **Mic ▾**: Mute or Unmute (the label says what pressing it does, as on the camera button;
  soft red while muted, with a red *You're muted* pill by the call timer). Before a call too:
  the call then starts muted, and the tooltip says so. The ▾ lists microphones and
  speakers; one picked between calls is used from the next call.
- **Camera ▾**: on a video call, Stop video or Start video (what pressing it does, whatever
  the source: the camera, or in diagnostics the test pattern; soft red while stopped, when
  the placeholder is sent); on an audio call, Add video. Before a call, whether a video call
  starts with your camera on, with the preview following. On Audio it isn't disabled:
  pressing it switches to Video with the camera on, since that's what pressing a camera
  means. Disabled only when video can't be had (No camera, No video, said on the button)
  and while a call is ringing (the Answer button has its own choice). The ▾ lists cameras
  and, in diagnostics mode, the test pattern.
- **Audio ◯━ Video**: a slide switch, with the chosen word bold beside the thumb. The track is
  neutral, so the green is only on Call.
- **Text** (an icon, at the right beside Self view, since it's rarely used): opens the
  real-time text panel in the dock (T does too). During a call only.
- **Keypad** (an icon, beside Text): opens the DTMF keypad for the selected call (see
  [Keypad](#keypad-dtmf)). During a call only, in Focus view.
- **Hold** (H): puts the selected call on hold, or Resume takes it off (see
  [Several calls](#several-calls)). During a call only.
- **End**: Cancel while calling, End once connected.

A device you pick takes effect at once, mid-call too, by swapping the track on the page's
connection (no renegotiation), and is remembered between runs (per profile). A remembered
device that's gone falls back to the default.

## Recent calls and contacts

The address field's suggestions, from the UX prototype (its *New call*): focusing the field (or
`/` anywhere outside a text field) lists contacts and recent calls, and typing filters and ranks
them (below). Transfer's *To an address* field is the same component (`addressField` in
`app.js`: the same lists, rows, keys, mark and ×; see Transfer). Typing part of a name highlights the first match. ↑/↓ move through the list (and open it),
and `Esc` closes it. **Picking never dials**: Enter or a click puts the highlighted entry's
address in the field (the list closes, focus stays), and only **Call**, or Enter again with
nothing highlighted, places the call, from the picked entry's account as below. A full address
or number typed highlights nothing, so Enter calls what you typed. Calls that didn't
connect show their outcome (*Cancelled*, *Busy*, with the SIP response on hover), and missed
calls are red.

- **Contacts** come first, in their own group, so they're easy to find among calls to anyone
  else: the header in the accent colour, and bigger rows, with a larger avatar (their Gravatar
  picture, if they have one), the name in bold and the address under it, and the saved-contact
  mark. With nothing typed, up to 5: the ones you call most (how often and how lately, as
  below), then by name. The host keeps them per profile in
  `%LOCALAPPDATA%\SIPSorcery\Softphonev2\contacts-{profile}.json` (name and address) and
  sends them to the page (`contacts`); the page sends `contact-save` and `contact-remove`.
  The web softphone's host keeps none, so there they're in the browser's localStorage.
- **Recent**: then the call history (see `history.js`), one entry per address, newest first, up
  to 6, less the contacts already listed. A recent call to a contact further down shows the
  contact's name. The list refreshes while it's open when a call ends.
- The person button on the highlighted (or hovered) entry adds a recent call to contacts, with
  the name it shows, or removes a contact.
- **Typing** filters both groups: up to 5 contacts that match, under *Contacts*, then up to 4
  other addresses you've called that match, under *Recent*. In each, a match at the start of a
  word of the name, or of the address's user part (`han` for *Hannah Desk* or
  `sip:hannah.lab@…`), beats one inside (`ann` in *Hannah*), then come the addresses you call
  most and most lately (each call counts 1, half that after 14 days, a third after 28…). A
  contact that matches is always above the recent calls, even when it only matches inside a
  word. A contact with calls shows once, as the contact, with its last call's outcome and when.
- **Calling from the right account**: a contact can have its own *Call from* account. Calling
  it, from this list, the Contacts tab, or by typing or pasting its exact address, first
  switches the dial bar's *Call from* to that account (the picker flashes), so it shows what the
  call uses. A recent call picked here goes from the account that call used. If the account has
  been removed or disabled, the call goes from the current *Call from* account and a note under
  the picker says so for 5 s (*G Old is disabled: calling from G Work*). A contact's row here
  shows its account after the address (*· from G Home*).
- **Saved contacts are marked**: one small icon, a person with a tick in the accent colour,
  tooltip *Saved contact: Name* (`contactmark.js`). It's after the name of a contact in this
  list, in the field itself (a pill with the contact's name at the field's right end, when what's
  typed, pasted or picked is a contact's address), on Recent rows and in their details
  (*Contact: Saved contact: Name*), on Active calls rows, grid tiles, and
  the call's header (the card while calling or ringing; once connected, the info overlay on the
  stage, with the contact's name in light green, and a *Contact* row in its details). Calls to or
  from other addresses show nothing extra. Addresses match as the list's dedupe does: the URI out
  of `Name <sip:…>`, without parameters, case aside and `sip:` optional.
- **Gravatar pictures** replace the initials (`avatar.js`): in this list, People's Recent and
  Contacts rows, Active calls rows, grid tiles and the stage.
  `sip:alice@example.com` is looked up as `alice@example.com` (SHA-256, at
  `gravatar.com/avatar/<hash>?d=identicon`), once per run: their picture, or without one
  Gravatar's identicon for that address (the same pattern each time); the initial stays only if
  gravatar.com can't be reached. Addresses
  with no user part, an IP address or dotless host (`localhost`) and `tel:` numbers aren't
  looked up. Settings > Appearance > *Show Gravatar pictures for people* (on by default) turns it
  off, since it sends gravatar.com a hash of each address shown.
- **Clear (×)**: a button at the field's far right while it has text (the contact pill sits just
  left of it) empties the field and keeps focus there, so the list shows Contacts and Recent as
  if you'd deleted the text. `Esc` still closes the list first; a second `Esc`, with the list
  closed, empties the field too.
- **Cleared on answer**: once a call is answered (you answer theirs, or they answer yours), the
  field is emptied and its list closed, ready for the next call. Not if you're in the field
  typing something else (another address than the call's), which is left as it is.
- The last row opens the **Contacts** tab of the People view (below): *Contacts…* with nothing
  typed, or *Add … to contacts…* for a typed address that isn't one yet, which opens it on the
  form with the address filled in. A name that matches nothing still closes the list, as in the
  prototype.

## People: Recent and Contacts

The **People** button (two heads) at the start of the dial bar is the one way into the call
history and contacts; to call someone, the address field's suggestions are quicker. It opens a
view over the stage on the tab you last had (**Recent** at first), and closes it again. It
carries the missed-call badge, and while the badge shows it opens straight on **Recent**, which
clears it. It's shown pressed while the view is open. ←/→ move between the tabs. The button shows once the host sends call
history; *Manage accounts* beside the account picker now has a server icon.

- **Recent** is the call history. Each row has a person button beside the chevron, and its
  details have a matching button: *Add to contacts* or, for a contact, *Edit contact*. Both open
  the Contacts tab's form (address filled in), and saving or cancelling comes back to Recent.
  History shows the contact's name in place of the one the call carried.
- **Contacts** (`contacts.js`) is laid out like Recent: search, *Add contact*, and a row per
  contact with its last call and when, a chevron and a call button. A row's details have the
  address, its last 5 calls, **Call**, **Edit**, *Show all N calls in Recent* (Recent, searched
  for that address) and **Delete**, which asks again on the button for 3 s. Add and Edit share a
  form at the top (name, number or SIP address, and *Call from*: *Any (the current Call from
  account)* or one of the accounts, a disabled one marked so); a blank address, or one another
  contact has, is refused there. The details show *Call from* too, and say when its account has
  been removed or disabled. *Add to contacts* from a call on Recent fills in the account that
  call used. The account is kept in `contacts-{profile}.json` as `account` (the account's id);
  files from before it have none, which means *Any*. The web softphone has no accounts, so it
  shows no *Call from* there. Changing a contact's address replaces it. `Esc` closes the form, then clears the
  search, then closes the view.
- **Calling from here** (Call, Call back, or the dial bar with People or Accounts open) leaves
  the view open while the call is placed, with the call's row (or the "Calling… · Cancel" banner,
  over another call) showing how it's going. Answered, the view closes (an unsaved edit is
  dropped) and the call is selected; declined or failed, you stay where you were.
- Without a host that keeps history there's no view, so no Contacts tab either; contacts still
  come up in the address field's list.

## Settings

The gear at the right of the top bar opens Settings (`wwwroot/settings.js`), a dialog as in the
UX prototype. Esc, its ×, or a click outside closes it. Only options with something behind them
in this app are there; each applies straight away:

- **Audio & video.** Microphone, speaker and camera: the same choice as the ▾ menus on Mute and
  Camera, so either changes the other. *System default* follows Windows. A microphone or
  speaker picked mid-call swaps in at once. A camera picked here never turns the camera on: if
  it's open it switches, otherwise the next video call uses it. Device names show once the
  microphone or camera has been used.
  *Start video calls with my camera on* is the Camera button's choice before a call, now
  remembered between runs (the button changes it too). `--camera-off` still starts with it off,
  without changing what's kept. *Show my self view* is the Self view button (V).
- **Calls.** *Answer calls automatically*: after a second, always with the camera off, since
  the camera is never opened without you asking. The microphone is on as soon as it answers.
  (`--auto-answer` is the testing switch, answered by the host with the Camera button's choice.)
  *Show all calls in a grid*: the view with two or more calls (see
  [Focus and grid views](#focus-and-grid-views)); the Grid button under Active calls and G change it too.
- **Appearance.** Theme: System, Light or Dark. System follows Windows' light or dark mode.
- **Account.** Who you are: the account, or with none, the address others can call, and a
  *Manage accounts* button that opens the same view as the dial bar's accounts (server) button (see
  [SIP accounts](#sip-accounts)).
- **Diagnostics.** Opens or closes the panel F12 does.

The page keeps them in its `localStorage`, which is in the WebView2 profile's folder, so each
`--profile` has its own. The prototype's ringtone (there's no ringtone yet) and its *Save text
conversations in call history* (the history doesn't keep transcripts yet) are left out until
there's something behind them.

## Several calls

As in the prototype: up to 8 calls at once, each with its own SIP user agent, media session and
relay on the host (`Phone.Call`), and its own peer connection, `<video>`, quality, details and
text on the page. Messages about a call carry its `id`; the host's `state` lists every call.

- **One live call at a time.** Like a desk phone: the call you're talking to has the microphone
  and speaker, and every other call you're on is on hold. Picking a held call (its row, its tile,
  its number key, or *Resume*) resumes it and holds the one you were on. Looking at a call
  without picking it (a ringing call, an ended one's *Back to …*, History, Accounts) holds
  nothing, and when a call ends nothing resumes on its own: a held call stays held, with Resume.
- **Hold** is the account's *Hold* setting (see [SIP accounts](#sip-accounts)), fixed when the
  call starts. A held call is never silent on the wire: media keeps flowing, so neither side's
  NAT binding times out and resuming doesn't depend on one being reopened.
  - **Music on hold** (the default): nothing is signalled. The page stops playing their audio
    and sends them music instead of the microphone, and on a video call an "On hold" card
    instead of the camera, a few frames a second. They keep sending, and switching calls is
    instant. The music is a few gentle chords rendered once with an `OfflineAudioContext` and
    looped, so it doesn't stutter when timers are throttled in a minimised window.
  - **SIP re-INVITE**, for a PBX that needs to see the hold (to play its own music, or park the
    call): a re-INVITE with our media sendonly (`SIPUserAgent.PutOnHold`), and the page sends
    silence. "Holding…" or "Resuming…" shows while it's out. The host sends a call's re-INVITEs
    one at a time (`Phone.Hold`): a change asked for while one is out waits for its answer (or
    4 s), and only the last counts, so switching quickly doesn't get a 491 Request Pending.

  *Hold* in the control bar, or `H`, holds the selected call. The stage shows "On hold · 02:10"
  with *Resume*, amber after a minute, as does the list. A call that isn't the live one gets
  music or silence, never the microphone and never nothing.
- **Held by them.** When they put us on hold with a re-INVITE, the call says so ("Alice put you
  on hold"), and we send silence rather than your microphone, even past our recvonly answer:
  the RTP session won't send then, so `MediaRelay.SendPastTheirHold` puts the page's packets
  straight onto the RTP socket (not on an encrypted browser-phone leg). Our NAT binding stays
  open and they get a stream to latch onto when they resume. On hold either way round, the
  "No audio arriving" and "isn't getting your audio" notes stay quiet: the hold note says it.
- **New call.** Typing an address changes nothing. On a call, the button says what it will do:
  *Call & hold Alice*; pressing it holds that call, which stays on the stage, and the new one
  isn't a call in the list yet: a banner over the stage says "Dave · Calling… · Cancel". Only
  once Dave answers does it join the list and take the stage (in Focus). Not answered (busy,
  declined, no such number), the banner says why until the call goes (or *Close*), and the call
  you were on stays on hold: you may want to call someone else, so resuming it is up to you.
  *Cancel* just takes the banner away. *End all*
  leaves a call you're placing to the banner. The dial bar is only disabled at 8 calls.
- **Incoming calls** ring alongside the others (only a ninth gets 486 Busy Here). While you're
  on a call, a banner over the stage says who, with *Answer & hold current* and *Decline*, and
  the call you're on carries on untouched (not held, still on the stage) unless you answer; the
  name opens the full answer view. With several ringing,
  the one waiting longest is on the banner, with "+N more waiting". `A` answers it.
  `--auto-answer` answers each call alongside the others, keeping the audio where it is.
- **When a call ends** it stays in the list for a few seconds with how it ended. If it was on
  the stage, the stage keeps saying so, with *Back to …* (or `Esc`) to another call.
- **Mute** is the selected call's; `M` too. `Shift+M` mutes every call, and the ones to come
  (before a call, Mute is the same thing: calls start muted). `Shift+C` (or *Cameras off*, below) turns
  the camera off on every call and for new ones; with every camera already off, it turns them all
  back on.
- **Grid, Mute all, Cameras off and End all**, under the Active calls list once there are two calls (icons only on
  the rail). *Grid* has its own row above the others: see
  [Focus and grid views](#focus-and-grid-views). *Mute all* is `Shift+M`, and reads *Unmute all* once every call is muted.
  *Cameras off*, beside it while any of the calls has video, is its equivalent for the camera
  (`Shift+C`): every video call sends your placeholder card (the camera-off picture, your
  initial) instead of the camera, which closes, and new calls start with the camera off. It reads
  *Cameras on* (pressed, in red) once every video call's camera is off, and then turns them all
  back on. With it there, *End all* has its own row under the two. *End
  all* asks once more ("End 3?", for 4 s) and then hangs up every call you've placed or are
  on; a call that's only ringing is left to Answer or Decline. On the collapsed rail, where it's
  only an icon, the first click opens the panel instead, so what you confirm is the full button
  ("End all", then "End 3?"): ending every call always takes the full button's two clicks.
- **Keys**: `1`–`8` put that call in the list on the stage (`1` is the top, the oldest), `A` answers,
  `M` mutes, `Shift+M` mutes all, `Shift+C` turns all cameras off (or back on), `H` holds or resumes, `G`
  switches between Focus and Grid.

## Focus and grid views

As in the prototype. **Focus** (the default) shows only the selected call, with its controls
under its info bar. **Grid** (the *Grid* button under Active calls, or `G`) shows every active call
as an equal tile, oldest first like the list, with no buttons: each tile shows what Focus
would, so you can tell the calls apart at a glance: their video while it's arriving (on hold
too: with music on hold it keeps coming), on an audio call the two rows of bars (theirs above,
yours below only while that call gets your mic), else their initial; and on a video call your
self view in the corner, showing what that call sends (your camera, your initial, or the "On
hold" card), unless Self view is off. Under it the call's time,
their name and its status line, the same as the list's: "On hold · 02:10" (amber), "Typing…",
their last unread line, and what the call gets from you (as in the list). The selected call's tile, which has the audio,
is outlined. The control bar's buttons go, so it's never in doubt which call a button acts on.
*Grid* looks pressed while it's the view; clicking it again goes back to Focus, though there's
no Focus button: picking a call does that. Clicking a tile opens that call in Focus, resuming it if it's held (and holding the
live one). The one button on a tile is on a call you've put on hold: the same "On hold · 00:42 ▶ Resume"
note as on the stage in Focus, over the middle of the tile ("Bob put you on hold", without Resume,
when it's them). Resume takes the call off hold, holds the live one, and selects it, so it has
the audio, and the view stays Grid.

- **Picking a call** from the grid, by its tile (or Enter on it), its row in Active calls, or
  its number key, opens it in Focus.
- **A call ringing in** never switches to the grid, and isn't a tile: it asks on the banner over
  the call (or grid) you're on. *Decline* leaves everything as it was; *Answer & hold current*
  holds the call you're on and opens the new one in Focus. Its own answer view (the banner's
  name) leaves the choice alone.
- **`T`** in the grid switches to Focus with the text panel open, as does *Reply* on a text
  toast. `M`, `H` and `V` do nothing in the grid; `Shift+M` and `Shift+C` still act on every call.
- The choice is remembered (`view` in the page's settings) and only applies with two or more
  calls: with one, it's Focus. Settings has it as *Show all calls in a grid*.
- Each call's `<video>` moves into its tile and back (`renderGrid` in `app.js` keeps the tiles
  from one render to the next), so its picture and audio carry on. Two columns up to four
  calls, then three; one on a narrow stage. Past what fits, the stage scrolls.

## Active calls

A panel down the left, beside the stage and the control bar, from the prototype's sidebar. It
lists every call, oldest at the top and new ones at the bottom, while it's live (calling, ringing or connected) and for a
few seconds after it ends, and otherwise says *No active calls*. Clicking a row puts that call
on the stage. The list is built from `listed()` and `callStatus()` in `app.js`.

- **A row**: their initial, their name (the full address in the tooltip), the status and, once
  connected, the timer. Calling… (grey ring, pulsing), Incoming call / Incoming video call
  (amber, pulsing), then Audio call or Video call (green), On hold · 02:10 (amber
  after a minute), Holding… or Resuming…, Muted in red, or how it ended. A connected call also shows,
  at a glance, what it gets from you: the mic, and on a video call the camera, as a filled
  green chip while it gets it, and struck out when it doesn't, red if that's your choice
  (muted, video stopped) and faint otherwise (on hold, held by them, the mic on another call).
  The tooltip says which: "Bob hears you", "On hold: they hear music, not you". With text,
  the status line says "Typing…" or their last line.
- **Collapse**: the chevron folds it to a 48 px rail of initials, the mic and camera chips, and
  timers, so a live call still shows. The chevron, or a click anywhere in the panel that isn't on a call, folds it or
  opens it again; a click on a call on the rail selects it and opens the panel (under 700 px it
  only selects it, leaving the stage its room). The choice is
  remembered (`localStorage`, `callsPanel`, per profile on the desktop).
- **Narrow windows** (under 700 px): it's always the rail, to leave the stage its room.
  Expanding it lays the panel over the stage until you collapse it, click away or press Esc,
  without changing the remembered choice.

## Call history

From the prototype's *Call history*. The host records every call when it ends and keeps the
list; the page (`history.js`) shows it in the People view's *Recent* tab, and the address field
suggests from it. (An earlier History section under the active calls was dropped: it was a third
way to the same calls.)

- **The badge**: a red count on the People button of missed calls you haven't looked at; opening
  Recent clears it (`historySeenAt` in `localStorage`).
- **The History view** (the People view's *Recent* tab, see above), over the stage like Accounts: it isn't a
  call, so opening it holds nothing and the call carries on under it. *Back to …* (while a call
  is going) and × close it, and so does `Esc` (which first clears a search). Picking a call in
  the panel, or the ringing banner's name, closes it too, and it and Accounts take turns. It has
  search (names and addresses), *All / Missed / Outgoing / Incoming*, and day groups (Today,
  Yesterday, the weekday, then the date). A row, or its chevron, opens its details: direction,
  when it started, duration (or "Didn't connect"), the account, the outcome with the SIP
  response, and the address, with **Call back** (from the call's account, if it's still enabled;
  otherwise from *Call from*'s) and **Copy address**.
- **Removing**: only the **Remove** button in a row's details, or *Remove all N from this
  address* beside it, for a flood of spam calls. *Clear history* in the toolbar asks first; with
  a search or filter on it's *Clear N shown* and only removes those. Each removal shows an
  **Undo** toast for 10 s, and removals while it's up are undone together.
- A call ringing on the stage takes over from the view. Answered, the view closes; not
  answered, it comes back.

What a call is recorded as:

| The call | Recorded as |
|---|---|
| Placed, answered | Outgoing, with its duration |
| Placed, turned down | Outgoing with the response in a word or two (*Busy*, *Declined*, *Number not found*, *No answer*, *Not available*, …) and the SIP response, e.g. `486 Busy Here` |
| Placed, you hung up before it was answered | Outgoing, *Cancelled* |
| Incoming, answered | Incoming, with its duration |
| Incoming, you declined it | Incoming, *Declined* |
| Incoming, they gave up before you answered | Missed (red) |
| Transferred (by you, or you by them) | As above, with *Transferred to …* and its duration |

**Where it's kept**: `%LOCALAPPDATA%\SIPSorcery\Softphonev2\history-<profile>.json`, beside the
accounts, newest first, the last 500 calls (`CallHistory` in `History.cs`). The host sends the
page all of it (`history {entries, max}`) when the page is ready and whenever it changes; the
page sends `history-remove {ids}` and `history-restore {ids}` (Undo), and the host keeps what was
removed in memory, so Undo works until the app closes. The address field's Recent list comes
from the same history. The web softphone's host keeps its history the same way in the browser's
`localStorage` (`callHistory`); a host that sends no history shows no People button at all.

A direct call (no account) arrives from SIPSorcery's anonymous `sip:thisis@anonymous.invalid`,
so it's recorded with that address, and calling it back goes nowhere; a call from an account
has the account's address.

## Call details

The info bar at the top left of the stage (their address and the call's duration) has a
chevron that drops down the call's details, in diagnostics mode only (F12): they're technical,
so otherwise the info bar is just who and how long, with no chevron. It's closed at the start
of every call, and when diagnostics closes.

The small `‹` arrow at the info bar's left end tucks it away: it slides off the stage's left
edge (0.2 s, instant with reduced motion), leaving a small `›` tab there, *Show call info*, to
bring it back. Tucking it closes the details. While you're muted the tab has a red dot (the
Muted pill is tucked away with the bar), and its tooltip names the call and says you're
muted. Tucked, the bar is inert, so Tab skips it; from the keyboard the focus moves to the tab
and back to the arrow. The choice is remembered with the other settings (`infoTucked`, per
profile on the desktop) and applies to every call.

| Row | What it says | From |
|---|---|---|
| Call | Outgoing or incoming, and the account (or "no account") | the host |
| Their phone | their `User-Agent` (or `Server` on the 200 OK we got), or "Didn't say" | the host |
| Security | Encrypted or not: SRTP shows as `RTP/SAVP` on all their media lines, SIP over TLS (or WSS) as the transport. Today it's always "Not encrypted: plain RTP, SIP over UDP", in amber | the host |
| Audio, Video | The codec each way, "↑ Opus 48 kHz ↓ G722 16 kHz", or one name when both match. Each end can send a different codec, and this is the only place that shows it. G.722 is labelled 16 kHz, its sample rate, not its 8 kHz RTP clock. Video has its frame size, "VP8 1280×720", refreshed every second while the panel is open | the page's WebRTC stats, which are the SIP leg's, since the relay doesn't re-encode |
| SIP | transport and their SIP address and port, `UDP 127.0.0.1:5080` | the dialog |
| Media | where their audio goes, the RTP address from their SDP | the SIP session |
| Call-ID | the dialog's Call-ID, with **Copy**, to find the call in logs or a capture | the dialog |

The host sends these as a `details` message when the call connects, and again if video is
added.

## Real-time text

Every call offers real-time text unless `--no-text`: on plain RTP calls an `m=text` stream
(RFC 4103: T.140 inside RED, with plain T.140 as the fallback), on WebRTC calls a T.140 data
channel (RFC 8865, below). Unlike audio and video it isn't relayed to the page's connection:
the host sends and receives it (`RttEndPoint` for RTP, `TextPeerConnection` for the data
channel), and the page swaps `text` messages with the host either way, so the popover is the
same on both.

- **Yours goes when you send it**, with the Send button in the entry box or Enter: the
  whole line, then a T.140 line separator (U+2028). Nothing is sent while you type. The first
  send starts with a byte order mark, as T.140 asks.
- **Theirs may arrive letter by letter**, from a phone that sends as it's typed: their
  unfinished line shows live, dashed with a caret. U+FEFF is dropped, backspace (U+0008)
  takes back their last letter, U+2028, U+2029, CR or LF end their line, and other control
  characters are ignored.
- **In the dock**, to the right of the call (see [The dock](#the-dock-keypad-and-text)). Esc,
  its ×, the Text button or T closes it, and Esc puts the focus back on the button.
- **Text arriving while it's closed** shows as a toast at the bottom of the stage: their name,
  what they've typed (live, if their phone sends letter by letter) and *Reply*, which opens the
  panel. It fades 6 s after the last letter, and the Text button keeps a red dot until the
  panel is opened. Nothing opens by itself.
- **Not on the call** (their phone didn't answer `m=text`): "Text isn't set up on this call"
  and *Add text to this call*, which sends a re-INVITE with `m=text`. If they turn it down
  (`m=text` with port 0) it says their phone doesn't support real-time text; with no answer in
  8 s, the same. Either side adding text starts it when their SDP agrees.
- A new call starts an empty conversation.
- **WebRTC calls** (browser phones, and every call with `--webrtc`) carry text on a data
  channel, as RFC 8865 has it: `m=application … UDP/DTLS/SCTP webrtc-datachannel` with
  `a=dcmap:1 label="t140";subprotocol="t140"`. The channel is agreed in the SDP (RFC 8864), so
  both ends open it on that stream with no in-band DCEP open; an offer of ours uses stream 1,
  an answer the stream their offer gives. Each message is T.140 text as a UTF-8 string, sent
  as it would be on RTP but with no RED (the channel is reliable and ordered). The SIP leg's
  `RTCPeerConnection` is a `TextPeerConnection`, which adds the dcmap to the SDP
  `RTCPeerConnection` writes (it keeps one it's given, but doesn't write its own). While the
  channel opens the popover says "Asking …'s phone to add text…" (a fraction of a second); an
  answer or offer with no T.140 channel, or one that doesn't open in 10 s, shows "…'s phone
  doesn't support real-time text". A far end that opens a `t140` channel in band instead is
  taken too. There's no *Add text* here: a WebRTC call has text from the start or not at all.

## Keypad (DTMF)

From the prototype: the **Keypad** button opens a 12-key pad (1–9, \*, 0, #, with their
letters) in the dock to the right of the call, above the text panel if that's open too (see
[The dock](#the-dock-keypad-and-text)). It acts on the selected call only, and its title says whose it is.

- **Each press sends a tone** on that call and adds the digit to the display above the keys;
  the log has `keypad '5' → Name` from the page and the host's `DTMF '5' sent (…)`.
- **While it's open the keyboard's number keys, `*` and `#` send tones** instead of their usual
  shortcuts (`1`–`8` normally pick calls), except while typing in a field. `Esc`, its ×, the
  button again or a click anywhere else closes it.
- **It closes**, and the button is disabled with a tooltip saying why, when the call is put on
  hold (either way), ends, or another call is selected, and in Grid view (which has no
  controls). It's also disabled until the call's audio to the page is connected, so the first
  tone isn't lost.
- **Tones they send** are logged by the host (`DTMF '5' received (RFC 4733)` or `(SIP INFO)`),
  and while the keypad is open on that call they show under the keys ("Name sent 123").

How the host sends them (`Phone.SendDtmf`):

- **RFC 4733 telephone-events** when their SDP has `telephone-event` (SIPSorcery offers
  `101 telephone-event/8000` on every audio track, so we always do). The events go in the call's
  own audio stream: `MediaRelay.SendDtmfAsync` gives them the timestamp of the audio going out
  when the key was pressed and the next sequence numbers, moving the page's audio after them
  along to match, so the far end sees one unbroken stream. (`RTPSession.SendDtmf` keeps a
  timestamp and sequence of its own, which jump about next to the page's relayed audio.) Each
  tone is 160 ms: a start packet with the marker bit, an update every 20 ms, and the end packet
  three times; the audio goes on meanwhile. Presses queue, one tone at a time.
- **SIP INFO** when they didn't agree `telephone-event`: `application/dtmf-relay`,
  `Signal=5` / `Duration=160`, in the dialog.
- **Received** either way: RFC 4733 events through `SIPUserAgent.OnDtmfTone` (once per tone),
  and INFO (`application/dtmf-relay` or `application/dtmf`) answered 200 by the host, since
  SIPSorcery's user agent leaves INFO alone when it doesn't own the transport; an INFO of
  another type gets 415, one outside our calls 481.

In the web softphone (`web/host.js`) the page's connection goes straight to the far end, so the
page's own audio sender sends the tones (`RTCDTMFSender.insertDTMF`, RFC 4733). There's no SIP
INFO there: a call whose far end didn't agree `telephone-event` says `dtmf: false` in its state
and the page disables the keypad. Browsers don't report tones they receive.

## Transfer

From the prototype: "Attended to another active call, or blind to an address". The **Transfer**
button (the phone with an arrow, beside Keypad) is there in Focus view only, since Grid view has
no controls, and is enabled on a connected call with no transfer already under way. It opens a
popover above it (beside the dock if the keypad or text is open, so it never covers them):

- **To another of your calls** (attended): your other connected calls, each with *Join*. The
  selected call's far end is asked to call that person, replacing your call with them (REFER
  with `Replaces`, `SIPUserAgent.AttendedTransfer`). Done, both your calls end.
- **To an address** (blind): the address bar's field, the same component (`addressField`), so
  the same *Contacts* then *Recent* groups and filtering as you type, the saved-contact mark and
  pill, the × and the add/remove contact button, with its list always showing and without the
  person being transferred or the *Contacts…* row. `↑`/`↓` pick one, and `Enter` or a click
  puts it in the field, as on the address bar, without sending anything: **Transfer** (or
  `Enter` again) sends the call there (REFER, `SIPUserAgent.BlindTransfer`), with the picked
  entry's name.
- **No From account**: a blind transfer has none to choose. The REFER goes on this call's
  dialog (with the call's account's password if challenged), and the far end calls the target
  as itself. A bare name or number goes to the call's account's server (`7000` on a call from
  *Office* is `sip:7000@office-server`; on a direct call, `sip:7000`), and on that server the
  account's transport goes in the `Refer-To` too (`;transport=tls`, say), so their phone reaches
  it the way we would. A full address goes as typed.

The call goes on hold as the transfer starts (both calls, attended), and a note says how it's
going, in place of the held note. It's a quiet pill at the top right of the stage (the call
info has the top left), dark and translucent like the call info, in small type, fading in, so it
doesn't sit over the middle of their video; in a narrow window it runs across the top, below the
call info. Done reads in light green, failed in soft red. It says how it's going from the far end's NOTIFYs
(the `sipfrag`): "Asking VtN's phone to call Dave…", "Transferring to Dave…", "Dave is
ringing…". The call's row says "Transferring to Dave…" too.

- **Done** (NOTIFY `200 OK`): they hang up on us (if they haven't 3 s later, we do), and the call
  ends with *Transferred to Dave*. Attended, both legs end: *Transferred to Carol* on Bob's,
  *Transferred to Bob* on Carol's. A far end that sends no NOTIFYs still counts as done when it
  hangs up on us after accepting.
- **Failed**: the note says why in words, with the response, e.g. "Couldn't transfer: Dave is
  busy (486 Busy Here)", "nothing answered at Dave's address (408 Request Timeout)", or "VtN's
  phone turned the transfer down". The call stays on hold, with **Resume** and **Try again** on
  the note: what happens next is your choice, never a silent resume. Resuming clears the note.
- **No word**: accepted, but no outcome in 60 s, it's marked failed ("No word from …'s phone on
  how the transfer went"), still on hold.

**Being transferred.** A REFER on one of your calls (`OnTransferRequested`) is **taken without
asking**, and the stage says so: "Alice is transferring you to Dave…". Asking would be the safer
default, but the user agent raises `OnTransferRequested` on its SIP receive thread and must
answer the REFER (202 or 603) when the handler returns, so there's no way to wait for a click
without stalling all SIP. Refusing every transfer would break the common case (being put
through by a receptionist), so it's taken, within limits:

- only inside a connected call, from the person you're talking to; not on a browser phone's
  (WebRTC) call, whose media can't move to another call; not while a transfer is already under
  way on it; only to a `sip:` or `sips:` address. Otherwise it's answered 603 and the note says
  "Alice tried to transfer you to Dave, but …".
- the call to the target goes out with your account's password only if the target is on that
  account's own server (`OnTransferCallDescriptorCreated`), so a transfer can't use your account
  to reach a number outside it. With no account it goes from the address the transferor reached
  you at. It carries `Referred-By`, naming the transferor.
- while it rings you can see it and hang up.
- the call to the target is routed as if you'd dialled it: to your account's server on the
  account's transport (TLS to sipsorcery.com, say), or on the transport the `Refer-To` names.
  (The user agent drops the address's parameters, so without this it went over UDP, and a
  TLS-only server never answered: the transfer failed with 408 after 32 s.)
- if the transferor hangs up while the target rings (they may, once you've accepted), the call
  to the target goes on: "Alice hung up. Still calling Dave…". If it then fails, there's no one
  to go back to, and the call ends saying so. Hanging up yourself meanwhile cancels the call to
  the target and hangs up on the transferor.

The user agent places that call on the same media session, and once it's answered hangs up on
the transferor (`OnTransferToTargetSuccessful`): the same call in the list, now with Dave, its
timer restarted, and "Alice transferred you to Dave" on the note for a few seconds. If it fails
(`OnTransferToTargetFailed`), the user agent takes the transferor off hold and the call carries
on: "Alice tried to transfer you to Dave, but Dave is busy (486 Busy Here). You're still with
Alice." The user agent sends the transferor no NOTIFYs, so the host does, in the old dialog:
`100 Trying`, `180 Ringing`, then the final response (`Subscription-State: terminated`).

The media session carries on, but each stream's RTCP keeps reporting on the first source it
heard, the transferor's, so the new far end would get reports that never mention its audio and
warn "… isn't getting your audio" though it is. So once the call's moved, the host drops the
reports on the sources that were arriving when the transfer began (`RemoveReceptionReport`), and
the new far end's first packet starts a fresh one. The same goes for a call taken over in place
(below).

Video to and from the new far end starts mid-stream, and a decoder can't show it cleanly until
a keyframe (before, 5 to 10 s of corrupted picture until the next periodic one). So once the
call has moved, the page is asked for a keyframe for them and they're asked (PLI) for one for
us; and whenever their video arrives from a new source, it's asked again (the log says
`relay: their video is from a new source (…): asking it for a keyframe`).

Audio needs the opposite care. The relay sends the far end's audio to the page as one stream,
whoever is sending it, and the new far end's sequence numbers and timestamps start anywhere: a
page that saw them jump on the same stream dropped them as late, so after a transfer there was
no audio at all ("no audio arriving") while video was fine. So when their audio arrives from a
new source, the relay carries on its own numbering to the page from where it was (the log says
`relay: their audio is from a new source (…): carrying on our sequence and timestamps to the
page`), and drops any late packets from the source before.

A call taken over in place that **you placed** (you called the person who then joined you to
someone else) used to stay up when the new party hung up: the user agent answered their BYE
but still matched it against the call it had placed, and your own hang-up went to the old
call. That was in the library (`SIPUserAgent.AcceptAttendedTransfer`): it now drops the placed
call's details once the replacing call is answered, and flags the replaced call's Call-ID, not
the new one's, as the one whose late re-INVITE answers are ignored.

**Being taken over** (the target of someone's attended transfer): an INVITE with `Replaces` for
one of your calls is answered by that call's user agent in place (`OnAttendedTransferRequested`,
`OnAttendedTransferAccepted`): "Alice is handing you over to Bob…", then the call is with Bob.
There's nothing to decide: the person you were talking to arranged it. An INVITE with `Replaces`
for a call you don't have gets 481.

**History.** A call that's transferred is recorded as it was, with *Transferred to Dave* and its
duration ("Transferred to Dave · 0:18"). On the side being transferred, the call before the
transfer is its own entry ("Alice transferred you to Dave"), and the call with Dave another
when it ends.

**Web softphone.** The web host doesn't send `canTransfer` in its state, so the button isn't
shown there.

**Known limits.** An attended transfer's `Refer-To` is the target's contact address, without a
name, so after a direct (no-account) attended transfer the transferee sees an address like
`127.0.0.1:5432` rather than a name. A failed blind transfer to an address where nothing
answers takes the SIP timeout (about 32 s) to fail.

## The dock: keypad and text

The keypad and the text panel open in a side panel (300 px) to the right of the selected call in
Focus view, and the stage narrows to make room. There's no choice to make: they used to be
popovers, each with a dock button and rules for joining the other, and that was more to learn
than it gave.

- **Both in the dock are stacked**, not tabbed: the keypad at the top, at its own size (the keys
  shrink to 28 px in a short window), and the text taking the rest. Stacked, you can read what
  an automated line says while you press keys, with no extra click to switch between them.
- **Closing**: `Esc`, × or the control-bar button closes each one as before (with both open,
  `Esc` closes the keypad first, then the text). A click elsewhere doesn't close them.
  Closing one leaves the other; closing both hides the dock and the stage widens again.
- **The call ends**: when the call on the stage ends (either side hangs up), its keypad and text
  panel both close, so the ended call's card has the stage at its full width.
- **The rest is as before**: the keypad's number keys and `Esc`, `T` for text. The keypad
  closes on hold, on another call, or in the grid; the text panel follows the selected call and
  closes in the grid. So the dock hides in Grid view, and `T` from the grid opens Focus with the
  text in the dock.
- **Narrow windows** (under 960 px): there's no room for a column, so the dock floats over the
  right of the stage, with the same stacking, and the stage keeps its width.

## Browser phones (WebSocket)

So a browser can call the desktop app without Asterisk or FreeSWITCH, the app also listens for
SIP over WebSocket (port 8081, `--ws-port`; 0 turns it off). The SIP
log's first lines give the address, e.g. `browser phones: connect to ws://192.168.1.123:8081`.

- **WebRTC media or plain RTP.** A call with a browser uses an `RTCPeerConnection` as its SIP
  media session (ICE, DTLS-SRTP, bundle, which is what a browser expects); other calls keep the
  plain `RTPSession`. Which one:
  - **Incoming:** WebRTC if it came over `ws`/`wss`, or if the offer is WebRTC's
    (`UDP/TLS/RTP/SAVPF` from a browser, `UDP/TLS/RTP/SAVP` from SIPSorcery): a browser
    registered with a server reaches us over UDP, TCP or TLS like anyone else.
  - **Outgoing:** plain RTP, or WebRTC to a `transport=ws` address or with `--webrtc`
    (`"WebRtcCalls": true`). An address on a server doesn't say whether a browser is behind
    it, so if they turn the offer down for its media profile the call is placed once more,
    straight away, with the other kind. That's a `488 Not Acceptable Here` with
    `Warning: 302 … "Incompatible transport protocol"` (RFC 3261 20.43: the m-line's profile,
    where 305 would be the codecs), or with an SDP body of what they can take (21.4.26) on the
    other kind of profile. The web softphone sends both. A bare 488, with neither, is retried
    too: sipsorcery.com's app server is a B2BUA and doesn't always pass them on (only when the
    call had a single destination), and if it was the codecs the retry costs one more INVITE.
    A 488 with any other Warning (305: the codecs) or an SDP on our own profile isn't retried:
    "They can't take this call's media (488)". The SIP log says when it retries.
  - A plain SIP phone that answers a WebRTC offer with RTP/AVP instead of a 488 ends the call
    with "Their answer's media doesn't match ours".
 `RTCPeerConnection` is an `RTPSession`, so
  `SIPUserAgent` and `MediaRelay` take either; the relay just waits for ICE and DTLS to connect
  before sending to it. If they fail, the call ends with "Media connection failed".
- **It isn't a registrar.** REGISTER gets 405. A browser has no address of its own to call, so
  calling one from here means both registering with a SIP server (sipsorcery.com takes `wss`)
  and calling its address there with `--webrtc`. We only offer host ICE candidates (no STUN or
  TURN yet), so a browser on another network may not get media through.
- **To call the desktop from a browser**, use the web softphone in [../web](../web/README.md)
  (this app's own page with SIP.js), or point any SIP.js or JsSIP page at `ws://<this PC>:8081`
  and dial any user at it, e.g. `sip:desktop@localhost`. The page has
  to be on `localhost` or `https` for the browser to give it the camera and microphone, and an
  `https` page can only open `wss://`, which the app doesn't listen on yet. So for now the
  browser runs on the same PC.


## Video and no video

- **The Audio | Video switch beside Call** picks the kind of call. *Audio* leaves video out of
  the INVITE; *Video* offers it, and the button reads "Video call". It always starts on
  Audio: most SIP calls go to phones, an audio offer never trips anything up, and the camera
  stays off (and the self view hidden) until you ask for video. With `--no-video` it's locked to
  Audio.
- **Add video**, on an audio call, is where the Camera button would be. It adds our video codecs
  to the SIP session and sends a re-INVITE with `m=video`. If they accept, the relay is rebuilt
  on the same SIP session and the page gets a new offer with video. If they refuse (`m=video 0`,
  an error, or no answer in 8 s), the button becomes a disabled *No video*. Video you add
  starts with your camera on. When the *other* side adds video, it's accepted with your camera
  off, so a request from them never turns your camera on by itself. That includes an audio call
  we placed, whose SIP session was made without video: our video codecs go on it as their
  re-INVITE is answered (`SIPUserAgent.OnRemoteMediaStreamAdded`), so the answer isn't
  `m=video 0`.
- **The camera is only open when it's needed:** before a call while the switch is on Video (as
  a preview in the self view), and during a video call with your camera on. With the switch on
  Audio, on an audio call, or with your camera turned off, it's closed: no self view and no
  camera light.
- **So is the microphone.** It opens when you press Call (alongside the INVITE, so it's ready
  when the call connects) or Answer, or when the call's media arrives (auto-answer, `--dial`),
  and closes when the call ends, so a webcam's mic light is off between calls. Not while a
  call is only ringing. If a call you placed never starts (a bad address), it closes after 5 s.
- **A camera in use elsewhere says so, and waits for you.** Windows lets one app have a camera
  at a time, so a second softphone (or Teams, say) gets `NotReadableError`. That shows as
  "Camera in use" (not "No camera"), and on a call your camera goes off and the placeholder is
  sent. It's never retried by itself: the camera only opens because of something you did
  (switching to Video, placing or answering a video call, Add video, Camera on). Otherwise the
  softphone would start streaming your camera on its own the moment the other app let go.
  A missing camera or a refused permission is "No camera".
- **Test pattern** (diagnostics mode): picked from the Camera button's ▾, it sends a test
  pattern instead of the camera. It stands in for the camera exactly:
  opened and closed when the camera would be, Camera off still sends the placeholder, and it
  can be switched mid-call. The source is per call: with several calls, the Camera ▾ picks
  for the selected call only, so one call can send the test pattern while another sends the
  camera. So is the camera itself: a camera picked for one call (even an audio call, as the
  one for Add video) doesn't change another's, and going to a call that wants another camera
  switches to it. The source and camera picked last are what the preview and new calls start
  with.
  It has colour bars and a grey ramp (colour and banding), a dial
  whose hand steps once per frame drawn (a dropped frame is a skipped tick), a clock to the
  millisecond (compare the far end's picture with this screen for the delay), the sender's
  name or address and a frame counter. Closing diagnostics (F12) goes back to the camera, but
  turned off: the camera is only sent once you press Start video, since it never starts by
  itself. With no camera the placeholder is sent, as it would be anyway.
- **Audio bars** (diagnostics mode), the Camera ▾'s other diagnostic source, sends your
  microphone's frequency bars as video, drawn like the audio-only view, with the microphone's
  name (or "Muted"), a level meter and the same clock and frame counter. The far end sees what
  you're sending as audio: sound or silence, a muted or dead microphone, and lip sync (a bar
  should jump as they hear the sound). The microphone only opens with a call, so before one
  the preview says "Microphone opens with the call".
- **Picking a source turns the camera on.** With the camera off, choosing a
  camera, the test pattern or audio bars from its ▾ sends it straight away (on a video call),
  or makes video calls start with it on (before one). On an audio call the ▾ only sets what
  Add video will send.
- **Answer ▾** on a ringing video call: under "Answer" the button names what you'll send (the
  camera's name, "Test pattern" or "Camera off"), starting from the Camera button's choice.
  The ▾ picks a camera, the test pattern (diagnostics mode) or camera off, for this call. An
  audio call gets a plain Answer. The menu is positioned against the window, not the stage
  (which clips its contents), so in a short window it opens upward over the stage or past it.
- **Self view**, at the right end of the control bar (under the self view it controls; the
  prototype has it in the info bar), or `V`, hides or shows your own picture. It's there
  whenever there's a self view: from the moment the switch goes to Video, and during a video
  call. On a call what's sent doesn't change. Before a call, with it off, the camera stays
  closed (the preview is only for you) and opens when the call connects. It's remembered
  between runs.
- **Moving and resizing the self view.** Drag it anywhere on the stage (mouse, pen or touch);
  it stays inside the stage, also when the window resizes. Its corner handle, on the corner
  facing the middle of the stage (top-left at first), resizes it, keeping 16:9, from the
  default size up to three times it, as far as the stage allows. Double-click it to put it
  back in the bottom-right at the default size. It can take keyboard focus (Tab): the arrow
  keys move it, Shift with the arrows resizes it (Up or Right larger, Down or Left smaller)
  and Home puts it back. The call's name and timer, Call quality, the on-hold note and the
  answer buttons stay above it, so it never blocks them and a drag can't start from them.
  Where it sits is kept in the page's `localStorage` (`selfViewBox`) as a fraction of the way
  across and down the stage, with its size as a multiple of the default, so it keeps its
  corner at any window size and is the same on the next call and the next run. Grid view
  has no self view. (`wwwroot/selfview.js`.)
- **Camera on/off** during a video call turns it on or off with no renegotiation: the page keeps
  the video sendrecv and swaps the track in and out (`replaceTrack`).
- **Video never stops while your camera is off.** A video call with your camera off, or with no
  camera at all, sends a placeholder: your initial (from the display name or user name of the
  call's account, so with two accounts each call has its own, or a head-and-shoulders icon with
  no account) in a circle, drawn on a canvas at
  1280×720 and sent at 5 fps. A still picture costs a few kbps. So any SIP phone just shows a
  picture, not a frozen last frame or a stream that looks broken. NAT bindings and media timers
  on the video port stay alive, and turning the camera on is a track swap with the encoder
  already running.
- **The self view shows what they see.** During a video call that's your camera (mirrored) or the
  placeholder (not mirrored, so the initial reads right). A note along the bottom says why the
  camera isn't showing: "Camera off", "Camera in use" or "No camera". Before a call, with the
  switch on Video, it's the same preview.

The stage then shows one of three things:

| They send video | What you see |
|---|---|
| Yes | Their video. |
| Agreed, but nothing arriving for 2 s | Their initial and "No video arriving from X". A softphone with its camera off sends its placeholder, so this means the video has stalled or the far end stopped sending, not that their camera is off. |
| No video on the call (`m=video` refused or never offered) | Two rows of frequency bars: their audio above the centre line, yours below. With reduced motion on, each row is a single level bar. |

The bars also show **one-way audio**, the classic SIP/NAT fault. If no audio packets arrive for
2 s, their row drops to a thin line with "No audio arriving from X". If the far end's RTCP
reports show our audio isn't reaching them (our source isn't listed, or its sequence number has
stopped), your row turns red with "X isn't getting your audio".

## Call quality (diagnostics mode)

**Call quality**, at the top right of the stage during a call, starts as one line: the title,
the round trip (an out-and-back arrow and the value, "⮐ 0.3ms", from their RTCP), both audio MOS (↑ what we send, ↓
what we receive) and a chevron to open it. It's closed at the start of every call. Opened, it's a small
table with a column for each direction, so there's no "they"
or "you" to work out:

| | | ↑ Sent | ↓ Received |
|---|---|---|---|
| Audio | MOS, loss, jitter | how the far end receives our audio | how we receive theirs |
| Video | loss, fps | how the far end receives our video (fps: the rate we send, since RTCP doesn't carry what they see) | how we receive theirs |

On video calls a **PLIs** row comes next: the legend (Rcvd in amber, from them; Sent in violet,
ours) in one half and, in the other, a tick for each second with one over the last 30 s, a row
for each. A PLI means a lost packet has broken the receiver's picture, which stays broken until
a keyframe arrives. It's an indicator, not a history. PLIs come from the page's `pliCount`; the
relay passes them straight through, so they're the SIP leg's.

Below that, a graph shows one column over the last two minutes. A switch above it on the
right, **↑ Sent | ↓ Received** (like the Audio | Video switch), picks which, and the title on
the left says who measured it: "Reported by them" (their RTCP on what we send) or "Measured by us" (what
we receive). The column on the graph is tinted in the table to match; the headings themselves
aren't clickable. The graph has audio MOS (a line, 1–5), audio loss (bars) and video loss (a line), both 0–20%. On ↓
Received, white bars along the bottom show how long our picture was actually frozen, from the
browser's `totalFreezesDuration`; it only counts a freeze once it has ended, so a bar is drawn
back over the seconds it covered when it does. ↑ Sent has none: RTCP doesn't say whether their
picture froze. The last line gives
the round trip ("RTT 0.3 ms"), or says it's waiting for their RTCP, or after 15 s that none has
come. Hovering over a value says
what it is, where it comes from, and for loss the total packets lost. Only video calls show the
video rows.

To see the reports react, the diagnostics panel's **Network** tab has two sliders, Audio
loss and Video loss (0–50%). They drop that share of the audio or video packets we send
on the SIP leg, at random, from the moment a slider is let go, and carry over to the next call.
While either is above 0, the Network tab has a red dot, so it isn't forgotten, and closing
diagnostics (F12) puts both back to 0. Each dropped packet still uses up its sequence number, so the far
end sees a real gap and counts it in its RTCP. Audio is relayed packet by packet, so dropping
one is easy. Video is handed to SIPSorcery a frame at a time and packetised inside the library,
with no way to drop a single packet, so while video loss is on, the relay packetises VP8 and
H264 itself, the same way, and sends each packet with the next sequence number from the track.
At 0% video goes back to SIPSorcery's own sending, and the numbering carries on.

Where the numbers come from:

- ↑ Sent: the host forwards each of their RTCP reception reports about our streams
  (`OnReceiveReport`) to the page, every 5 s or so. The graph holds each report until the
  next. Jitter is converted from RTP units with the clock of the codec we send; round trip is
  now − LSR − DLSR. The running loss total is worked out from each report's loss fraction and
  the change in its highest sequence number, since SIPSorcery (and some phones) report the
  interval's loss rather than the cumulative one.
- ↓ Received audio comes from the page's WebRTC stats, once a second. Those only cover the hop
  from the relay, but audio is relayed packet by packet with its sequence numbers kept, so its
  loss and jitter are the SIP leg's. Its loss is over the last 5 s: the browser only updates
  `packetsLost` when it builds its own RTCP report, so a 1 s difference read 0 nearly every time.
- ↓ Received video is packetised again for the page, so the page's loss count isn't the SIP
  leg's. Its loss comes from the RTCP report we send about their video (`OnSendReport`), and its
  frame rate from the page's decoder.
- The MOS is a simplified ITU-T G.107 E-model: delay is half the round trip (40 ms if there
  isn't one) plus twice the jitter plus 20 ms, and loss is random with concealment (Bpl 25).
  Wideband codecs sound better than it says. It's for spotting trouble, not grading codecs.

There are no contacts or transfer yet.

**Later:** a help or info section, to explain the Call quality panel: that ↑ Sent comes from
the far end's RTCP reports (every 5 s or so) and ↓ Received is measured here, what the MOS
estimate is and isn't, and where each number comes from (as above). The panel had a footnote
saying this, but there's no room for it there; for now the tooltips carry it.

## SIP accounts

The accounts this phone registers with and calls from are managed in the app, as in the UX
prototype's *Call from* and *Manage accounts* (`prototype/web`). Every enabled account registers
at once; *Call from* picks the one calls go out from.

- **Call from**, at the left of the dial bar: the enabled accounts, plus *No account (direct)*
  for calls straight to an IP address or host. The dot is the chosen account's registration:
  green registered, amber registering, red failed, grey not registering. Its tooltip says why
  it failed, and so does the Ready card ("alice@… isn't registered: Wrong username or
  password (401 Unauthorised)"). The choice is remembered, *No account* included.
- **Manage accounts** (the server icon beside it; the two heads are People's) opens the Accounts view over the stage; a call
  carries on underneath, and a ringing call hides the view (the icon brings it back, with
  what you'd typed). The icon turns red with an alert while any enabled account has failed,
  and its tooltip names them. The view starts on a table of every account and its status;
  clicking one opens its form. `Esc` steps back: form, then table, then closed.
- **The form**: account name (blank: user@server), display name (shown to people you call, and
  used for the camera-off placeholder), username, server (`host` or `host:port`), transport
  (UDP, TCP or TLS), auth username (if not the username), password, *Register*, expiry
  (60–7200 s, default 300), and under *Calls*, *Hold*: *Music on hold* (the default) or *SIP
  re-INVITE* for a PBX (see [Several calls](#several-calls)). A call keeps the Hold it started
  with. Outbound proxy, STUN, SRTP and the DTMF choice from the prototype aren't there yet: the
  host doesn't do them per account (DTMF is RFC 4733, or SIP INFO when the far end doesn't
  agree it; see [Keypad](#keypad-dtmf)). *Re-register* registers
  again now; the *Enabled* switch turns the account off (unregistered, not offered in Call
  from) without losing it; *Remove account* asks for a second click.
- **What changes registration**: a new or enabled account registers; a removed or disabled one
  unregisters (REGISTER with Expires: 0); a change to the username, server, auth username,
  password, expiry or *Register* registers again (unregistering the old address only if the
  address changed). A new name or display name leaves the registration alone.
- **Failures in plain words**: 401/403/407 "Wrong username or password", 404 "*server* doesn't
  know the user *x*", DNS failure "Can't find the server *x*", no response "No answer from
  *x*", each followed by the server's own status line. A temporary failure says "Retrying…".
- **Calls** go out from the chosen account: From is its display name and `user@server`, and its
  credentials answer the server's challenge. A bare number or name is dialled at its server.
  An incoming call is matched to the account in its To header (user and domain), which Call
  details shows ("Incoming · alice@…").

**Where they're kept**: `%LOCALAPPDATA%\SIPSorcery\Softphonev2\accounts-<profile>.json`, one file
per `--profile`, so test copies each have their own. Passwords are encrypted with DPAPI for
the current Windows user (`protectedPassword`); the page never sees them, only whether one is
saved, so the password box is blank with "Saved: leave blank to keep it". A file from another
user or PC loads with blank passwords, and the log says to enter them again.

**The config file's account is a seed.** `softphone.json` (or `--config`) can still have an
`account`. The first time a profile sees it, it's added to that profile's accounts (named by
`user@server`); after that it's an account like any other, and removing it in the app keeps it
removed. The file remembers which config accounts it has already added (`seeded`).

## Running it

```bash
dotnet run -c Release
```

On its own it listens on port 5060 (UDP and TCP), or if that's taken on a port the OS picks (the log says which), and makes direct calls: type
`sip:user@host[:port]` or `user@host` and press Call.

**Diagnostics: F12.** As in a browser's developer tools, F12 opens a panel docked along the
bottom of the window, and F12 again (or its ×) closes it. It's closed at every start
(`--diagnostics` opens it). Drag its top edge to resize it, between 120 px and 70% of the
window. The height you leave it at is remembered per profile and used whenever it opens.
It has two tabs:

- **SIP log**: everything the host and page log, following the newest line unless you've
  scrolled up. *Copy log* puts the whole log on the clipboard; *Clear* empties it.
- **Network**: the simulated loss sliders (see Call quality).

While it's open the app is in diagnostics mode: Call quality over the stage, this softphone's
address in the header, and the Test pattern and Audio bars sources on the Camera ▾. Closing it
hides them all. The page catches F12 before WebView2 does, so it doesn't open WebView2's own
DevTools; Ctrl+Shift+I still does, for working on the page.

The address in the header reads e.g. `No account · sip:192.168.1.20:5070`; click it to copy it for the other side to
dial. It's the address this PC uses to reach the internet (which works from this PC too) and the
UDP SIP port. With an account chosen in Call from it copies that account's address. To register
with a server, add an account with Manage accounts (see [SIP accounts](#sip-accounts)), or seed one
from `softphone.json` (copy `softphone.sample.json` next to the exe and fill it in).

Switches, mostly for testing:

| Switch | What it does |
|---|---|
| `--config <file>` | Use another settings file. Its `account` seeds the profile's accounts once. |
| `--port <n>` | SIP port (default 5060). |
| `--profile <name>` | Names the WebView2 profile and log file (default `default`). If another running copy already has that profile, the next free `name-2`, `name-3`… is used. |
| `--fake-media` | Chromium's fake camera and microphone (a test pattern and a beep). |
| `--dial <uri>` | Call this address as soon as the window is up. |
| `--auto-answer` | Answer incoming calls after a second. |
| `--video` | Start with the switch on Video, so `--dial` places a video call. |
| `--test-pattern` | Send the test pattern instead of the camera. |
| `--audio-bars` | Send the microphone's frequency bars instead of the camera. |
| `--diagnostics` | Open the diagnostics panel (F12) at start. |
| `--loss-audio <percent>`, `--loss-video <percent>` | Start with the Network tab's loss sliders at these values. |
| `--ws-port <n>` | SIP over WebSocket port for browser phones (default 8081, 0 for none). |
| `--webrtc` | Calls we place use WebRTC media whatever the transport, for browsers registered with a server. A plain SIP phone won't take them. (Will be per account.) |
| `--no-text` | No real-time text: it's left out of offers and answers (no `m=text`, and on WebRTC calls no T.140 data channel), so the other side adding it is turned down. (Will be per account.) |
| `--say <text>` | Once text is up on a call, send this message. |
| `--dtmf <keys>` | Once a call's audio is up, open the keypad and press these keys (0–9, \*, #), as typed keys, 2 s in and one every 400 ms. |
| `--no-video` | No video at all: the switch is locked to Audio, and video offers or Add video from the other side are turned down. (Will be the per-account "Video calls" setting.) |
| `--camera-off` | Start with the Camera button off, so calls and answers start with the camera off. |
| `--add-video <s>` | Press Add video this many seconds into an audio call. |
| `--hangup-after <s>` | Hang up a call this many seconds after it connects. |
| `--dial-count <n>` | With `--dial`, place `n` calls one after another, each 2 s after the last one ends. |

The log, with every full SIP message, goes to
`%LOCALAPPDATA%\SIPSorcery\Softphonev2\softphone-<profile>.log`. Every 5 seconds during a call it
also records the page's receive and send rates, and the relay's packet and frame counts.
An error the app doesn't expect shows a "Softphone failed" box and is also added to
`%LOCALAPPDATA%\SIPSorcery\Softphonev2\errors.log`, since one while it's closing can end the
process before the box can be seen.

The window opens where it was last closed, at the same size (maximised if it was), from
`%LOCALAPPDATA%\SIPSorcery\Softphonev2\window-<profile>.json`, so each profile keeps its own. If
that place is no longer on a screen, it opens at the default size instead.

### Two softphones calling each other

```bash
Softphone.exe --port 5080 --profile b --fake-media --auto-answer
```

```bash
Softphone.exe --port 5070 --profile a --fake-media --dial sip:b@127.0.0.1:5080
```

### Against the spike's far end

The far end streams a pre-encoded clip and has no audio. Start it with `--any`, because the
softphone offers its LAN address:

```bash
..\spikes\VideoPipeline\artifacts\bin\FarEnd\Release\FarEnd.exe --media ..\spikes\VideoPipeline\media --any
```

```bash
Softphone.exe --fake-media --port 5070 --dial sip:h264-720p30@127.0.0.1:5090
```

## What's been checked

| Test | Result |
| Call history, two copies (`--profile hiA --port 5381 --ws-port 8381` and `hiB --port 5382 --ws-port 8382`, both `--fake-media`, no accounts); A calls `sip:hiB@127.0.0.1:5382` | B answers (`A`), A hangs up after 4 s: A's History "HiB · Just now" (Outgoing · 0:04), B's "Thisis" (Incoming · 0:04). B declines: A *Busy* (`486 Busy Here`), B *Declined*. A cancels while it rings: A *Cancelled*, B *Missed* in red, with the badge at 1. Each is in `history-hiA.json` / `history-hiB.json`. |
| A: the call-back button on the latest History entry | A calls `sip:hiB@127.0.0.1:5382` again; B answers and it's recorded like the first. |
| B: People | The view over the stage on Recent, "5 calls · 2 missed", the badge cleared; *Missed* lists the two missed calls and the button reads *Clear 2 shown*; the chevron opens the details (Incoming, Today 08:11, 0:03, No account (direct), Completed · They hung up) with Call back, Copy address, Remove and *Remove all 5 from this address*. |
| B: Remove, then Remove on another call, then Undo; *Remove all 5*, Undo | "Removed Thisis from history", then "Removed 2 calls from history" (one batch); Undo: "Restored 2 calls", 5 calls again in the file. Remove all: 0 calls; Undo: all 5 back. |
| B: *Missed*, *Clear 2 shown* | A dialog: "Clear 2 calls? This removes the 2 missed calls. Other calls stay in history." Clear: "3 calls · 0 missed", with Undo. |
| B with History open; A calls B, then cancels | The ringing call takes the stage from the view; when it stops ringing, History comes back with the new missed call, already seen (no badge). |
| B: `Esc`; then with a call answered, People | `Esc` closes the view. During the call the view has *Back to Thisis*, which closes it with the call as it was (not held); opening it again and clicking the call's row closes it too. |
| Both copies restarted | The same history on each, and B's unseen missed badge still 1. |
| Web softphone (`web/index.html?server=ws://localhost:8381&user=web`) with three entries put in its `localStorage` | The People button and view as on the desktop (Dana *Missed* with the badge, Echo, Eve *Busy* under Yesterday); Remove takes it out of `callHistory`, Undo puts it back. With nothing stored: "No calls yet". |
|---|---|
| Call info tucked away: two copies (`--profile vtK --port 5421 --ws-port 8421`, `vtL --port 5422 --ws-port 8422`, both `--fake-media --test-pattern`, built to a separate folder); L calls K, K answers (`A`), then the `‹` at the start of K's info bar | The bar slides left (mid-slide `translateX` −211 px) off the stage's edge; a 20×30 px `›` tab at the stage's top left, "Show call info · sip:thisis@anonymous.invalid". `inert` on the bar; `infoTucked: true` stored. No focus ring on the tab after a mouse click. |
| K: `M` while tucked | A red dot on the tab; tooltip "… · You're muted". |
| K restarted, L calls again | No tab before the call; once answered, the bar starts tucked with the tab. A click on the tab brings the bar back; `infoTucked: false`. |
| K in diagnostics (F12): the info bar's chevron, then `‹` | The details drop down; tucking the bar closes them. |
| K with reduced motion emulated | No transition on the bar, no animation on the tab. |
| K: the Active calls chevron | The 48 px rail as before (initials, chips, timer, History button with its badge); the chevron opens it again. |
| Several calls: three copies, A (`--profile fgA --port 5191 --ws-port 8191 --video --diagnostics`), B and C (`fgB`/`fgC`, ports 5192/8192 and 5193/8193, `--auto-answer`), all `--fake-media --test-pattern` | A calls B: video both ways. A calls C: A's B call goes on hold ("On hold · 00:06", B shows "Thisis put you on hold"), C's call on the stage with the audio. |
| A: `2` (the older call) | B's call comes off hold and has the audio (its `<video>` plays, C's is muted), and C's goes on hold: one live call. B gets A's video again within a second (the held call's sender goes back on). |
| A: `1`, `2`, `1`, `2` quickly | Each call ends up where the last key left it (B live, C on hold). With *SIP re-INVITE*, no 491 in the log: the host queues a call's re-INVITEs ("after the re-INVITE that's out"). |
| Music on hold (the default): A (an account with Hold *Music on hold*) calls B twice, `--auto-answer` on B | A's log: "holding …: music on hold, nothing signalled", no re-INVITE. Both relays keep 50 audio packets a second both ways on the held call; B's stats for it show audio in with a varying level (the music), and on a video call (`--video` on A) video in at 5 fps (the "On hold" card). |
| SIP hold: the same, A's account Hold *SIP re-INVITE* | A sends the re-INVITE; B says "put the call on hold". B still sends (silence) on that call, and A's relay receives it; no "Stream Status set to RecvOnly" warnings in B's log. |
| C (audio) calls B while B is on its call with A | B's banner: "Thisis · Calling" with Answer and Decline; B's host auto-answers it alongside (a test mode, so nothing is held): "Audio off" on the new row, A's call keeps the audio. No 486. |
| B: `1`, `M`, End, `Esc` | C's call on the stage with the audio; `M` mutes only it ("Muted", red mic; A's row "Audio is on another call"). End: "You hung up" with *Back to Thisis*; `Esc` goes back to A's call, which takes the audio. |
| Two accounts a side, B calls A twice (audio, A `--auto-answer`), A adds video to the first | A's list: Bob with filled mic and camera chips ("Bob hears you", "Bob sees your video"), Dave with a faint struck-out mic ("Your mic is on another call"); *Mute all* and *End all* under the list. Grid: Bob's tile shows B's video (B's "On hold" card, as B has it held), Dave's the bars, yours flat. |
| Same: *Mute all*, collapse, expand, *Unmute all*, *End all* twice | Both rows Muted, red chips, the button *Unmute all*; the rail shows the chips under each initial without a scrollbar. *End all* reads "End 2?", then both calls end ("They hung up" on B) and the buttons go. |
| A with two calls: `Shift+M`, `Shift+C`, `Shift+M` | Both rows "Muted" and "Video stopped"; then both unmuted, cameras still off. |
| Address bar cleared on answer: M types `sip:vtN@127.0.0.1:5432` and presses Enter (focus stays in the field); N has `stale text` in its bar; N answers | M's field empty (focus still there), N's empty too; each logs `call 1 answered: address bar cleared`. |
| Buttons in the calls panel don't toggle it: M with two video calls; Mute all, Unmute all, Cameras off, Cameras on, with the panel open and then collapsed (clicks on the buttons' icon or label, which they redraw); then End all on the rail; a click on the panel's empty space | The panel stays as it was for all four (before: each click toggled it, as the redrawn label was no longer inside the panel when the panel's own click handler checked). End all on the rail opens the panel and nothing more (before: opened, then closed again, so it seemed to do nothing). The empty space still toggles it. |
| End all on the rail: M with two calls (N held, O live), panel collapsed, End all three times | First click: the panel opens, both calls up, the button *End all* (the rail icon's tooltip says it opens the panel to confirm). Second: *End 2?*. Third: both end *You hung up*. |
| Missed-call badge: N calls M and hangs up while it rings (M's People view closed) | The badge at 1 on People. People: the view opens on Recent (People pressed), the badge cleared; People again: closed. Contacts tab, close, People: opens on Contacts (the last tab); a missed call meanwhile: People opens on Recent. |
| Panels close when the call ends: M calls N, opens the keypad and the text (both in the dock); N hangs up | Before: the dock at the right, the stage narrowed. After: M logs `call 1 ended: keypad and text closed`, the dock is hidden and the stage is back to full width with "They hung up". |
| Cameras off: M with two video calls (N held, O live; `--fake-media`), *Cameras off*, then *Cameras on* | The footer: Grid; *Mute all* and *Cameras off* side by side; *End all* in its own row. *Cameras off*: both calls `camera off`, the camera closes, the self view shows the camera-off card, O's stage shows M's placeholder (the initial in a circle); the button reads *Cameras on*, pressed, in red, and the call's own button *Start video*. *Cameras on*: both calls `camera on`, the camera opens, O sees M's video again, the button *Cameras off*. With only audio calls the button isn't shown. |
| Grid, the same three copies: A with video calls to B and C, then *Grid* under the list | Two tiles, C's (selected, outlined) with its test pattern, B's "On hold · 00:14"; *Grid* pressed; the control bar empty. `M` and `H` change nothing. |
| From the grid: B's tile; `G`; C's row in the list; `G`; `2`; `G`; `T` | Each opens that call in Focus, resuming it and holding the other; `G` goes back to the grid each time; `T` opens Focus on the selected call with the text input focused. |
| In the grid, C calls A | The banner over the grid, no ringing tile; the view stays Grid. The banner's name: the answer view (Settings still says grid). *Answer & hold current*: the new call in Focus, the other two "On hold" in the list. |
| Two accounts a side: A calls Bob (B answers), then calls Dave; B *Decline*; A calls Dave again, B answers; A calls Bob again, *Cancel* | While Dave rings, A's list has only Bob (On hold, still on the stage) and the "Dave · Calling… · Cancel" banner; B's Alice call stays a Video call (not held) under its banner. Declined: A's banner "The far end said 486 BusyHere · Close", gone when the host drops the call; Bob still on hold. Answered: Dave joins A's list, selected, in Focus; Bob on hold. Cancel: the banner goes, the list as it was (both on hold), B shows "Missed call". |
| Grid chosen, one call (so Focus), C calls A; *Decline*; C calls again; *Answer & hold current* | No switch to the grid while it rings: the banner over the call. Declined: the call as it was, still live. Answered: the first call "On hold", the new one in Focus. |
| A text message without its line end on the selected tile's call (injected as the host's `text` message), then Reply on the toast | The tile and the row say "Typing…" with the text icon's red dot; Reply opens Focus with the text panel. (Two copies of this app send a line at a time, on Enter, so they don't show Typing… to each other; with Enter, the tile shows the line, unread.) |
| Settings: *Show all calls in a grid* | Ticked: the grid, `view: "grid"` stored; after restarting the copies, A's two calls open in the grid. |
| Accounts, against a stand-in SIPSorcery registrar with digest auth on 127.0.0.1:5070 (`--profile accttest --fake-media --diagnostics --auto-answer`, a config with alice) | First start: alice seeded from the config, stored with a DPAPI password, registered. Bob added in the form with a wrong password: "Not registered: Wrong username or password (401 Unauthorised)", Manage accounts red. Password fixed: registered. An account with auth username carol registered as dave. `nosuch.invalid`: "Can't find the server nosuch.invalid (DNS lookup failed)". |
| Calls from accounts, same set-up | Call from Bob to `echo`: the server saw From `"Bob B" <sip:bob@127.0.0.1:5070>`, authenticated as bob. Call from alice to `callback`: the server called alice's contact back (To alice), auto-answered, Call details "Incoming · alice@127.0.0.1:5070". |
| Account changes and restarts | Restart: all three accounts back, registered, the seed not added twice. Rename: no REGISTER. Disable Bob, remove Dave: each unregistered (Expires: 0). Re-register: one new REGISTER, no unregister racing it. *No account (direct)* still chosen after a restart. Closing the app unregisters every account. |
| Web softphone (`web/index.html?api=stub`) with the same page | No Call from or Accounts view; its own sign-in as before; no console errors. |
| Settings, two copies (`--profile setA` / `setB`, `--fake-media --diagnostics`) | The gear opens Settings with the fake devices named, and *System default (Fake Default Audio Input)*. Focus starts on Microphone. |
| B ticks *Answer calls automatically* in Settings (no `--auto-answer`); A places a video call to B | B answers after a second with its camera off ("answering automatically, camera off", "call media: video, your camera off"), although B's *Start video calls with my camera on* is ticked. |
| Mid-call on A, Settings: Microphone and Speaker to the second fake devices | "microphone: Fake Audio Input 2" (a track swap), the remote element's `sinkId` changes; both remembered. |
| A idle on Audio, Settings: Camera to `fake_device_0`, theme Dark | The camera stays closed ("camera for calls: …"); the page goes dark at once. |
| Both copies restarted | A comes back dark with its devices; B has auto-answer and the system theme: each profile keeps its own. |
| A on Video, Camera button off, then Settings | *Start video calls with my camera on* is unticked, and kept. |
| Active calls: copy A (`--port 5091 --ws-port 8091`) calls B (`--auto-answer`, `--port 5092 --ws-port 8092`) | A's panel: "B · Calling…", then "B · Audio call" with the timer counting; B's "Thisis · Audio call". Mute: "Muted" in red, red mic; Add video: "Video call", camera green on A, red ("Video stopped") on B, which answered with its camera off. Hang up: "No active calls" on both. |
| Active calls: collapse, restart, narrow | Collapsed: a 48 px rail with "B" and the timer ticking. Restarting A with the same profile keeps the rail; a click on it opens the panel and saves "open". With two calls, a click on the other call's initial on the rail selects that call and opens the panel. At 560 px it's the rail whatever was saved; expanding lays the 240 px panel over the stage and the saved choice stays put. |
| Active calls: B calls A (no auto-answer) | A's row: "Thisis · Incoming call" in amber; Decline empties the list. |
| Video call, `--test-pattern` to `--fake-media` | Both ways at 30 fps; the callee shows the pattern with the caller's clock, and the caller's self view shows it unmirrored. |
| Same, Camera off, Camera on, then diagnostics off | Off: placeholder at 5 fps; on: pattern back at 30 fps; diagnostics off: the pattern stops, the placeholder is sent, the self view says "Camera off" and the button "Turn camera on". The camera only opens when that's pressed. |
| Control bar, idle | Turn mic off ▾, Camera ▾ and a disabled End. Camera ▾ lists the fake camera and, with diagnostics on, Test pattern; the mic ▾ lists three fake microphones and three speakers. |
| Before a call: Test pattern, Camera off | The preview goes from the pattern to the placeholder with "Camera off"; the button's tooltip says video calls start with your camera off. |
| Switch on Audio, idle | Camera and its ▾ are disabled. |
| Start, then a call, then hang up | The log shows no microphone until Call (caller) or Answer (callee; none while ringing), and "microphone closed" on both when the call ends. The mic ▾ menu still lists the devices with the mic closed. |
| Mic off before dialling | The caller's microphone opens muted; the callee reads level 0. |
| Incoming video call | Decline and Answer ▾, with "fake_device_0" under Answer; the ▾ lists the camera, Test pattern and Camera off. The control bar's Camera is disabled. |
| Answer ▾: Test pattern, then Answer | The label changes to "Test pattern"; the answered call sends it at 30 fps. |
| Answer ▾: Camera off, then Answer | The label says "Camera off" with a crossed-out camera; the call sends the placeholder at 5 fps. |
| Incoming audio call | A plain Answer, no ▾. |
| Answer ▾ in a 900×560 window | Not enough room below the button, so the menu opens upward, whole, over the top of the stage. |
| Switch to Video, Self view off, then a video call | The button appears with the switch; off hides the preview and closes the camera; the camera opens when the call connects and sends 30 fps. |
| Video call, `--audio-bars` to `--fake-media` | The callee sees the caller's microphone bars move with the fake beep, the microphone's name, and the caller's clock, at 30 fps. Before the call the preview shows flat bars and "Microphone opens with the call"; muting mid-call shows "Muted" and flat bars at the far end. |
| Video call, caller with `--say hello_there_from_A`, to `--auto-answer` | Both log "real-time text on (RFC 4103)". The callee gets a toast, "Thisis hello_there_from_A" with Reply, for 6 s, and a dot on the Text icon; nothing opens. |
| Same, callee: Text icon, types "Hi A", Enter, then clicks the video | The text opens in the dock with both messages and stays open after the click. The caller gets a "B Hi A" toast. |
| Caller: Text icon, then Esc | The text panel opens with the conversation; Esc closes it and focuses the Text icon. |
| Audio call, caller types "Hi from A" | No "Audio only" label. Nothing reaches the callee while the caller types; on Enter the callee gets the toast and the dot. |
| `--webrtc` call, caller `--say hello`, to `--auto-answer --say hello` | Both offer and answer have `a=dcmap:1 label="t140";subprotocol="t140"` on `m=application`. Both log "real-time text agreed", then 0.3 s later "real-time text on (RFC 8865, T.140 data channel on stream 1)", and each gets the other's message ("real-time text in: 7 characters": BOM, "hello", U+2028). |
| Same, callee `--no-text` | The callee's answer has `m=application` but no dcmap; the caller logs "they didn't take text (no T.140 data channel in their SDP)" and stays at refused, though its own negotiated channel opens when SCTP comes up. |
| Same, caller `--add-video 4` | Text on, then the re-INVITE (with the dcmap again) adds video; text stays on. |
| Plain RTP call, both `--say` (after the data channel work) | "real-time text on (RFC 4103)" both ends, and each message arrives. |
| Web softphone dials it (`server=`), both saying something | Both log "real-time text on (RFC 8865 …)"; each text panel has both messages. See the web README. |
| Caller with `--no-text`; callee: Text, then Add text to this call | "Text isn't set up on this call" with the button; after it, "Asking … to add text", then within 2 s "…'s phone doesn't support real-time text" (their answer has `m=text` with port 0). |
| Video call to `--auto-answer` | The caller's panel shows the callee's RTCP: no loss, jitter under 1 ms, round trip about 0.3 ms, MOS 4.4. |
| Network tab: Audio loss 12%, Video loss 20%, then F12 to close diagnostics | Both sliders back to 0% and the tab's red dot gone; the host is told loss 0/0. (Checked in the web page, which runs the same script.) |
| Same, caller with `--loss-audio 5 --loss-video 10` | The sliders start at 5% and 10%; their RTCP shows 2–5.5% audio and about 7% video loss per report, the totals climbing. |
| Mid-call: Audio loss to 20%, Video loss to 10%, then both back to 0 | Their reports go to 17–23% audio and 9.4–10.5% video loss, MOS about 2.7; at 10% video loss the far end decodes 4–10 fps between keyframes. Back at 0, loss returns to 0, the totals stop climbing and the far end is back to 30 fps (no sequence jump). |
| `--camera-off` video call: Camera ▾, Test pattern, then Camera off, then Camera ▾, the camera | Each pick turns the camera on and sends the choice straight away; the button shows Turn camera off. |
| Idle on Video with Camera off: Camera ▾, Test pattern | The preview shows the pattern and the button shows Turn camera off. |
| Both ends with loss (caller audio 10%, video 5%; callee audio 6%, video 3%) | The caller's ↑ Sent shows about 10% audio and 4.7% video loss, MOS 3.4; ↓ Received shows 5.6% audio and 2.7% video loss, MOS 3.8, and the decoded frame rate. Clicking ↓ Received moves the graph to that column. |
| Audio call to a callee with 6% audio loss | The panel has only the audio rows; ↓ Received shows 6.7% loss, MOS 3.7. |
| Video call, panel never opened | One line, "Call quality ↑ 3.3 ↓ 3.7" and a chevron; clicking opens the table (sent fps 30) with the ↑ Sent \| ↓ Received switch on ↑ Sent and "Reported by them" over the graph, and the chevron turns up. |
| Same, ↓ Received on the switch | The tint moves to the ↓ Received column, the title says "Measured by us", and Frozen joins the legend. Switch and title fit on one line in the 260 px panel. |
| Video call, caller `--loss-video 2`, callee `--loss-video 5` | The folded line reads "Call quality ⮐ 0.6ms ↑ 4.4 ↓ 4.4"; opened, "RTT 0.6 ms" is the last line. The PLIs row under the table ticks Rcvd (amber) and Sent (violet) over the last 30 s, whichever column is picked; ↓ Received's graph has white "Frozen" bars along the bottom. |
| Browser video call (../web), Call details | "Video: VP8 640×360" (the same both ways, so once). |
| Video call, open the info bar's chevron | Call details: "Outgoing · no account", their phone `sipsorcery_v10.0.18.0`, "Not encrypted: plain RTP, SIP over UDP", audio "↑ Opus 48 kHz ↓ G722 16 kHz", video VP8, SIP `UDP 127.0.0.1:5080`, their media address and the Call-ID. The callee's shows "Incoming" and the codecs the other way round. |
| Far end with `--loss-video 100` | "No video arriving from B" under their initial; ↓ Received video fps 0. |
| Self view, then `V` | The self view hides and comes back; the far end still gets 30 fps throughout. |
| Video call between two copies (`--profile sdA`/`sdB`, `--fake-media --test-pattern`, B `--auto-answer`); A's self view dragged past the stage's top-left, its handle dragged far out, then moved and the page reloaded | It stops 12 px from the stage's edges; the call's name and timer stay on top and clickable. It grows to 780 px (3 × 260) and no further, inside the stage. After the reload it's in the same place at the same size. `V` hides and shows it, `G` (two calls) hides it in the grid and Focus brings it back. No console errors. |
| Mic off before dialling, then a video call | The call connects with the mic off (Turn mic on shown). |
| Mid-call: another microphone, then the fake camera | Both swap without renegotiation; video carries on at about 30 fps, and the far end gets the new microphone's beep. |
| Pick a microphone, restart | The menu shows the picked microphone in use. |
| Idle, then F12, Network, drag the top edge up, F12 | Starts with no header address and no panel. F12 opens the panel on SIP log ("SIP listening on…") with the address in the header, and WebView2's DevTools stays shut. Network shows the sliders; the drag makes the panel taller and the stage shorter. F12 again closes it and hides the address. |
| Video call, caller with `--diagnostics --loss-audio 5` | The caller has Call quality (↑ MOS 3.8), a red dot on Network and the log following the call; the callee, panel closed, has neither. |
| Video call answered (`--video`, `--auto-answer`) | Each stage goes card, then their initial, then video; never the audio-only bars (the page logs each `stage:` change). |
| Click the header on B, then dial what was copied from A | The clipboard held `sip:192.168.1.123:5080`; the header showed "Copied"; A's call to it connected. |
| Softphone to softphone, audio call | Both stages show the audio-only bars and Add video. |
| Softphone to softphone, video call (`--video`) | Audio at 50 pkt/s each way; video at 30 fps each way, VP8. The first stats sample, at 5 s, already shows video. |
| Video call with `--camera-off` on the caller | The caller sees the callee's video at 30 fps and its self view says "Camera off". The callee gets the caller's placeholder at 5 fps (640×360 at first, 1280×720 once the encoder ramps up): the initial with an account, the icon without. |
| Same, then Camera on, then Camera off | The callee's video goes 5 fps placeholder, then 30 fps camera, then 5 fps placeholder, without a gap. The caller's camera opens and closes with it. |
| Two softphones on Video with the real camera, then the first switched to Audio | The second shows "Camera in use" and still hasn't opened the camera 8 s after the first closed it. Clicking Video opens it. |
| A softphone answers a video call while another has the real camera | It answers with "Camera in use" and sends the placeholder. 8 s after the other lets go it still hasn't opened the camera; Camera on opens it. |
| Two accounts a side (A: Alice, Carol, `--auto-answer`; B: Bob, Dave). B calls A from Bob, then from Dave, audio only. A adds video to Dave's call, then to Bob's, with Dave still B's selected account | B takes both ("is adding video: taking it"), though it placed them without video. A gets D on Dave's call and B on Bob's (the placeholder), and on whichever B has held, the hold card with that account's name. |
| Same, A: Camera ▾ picks a camera for Dave's call, then goes to Bob's and back | "camera for call 2: …"; Bob's call keeps its camera: each switch logs "camera closed, to switch cameras" and opens that call's. |
| Audio call, caller presses Add video (`--add-video 4`) | re-INVITE accepted; both relays rebuilt with VP8. The callee shows the caller's video at 30 fps; the caller gets the callee's placeholder at 5 fps, since the callee accepted with its camera off. |
| Three video calls in a row, the caller's camera already open from the preview (`--video --dial-count 3`, callee `--hangup-after 9`) | The callee shows the caller's video on every call. |
| Same, but the callee has `--no-video` | The callee answers `m=video 0`; the caller logs "they didn't take video", shows *No video*, and both stay on the audio bars. |
| Browser (SIP.js 0.16 in Edge, fake media) registers as `web`, dials `sip:desktop@localhost`; desktop `--auto-answer` | INVITE over `ws`, answered with an `RTCPeerConnection`; "WebRTC media (ICE, DTLS) connected" 0.2 s later. Each side shows the other's video; the browser receives 30 fps (640×360, then 1280×720) and audio with no loss. ↑ MOS 4.4. |
| Desktop dials `web` (with the old test registrar, since removed) | INVITE to the browser's `.invalid;transport=ws` Contact down its WebSocket; 200, ACK, media both ways. |
| Browser call, info bar's chevron (diagnostics) | "Media encrypted (SRTP); SIP not (WS)", their phone `SIP.js/0.16.0`. |
| Browser call, Text icon | Before the T.140 data channel: the popover said text isn't available on calls with browser phones. Now text is on there (rows above). |
| Two copies over UDP, caller `--webrtc --video`, callee `--auto-answer` | The offer is `UDP/TLS/RTP/SAVP`; the callee answers with an `RTCPeerConnection` too. ICE and DTLS connected in 0.3 s, video both ways (640×360), BYE: "You hung up" / "They hung up". The same without `--webrtc`: RTP/AVP and real-time text as before. |
| Local sipsorcery-servers2: desktop on a guest account (`Server: localhost`, UDP) and a browser on another, both ways | Browser → desktop: the INVITE arrives over UDP with a WebRTC offer and is answered with WebRTC media. Desktop `--webrtc` → browser: through the proxy to its WebSocket. Both connect in about 0.2 s with video both ways (see ../web for details). |
| Calling a stand-in callee that answers `488` + `Warning: 302 … "Incompatible transport protocol"`, then 486 | The first INVITE offers RTP/AVP; 0.2 s after the 488, a second offers `UDP/TLS/RTP/SAVP`. Ends with "486 Busy Here". |
| The same with `Warning: 305 … "Incompatible media format"` | One INVITE, no retry: "They can't take this call's media (488)". |
| A stand-in callee that answers a bare `488` (no Warning, no SDP) | The first INVITE offers RTP/AVP, the second `UDP/TLS/RTP/SAVP`: "they can't take plain RTP media (488, no reason given); calling again with WebRTC". |
| Calling the web softphone's guest account through the local sipsorcery-servers2, no `--webrtc` | The browser turns the RTP/AVP offer down with 488 in 60 ms, without ringing. The app server doesn't pass the Warning on yet, so no retry: "They can't take this call's media (488)". With `--webrtc`: connected, and the browser's BYE ends it "They hung up". |
| Browser call, Text panel open, then one click on End | The call ends ("You hung up", BYE, 200). |
| The web softphone (../web), audio only, `dial=desktop&hangup=12` | Answered with audio only; the browser's BYE ends it here with "They hung up". |
| Softphone to spike far end, H264 720p30 | 30 fps at 1280x720 in, decoded by `D3D11VideoDecoder` (hardware); camera out at 30 fps. |
| Address suggestions, two copies (`--profile vtC --port 5391 --ws-port 8391 --diagnostics` and `vtD --port 5392 --ws-port 8392 --auto-answer`, both `--fake-media`). C calls D (answered, 3 s), calls D and cancels at once, then D calls C and gives up while it rings | `/` in C (focus on the page) focuses the field without typing a slash and opens Recent: *Thisis · Missed · Just now* in red, then *VtD · Cancelled · Just now* (one entry per address, the newest call). |
| C: `vt`, Enter | VtD is highlighted as you type (`VT` matches too); Enter fills in `sip:vtD@127.0.0.1:5392` and places the call, which D answers; the list closes. `dax` matches nothing and the list closes. |
| C: the person button on a recent entry, then on the contact; ↑ from no selection | *Add Dana to contacts* writes `contacts-vtC.json`; with Dana's recent entry gone (a stand-in history), Dana is listed under Contacts, and typing `da` highlights it. ↑ wraps to the last entry; *Remove Dana from contacts* empties the file. `Esc` closes the list and leaves the typed text and focus. |
| C restarted (same profile) | The contact added before the restart is listed under Contacts. |
| The web softphone (`python -m http.server`, `web/index.html?server=ws://localhost:8391`), with a stand-in history entry | Same list and keyboard; *Add to contacts* saves to localStorage (`softphone.contacts`), and a `history-changed` with the entry gone moves it under Contacts. No console errors. |
| Contacts (then a dialog, now the Contacts tab), in C: ↑ from the empty field to *Contacts…*, Enter | It opens on the search box with the one contact. The rows to *Call on Desk D* below were checked in the dialog. |
| Edit VtD to *Desk D*; Add contact *Echo* with no address, then `SIP:vtD@127.0.0.1:5392`, then `sip:echo@127.0.0.1:5392` | The file has *Desk D*, and the suggestions show *Desk D* for VtD's recent call. "Type a number or SIP address." then "VtD already has that address." (case aside), then Echo is saved, sorted by name. |
| Echo's address changed to `sip:echo2@…`; search `zz`, then `ech`; Delete twice | One Echo, at the new address. "No contacts match “zz”."; `ech` shows Echo only. The first click turns the button red, *Delete?*; the second removes it from the file. |
| Add contact, `Esc`, `Esc` | The form closes with focus on the search box; then the dialog closes. |
| `sip:Grace@example.net` typed in the address field, ↑, Enter, name *Grace*, Enter | The list's only row is *Add sip:Grace@example.net to contacts…*; it opens the dialog's form with the address in and the name focused. Saved with its case kept; then `gr` in the field highlights Grace under Contacts. |
| Call on Desk D in the dialog | The dialog closes and the call goes to D, which answers; when it ends, the open list shows it under Recent at once (*Just now*). |
| C: People, then the *Contacts* tab; Desk D's row | Tabs *Recent* / *Contacts*, "2 contacts", focus in the contacts search. Desk D's row shows its last call and when; its details list its 5 calls with day and time, and *Show all 5 calls in Recent*. |
| *Show all … in Recent*; then on Recent, *Add to contacts* in a non-contact's details, `Esc`; the row's person button, name *Anon Tester*, Save | Recent, searched for `sip:vtD@…`, 4 rows. The form opens on Contacts with the address in and the name focused; `Esc` goes back to Recent with the details still open. After Save it's back on Recent, and the row shows *Anon Tester*. |
| `→` on the Recent tab; Edit *Anon Tester* to *Anon Two*; *Add contact* with `SIP:vtD@127.0.0.1:5392`; `Esc`, `Esc`; Delete twice | Contacts tab, focused. The form says *Edit Anon Tester*, saves on Contacts, focus back in search. "Desk D already has that address." `Esc` closes the form, then the view. Delete reads *Delete? Click again*, then removes it. |
| Address field: ↑, Enter on *Contacts…*; then `sip:zed@example.org`, ↑, Enter | The view opens on Contacts with focus in search; then on its form, *New contact*, with the address in. |
| Call on Desk D's row on the Contacts tab | The call goes to D, which answers, and the view closes; after it ends, Desk D's row shows *Outgoing · 0:03*, *Just now*. |
| vtA/vtB (two accounts a side): A opens People, calls Bob, B declines; A opens *Manage accounts*, calls Bob, B answers; A opens People, calls Dave over Bob, B answers | Declined: People stays open, the row says "486 BusyHere". From Accounts: Accounts open while calling; answered, it closes and Bob is selected (Video call). Over Bob: People open with the "Dave · Calling… · Cancel" banner over it, Bob on hold; answered, People closes and Dave is selected, Bob on hold. |
| C: the People button; *Contacts*; People again, and again; then *Manage accounts* (server icon), then People | (Before People always opened on Contacts.) People shows with history: it opens on Recent, pressed (accent, not Mute's red); it closes, then reopens on Contacts. *Manage accounts* swaps the view for Accounts, and People swaps it back. |
| Keypad, two copies (`--profile vtE --port 5195 --ws-port 8195` and `vtF --port 5196 --ws-port 8196 --auto-answer`, both `--fake-media --diagnostics`): E `--dtmf 159*0# --dial sip:vtf@127.0.0.1:5196`, F `--dtmf 42` | Both keypads open 2 s after the page's audio connects. E logs `DTMF '1' sent (RFC 4733, telephone-event/8000 pt 101)` for each key; F's `SIPUserAgent.OnDtmfTone` logs `DTMF '1' received (RFC 4733)` … `'#'`, all six in order, and E receives F's `4` and `2` the same way. |
| E places two calls to F (Call, then Call & hold), keypad open on the second, keys `1` and `#` | Both go as tones on call 2 (F logs them on its call 2); call 1 stays on hold and isn't selected, so `1` didn't pick it. |
| Same: `H`, `H`, Keypad, `Esc`, Keypad, a click on call 1's row, then `G` and `G` | Hold closes it and disables the button ("Keypad: resume the call to send tones"); resumed, it's enabled but closed. `Esc` closes it. Picking call 1 closes it. The grid closes and disables it ("Keypad: pick the call to open it in Focus"). |
| Same, a 1100×720 window with diagnostics open (a 308 px stage) | The popover fits the stage, the keys shortened to 36 px with their letters inside. |
| E calls a scripted SIP UAS whose answer is PCMU only (no `telephone-event`), `--dtmf 3#` | E: "their SDP has no telephone-event, sending SIP INFO"; the UAS gets two INFOs, `application/dtmf-relay` `Signal=3` and `Signal=#` with `Duration=160`, and its 200s are logged. |
| An INFO sent by hand into F's call (its Call-ID and tags): `application/dtmf-relay` `Signal=7`, `application/dtmf` `#`, `text/plain` | 200, 200 and 415. F logs `DTMF '7' received (SIP INFO)` and `'#'`. |
| The web softphone (headless Edge with fake media, `web/index.html?server=ws://127.0.0.1:8196&user=webdtmf`) calls F; keypad, `8`, `6`, `#` | The page's `RTCDTMFSender` sends them ("sent (RFC 4733, RTCDTMFSender)"); F receives all three on the WebRTC call (`DTMF '8' received (RFC 4733)` …). |
| Dock, two copies (`--profile vtI --port 5397 --ws-port 8397` and `vtJ --port 5398 --ws-port 8398 --auto-answer`, both `--fake-media --test-pattern`, built to a separate output folder), I calls J, 1100×720 | Keypad: it opens in a 300 px panel right of the call and the stage narrows from 816 to 504 px. Text (button): it opens below the keypad in the dock, input focused. |
| The same: a click on the stage; `5`, `#`; `Esc`, `Esc` | Neither closes on the click. `5` and `#` go as tones (J logs `DTMF '5' received (RFC 4733)`, `'#'`). The first `Esc` closes the keypad and leaves the text docked; the second closes the text, the dock goes and the stage is 816 px again. |
| Both open: `H`, `H`; Call & hold to J (a second call); `Esc`, `1` | Hold closes the keypad (button disabled, "Keypad: resume the call to send tones"); the text stays. The second call closes the keypad again (another call); the text stays, now that call's. |
| `G`; `T` | The grid hides the dock (the stage full width); `T` goes back to Focus with the text in the dock. |
| Both open at 820×640 and 1100×560 (CDP device metrics) | 820 wide: the dock floats over the stage's right edge (stage 536 px, no horizontal scroll), keypad at the top, text below it. 1100×560: beside the call, the keypad's keys at their smallest; the dock scrolls for the rest of the text panel. |
| The web softphone (headless Edge, fake media, `web/index.html?server=ws://127.0.0.1:8398&user=webdock`) calls J; Keypad, Text, `9` | The same dock (stage 504 px, both stacked); J logs `DTMF '9' received (RFC 4733)`. |
| Smarter address field, two copies (`--profile vtG --port 5395 --ws-port 8395`, `vtH --port 5396 --ws-port 8396`), G with accounts *G Work*, *G Home* and a disabled *G Old*, all to 127.0.0.1:5396; G's contacts file in the old format (no account): *Hannah Desk* (`hdesk@…`, one Busy call 1 h ago), *Bob Smith*, *Annabel* (`zz@example.org`); history: `hannah.lab@…` ×3 (G Home), `ohanna@…`, `hal@…` (missed) | The contacts load. Nothing typed: Recent and Contacts as before. `han`: *Hannah Desk* (contact, "Busy · 1 h ago", highlighted), *Hannah.lab*, *Ohanna*; no *Hal*. `ann`: *Annabel* first (word prefix), then the three `han…` (inside a word). `zz`: *Annabel* (user part). `sip:new@…`: only *Add … to contacts…*. |
| G: Contacts tab, Hannah Desk, Edit: *Call from* lists *Any…*, G Work, G Home, *G Old (disabled)*; G Home, Save | Details say *Call from G Home*; the file has `"account": "g2"`, the others none. After restarting G it's still there. |
| G on *G Work*: `hann`, Enter | *Hannah Desk · from G Home* highlighted; *Call from* switches to G Home (flash) before the INVITE; H logs `ringing G Home <sip:ghome@127.0.0.1:5396>`. |
| G back on *G Work*: `hannah.l`, Enter (a recent call on G Home, not a contact) | *Call from* switches to G Home; H rings from `ghome`. |
| G on *G Work*: Recent, *Hannah.lab*'s person button | The Contacts form opens with the address and *Call from* G Home filled in. Saved as *Hannah Lab* on *G Old* (disabled). |
| G: `sip:hannah.lab@127.0.0.1:5396` pasted, Call | *Call from* stays on G Work, with "G Old is disabled: calling from G Work" under it; H rings from `gwork`. |
| G on *G Work*: the call button on Hannah Desk's Contacts row | *Call from* switches to G Home and the view stays open while it rings; H rings from `ghome`. |
| G: Annabel given an account id no account has | Details: "A removed account (calls use Call from's)"; the form shows *A removed account* selected. Calling it leaves *Call from* alone with "Annabel's account has been removed: calling from G Work". |
| G (`--diagnostics`) calls Hannah Desk, H answers; G opens the call's details | The stage's info overlay reads `sip:hdesk@… ✓ Hannah Desk 00:03`, the mark and name in light green beside the address (not cut off by its ellipsis); the details add *Contact: Saved contact: Hannah Desk* under *Call*. H's overlay shows *Gee Home*. |
| Gravatars: G gets a contact and an incoming history entry for `sip:beau@dentedreality.com.au` (Gravatar's documentation example, which has a picture) | Its avatar is the picture (160 px from gravatar.com) in the sidebar's History, the `b` suggestions and the Contacts tab; *Bob Smith* (`bob@example.net`, no Gravatar) and *Annabel* keep their initials, and the `127.0.0.1` addresses aren't looked up. |
| Settings: *Show Gravatar pictures for people* off, then on | Off: every picture goes, initials only. On: the picture is back (from the cache, no new lookup). |
| Fallback changed from `d=404` to `d=identicon` (the row above Settings was before it). C: People > Contacts and the sidebar's History | *Grace* (`sip:Grace@example.net`, no Gravatar) and the `thisis@anonymous.invalid` caller show their identicons in place of the initial; *Desk D* (`127.0.0.1`) keeps its initial. |
| An avatar built in code, as call rows, tiles and the stage are: given the address, redrawn with the same initial, then given an IP address | The picture appears, survives the redraw, and goes for the IP address. `emailOf`: `sip:alice@Example.com:5060;transport=tcp` → `alice@example.com`, `Bob <sips:bob@example.net>` → `bob@example.net`; `tel:+35312345`, `sip:1234@10.0.0.1`, `sip:x@localhost` → none. A live G→H call still shows *H* on the stage and its row. |
| Saved-contact mark, vtG/vtH as above (H has *Gee Home* = `sip:ghome@127.0.0.1:5396`): G's sidebar History, Recent tab, and Hannah Desk's details | Every row to Hannah Desk or Hannah Lab has the mark (tooltip *Saved contact: Hannah Desk*); the details add *Contact: Saved contact: Hannah Desk*. |
| G's address field: `SIP:HDESK@127.0.0.1:5396`, `hdesk@127.0.0.1:5396`, `sip:new@127.0.0.1:5396`; then `han`, then empty | The first two show the *Hannah Desk* pill in the field; the new address shows none. Suggestions: the contacts have the mark (*Hannah Lab*, *Hannah Desk*), *Ohanna* and *Hal* don't. |
| G calls Hannah Desk; H answers | G: the Active calls row and the *Calling…* card have the mark, then the info bar once connected. H: the ringing row and card, then the row and info bar, show *Saved contact: Gee Home* for `G Home <sip:ghome@…>`. |
| The web softphone (`web/index.html?api=stub`, contacts in localStorage): `ann`; a new contact's form | *Annabel*, then *Hannah Desk*, ranked as on the desktop; no *Call from* in the form or the dial bar; no console errors. |
| Clear (×), vtG (`--port 5395 --ws-port 8395`): type `han`, click ×; type `sip:hdesk@127.0.0.1:5396`, click ×; type it again, `Esc`, `Esc`; set the address in code and blur | `han`: × shows, *Best matches* (3); × empties the field, focus stays, the list shows *Recent* and *Contacts* (8). The contact's address: the *Hannah Desk* pill just left of the × (pill 621–729 px, × 733–759), the text stops before both (padding 198 px); × clears it the same way. First `Esc` closes the list, the second empties the field with the list still closed. Set in code: pill and × show. Web build (`?api=stub`): × clears and reopens the list, no console errors. |
| Transfer, blind: three copies (`--profile vtM --port 5431 --ws-port 8431`, `vtN` 5432/8432, `vtO` 5433/8433, all `--fake-media`, no accounts); M calls `sip:vtN@127.0.0.1:5432`, N answers; M: Transfer, `sip:vtO@127.0.0.1:5433`, Transfer | M: the call held, "VtO is ringing…" on the note and "Transferring to VtO…" in the list; N: "Thisis is transferring you to VtO…"; O rings. O answers: M's call ends *Transferred to VtO*; N's call is now with VtO (its video arrives), "Thisis transferred you to VtO"; history: M *Transferred to VtO* (Outgoing), N *Transferred to VtO* (Incoming). |
| Transfer, blind, failing: N (with O) transfers to `dave@nowhere.invalid` | After the SIP timeout (32 s): N's note in red "Couldn't transfer: nothing answered at Dave's address (408 Request Timeout). The call is still on hold." with Resume and Try again, the row "Transfer failed · on hold"; O: "… tried to transfer you to Dave, but nothing answered at Dave's address (408 Request Timeout). You're still with …", and the call carries on. Resume takes the call off hold and clears the note. |
| Transfer, attended: M calls N, then O (N held); on O's call, Transfer, *Join* VtN | M: both calls end, *Transferred to VtN* and *Transferred to VtO*, "VtO and VtN are talking". O (REFER with Replaces) calls N; N (INVITE with Replaces) takes it over in place: "Thisis handed you over to VtO", the call now with VtO, its video arriving. O's call is now with `127.0.0.1:5432` (see Known limits). Each history has the transferred call. |
| Web softphone (`web/index.html?server=ws://127.0.0.1:8431`) on a call with vtM | No Transfer button, and no console errors. |
| After a blind transfer (M sends N to O), and an attended one (M joins N and O), 25 s on each | No "isn't getting your audio" on O, or on either side of the attended one: N and O each log `RTCP: dropped the reports on the transferor's media`, and O's Call quality has N's reports on its audio (0% loss). Before, N's RTCP went on reporting the transferor's stream, and the target warned that its audio wasn't reaching N. |
| Contacts first, vtM with contacts *Olivia Vt* (`sip:vtO@127.0.0.1:5433`, account *Office*), *Alice Smith*, *Bob Builder*, *Dave Example*, and recent calls to VtN, VtO and `web` | Focus the empty field: *Contacts* (accent header) with the four, Olivia first (the one called), larger rows with bold names and Gravatars, *· from Office* on Olivia's; then *Recent*: VtN and web (VtO is Olivia, already listed); *Contacts…*. `v`: *Contacts* Olivia Vt (highlighted), Dave Example; *Recent* VtN. `web`: *Recent* web only. `ali`: Alice. `sip:x@y.z`: only *Add … to contacts…*. `zzz`: the list closes. `vtn`, `Enter`: VtN's address in the field; Call calls it. |
| Transfer's field, vtM (accounts *Office*, 127.0.0.1:5433, and *Lab*) on a call with vtN placed from Office | The popover: *Contacts* and *Recent* as on the address bar, without VtN, and no account picker. `oli`: Olivia highlighted, ↓ stays on it; × clears and lists both groups; pasting `sip:vtO@127.0.0.1:5433` shows the *Olivia Vt* pill. `7000`, Transfer: `REFER … to sip:7000@127.0.0.1:5433` (the call's account's server); O answers, M's call ends *Transferred to 7000*, N is with O. Attended *Join* below the field still works (both calls end). |
| Web softphone (`web/index.html?server=ws://127.0.0.1:8431`) after this change | The address bar lists *Contacts…* (no contacts there), no Transfer, and no new console errors. |
| Attended transfer, then hang up, video on all three: N calls M, M calls O, on O's call M *Join*s N (so N is taken over in a call it placed). Then O hangs up; again, and N hangs up; again with M placing both calls, and O hangs up | After the join, both ways: N and O each show video ~30 fps and *audio in 50 pkt/s*; N logs `relay: their audio is from a new source (…): carrying on…`. Before: N "no audio arriving", 0 pkt/s. O hangs up: N's call ends *They hung up* (before: BYE answered 200, the call stayed up). N hangs up: its BYE goes to O (`BYE sip:Anonymous@127.0.0.1:5433`), both end. M placing both: audio both ways, and O's hang-up ends N's call. `SIPUserAgent` unit (31) and integration (20) tests pass. |
| Transfer note on a video call: M calls N (video), transfers to O; O answers. Again, and O declines | The note is a small dark pill at the top right, over the black bar above the video, not the middle of it: M "Olivia Vt is ringing…", N "VtM is transferring you to VtO…", then on N "VtM transferred you to VtO" in light green, with ×. Declined: M's "Couldn't transfer: Olivia Vt is busy (486 BusyHere). The call is still on hold." in soft red, wrapping within the pill, with Resume, Try again and ×, clear of the call info at the top left. |
| Picking doesn't dial: vtM, address bar `vtn`, `Enter`; a click on *Olivia Vt*; then Call. On a call with N, Transfer: `oli`, `Enter`; ×, a click on the first row; then Transfer | Address bar: `Enter` puts `sip:vtN@127.0.0.1:5432` in the field, the list closes, focus stays, no call; a click puts Olivia's address in, no call; **Call** places it. Transfer: `Enter` puts `sip:vtO@127.0.0.1:5433` in the field, the popover stays open, nothing highlighted, no transfer; the click the same; **Transfer** sends it (`transfer to sip:vtO@…, blind`), and the call ends *Transferred to Olivia Vt*. |

Not tested yet: the saved-contact mark on grid tiles (a second call wasn't answered in that
run), the one-way audio warnings (there's no easy way to break one direction yet),
the placeholder on a non-SIPSorcery far end, a browser on another machine (`wss`), adding video to a browser call, the far end adding video to our audio call
(only SIPSorcery to SIPSorcery so far, where we're both), a real SIP server with registration, a hardware desk phone or another softphone
brand, calls across NAT, or the real camera and microphone in this app (the spike covered the
camera).

## Things learned building it

- **A negotiated data channel opens whether or not the far end has one.** With `negotiated`
  and an `id` there's no DCEP open and no ack: SIPSorcery (and browsers) call the channel open
  as soon as SCTP is up. So real-time text on a WebRTC call counts as on only when their SDP has
  the matching `a=dcmap`; the channel opening isn't enough. (A `--no-text` callee showed it:
  its answer had `m=application` without a dcmap, and the caller's channel opened anyway.)

- **Each end of a SIP call may send a different codec.** In the softphone test, A sent Opus and
  B sent G722, both from the list the two sides had agreed. So the page's connection offers every
  audio codec both SIP ends support, not just the one we send. Otherwise the page drops the
  other side's packets.
- **Keyframe requests need both ends to agree on them, and to get through.** Two library
  fixes came out of this prototype:
  - SIPSorcery's SDP offered no keyframe feedback, only `transport-cc`. With no `nack pli`, the
    browser never asked for a keyframe, and incoming video took about 20 seconds to appear.
    Fixed in #1883, which offers `nack pli` and `ccm fir` on both the SIP side and the page
    offer. The relay used to add these lines to the page offer itself.
  - SIPSorcery dropped any RTCP packet of 12 bytes or less, and a PLI on its own is exactly 12.
    So between two SIPSorcery ends no keyframe request ever arrived, and a page that missed the
    first keyframe never showed video. Browsers send PLIs inside a compound packet, so they
    weren't affected. Fixed in #1884. The relay used to pad each PLI with an SDES packet.
  The PLI's media SSRC is the one learned from received RTP, since SIPSorcery routes feedback
  by it.
- **A failed step stalled the camera queue for good.** `syncCamera` chains each open and close
  on one promise, so they never overlap, and a throw rejected it, which skipped every later
  step. With the test pattern chosen, video was wanted before the microphone (and so `local`)
  existed. The chain now catches, and nothing opens until `local` is set.
- **A canvas only produces a frame after it's drawn on.** The placeholder track is
  `captureStream(0)` with `requestFrame()` on a timer. Requesting frames of an unchanged canvas
  sent nothing, 0 fps, so the picture is redrawn before every request.
- **SIPSorcery hung up calls 30 s in because of the idle video stream.** It ends a call when a
  stream has had no RTP or RTCP for 30 s. Our sessions carry a video stream so video can be
  added, and on an audio call (or after the far end refuses video) nothing ever arrives on it.
  So the callee hung up about 30 s in, and both ends logged "They hung up". The pair tests were
  all shorter than that, so they never caught it. Fixed in #1885: RTCP doesn't start on a stream
  that wasn't negotiated, and a video timeout no longer hangs up a call while audio flows.
- **Don't insist that SIP over UDP and TCP share a port.** The fallback used to ask the OS for
  a UDP port and then open TCP on the same number. Windows hands out UDP ports sequentially,
  and blocks of about 100 ports are reserved for TCP (by Hyper-V and WSL:
  `netsh int ipv4 show excludedportrange protocol=tcp`). With the counter in such a block,
  every retry got a neighbouring port TCP couldn't bind, and a second softphone failed to start
  until the counter moved on. Each channel advertises its own port in Via and Contact, so now
  each takes an OS-assigned port. TCP tries UDP's number first, but takes its own if that's
  reserved.
- **SIPSorcery makes a video stream for any `m=video` it's offered**, with an inactive local
  track, even when we have no video codecs. So "the call has video" has to mean our local video
  track is active and their video port isn't 0, not just `HasVideo`.
- **Adding video rebuilds the relay.** The page connection is torn down and offered again with
  a video line. The old relay's handlers come off the SIP session first, or both would forward
  media.
- **`requestVideoFrameCallback` never fires on a hidden `<video>`**, so it can't tell whether
  frames are arriving while the stage shows something else. The page uses `framesDecoded` from
  `getStats` instead.
- **SIPSorcery timed received audio with the wrong clock when the two ends send different
  codecs.** Its RTCP session measures arrival times with the clock of the first codec offered
  (Opus, 48 kHz). With G722 arriving (an 8 kHz RTP clock), each 20 ms packet looked 800 units
  early or late, and the jitter we reported read a steady 17 or 100 ms on a loopback call.
  Fixed in #1887: each packet is timed at its own payload type's clock.
- **A bundled WebRTC leg reports on both streams under either kind.** With audio and video on
  one transport, SIPSorcery may raise an RTCP report for video under `audio` and the other way
  round, so "is our audio reaching them?" said no on browser calls. The relay now picks report
  blocks and PLIs by our SSRCs, not by the kind they arrived under.
- **`SIPUserAgent.Hangup()` raises `OnCallHungup` itself**, before it returns. Our handler for
  that records "They hung up", so every call we ended was logged as theirs. Hang up now marks the
  call as ours before calling it, and the handler ignores that call. (Ending the call here first
  doesn't work: it closes the media session, and the user agent only sends BYE while the call
  is still up.)
- **`--auto-answer` could answer twice.** It answered on every state change while ringing, and
  a second answer made its own media session, failed and ended the call ("Couldn't answer")
  just as the first connected. It answers once on starting to ring now, and `AnswerAsync`
  ignores a second answer while one is under way (a double click would have done the same).
- **A WebRTC call we place that fails raises `OnCallHungup`.** Closing the `RTCPeerConnection`
  makes `SIPUserAgent` hang up, so a 486 showed as "They hung up" (and so did the retry after a
  488, when the first attempt's media closed). "They hung up" now only counts for the current
  call once it's connected; before that, the call's failure says why.
- **A WebRTC answer can't add a stream the offer didn't have.** Answering a browser's audio-only
  offer, `RTCPeerConnection` put our video in as an extra m-line and the browser rejected the
  answer. Browser calls are answered with video only if it was offered.
- **The text popover closed on pointerdown, which ate the click.** Closing it re-renders the
  controls, so a click on End with the popover open only closed it. It closes on click now.
- **The page caps its camera at 1.5 Mbit/s.** Its connection to the host is local, so its own
  bandwidth estimate would grow without limit. The real fix is to pass the far end's RTCP
  feedback through to the page.
