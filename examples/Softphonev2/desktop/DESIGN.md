# Softphone v2 desktop – how it works

How each part of the desktop softphone behaves, and why, then what was learned building it.
[README.md](README.md) says what it is and how to run it; [TESTING.md](TESTING.md) has what's
been checked.

## Window size

The page reacts to the window's size as the web softphone does to the browser's. At 960 px and
under the dial bar can go into rows (People, Manage accounts, Call from and the address, then
Call and Video call together on the next row, sharing it: they always move as a pair, never one
left beside the address), so the address keeps room to type in; at 860 px and under the
control bar's buttons are icons, before a call's Mute and Stop video would be cut short, laid
out as with their words (the call's buttons together in the middle, the rest at the right end,
not spread across the bar); the
calls list goes to a rail under 700 px; and at 500 px and under it's the web softphone's phone
layout (see the web README's "On a phone"): the address on a row of its own, and taller buttons.
There the control bar is Mute ▾, Camera ▾, Hold, End and a **⋯ More**, centred together: eight
buttons won't fit in 360 px without being squashed, so Transfer, Keypad, Text and Self view move
into More's menu. Each item does what its button does (and shows its state: greyed when the
button's disabled, ticked when on); More is accent-edged while a panel it opened is open, and has
Text's red dot for a new message. The window can't get smaller
than a 360 × 560 page, the narrowest phone that layout is made for. That's in CSS pixels, so the
minimum is the same page at any display scaling, and it's set again when the window moves to a
monitor with other scaling (`MainForm.MinPageSize`). Narrow, the Accounts view puts this PC's
direct address under its title, the Diagnostics bar has a ⋯ too (the open tab always on the
bar, the others as fit, the rest in its menu, with Copy log and Clear log on the SIP log; a tab's
red dot, Network's with loss on, shows on the ⋯ while it's in there), and the address field's
direct-mode hint drops its example (it's in the tooltip).

## The app icon

The SIPSorcery sorcerer on the phone, in the logo's lime on black: the same mark in the window's title bar,
the taskbar and Explorer (`softphone.ico`, the `ApplicationIcon`, and embedded for `MainForm`'s `Icon`) and at the
top left of the page (inline in `wwwroot/index.html`). `softphone.svg` is its source, on a 48-unit grid. Each size
in the `.ico` (16, 20, 24, 32, 40, 48, 64 and 256 px) is drawn on its own, with the gaps between the hat, brim,
face, robe and handset about a pixel wide at that size, so the parts stay apart at 16 px. The header copy is
drawn for 28 px. The tile is the same in light and dark.

## Call and Video call

Beside the address, two buttons, so the kind of call is the button you press, as on a phone's
contact card: **Call** (Enter) leaves video out of the offer, and video can be added once it
connects; **Video call** (Ctrl+Enter) offers it, with your camera on from the start (privacy
mode sends your camera-off card instead). There's no setting for it: the button you press says
it, and nothing carries from one call to the next. On a call, both say
*& hold current*. A call ringing in has **Answer** (you see them, your camera stays closed) and,
when they offered video, **Answer with video**; the banner for a call ringing while you're on
another has Answer and a camera icon for Answer with video.

Every list of people has the same three buttons on each row: Home's Recent, the dial view's New
call, and People's Recent and Contacts. **Copy address** puts the address on the clipboard (with a
*Copied* notice), to paste into the address bar or elsewhere and call it some other way, with
another account say; **Call** and **Video call** place the call straight away. In People the
details also have Call (or Call back), Video call and Copy address.

While a call you placed is ringing, its card shows [Your devices](#your-devices) read-only: the
microphone and speaker it's using, and the camera (its name and *opens when they answer*, *Off:
your camera-off card*, or *Not used: it's an audio call*). They can't be changed there; once it's
answered the ▾ on Mute and Camera change them.

## Home and the dial view

With no call on the stage, it's **Home**: your recent calls on the left (missed ones red, the
unseen ones bold, a call that's just ended highlighted for a moment), each with Copy address,
**Edit contact** (or **Add to contacts**, as in History: the Contacts form, its name the one the
row shows, the call's or else one from the address, and Save or Cancel comes back to Home), Call and Video call (at phone width Copy address goes, for the name's room;
People has it); on the right your accounts (each with its registration, Retry when it
failed, and two icons: Copy address, its `sip:` address on the clipboard, and Edit, straight to
its form in Accounts; then the address direct calls reach you at, with its own Copy; the web
softphone's one account has Copy only), [Your devices](#your-devices) and, while it's on, privacy
mode with Turn off. A call that ends with no other call going (hung up, declined, busy) shows
how it ended for 3 s, then Home; its **Close** button (or Esc) goes to Home straight away. Esc
from the address, where a call that's ended leaves you, empties it first if it still has the
address you called (kept for a retry), then closes.

Focusing the address (or `/`) turns Home into the **dial view**: one column, Your devices at the
top, then *New call*, your contacts and the addresses of recent calls. Picking one fills the
address bar and Call from (the field flashes) and calls no one: Call or Video call places it.
Each row also has its own Copy address, **Call** and **Video call**, as Recent's rows do; the
calls go straight away (from the contact's account, or the one it was last called from). Esc
empties the field and puts Call from back; Esc again, Back to Home, or (with the field empty)
clicking or tabbing to anything outside the dial bar and the dial view, leaves. Your devices'
buttons and pickers are part of the dial view, so using them keeps it. The drop-down under
the address only opens once you type, since the dial view is already the list. During a call,
focusing the address opens the drop-down as before.

## Your devices

On Home, the dial view and under a call ringing on the stage (and read-only under one you're
placing): three lines, the microphone, speaker
and camera the next call (or the one ringing) will use, each with a hint when it's not plain
(*on only if you answer with video*, *off (privacy mode)*, and *for now* on one changed from
your default). Clicking a line opens the panel with that
device's test going: the camera's line starts a preview (only you see it), the microphone's a
level meter; the speaker's, and *Change or test*, start nothing. Open, each device has its picker,
what the call will do with it, and a test: Test mic, Play a sound (on the speaker picked) and
Preview. In diagnostics mode the camera's list has the test pattern and audio bars too, as the
Camera ▾ does: picked, the preview shows it and the next video call sends it. Picking another
camera or source switches the preview straight away. *Done* folds it and stops the tests; so does placing or answering a call, which takes the
devices over. The camera's list ends with **Camera off: send my camera-off card**: the next
video call (Video call or Answer with video) starts as if you'd pressed Stop video, so they see
your camera-off card until you press Start video. Preview then shows that card, as they'll see
it (the selected account's picture or initial), and the camera stays closed. It's on
demand like a device: once the last call ends, video calls open the camera again. Settings has
your **default devices**, kept between runs; every call you place or
answer starts with them. A device picked here, or from the ▾ on Mute and Camera, is on demand:
it's used from then on, for the call that's coming and the calls going, until the last call
ends, and then it's your defaults again. Nothing picked here is remembered. Open, with a device
changed, the panel says so, with *Use my defaults* to go back now. Whether the camera is on is
the call's.

## The indicator

Left of privacy mode, only while your microphone or camera is open: nothing shows while both are
closed, so there's nothing to work out. A microphone icon with a green dot is live (they hear you,
or it's your mic test), amber is open but muted or on hold; a camera icon is open (they see you,
or it's your preview). A click says why each is open, the device, and when each opened and closed
(the last eight).

## The control bar

Under the stage, from the UX prototype (`prototype/web`), once a call's been answered (connected,
or on hold) and until the last one ends. Before that, while Home shows, a call is being placed
or one is ringing in, there's no bar and the stage has its room: Your devices is there instead.
Mute and the camera button are for the call you're on, and only that call; their ▾ menus pick
the device:

- **Mic ▾**: Mute or Unmute (the label says what pressing it does, as on the camera button;
  soft red while muted, with a red *You're muted* pill by the call timer). Every call starts
  unmuted. The ▾ lists microphones and speakers.
- **Camera ▾**: on a video call, Stop video or Start video (what pressing it does, whatever
  the source: the camera, or in diagnostics the test pattern; soft red while stopped, when
  the placeholder is sent); on an audio call, Add video. Disabled before a call (Your devices
  has the preview) and when video can't be had (No camera, No video, said on the button). The
  ▾ lists cameras, in diagnostics mode the test pattern, and on a video call *Camera off: send
  my camera-off card* (ticked while your camera's off: picking it is Stop video, picking a camera
  is Start video with that camera).
- **Text** (an icon, at the right beside Self view, since it's rarely used): opens the
  real-time text panel in the dock (T does too). During a call only.
- **Keypad** (an icon, beside Text): opens the DTMF keypad for the selected call (see
  [Keypad](#keypad-dtmf)). During a call only, in Focus view.
- **Hold** (H): puts the selected call on hold, or Resume takes it off (see
  [Several calls](#several-calls)). During a call only.
- **End**: ends a connected call. A call you're placing is cancelled with **Cancel** on its card,
  under *Calling…*, as a call ringing in is declined from its card.

A device you pick takes effect at once, mid-call too, by swapping the track on the page's
connection (no renegotiation). It's for now: once the last call ends, the next starts with
Settings' default devices again (kept between runs, per profile). A default that's gone falls
back to the system default.

## Privacy mode

At the top right, just left of Settings: the incognito sign (a hat over round glasses), as a
browser marks a private window, so it reads as a mode the whole app goes into. It's a hard off.
One click (or `Shift+M`) turns it on: the microphone and camera close, every call gets silence
and on video your camera-off card, and calls you place or answer while it's on start the same
way (still `sendrecv`, sending silence, so turning it off needs no renegotiation). The button
becomes a chip in the text colour (dark in the light theme, light in the dark) with the sign and
*Privacy mode*; on a connected call a note under the header says what they get (*Bob hears
silence and sees your camera-off card*) with Turn off, and Home has a card saying so.

While it's on, Mute, the camera button and Your devices' Test mic and Preview are disabled, and
Video call or Answer with video send your camera-off card, so nothing turns it off behind your
back: only its own button (or
`Shift+M`, or Turn off) does. Off again, the microphone opens for the calls, and each call goes
back to how it was before (a call that started meanwhile, unmuted with its camera off: it never
turns a camera on unasked), so a call you'd muted stays muted. There's no second click to
confirm: turning it on only protects you, and turning it off loses nothing. It's never
remembered, so the app always starts with it off. It's always there, with or without a call, so
you can switch it on before answering.

## Names, numbers and SIP addresses

With an account, a call goes to the account's server, which works out where it goes from the name
or number: `alice` is dialled as `sip:alice@<the account's domain>`. The address bar's hint is
*Name, number or SIP address* (*Name or number* at phone width), and **nothing typed is
refused**. A pasted or dropped address on the account's domain goes in as its user part
(`sip:alice@sipsorcery.com` → `alice`), and one typed there stays with the account (the domain is
compared without its port). A **full SIP address on another domain** (`bob@example.com`, or
anything starting `sip:`) is a direct call: on the desktop, *Call from* switches to **No account
(direct)** as the address becomes full, with the picker's flash and a log line, as a contact set to
call directly switches it, and back to the account if you take the `@…` out again (or Esc or ×
empties the field). It only follows the change: picking an account in Call from afterwards is
yours to keep, and a row picked from a list keeps the account it came with. After the call, Call
from stays direct, as after a direct contact. The web softphone has no direct calls (no account
list, always its account), so it takes the address, keeps Call enabled, and after a pause the
field's bubble says *The web softphone can't call an address directly: this call goes through
sipsorcery.com, which may not reach it.*; the call goes to its server as typed. The field's
tooltip says which happens. Transfer's field takes anything too (the transfer goes through the
call's account). Addresses on the account's domain show as their user part everywhere a call or suggestion
shows its address (the stage, the call header, the suggestions' second line, its full address in
the tooltip); one elsewhere shows as `bob@example.com`, without `sip:`. A saved contact or recent
call elsewhere still goes into the field whole when it's picked: it was saved, so it stays
callable. Contacts and history keep full addresses, and Copy address and Call details give them.

**No account (direct)** is the mode for full SIP addresses: there's no server, so the address
says where the call goes (`sip:alice@192.168.0.20`, or `192.168.0.20:5060`). The hint says so
(*Name, number or SIP address, e.g. sip:alice@192.168.0.20*), anything can be typed, and addresses
show in full. The mode follows *Call from*. The web softphone always has its account.

**Contacts follow the same rule, by the contact's own *Call from*** (or, for *Any*, the one in Call
from now). With an account the form's field is *Name or number*, and nothing typed is refused
(a pasted address on that server goes in as its user part). A name or number is saved as that
user at the account's server (`bob` → `sip:bob@sipsorcery.com`), so calls and history still match
it. A full address on another server switches the form's Call from to *No account (direct)* as
it's typed (the picker flashes), and back if the `@…` comes out, as the address bar does; pick an
account again afterwards and the contact is saved with it, the full address as typed (`sip:`
added), for the account's server to reach. The web softphone (no Call from) keeps it too, with
the field's bubble saying *The web softphone can't call an address directly: this contact's calls
go through sipsorcery.com, which may not reach it.*, and saves it. Something that's neither a name
nor a full address (`bob@`) gets *That isn't a SIP address* on Save. A line under the fields says where its calls go: *Calls go to bob@sipsorcery.com
through Office's server* (with *Any*, that it's whichever account Call from has when you call).
With *No account (direct)* the field is *Number or SIP address* and takes a full one, as typed.
Editing shows the address as its user part on the contact's server; changing Call from re-reads
it. One on another server opens as it is, and doesn't switch Call from unless you retype it. The list shows each contact's address as
calls do (`bob`); its details keep the full address. The address bar, Transfer and the contact
form are `novalidate`, so a bubble only explains (the web's note) and never blocks Call or Save.

## Recent calls and contacts

The address field's suggestions, from the UX prototype (its *New call*): focusing the field (or
`/` anywhere outside a text field) lists contacts and recent calls, and typing filters and ranks
them (below). Transfer's *To an address* field is the same component (`addressField` in
`address.js`: the same lists, rows, keys, mark and ×; see Transfer). The address bar's rows are
just who and when: no Add/Remove contact button or Call icon (a row fills the field; the bar's
Call and Video call place it, and People and the dial view's rows have their own buttons).
Transfer's keep theirs. Typing part of a name highlights the first match. ↑/↓ move through the list (and open it),
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
  call uses (or to *No account (direct)*, for a contact set to it). A recent call picked here
  goes from the account that call used. If the account has
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
  Contacts rows, Active calls rows, grid tiles and the stage, and your own on the camera-off card.
  `sip:alice@example.com` is looked up as `alice@example.com` (SHA-256, at
  `gravatar.com/avatar/<hash>?d=identicon`), once per run: their picture, or without one
  Gravatar's identicon for that address (the same pattern each time); the initial stays only if
  gravatar.com can't be reached. Addresses
  with no user part, an IP address or dotless host (`localhost`) and `tel:` numbers aren't
  looked up. Settings > Appearance > *Show Gravatar pictures for people* (on by default) turns it
  off, since it sends gravatar.com a hash of each address shown (and of your account's).
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
history.

- **Recent** is the call history. Each row has a person button beside the chevron, and its
  details have a matching button: *Add to contacts* or, for a contact, *Edit contact*. Both open
  the Contacts tab's form (address filled in), and saving or cancelling comes back to Recent.
  History shows the contact's name in place of the one the call carried.
- **Contacts** (`contacts.js`) is laid out like Recent: search, *Add contact*, and a row per
  contact with its last call and when, a chevron and a call button. A row's details have the
  address, its last 5 calls, **Call**, **Edit**, *Show all N calls in Recent* (Recent, searched
  for that address) and **Delete**, which asks again on the button for 3 s. Add and Edit share a
  form at the top (name, number or SIP address, and *Call from*: *Any (the current Call from
  account)*, one of the accounts, a disabled one marked so, or *No account (direct)*, for a
  contact always called directly, such as a phone on the LAN); a blank address, or one another
  contact has, is refused there. The details show *Call from* too, and say when its account has
  been removed or disabled. *Add to contacts* from a call on Recent fills in the account that
  call used. The account is kept in `contacts-{profile}.json` as `account` (the account's id,
  or `direct`); files from before it have none, which means *Any*. The web softphone has no accounts, so it
  shows no *Call from* there. Changing a contact's address replaces it. `Esc` closes the form, then clears the
  search, then closes the view.
- **Calling from here** (Call, Call back, or the dial bar with People open) closes the view
  straight away, so the stage shows the call ringing out and how it ends (or the "Calling… ·
  Cancel" banner, over another call). With *Manage accounts* open, the call leaves you there
  until it's answered, then it closes (an unsaved edit is dropped) and the call is selected.
- Without a host that keeps history there's no view, so no Contacts tab either; contacts still
  come up in the address field's list.

## Settings

The gear at the right of the top bar opens Settings (`wwwroot/settings.js`), a dialog as in the
UX prototype. Esc, its ×, or a click outside closes it. Only options with something behind them
in this app are there, and none that repeats a button you already have; each applies straight
away:

- **Audio & video: default devices.** Default microphone, speaker and camera: what every call
  you place or answer starts with, kept between runs. *System default* follows Windows. With no
  call going, a default picked here is what's in use too; during a call it's for the calls after
  (*The call you're on keeps the devices it has*). A device for one call is picked on demand,
  from Your devices or the ▾ menus, and lasts until the last call ends (see
  [Your devices](#your-devices)). Device names show once the microphone or camera has been used
  (Your devices' Test mic or Preview does it). *Default camera* can also be **Camera off: send
  my camera-off card** (`cameraOff` in the page's settings): video calls you place or answer with
  video then start with your camera-off card until Start video, every time, and Your devices
  shows *Off · your camera-off card* (with no *for now*: it's the default). Otherwise Call or
  Video call, Answer or Answer with video say it, and privacy mode is the hard off.
  `--camera-off` (testing) still starts video calls with the camera off. Nothing else carries from one call to the next: Mute and the
  camera button change only the call they're on. The self view is the
  Self view button's (V), remembered. The grid is the Grid button's (and G's) choice, remembered (see
  [Focus and grid views](#focus-and-grid-views)). Accounts are the dial bar's Call from and
  accounts (server) button (see [SIP accounts](#sip-accounts)). Calls are answered automatically
  only with `--auto-answer`, a testing switch, as Video call would start.
- **Appearance.** Theme: System, Light or Dark. System follows Windows' light or dark mode.
  *Show Gravatar pictures for people*: kept, though it has no button, since it decides whether
  hashes of addresses go to gravatar.com.
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
- **New call.** **Call** is enabled once there's something in the address field (greyed, with
  "Type a number or address to call", while it's empty or only spaces), kept in step as you
  type, clear it with ×, pick a suggestion or paste. On a call, the button says what it will do:
  *Call & hold current*, the same words as a ringing call's *Answer & hold current* (its
  tooltip names who: "Call, and put Alice on hold"; under 1000 px it's just *Call* and
  *Video call*, so both fit beside the address before they move to a row of their own); pressing it holds that call, which stays on the stage, and the new one
  isn't a call in the list yet: a banner over the stage says "Dave · Calling… · Cancel". Only
  once Dave answers does it join the list and take the stage (in Focus). Not answered (busy,
  declined, no such number), the banner says why until the call goes (or *Close*), and the call
  you were on stays on hold: you may want to call someone else, so resuming it is up to you.
  *Cancel* just takes the banner away. *End all*
  leaves a call you're placing to the banner. **One unanswered call at a time**: while a call
  you placed is calling or ringing out (on the stage or as the banner), Call is greyed ("Wait
  for Dave to answer, or cancel it, to place another call"), even with another address typed,
  and Enter, a contact's Call in People and Call back do nothing (`callBlocked` in `app.js`,
  which `placeCall` checks, so no way of calling gets round it). It's back once that call is
  answered or ends (declined, failed, cancelled). Calls ringing in, connected or held don't
  count, so Call & hold works as above. The address field is only disabled at 8 calls, when
  Call says "That's as many calls as there can be".
- **Incoming calls** ring alongside the others (only a ninth gets 486 Busy Here). While you're
  on a call, a banner over the stage says who, with *Answer & hold current* and *Decline*, and
  the call you're on carries on untouched (not held, still on the stage) unless you answer; the
  name opens the full answer view. With several ringing, the one waiting longest is on the
  banner, with how long each has waited and a **+N more** button that drops the others down
  inside the banner: a row each (name, "Calling · 00:14", *Decline*, *Answer & hold current*,
  the same as the top one, as they all do the same), scrolling past
  three, so you pick which to answer. Answering from it folds the list away; declining leaves
  it open for the next; the button again or `Esc` folds it. Two with the same name from
  different addresses say the address too. `A` answers the one on the banner.
  With no call going it's the same: one ringing call has the stage to itself (the full answer
  view), but several are all in the banner, with the same list, and the stage says "3 calls
  ringing". A name (in the banner, or its row in Active calls) gives that call the answer view,
  and the banner keeps all of them as you switch between them, the one in the answer view
  with its name in the accent colour: the banner is every call waiting, the stage the one
  you're looking at. Declining or losing the call on the stage moves it to the ones still
  ringing (the banner, or the answer view once there's one), not the ended call's card.
  `--auto-answer` answers each call alongside the others, keeping the audio where it is.
- **When a call ends** it stays in the list for a few seconds with how it ended. If it was on
  the stage, the stage keeps saying so, with *Back to …* (or `Esc`) to another call, or with
  none, *Close* (or `Esc`) for Home, after 3 s by itself. The host keeps listing an ended call a
  little longer (about a second more, after an attended transfer), so a call the page has closed
  is remembered until the host lets it go: before, it came back as a new call, its card over
  Home again (Home, then a second *Transferred to Carol*).
- **Mute** is the selected call's; `M` too. For every call at once, mic and camera, there's
  [privacy mode](#privacy-mode) at the top (`Shift+M`).
- **Grid and ⋯**, under the Active calls list (icons only on the rail). *Grid*: see
  [Focus and grid views](#focus-and-grid-views). *⋯*, with two calls you can end, opens *End all
  N calls*, in red, which is rarely needed: it asks once more ("End 3 calls? Click again", for
  4 s, the menu staying open) and then hangs up every call you've placed or are on; a call
  that's only ringing is left to Answer or Decline.
- **Keys**: `1`–`8` put that call in the list on the stage (`1` is the top, the oldest), `A` answers,
  `M` mutes, `Shift+M` turns privacy mode on or off, `H` holds or resumes, `G`
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
  toast. `M`, `H` and `V` do nothing in the grid; `Shift+M` (privacy mode) still works.
- The choice is remembered (`view` in the page's settings) and only applies with two or more
  calls: with one, it's Focus.
- Each call's `<video>` moves into its tile and back (`renderGrid` in `app.js` keeps the tiles
  from one render to the next), so its picture and audio carry on. Two columns up to four
  calls, then three; one on a narrow stage. Past what fits, the stage scrolls.

## Active calls

A panel down the left, beside the stage and the control bar, from the prototype's sidebar, shown
only while there are two or more calls (or the grid is on screen): with one, the stage already
says who it's with and how it's going, so the stage and controls take the whole width. It
lists every call, oldest at the top and new ones at the bottom, while it's live (calling, ringing or connected) and for a
few seconds after it ends. Clicking a row puts that call on the stage. The list is built from
`listed()` in `app.js` and `callStatus()` in `calllist.js`.

- **A row**: their initial, their name (the full address in the tooltip), the status and, once
  connected, the timer. Calling… (grey ring, pulsing), Incoming call / Incoming video call
  (amber, pulsing), then Audio call or Video call (green), On hold · 02:10 (amber
  after a minute), Holding… or Resuming…, Muted in red, or how it ended. A connected call also shows,
  at a glance, what it gets from you: the mic, and on a video call the camera, as a filled
  green chip while it gets it, and struck out when it doesn't, red if that's your choice
  (muted, video stopped) and faint otherwise (on hold, held by them, the mic on another call).
  The tooltip says which: "Bob hears you", "On hold: they hear music, not you". With text,
  the status line says "Typing…" or their last line.
- **Folding it**: the chevron at the top right of the panel folds it to a 48 px rail of
  initials, the mic and camera chips, and timers (a click on one selects that call), and the
  chevron at the top of the rail opens it again. The choice is remembered (`localStorage`,
  `callsPanel`). The call header stays on the stage either way, for the selected call.
- **Narrow windows** (under 700 px): always the rail, with no chevron, to leave the stage its
  room.

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
  otherwise from *Call from*'s), **Video call** and **Copy address**. Each row also has Add or
  Edit contact, the details chevron, Copy address, Call and Video call.
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

The call info is a **call header** across the top of the stage on a connected call, where
people look for who a call is with: a phone's call screen has the name at the top, as an
email has its From line and a web page its address. The video, the frequency bars and the
self view start below it, so it never covers their picture, and the control bar holds only
buttons. The header is one line (40 px), read left to right:

- **Who**, in bold: their address, or for a saved contact the contact's name (with the
  contact mark) and then, in grey, the address. The address leaves out its URI parameters and
  headers (`;transport=tcp`, `?X-Foo=1`), which say nothing to a person; the *Address* row in
  Call details has the full one. Short of room, the address is cut short with "…" first, then
  the name, never the timer; hovering shows all of it.
- **How long**, after a "·", and a small red mic after it while you're muted.
- **The padlock**, before them, says how the call is protected: **green** with SIP and the
  media both encrypted (TLS and SRTP), **yellow** with only one of them, and **grey and open**
  with neither. It comes first, and its tooltip says which. A click shows **the call's
  security** in a small card under it, for anyone, not just the technical: a headline ("This
  call is encrypted", "This call is only partly protected", "This call isn't encrypted"), then
  *Your conversation* (voice and video) and *Call setup* (who's calling whom), each Encrypted or
  Not encrypted with the technical name small beside it (DTLS + SRTP, SRTP, plain RTP; SIP over
  TLS, WSS or UDP) so it can be recognised, a sentence on what that means (who on the network
  could listen in, or see who you're calling). The rest is in Call details (Diagnostics,
  F12), for those who want it. SRTP with its keys
  in unencrypted SIP is "only partly protected": anyone who can read the SIP can read the keys.
  Esc, a click away or the padlock again closes it, and it goes with the call.
- **The call's status** at its right, while there is one: how a transfer's going, in a few
  words in a tinted pill (under way in the accent colour, done green, failed red), with what you
  can do (see [Transfer](#transfer)); the full sentence is its tooltip. It's passing news, so it
  comes first: short of room the address and then the name give way to it, and in a narrow
  header (under ~560 px, a container query) it goes over the top of the whole line, as it's
  gone in a few seconds and who the call's with is back.

**One place for each kind of news.** The header line is this call's: its status, one at a time
(there's only ever one transfer on a call). Other calls' news has its own place below the
header: a call ringing in (*Answer* / *Decline*, the one waiting longest first, "+N more"
dropping down the rest) and a call you're placing over this one have their banners just under
it, so a call ringing in while a transfer's going covers neither. Another call's transfer shows
on its row in Active calls ("Carol is ringing…", "Transfer failed · on hold"). Text they type
has its toast at the bottom of the stage.

On hold, the *On hold · Resume* note stays over the middle of the stage (below the header).
Calling, ringing and ended calls have the stage's card, which already names the call, so the
header shows once the call's connected. In Grid view there's no header: each tile has its own
name and time.

The call's details are technical, so they're a tab in Diagnostics (F12), **Call details**,
for the selected call; with no call it says they show here during one. They follow the call
you pick, and the codecs fill in once the media flows.

The header is above the picture, not over it, for every call, audio too, and on one line, as
video-call apps do: everything about the call where you read it, without covering what you're
looking at.

| Row | What it says | From |
|---|---|---|
| Call | Outgoing or incoming, and the account (or "no account") | the host |
| Address | Their full address, as the call has it, URI parameters and headers included (`sip:bob@host;transport=tcp`) | the page |
| Their phone | their `User-Agent` (or `Server` on the 200 OK we got), or "Didn't say" | the host |
| Security | Encrypted or not, and how: the media is when every stream has SRTP keys (see [SRTP](#srtp)) or is WebRTC's DTLS-SRTP, SIP when it's over TLS (or WSS). The media's kind is named: "DTLS + SRTP" when the keys came from a DTLS handshake on the media path (a browser phone, WebRTC), "SRTP" when they were in the SDP (SDES), so only as private as the SIP. "Media encrypted (SRTP); SIP unencrypted (UDP)" between two of these softphones, "Media encrypted (DTLS + SRTP); SIP unencrypted (WS)" with a browser phone, "Not encrypted: plain RTP, SIP over UDP", in amber, with a phone without SRTP | the host |
| Audio, Video | The codec each way, "↑ Opus 48 kHz ↓ G722 16 kHz", or one name when both match. Each end can send a different codec, and this is the only place that shows it. G.722 is labelled 16 kHz, its sample rate, not its 8 kHz RTP clock. Video has its frame size, "VP8 1280×720", refreshed every second while the panel is open | the page's WebRTC stats, which are the SIP leg's, since the relay doesn't re-encode |
| SIP | transport and their SIP address and port, `UDP 127.0.0.1:5080` | the dialog |
| Media | where their audio goes, the RTP address from their SDP | the SIP session |
| Call-ID | the dialog's Call-ID, with **Copy**, to find the call in logs or a capture | the dialog |

The host sends these as a `details` message when the call connects, and again if video is
added.

## SRTP

Every plain SIP call offers SRTP, best effort: each media line stays `RTP/AVP`, which every SIP
phone takes, with `a=crypto` lines (SDES keys, AES_CM_128_HMAC_SHA1_80 and \_32) added. A phone
without SRTP ignores them and answers plain RTP, so the call goes ahead unencrypted rather than
failing; one with SRTP answers with a crypto line of its own and the media is encrypted. It's
SIPSorcery's `RtpSecureMediaOptionEnum.SdpCryptoBestEffort`.

- **Answering**: crypto on `RTP/AVP` is answered with crypto (SRTP), an offer without it plain RTP,
  and `RTP/SAVP` (a phone set to require SRTP) with `RTP/SAVP` and SRTP; `RTP/SAVP` without a key
  we can use is turned down (488).
- **Each stream on its own**: audio, video and text each settle on SRTP or plain RTP. Once one is
  plain our re-offers (hold, adding video) leave its crypto out; a re-offer from them without
  crypto on an SRTP call changes it to plain, one with crypto on a plain call to SRTP.
- **Where it shows**: the padlock in the call info (green: SIP and media; yellow: one; grey and open: neither), and the
  call details' *Security* row (Diagnostics).
- **The limit**: a phone that requires SRTP and only takes `RTP/SAVP` offers turns down our calls
  to it (488), as it would any best-effort offer. Calls from it work.
- **The keys are in the SDP**, so they're only as private as the SIP: over UDP or TCP anyone who
  can see the media can read them from the INVITE. Over TLS (a `sips:` or `transport=tls`
  account) they aren't.
- `--no-srtp` offers none, for checking a phone that mishandles crypto lines; calls from a phone
  that requires SRTP are still answered with it. Browser phones (WebSocket) use WebRTC's DTLS-SRTP
  regardless.

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

**Blind, the call isn't held.** The person you're transferring may be asked whether to go (this
phone asks), and can talk it over with you while they decide; once they accept, their phone holds
you itself while it calls Dave (the user agent does), and if it doesn't go ahead you're simply
still talking. (It used to hold them first, as RFC 5589's example does, so they decided to your
hold music and a decline left you to Resume.) **Attended, both calls go on hold** as it starts:
you've put them on hold to speak to Dave. Either way the call header's status says how it's going, in a few words on the header's line ("Transferring to Dave…",
"Dave is ringing…", "Transferred to Dave", "Transfer failed: Dave is busy (486 Busy Here)"),
from the far end's NOTIFYs (the `sipfrag`). The host's full sentence ("Couldn't transfer: Dave
is busy (486 Busy Here). You're still on the call: Try again, if you like.", or attended, "The
call is still on hold: Resume, or Try again.") is its tooltip; the host sends a short
`reason` for the line and `rang` once the target's ringing. The call's row says the same. A
`100 Trying` that arrives after a `180 Ringing` (NOTIFYs can cross) doesn't take it back from
ringing. While it's under way the *On hold* note in the middle of the stage is hidden (Resume
there would be Cancel by another name); once it's over, it's back.

While it's under way the status has what you can do:

- **Cancel** (asking or calling, blind or attended): you take the call back. SIP has no way to
  call off a REFER once the far end has accepted it (RFC 3515), so this is the most that can be
  done: the transfer is forgotten, attended the calls come off hold (blind, it was never held),
  and the status says
  "Transfer cancelled" for a few seconds, with × (the tooltip: "If VtN's phone still goes ahead
  with it, it will leave this call."). If it does go ahead, their phone hangs up on you and the call
  ends *Transferred to Dave (their phone went ahead after you cancelled)*, not *They hung up*.
  The person being transferred can stop it for real (below), and then you see "Transfer failed:
  VtN stopped it" (their `487`). If they're still being asked (this phone), their question stays:
  Decline (or no answer) leaves things as they are, but Accept still transfers them.
- **Ending one of an attended transfer's calls** before it's gone through (End on it, or they
  hang up) is the transfer over: the other call isn't being transferred any more, and can be
  again. (Before, it stayed "under way", Transfer disabled on it, until it ended too: seen with a
  MicroSIP call as the target, which rang and never answered.)
- **Hang up** (blind, accepted): you hang up now and leave it to their phone, as a blind
  transfer usually goes (RFC 5589 lets the transferor leave once it has the 202). The call ends
  *Transferred to Dave (you left it to their phone)*; their phone carries on calling Dave.

- **Done** (NOTIFY `200 OK`): they hang up on us (if they haven't 3 s later, we do), and the call
  ends with *Transferred to Dave*. Attended, both legs end: *Transferred to Carol* on Bob's,
  *Transferred to Bob* on Carol's. A far end that sends no NOTIFYs still counts as done when it
  hangs up on us after accepting.
- **Failed**: the status says why, with the response: "Transfer failed: Dave is busy (486 Busy
  Here)", "… nothing answered at Dave's address (408 Request Timeout)", "… VtN's phone turned it
  down". **Try again** and **×** on the status. Blind, you're still talking. Attended, the call
  stays on hold, and the *On hold* note in the middle of the stage says why too, with Try again
  and **Resume**: what happens next is your choice, never a silent resume. Resuming clears the
  status; × just drops it, the call staying on hold.
- **No word**: accepted, but no outcome in 60 s, it's marked failed ("Transfer failed: no word
  from VtN's phone"), with the same.

**Being transferred.** A REFER on one of your calls is **yours to accept or decline**. The call
header says "Alice wants to transfer you to Dave" (accent-coloured; the tooltip has the full
sentence) with **Accept** and **Decline**, and nothing else to do there; the call goes on as it
was meanwhile (no hold). The REFER itself is answered **202 Accepted at once**, with a NOTIFY
`100 Trying`: 202 only says the phone will deal with it, and a proxy won't wait while you decide
(sipsorcery.com answers a REFER `408 The far party did not answer the transfer` itself after
about 8 s, and Alice's phone then took the transfer as failed while you were still being asked).
Your choice decides what happens next, through the user agent's `OnTransferConfirmAsync` (added
to `SIPUserAgent` for this: after the 202 it waits for the task before it holds Alice or calls
anyone; false drops the transfer):

- **Accept**: the call to Dave starts (below), and Alice's phone hears how it goes in NOTIFYs.
  If Dave answers, you're with Dave and Alice's call ends ("Transferred to Dave"); if not (say
  `480 dave@sipsorcery.com is not registered`), you're back with Alice.
- **Decline**: "Transfer declined · still with Alice", and a final NOTIFY `603 Transfer
  declined`. Alice's phone (this one) knows it by its reason phrase and says *Transfer failed: Bob
  declined it* in the call header, with Try again; blind, Alice and Bob are still talking (an
  attended one stays on hold, and its *On hold* card says it too). Another phone sees the
  transfer fail.
- **No answer in 20 s** declines it: "Transfer not answered · still with Alice", and the NOTIFY
  says `603 Transfer not answered` (*Bob didn't answer it*, on Alice's phone).

While you decide, Alice's phone shows *Transferring to Dave…* with Cancel and, on a blind
transfer, Hang up (leave it to their phone: the call then ends, and so does the question). An
immediate refusal of her REFER (a browser phone's, or a proxy's 408) reads *Bob's phone turned it
down*, not *declined*. Our own REFER waits 10 s for its answer. Within limits, still:

- only inside a connected call, from the person you're talking to; not on a browser phone's
  (WebRTC) call, whose media can't move to another call; not while a transfer is already under
  way (or being asked) on it; only to a `sip:` or `sips:` address. Otherwise it's answered 603 at
  once (`OnTransferRequested`), without asking, and the note says "Alice tried to transfer you to Dave, but …".
- the call to the target goes out with your account's password only if the target is on that
  account's own server (`OnTransferCallDescriptorCreated`), so a transfer can't use your account
  to reach a number outside it. With no account it goes from the address the transferor reached
  you at. It carries `Referred-By`, naming the transferor.
- while it's under way you can see it ("Transferring you to Dave · ringing…", on the call
  header beside Alice's name) and **Cancel** it (its tooltip: *Stay with Alice*): the call
  to the target is cancelled (`CANCEL`, on the client user agent the transfer call's first
  response hands us; asked for before any response, it goes with the first one), which the
  user agent takes as the transfer failing: it takes Alice off hold and the call carries on,
  "Transfer cancelled · still with Alice", and Alice's phone gets a final
  NOTIFY `487 Request Terminated`. Not once Alice has hung up (there's no one to go back to:
  End is the way out then). Or hang up altogether.
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
`100 Trying`, `180 Ringing`, then the final response (`Subscription-State: terminated`). The
first `100 Trying` goes 200 ms after the 202, and not at all if the target has already rung by
then, so the transferor's note never goes back from "ringing" to "Transferring…".

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
Focus view, and the stage narrows to make room. There's no choice to make.

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
    "They can't take this call's media (488)". `406 Failed creating media transport` is retried
    too, alongside those: it's what sipsorcery.com's server says when it can't set up the media
    the offer asked for. Any other 406 isn't. The SIP log says when it retries, and why
    ("they can't take plain RTP media (406, Failed creating media transport); calling again with
    WebRTC").
  - **A bare 488 to plain RTP tries SRTP required first.** Our plain RTP offer puts SDES keys
    on `RTP/AVP`, which a phone with SRTP mandatory turns down whatever keys it carries (MicroSIP
    with SRTP set, a bare 488). So a bare 488 to it is retried with `RTP/SAVP` and the same keys,
    and only a 488 to that goes on to WebRTC: up to three INVITEs. That has to come before
    WebRTC for such a phone, as pjsip (MicroSIP's stack) doesn't do BUNDLE: it answers a WebRTC
    offer with an ICE and DTLS session per m-line, of which our one bundled transport only ever
    completes the first, so the call has audio but no video.
  - **A contact's *Media*** skips the guessing when you know what their phone takes: the
    contact form's *Media* is *Automatic* (all of the above), *Plain RTP* (`RTP/AVP` with SDES
    keys), *SRTP required* (`RTP/SAVP`) or *WebRTC*. A call to the contact offers just that in
    its first INVITE, and a refusal ends the call rather than trying another kind. The page
    sends it with the call (`dial {…, media}`), and it's kept in the contacts file. Desktop only:
    the web softphone's calls are always WebRTC, so its contact form doesn't have it.
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

- **Codecs**: H264 (packetization-mode 1, Constrained Baseline `42e01f`) first, then VP8. More
  SIP phones and PBXs take H264, and it's usually decoded in hardware. A phone that answers
  with VP8, or offers it first, gets VP8: the answer follows the offer's order. The relay
  doesn't re-encode, so the page's WebRTC connection uses whichever was agreed.
- **Call and Video call** beside the address place the kind of call (see
  [Call and Video call](#call-and-video-call)). Call leaves video out of the INVITE: most SIP
  calls go to phones, and an audio offer never trips anything up. Video call offers it. With
  `--no-video` there's no Video call (nor Answer with video).
- **Add video**, on an audio call, is where the Camera button would be. It adds our video codecs
  to the SIP session and sends a re-INVITE with `m=video`. If they accept, the relay is rebuilt
  on the same SIP session and the page gets a new offer with video. If they refuse (`m=video 0`,
  an error, or no answer in 8 s), the button becomes a disabled *No video*. Video you add
  starts with your camera on. When the *other* side adds video, it's accepted with your camera
  off, so a request from them never turns your camera on by itself. That includes an audio call
  we placed, whose SIP session was made without video: our video codecs go on it as their
  re-INVITE is answered (`SIPUserAgent.OnRemoteMediaStreamAdded`), so the answer isn't
  `m=video 0`.
- **The camera is only open when it's needed:** during a video call with your camera on, and
  while Your devices' Preview is on. Otherwise (no call, an audio call, your camera turned off)
  it's closed: no camera light, and the indicator shows no camera.
- **So is the microphone.** It opens when you press Answer, or when a call you placed is
  answered, or when the call's media arrives on a connected call (auto-answer), and closes when
  the call ends, so a webcam's mic light is off between calls. Not while a call is ringing,
  either way (the indicator shows nothing then): the web softphone sets up a placed call's
  media before the answer, and sends silence until the microphone opens. Not in privacy mode. Your devices' Test mic opens it separately, for the meter only.
- **A camera in use elsewhere says so, and waits for you.** Windows lets one app have a camera
  at a time, so a second softphone (or Teams, say) gets `NotReadableError`. That shows as
  "Camera in use" (not "No camera"), and on a call your camera goes off and the placeholder is
  sent. It's never retried by itself: the camera only opens because of something you did
  (Preview, placing or answering a video call, Add video, Start video). Otherwise the
  softphone would start streaming your camera on its own the moment the other app let go.
  A missing camera or a refused permission is "No camera".
- **Test pattern** (diagnostics mode): picked from the Camera button's ▾, it sends a test
  pattern instead of the camera. It stands in for the camera exactly:
  opened and closed when the camera would be, Camera off still sends the placeholder, and it
  can be switched mid-call. The source is per call: with several calls, the Camera ▾ picks
  for the selected call only, so one call can send the test pattern while another sends the
  camera. So is the camera itself: a camera picked for one call (even an audio call, as the
  one for Add video) doesn't change another's, and going to a call that wants another camera
  switches to it. The source and camera picked last are what new calls start with.
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
  should jump as they hear the sound).
- **Picking a source on a video call turns the camera on.** With the camera off, choosing a
  camera, the test pattern or audio bars from its ▾ sends it straight away. Before a call, or
  on an audio call, the ▾ only sets what the next video call (or Add video) will use.
- **Answer and Answer with video** on a ringing video call: Answer opens no camera (you see
  them; they get your camera-off card until you press Start video); Answer with video opens the
  camera picked. Your devices under the card shows which, with a quick change. An audio call
  gets Answer alone.
- **Self view**, at the right end of the control bar (under the self view it controls; the
  prototype has it in the info bar), or `V`, hides or shows your own picture. It's there
  during a video call. What's sent doesn't change. It's remembered between runs. Before a call
  there's no self view: Your devices has the preview.
- **Moving and resizing the self view.** Drag it anywhere on the stage (mouse, pen or touch);
  it stays inside the stage, also when the window resizes. Its corner handle, on the corner
  facing the middle of the stage (top-left at first), resizes it, keeping 16:9, from the
  default size up to three times it, as far as the stage allows. Double-click it to put it
  back in the bottom-right at the default size. It can take keyboard focus (Tab): the arrow
  keys move it, Shift with the arrows resizes it (Up or Right larger, Down or Left smaller)
  and Home puts it back. The on-hold note, the call header and the
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
  1280×720 and sent at 5 fps. With *Show Gravatar pictures for people* on, the account's
  Gravatar picture (or identicon) fills the circle in place of the initial, with the account's
  address under it, once it has loaded; a direct call (no account) keeps the icon. A still
  picture costs a few kbps. So any SIP phone just shows a picture, not a frozen last frame or
  a stream that looks broken. NAT bindings and media timers on the video port stay alive, and
  turning the camera on is a track swap with the encoder already running.
- **The self view shows what they see.** During a video call that's your camera (mirrored) or the
  placeholder (not mirrored, so the initial reads right). A note along the bottom says why the
  camera isn't showing: "Camera off", "Privacy mode", "Camera in use" or "No camera".

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

**Call quality** is a tab in Diagnostics (F12), after Call details, for the selected call;
with no call it says it shows here during one. It's here, not over the stage, so it covers
none of their video and has room to show it all. It's in the theme's colours, like the other tabs; only the graphs and the
PLI strip are dark, in either theme, because their lines are drawn for that. The table and PLIs
are at the left, a graph for each direction beside them, each with a line on where it comes from
(↑ Sent from their RTCP, ↓ Received measured here), and under the graphs the legend and RTT
(under 900 px the graphs go below the table). What MOS, loss, jitter and PLIs mean is left to the
tooltips for now (see Later). The table has a column for each direction, so there's no "they"
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

Beside it, two graphs show both directions over the last two minutes, each the receiver's
view: **↑ Sent**, how they receive what we send ("Reported by them, in their RTCP every 5 s or
so"), and **↓ Received**, how we receive what they send ("Measured by us, every second"). Each has audio MOS (a line, 1–5),
audio loss (bars) and video loss (a line), both 0–20%. On ↓
Received, white bars along the bottom show how long our picture was actually frozen, from the
browser's `totalFreezesDuration`; it only counts a freeze once it has ended, so a bar is drawn
back over the seconds it covered when it does. ↑ Sent has none: RTCP doesn't say whether their
picture froze. The last line gives
the round trip ("RTT 0.3 ms"), or says it's waiting for their RTCP, or after 15 s that none has
come. Under that, a short key says what each measure means: MOS (green from 4, amber from 3.6,
red below), loss, jitter, PLIs, Frozen and RTT. Hovering over a value says
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
  for calls straight to an IP address or host. It's always there, with a single account or
  none too, as *No account (direct)* is always a choice. The dot is the chosen account's registration:
  green registered, amber registering, red failed, grey not registering. Its tooltip says why
  it failed, and so does the Ready card ("alice@… isn't registered: Wrong username or
  password (401 Unauthorised)"). The choice is remembered, *No account* included.
- **Manage accounts** (the server icon beside it; the two heads are People's) opens the Accounts view over the stage; a call
  carries on underneath, and a ringing call hides the view (the icon brings it back, with
  what you'd typed). The icon turns red with an alert while any enabled account has failed,
  and its tooltip names them. The view starts on a table of every account, its address
  (`sip:alice@example.com`, or `sips:` when its transport is TLS) and its status; clicking one
  opens its form. `Esc` steps back: form, then table, then closed. Right of the title, by Close,
  a note gives this PC's own address for direct calls, without an account ("Direct calls reach
  this PC at `sip:192.168.1.20:5070`"): the address this PC uses to reach the internet (which
  works from this PC too) and the UDP SIP port. A click copies it ("Copied") for the other side
  to dial.
- **The form**: account name (blank: user@server), display name (shown to people you call, and
  used for the camera-off placeholder), username, server (`host` or `host:port`), transport
  (UDP, TCP or TLS), auth username (if not the username), password, *Register*, expiry
  (60–7200 s, default 300), and under *Calls*, *Hold*: *Music on hold* (the default) or *SIP
  re-INVITE* for a PBX (see [Several calls](#several-calls)). A call keeps the Hold it started
  with. Outbound proxy, STUN, an SRTP choice (it's always offered, best effort: see [SRTP](#srtp)) and the DTMF choice from the prototype aren't there yet: the
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
    offer.
  - SIPSorcery dropped any RTCP packet of 12 bytes or less, and a PLI on its own is exactly 12.
    So between two SIPSorcery ends no keyframe request ever arrived, and a page that missed the
    first keyframe never showed video. Browsers send PLIs inside a compound packet, so they
    weren't affected. Fixed in #1884.
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
- **A call you placed that was taken over in place stayed up when the new party hung up.** The
  user agent answered their BYE but still matched it against the call it had placed, and your
  own hang-up went to the old call. Fixed in the library (`SIPUserAgent.AcceptAttendedTransfer`):
  it drops the placed call's details once the replacing call is answered, and flags the replaced
  call's Call-ID, not the new one's, as the one whose late re-INVITE answers are ignored.
