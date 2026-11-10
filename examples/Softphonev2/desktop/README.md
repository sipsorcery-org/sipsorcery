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
  real-time text popover (T does too). During a call only.
- **Focus** and **Grid** (at the right, G too): see [Focus and grid views](#focus-and-grid-views).
  There once there's a call. Each goes to its view, so the one on screen is disabled; Grid needs
  two calls, and shows how many.
- **Hold** (H): puts the selected call on hold, or Resume takes it off (see
  [Several calls](#several-calls)). During a call only.
- **End**: Cancel while calling, End once connected.

A device you pick takes effect at once, mid-call too, by swapping the track on the page's
connection (no renegotiation), and is remembered between runs (per profile). A remembered
device that's gone falls back to the default. The prototype's Keypad and Transfer aren't here
yet; they come with those features.

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
  [Focus and grid views](#focus-and-grid-views)); the Focus and Grid buttons and G change it too.
- **Appearance.** Theme: System, Light or Dark. System follows Windows' light or dark mode.
- **Account.** Who you are: the account, or with none, the address others can call, and a
  *Manage accounts* button that opens the same view as the dial bar's people button (see
  [SIP accounts](#sip-accounts)).
- **Diagnostics.** Opens or closes the panel F12 does.

The page keeps them in its `localStorage`, which is in the WebView2 profile's folder, so each
`--profile` has its own. The prototype's ringtone (there's no ringtone yet) and real-time
text options (saved transcripts; there's no call history) are left out until there's something
behind them.

## Several calls

As in the prototype: up to 8 calls at once, each with its own SIP user agent, media session and
relay on the host (`Phone.Call`), and its own peer connection, `<video>`, quality, details and
text on the page. Messages about a call carry its `id`; the host's `state` lists every call.

- **Audio focus.** Only one connected call has the microphone and speaker. Selecting another
  connected call (its row, or its number key) moves them to it: its sender gets the microphone
  and its `<video>` plays. The previous call stays up, with its video, and shows *Audio off*;
  the page sends it no audio.
- **Hold** is separate: *Hold* in the control bar, or `H`, sends a re-INVITE with our media
  sendonly (`SIPUserAgent.PutOnHold`), and the page sends a held call nothing at all. The stage
  shows "On hold · 02:10" with *Resume*, amber after a minute, as does the list. A call they've
  put on hold says so ("Alice put you on hold").
- **New call.** Placing a call while you're on one holds that call, and the new one goes on the
  stage. The dial bar is only disabled at 8 calls.
- **Incoming calls** ring alongside the others (only a ninth gets 486 Busy Here). While you're
  on a call, a banner over the stage says who, with *Answer & hold current* and *Decline*; the
  name opens the full answer view, which also has *Answer, keep current*. With several ringing,
  the one waiting longest is on the banner, with "+N more waiting". `A` answers it.
  `--auto-answer` answers each call alongside the others, keeping the audio where it is.
- **When a call ends** it stays in the list for a few seconds with how it ended. If it was on
  the stage, the stage keeps saying so, with *Back to …* (or `Esc`) to another call.
- **Mute** is the selected call's; `M` too. `Shift+M` mutes every call, and the ones to come
  (before a call, Mute is the same thing: calls start muted). `Shift+C` turns the camera off
  on every call and for new ones; it never turns cameras on.
- **Keys**: `1`–`8` put that call in the list on the stage (`1` is the newest), `A` answers,
  `M` mutes, `Shift+M` mutes all, `Shift+C` turns all cameras off, `H` holds or resumes, `G`
  switches between Focus and Grid.

## Focus and grid views

As in the prototype. **Focus** (the default) shows only the selected call, with its controls
under its info bar. **Grid** (the Focus and Grid buttons in the control bar, or `G`) shows every active call
as an equal tile, newest first like the list, with no buttons: each tile is their video (or
their initial when there's none, or it's on hold), the call's time, their name and its status
line, the same as the list's: "On hold · 02:10" (amber), "Typing…", their last unread line,
"Audio off", and the mic, camera and text icons. The selected call's tile, which has the audio,
is outlined. The control bar keeps only Focus and Grid, so it's never in doubt which call a button
acts on.

- **Picking a call** from the grid, by its tile (or Enter on it), its row in Active calls, or
  its number key, opens it in Focus.
- **A ringing call** shows its answer view and leaves the choice alone: answered, it's back to
  the grid. While you're in the grid, a call that rings shows on the banner, and as a tile.
- **`T`** in the grid switches to Focus with the text panel open, as does *Reply* on a text
  toast. `M`, `H` and `V` do nothing in the grid; `Shift+M` and `Shift+C` still act on every call.
- The choice is remembered (`view` in the page's settings) and only applies with two or more
  calls: with one, it's Focus. Settings has it as *Show all calls in a grid*.
- Each call's `<video>` moves into its tile and back (`renderGrid` in `app.js` keeps the tiles
  from one render to the next), so its picture and audio carry on. Two columns up to four
  calls, then three; one on a narrow stage. Past what fits, the stage scrolls.

## Active calls

A panel down the left, beside the stage and the control bar, from the prototype's sidebar. It
lists every call, newest at the top, while it's live (calling, ringing or connected) and for a
few seconds after it ends, and otherwise says *No active calls*. Nothing is made up: there's no
call history yet, so the prototype's History section isn't here. Clicking a row puts that call
on the stage. The list is built from `listed()` and `callStatus()` in `app.js`.

- **A row**: their initial, their name (the full address in the tooltip), the status and, once
  connected, the timer. Calling… (grey ring, pulsing), Incoming call / Incoming video call
  (amber, pulsing), then Audio call or Video call (green), Audio off, On hold · 02:10 (amber
  after a minute), Muted in red, or how it ended. A connected call also shows what you're
  sending: the mic (red when muted, faint when the audio is on another call or it's on hold)
  and camera (green when sending, red when stopped, faint on an audio call or on hold), and
  with text on, a text icon that pulses while they type and gets a red dot for text you haven't
  read; the status line then says "Typing…" or their last line.
- **Collapse**: the chevron folds it to a 48 px rail of initials and timers, so a live call
  still shows; the chevron, or a click anywhere on the rail, opens it again. The choice is
  remembered (`localStorage`, `callsPanel`, per profile on the desktop).
- **Narrow windows** (under 700 px): it's always the rail, to leave the stage its room.
  Expanding it lays the panel over the stage until you collapse it, click away or press Esc,
  without changing the remembered choice.

## Call details

The info bar at the top left of the stage (their address and the call's duration) has a
chevron that drops down the call's details, in diagnostics mode only (F12): they're technical,
so otherwise the info bar is just who and how long, with no chevron. It's closed at the start
of every call, and when diagnostics closes.

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
- **A popover, not a panel.** It opens over the bottom right of the stage, just above the Text
  button, so the video doesn't shrink (it covers the self view while open). Esc, its ×, the
  Text button, T, or a click anywhere else closes it, and Esc puts the focus back on the button.
- **Text arriving while it's closed** shows as a toast at the bottom of the stage: their name,
  what they've typed (live, if their phone sends letter by letter) and *Reply*, which opens the
  popover. It fades 6 s after the last letter, and the Text button keeps a red dot until the
  popover is opened. Nothing opens by itself.
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
  off, so a request from them never turns your camera on by itself.
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
  camera. The source picked last is what the preview and new calls start with.
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
  camera at all, sends a placeholder: your initial (from the account's display name or user
  name, or a head-and-shoulders icon with no account) in a circle, drawn on a canvas at
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

There is no call history, contacts, DTMF or transfer yet.

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
- **Manage accounts** (the people icon beside it) opens the Accounts view over the stage; a call
  carries on underneath, and a ringing call hides the view (the icon brings it back, with
  what you'd typed). The icon turns red with an alert while any enabled account has failed,
  and its tooltip names them. The view starts on a table of every account and its status;
  clicking one opens its form. `Esc` steps back: form, then table, then closed.
- **The form**: account name (blank: user@server), display name (shown to people you call, and
  used for the camera-off placeholder), username, server (`host`, `host:port`, or a SIP URI
  such as `sip:host;transport=tcp`), auth username (if not the username), password, *Register*,
  and expiry (60–7200 s, default 300). Transport, outbound proxy, STUN, SRTP and DTMF from the
  prototype aren't there yet: the host doesn't do them per account. *Re-register* registers
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
window. It has two tabs:

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
| `--no-video` | No video at all: the switch is locked to Audio, and video offers or Add video from the other side are turned down. (Will be the per-account "Video calls" setting.) |
| `--camera-off` | Start with the Camera button off, so calls and answers start with the camera off. |
| `--add-video <s>` | Press Add video this many seconds into an audio call. |
| `--hangup-after <s>` | Hang up a call this many seconds after it connects. |
| `--dial-count <n>` | With `--dial`, place `n` calls one after another, each 2 s after the last one ends. |

The log, with every full SIP message, goes to
`%LOCALAPPDATA%\SIPSorcery\Softphonev2\softphone-<profile>.log`. Every 5 seconds during a call it
also records the page's receive and send rates, and the relay's packet and frame counts.

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
|---|---|
| Several calls: three copies, A (`--profile fgA --port 5191 --ws-port 8191 --video --diagnostics`), B and C (`fgB`/`fgC`, ports 5192/8192 and 5193/8193, `--auto-answer`), all `--fake-media --test-pattern` | A calls B: video both ways. A calls C: A's B call goes on hold ("On hold · 00:06", B shows "Thisis put you on hold"), C's call on the stage with the audio. |
| A: `2` (the older call), then `H` | B's call on the stage with "On hold" and Resume; after `H` it has the audio (its `<video>` plays, C's is muted) and C's row says "Audio off". B gets A's video again within a second (the held call's sender goes back on). |
| C (audio) calls B while B is on its call with A | B's banner: "Thisis · Calling" with Answer and Decline; B's host auto-answers it alongside: "Audio off" on the new row, A's call keeps the audio. No 486. |
| B: `1`, `M`, End, `Esc` | C's call on the stage with the audio; `M` mutes only it ("Muted", red mic; A's row "Audio is on another call"). End: "You hung up" with *Back to Thisis*; `Esc` goes back to A's call, which takes the audio. |
| A with two calls: `Shift+M`, `Shift+C`, `Shift+M` | Both rows "Muted" and "Video stopped"; then both unmuted, cameras still off. |
| Grid, the same three copies: A with video calls to B and C, then the Grid switch | Two tiles, C's (selected, outlined) with its test pattern, B's "On hold · 00:14"; the control bar shows only Focus \| Grid 2. `M` and `H` change nothing. |
| From the grid: B's tile; `G`; C's row in the list; `G`; `2`; `G`; `T` | Each opens that call in Focus (B's with "On hold" and Resume); `G` goes back to the grid each time; `T` opens Focus on the selected call with the text input focused. |
| In the grid, C calls A | The banner, and a ringing tile; the view stays Grid. The banner's name: the answer view (Settings still says grid). *Answer & hold current*: back to the grid, three tiles, the new call selected and the other two "On hold". |
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
| Active calls: collapse, restart, narrow | Collapsed: a 48 px rail with "B" and the timer ticking. Restarting A with the same profile keeps the rail; a click on it opens the panel and saves "open". At 560 px it's the rail whatever was saved; expanding lays the 240 px panel over the stage and the saved choice stays put. |
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
| Same, callee: Text icon, types "Hi A", Enter, then clicks the video | The popover opens over the stage (the video keeps its size) with both messages; the click closes it. The caller gets a "B Hi A" toast. |
| Caller: Text icon, then Esc | The popover opens with the conversation; Esc closes it and focuses the Text icon. |
| Audio call, caller types "Hi from A" | No "Audio only" label. Nothing reaches the callee while the caller types; on Enter the callee gets the toast and the dot. |
| `--webrtc` call, caller `--say hello`, to `--auto-answer --say hello` | Both offer and answer have `a=dcmap:1 label="t140";subprotocol="t140"` on `m=application`. Both log "real-time text agreed", then 0.3 s later "real-time text on (RFC 8865, T.140 data channel on stream 1)", and each gets the other's message ("real-time text in: 7 characters": BOM, "hello", U+2028). |
| Same, callee `--no-text` | The callee's answer has `m=application` but no dcmap; the caller logs "they didn't take text (no T.140 data channel in their SDP)" and stays at refused, though its own negotiated channel opens when SCTP comes up. |
| Same, caller `--add-video 4` | Text on, then the re-INVITE (with the dcmap again) adds video; text stays on. |
| Plain RTP call, both `--say` (after the data channel work) | "real-time text on (RFC 4103)" both ends, and each message arrives. |
| Web softphone dials it (`server=`), both saying something | Both log "real-time text on (RFC 8865 …)"; each popover has both messages. See the web README. |
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
| Browser call, Text popover open, then one click on End | The call ends ("You hung up", BYE, 200). |
| The web softphone (../web), audio only, `dial=desktop&hangup=12` | Answered with audio only; the browser's BYE ends it here with "They hung up". |
| Softphone to spike far end, H264 720p30 | 30 fps at 1280x720 in, decoded by `D3D11VideoDecoder` (hardware); camera out at 30 fps. |

Not tested yet: the one-way audio warnings (there's no easy way to break one direction yet),
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
