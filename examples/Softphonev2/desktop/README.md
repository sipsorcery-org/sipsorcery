# Softphone v2 – desktop prototype

A minimal Windows softphone that makes and takes **SIP video calls, several at once**. It is the first build of
the desktop app, using the **relay** media design. An earlier spike measured three ways of
showing 8 incoming 720p30 calls plus the camera (WebView2 with a local WebRTC connection, WebView2
with shared buffers and C# decoding, native WPF); the relay cost least, because the browser engine
decodes and encodes, in hardware where it can, and C# never touches pixels.

- **C# (this project)** does SIP with SIPSorcery: the transport, a registration for each SIP
  account (see [SIP accounts](DESIGN.md#sip-accounts)), and up to 8 calls at once (dial, answer, decline,
  cancel, hang up, hold; see [Several calls](DESIGN.md#several-calls)).
- **The page** (`wwwroot`, shown in WebView2) does all the media: it captures the camera and
  microphone, does echo cancellation, encodes, decodes (in hardware where it can) and renders.
- **`MediaRelay`** joins the two, one per call. Once a call connects, it creates a local `RTCPeerConnection` to
  the page that offers the codecs the SIP call negotiated, and passes the media between the two
  without decoding it:
  - audio packet by packet, keeping the timestamps and sequence numbers;
  - video frame by frame;
  - keyframe requests (PLI/FIR) in both directions, plus one request each way when the relay
    comes up, so video starts at once.

The rest of its documentation:

- [DESIGN.md](DESIGN.md): how each part behaves and why (the window, the controls, several
  calls, transfer, video, call quality, SIP accounts…), and what was learned building it.
- [TESTING.md](TESTING.md): what's been checked, test by test, and what hasn't yet.

## Files

The host (C#: WinForms and WebView2):

| File | What it is |
|---|---|
| `Program.cs` | Start-up: the switches, and one running copy per profile. |
| `MainForm.cs` | The window: WebView2 showing the page, and the messages between the page and `Phone`. |
| `Phone.cs` | SIP: the transport, each account's registration, and up to 8 calls (dial, answer, hold, transfer). |
| `MediaRelay.cs` | One per call: passes its SIP media to and from the page's WebRTC connection without decoding it. |
| `TextPeerConnection.cs` | A WebRTC call's SIP leg, with real-time text on a data channel. |
| `Accounts.cs`, `Contacts.cs`, `History.cs` | SIP accounts, contacts and call history, kept per profile in `%LOCALAPPDATA%\SIPSorcery\Softphonev2`. |
| `Settings.cs` | `softphone.json` and the switches. |
| `WindowPlacement.cs` | Where the window was, and how big, per profile. |
| `LibraryLog.cs` | The SIPSorcery library's log lines, for the app's log. |

The page (`wwwroot`, shared with the web softphone in [../web](../web/README.md)) is `index.html`,
`styles.css` and these modules:

| Module | What it is |
|---|---|
| `app.js` | The calls: their state, the stage, the controls, the dial bar (Call and Video call), answering, privacy mode, the keys, and the messages to and from the host. It starts the rest. |
| `util.js` | Small helpers the others share: elements by id, escaping, messages to the host, the log, names from addresses, the icons. |
| `address.js` | Addresses as typed and shown, and the address fields (the dial bar's and Transfer's) with their suggestions. |
| `home.js` | Home, the stage with no call on it (recent calls, accounts, Your devices, privacy mode), and the dial view the address field opens. |
| `yourdevices.js` | Your devices (the microphone, speaker and camera, with Test mic, Play a sound and Preview), and the indicator that shows only while the microphone or camera is open. |
| `video.js` | What each call sends as video: the camera, the test sources, the camera-off placeholder and the "On hold" card. |
| `holdmusic.js` | Music on hold, and the silence a call sends when it isn't the live one. |
| `menus.js` | The ▾ device menus on Mute and Camera, and the self view's button. |
| `calllist.js` | The Active calls panel, the grid, and End all. |
| `levels.js` | Audio levels: the bars of the audio-only view and of the grid's tiles. |
| `keypad.js`, `rtt.js`, `transfer.js` | The keypad (DTMF), real-time text, and transfer. |
| `quality.js` | Call details and Call quality. |
| `diagnostics.js` | The F12 panel, the test sources' menu items, and the security popover. |
| `accounts.js`, `contacts.js`, `history.js` | Call from and Manage accounts; contacts and the People view; call history. |
| `settings.js`, `avatar.js`, `contactmark.js`, `selfview.js` | Settings; Gravatar pictures; the saved-contact mark; moving and sizing the self view. |

The modules import from `app.js`, and it from them. What each does at start-up is in an
`init…()` function that `app.js` calls at its place in the start-up, so the order things are set
up in is fixed.

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
It has four tabs:

- **SIP log**: everything the host and page log, following the newest line unless you've
  scrolled up. *Copy log* puts the whole log on the clipboard; *Clear* empties it.
- **Call details**: how the selected call was set up (see [Call details](DESIGN.md#call-details)).
- **Call quality**: the selected call's table and graphs (see [Call quality](DESIGN.md#call-quality-diagnostics-mode)).
- **Network**: the simulated loss sliders (see [Call quality](DESIGN.md#call-quality-diagnostics-mode)).

While it's open the app is in diagnostics mode: the Test pattern and Audio bars sources on the Camera ▾. Closing it
hides them all. The page catches F12 before WebView2 does, so it doesn't open WebView2's own
DevTools; Ctrl+Shift+I still does, for working on the page.

For direct calls (no account) the other side dials this PC's address, which the note on
*Manage accounts* gives and copies (see [SIP accounts](DESIGN.md#sip-accounts)). To register with a server, add an account with Manage accounts (see [SIP accounts](DESIGN.md#sip-accounts)), or seed one
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
| `--video` | Enter places a video call (as Ctrl+Enter does), and so does `--dial`. |
| `--test-pattern` | Send the test pattern instead of the camera. |
| `--audio-bars` | Send the microphone's frequency bars instead of the camera. |
| `--diagnostics` | Open the diagnostics panel (F12) at start. |
| `--loss-audio <percent>`, `--loss-video <percent>` | Start with the Network tab's loss sliders at these values. |
| `--ws-port <n>` | SIP over WebSocket port for browser phones (default 8081, 0 for none). |
| `--webrtc` | Calls we place use WebRTC media whatever the transport, for browsers registered with a server. A plain SIP phone won't take them. (Will be per account.) |
| `--no-srtp` | No SRTP offered on plain SIP calls (see [SRTP](DESIGN.md#srtp)); an offer that requires it (`RTP/SAVP`) is still answered with it. |
| `--no-text` | No real-time text: it's left out of offers and answers (no `m=text`, and on WebRTC calls no T.140 data channel), so the other side adding it is turned down. (Will be per account.) |
| `--say <text>` | Once text is up on a call, send this message. |
| `--dtmf <keys>` | Once a call's audio is up, open the keypad and press these keys (0–9, \*, #), as typed keys, 2 s in and one every 400 ms. |
| `--no-video` | No video at all: the switch is locked to Audio, and video offers or Add video from the other side are turned down. (Will be the per-account "Video calls" setting.) |
| `--camera-off` | Video calls you place start with your camera off (your camera-off card until Start video). |
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
