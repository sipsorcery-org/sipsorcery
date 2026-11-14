# Softphone v2 – web softphone

The desktop softphone's page, running in an ordinary browser, so the desktop app can be tested
against a browser phone without Asterisk or FreeSWITCH. It registers with a SIP server
(sipsorcery.com) over `wss`, or connects straight to the desktop app's SIP WebSocket to call it
(see the desktop README, "Browser phones").

Nothing here is a copy of the desktop's UI. `index.html` sets its base to `../desktop/wwwroot/`,
and `host.js` loads that page's markup, styles and `app.js` as they are. What `host.js` adds is
a stand-in for the desktop's C# host:

- **`chrome.webview`**: the page talks to its host with WebView2's `postMessage`; `host.js`
  provides the same object and answers the same messages (`dial`, `answer`, `hangup`, `state`,
  `log`…).
- **SIP**: [SIP.js](https://sipjs.com) 0.21.2, from jsDelivr, over WebSocket.
- **Media**: on the desktop the page's `RTCPeerConnection` goes to the host's relay; here it
  goes straight to the far end. A SIP.js session description handler passes SDP between SIP.js
  and the page: an incoming offer goes to the page as `media-offer` and its `media-answer` goes
  back in the 200 OK. For a call we place, the page makes the offer (`media-request` →
  `media-offer-made`) and gets the 200 OK's answer as `media-remote-answer`. Those two messages
  are the page's only change for the web.

## Accounts

The web softphone registers with a SIP account, normally on sipsorcery.com over `wss`
(`account.js`):

- **Create a test account**: a captcha (Cloudflare Turnstile), then
  `POST <api>/api/guest-accounts { captchaToken }` returns a new guest account,
  `{ username, password, domain, wsUri, expiresAfterDays }`, with an easy-to-say user name like
  `brave-falcon-5128`. The page shows the user name, password and address with Copy buttons
  ("Save these somewhere…"; the password is dots until its eye button shows it), keeps them in
  this browser and registers. A new account is kept
  even if registering fails, since its password exists nowhere else.
- **Sign in with GitHub**, under it: a guest account named after your GitHub login, lower-cased
  (`sips:octocat@sipsorcery.com`), with no captcha. The server ties it to your GitHub user, so
  signing in with GitHub again, in any browser, gets the same account and password back. Someone
  who renames on GitHub and signs in again gets a new account under the new login, with a new
  password, and the old one is deleted (even when the new name can't be had). The server refuses (the dialog shows its sentence)
  when the name is taken (a customer's user name or GitHub login, any account on the domain,
  another guest). Every GitHub login fits: SIP user names take up to 39 characters, GitHub's
  longest, and the chip cuts a long one short with "…" on a phone. The flow is a popup and `postMessage`, since this page
  is on another origin (no cookies or CORS):
  1. In the click itself (or the browser blocks it: "Allow pop-ups for this page to sign in with
     GitHub."), a popup opens `<api>/api/guest-accounts/github?origin=<this page's origin>`. The
     origin must be one of the server's `GuestAccounts:AllowedOrigins`, or the popup shows a 400.
  2. After GitHub, the server's page posts `{ type: "sipsorcery-guest-account", account: { username,
     password, domain, wsUri, expiresAfterDays, github } }`, or `{ type, error }`, and closes.
  3. A message counts only from the server's origin, from that popup, with that `type`. The popup
     closed with nothing posted (checked every 0.5 s) is a cancel: the dialog goes back as it was,
     with no error.

  The account is then shown as "Your GitHub test account" ("Signed in with GitHub as octocat…
  signing in with GitHub again, here or in any other browser, gets it back"), kept with `github`
  set, and registered, as a captcha one is; a different one (renamed) replaces the saved one.
  Signing out of it doesn't ask first. A saved GitHub account the server turns down (401, 403 or
  407 to REGISTER: most likely replaced after a rename, signed in elsewhere) opens the sign-in
  dialog at start-up, saying "The server didn't take octocat's password. If you've renamed on
  GitHub, signing in with GitHub replaced this account…: sign in with GitHub again to get it.",
  with the GitHub button focused; the chip's menu has *Sign in with GitHub again* while it's like
  that. (A captcha account turned down just shows the failure, as before.) The server's
  side, and setting up its GitHub OAuth app, are in sipsorcery-servers2's
  `src/SIPSorcery.Web/README.md`, "GitHub guest accounts".
- **Sign in**: user name and password (and, under Server, the domain and WebSocket address) for
  an account you already have, such as a test account saved earlier. SIP.js answers the
  registrar's digest challenge. It's kept only once registering works; otherwise the dialog says
  why ("Couldn't reach …", "Wrong user name or password.").
- **The header chip** shows the account with a dot (green registered, amber registering, red
  failed, with the reason in its tooltip and menu). Its menu has Copy my address, Show account
  details (the saved password again) and Sign out, which unregisters and forgets the account.
  Signing out of a test account asks first, since its password may not be saved anywhere.
  A sipsorcery.com address is shown and copied as `sips:` (e.g. `sips:brave-falcon-5128@sipsorcery.com`):
  a desktop phone calling it over UDP sends an INVITE with video too big for one packet, and
  sipsorcery.com doesn't get the fragments. The page itself still registers as `sip:`. Diagnostics
  doesn't show the desktop's address pill here: the chip already says who you are.
- The account lives in `localStorage` (`softphonev2.account`), not a cookie: a cookie would go
  to the web server with every request, and the page's own script needs the password for the
  SIP digest.

Guest accounts are created and limited on the server (sipsorcery-servers2): all owned by one
`guest` owner with no providers or dial plans, so they can call SIPSorcery users and SIP
addresses but nothing that costs money, a few per IP per hour, deleted after 30 days unused.
The page creates them on sipsorcery.com (`https://www.sipsorcery.com/api/guest-accounts`).
`api=stub` uses a stub instead (Sign in with GitHub too: no popup, the login `octocat`, or
`github=<login>`), which makes the account up in the browser and connects it to
the desktop app's WebSocket (`ws://localhost:8081`) without registering: the desktop isn't a
registrar, so it can call the desktop but nothing can call it ("Calls out only" in the chip's
menu). A test account kept in the browser is only used with the `api=` that created it, so one
from the stub or a local server isn't tried against sipsorcery.com.

## Running it

1. Start the desktop app. Its SIP log starts with "browser phones: connect to ws://…:8081".
2. Serve this folder's parent (`examples/Softphonev2`) over HTTP, from the same PC:

   ```bash
   python -m http.server 5181 --bind 127.0.0.1 --directory examples/Softphonev2
   ```

3. Open <http://localhost:5181/web/index.html?api=stub> and click **Create a test account**.
   With the stub, it connects to the desktop app as e.g. `sip:brave-falcon-5128@localhost`; the header
   chip turns green ("Calls out only").
4. From the web page, call `desktop` (any user name reaches the desktop app). The desktop can
   only call the web page once both are registered with a SIP server, and with `--webrtc`.

Or skip accounts: `?server=ws://localhost:8081` connects straight away as `sip:web@localhost`
(`user=` to change it), calls out only.

**Against a local sipsorcery-servers2** (the web app on `https://localhost:7443`, the proxy's
WebSocket on `ws://localhost:8090`):
<http://localhost:5181/web/index.html?api=https://localhost:7443&ws=ws://localhost:8090&captcha=1x00000000000000000000AA>.
`ws=` is needed because the server hands out its `GuestAccounts:WebSocketUri`, normally
`wss://sipsorcery.com`. The server's `AllowedOrigins` has to let `http://localhost:5181` in
(Development allows the local origins by default). To call the desktop app this way, give it an
account on the same server, `"Account": { "Server": "localhost", … }` (the local registrar takes
`localhost` as an alias for `sipsorcery.com`), with `--port` off 5060 since the proxy has it, and
`--webrtc` for its own calls to browsers.

The page has to be on `localhost` (or `https`) for the browser to give it the camera and
microphone, and the desktop app only listens on `ws://` for now, so the browser runs on the
same PC as the desktop app.

Query parameters (the page's own, like `diag=1`, `camera=off` or `call=video`, work too):

| Parameter | What it does |
|---|---|
| `server=ws://host:port` | No account: connect to the desktop app at this address, without registering (calls out only). |
| `user=web` | With `server=`, our user name: we're `sip:<user>@<server host>`. |
| `api=https://…` | The guest-account API (default `https://www.sipsorcery.com`). `api=stub`: the stub. |
| `captcha=<site key>` | Turnstile's site key (default: sipsorcery.com's; with `api=stub`, Turnstile's test key, which always passes). A local server wants the test key: `captcha=1x00000000000000000000AA`. |
| `stubws=ws://…` | Where the stub's accounts connect (default `ws://<this page's host>:8081`). |
| `ws=ws://…` | Register guest accounts through this WebSocket instead of the one the server returns (testing against a local server). |
| `wsdefault=wss://…` | The sign-in form's default WebSocket address (default `wss://sipsorcery.com`). |
| `name=…` | Our display name. |
| `dial=desktop` | Call this once registered or connected (with `call=video`, a video call). |
| `auto=1` | Answer incoming calls after a second. |
| `hangup=<s>` | Hang up calls this many seconds after they connect. |

## What works, and what doesn't yet

- Audio and video calls both ways, with the desktop's controls (the same page, so they change
  together): Mute / Unmute with the *You're muted* pill, Stop video / Start video, the camera
  button switching an Audio call to Video before dialling, the device menus, self view (which
  you can drag, resize from its corner and double-click to reset),
  ringing, answer, decline, cancel, hang up, the SIP log (F12).
- **The privacy switch**, *Block Audio and Video Devices* (the block sign, top right, just left of Settings: click to arm, click again; or `Shift+M`): every call muted with its
  camera off, new calls too, until you press it again, unmute a call or turn a camera on. Never
  remembered. See the [desktop README](../desktop/README.md#the-privacy-switch).
- **Settings** (the gear, top right): the desktop page's own (see the desktop README), kept in
  this browser's `localStorage` (`softphonev2.settings`, and `devices` and `selfView` as before;
  `selfViewBox` is where you've dragged the self view and how large, as in the desktop README).
  Devices, theme, Gravatar pictures and a button for diagnostics, handy since a browser may keep
  F12 for its own tools. (Who you are is the account button's, at the top.) The speaker list is hidden where the browser can't pick one
  (no `setSinkId`). Device names show once the page has had the microphone or camera.
- **Several calls at once**, as on the desktop (see the [desktop README](../desktop/README.md#several-calls)):
  each call is a SIP.js session with its own record in `host.js` and its own connection on the
  page (`new RTCPeerConnection({ callId })`, which the wrapper uses to tell them apart), and
  messages about a call carry its id. An incoming call rings alongside the others (486 only
  past 8). Hold is a re-INVITE whose offer the page makes on the call's own connection
  (sendonly, or sendrecv to resume); a re-INVITE from the far end is answered on it too, rather
  than with a new connection. Hold is music on hold, as on the desktop by default: the page
  sends music (and an "On hold" card on a video call) and nothing is signalled; `?hold=sip`
  sends re-INVITEs instead, one at a time per call, as on the desktop. When the far end holds
  us with a re-INVITE, the browser's connection is recvonly, so unlike the desktop nothing is
  sent until they resume.
- The **Active calls** panel down the left (see the [desktop README](../desktop/README.md#active-calls)):
  every call, shown with two or more calls (a rail on narrow windows).
- **Focus and grid views**, the desktop page's (see the [desktop README](../desktop/README.md#focus-and-grid-views)):
  the Grid button under the calls, or G, remembered.
- **A call it can't take doesn't ring.** A browser can only answer a WebRTC offer, so an
  incoming offer with no DTLS-SRTP m-line (an ordinary SIP phone's RTP/AVP) is turned down
  before ringing: `488 Not Acceptable Here` with `Warning: 302 <host> "Incompatible transport
  protocol: WebRTC media only"` and an SDP of what we can take (a receive-only offer from a
  throwaway `RTCPeerConnection`, made once at start). The desktop app takes that as its cue to
  call again with WebRTC. SIP.js would normally send 180 before the page sees the INVITE, so
  `sendInitialProvisionalResponse` is off and the page sends the 180 itself once the offer is
  fine. An INVITE with no SDP rings as usual: our offer goes in the 200.
- **Real-time text** goes on a T.140 data channel on the page's own connection (RFC 8865; the
  desktop does the same on its WebRTC calls, see its README). The `RTCPeerConnection` wrapper
  adds a negotiated `t140` channel on stream 1 before the page makes an offer, or, for an offer
  with `a=dcmap:<n> …subprotocol="t140"`, on stream *n* before the page answers. Browsers don't
  write a dcmap, so `PageMedia` adds `a=dcmap:<n> label="t140";subprotocol="t140"` to the
  page's `m=application` line on its way to SIP.js. The page's `text` messages go out on the
  channel as strings and what arrives comes back as `text` messages, so the popover is the
  desktop's. The state the page sees: adding while the channel opens, on once it's open and
  their SDP has the dcmap, refused if it doesn't (or the channel doesn't open in 10 s).
- **Not yet:** reconnecting when the WebSocket drops (the chip goes red and stays red until a reload),
  adding text to a call that started without it, and adding video to an audio call (refused either way).
- **Simulated loss** (F12, Network) drops frames rather than packets. The desktop host drops RTP
  packets on the SIP leg, but a page can't reach RTP packets, so `host.js` puts an encoded
  transform (`RTCRtpScriptTransform`, in a worker) on each sender that drops that share of the
  encoded frames before the browser packetises them. An audio frame is one packet, so the far
  end hears the same gaps; a dropped video frame leaves its decoder without a reference, so it
  freezes and asks for a keyframe. But no sequence numbers go missing, so the far end's RTCP
  shows no loss. Chrome only passes frames through a transform that's on the sender before the
  media is negotiated, so every sender gets one as it's made (passing every frame at 0%), from
  the same `RTCPeerConnection` wrapper as Call quality. Every 5 s with loss on, the log says how
  many frames were dropped. In a browser without `RTCRtpScriptTransform` the sliders are off.
- **The call info** is the desktop's (the page is shared): a header across the top of the stage
  on a connected call (see the [desktop README](../desktop/README.md#call-details)), and the
  call's details are a Diagnostics tab, *Call details*.
- **Call quality** works as on the desktop, in its own Diagnostics tab, with both directions' graphs. There the host reads the far end's RTCP on the SIP
  leg; here the page's own `RTCPeerConnection` is the media leg and the browser reads their
  reports, so `host.js` finds that connection (it wraps the `RTCPeerConnection` constructor
  before `app.js` loads) and posts the same `quality` messages from `getStats()`: each new
  `remote-inbound-rtp` report for ↑ Sent (loss, jitter, round trip), and our received video's
  loss every 5 s for ↓ Received.
- **"X isn't getting your audio"** (one-way audio, the desktop's red row under the bars) comes
  from the same stats: a report from them (their `remote-outbound-rtp` moves on) that doesn't
  list our audio (our `remote-inbound-rtp` doesn't), after we've sent 100 audio packets since one
  last did. Chromium has no `reportsReceived` there, so a new report is told by its timestamp.
  The desktop also catches a listing whose highest sequence number has stopped; the browser's
  stats don't give that number, so that case isn't caught here.
- **H264 first**, as on the desktop: the browser's own offer has VP8 first, so the
  `RTCPeerConnection` wrapper's `createOffer` calls `setCodecPreferences` on each video
  transceiver with H264 (Constrained Baseline `42e01f`, packetization-mode 1, ahead of the other
  H264 profiles) before the rest. An answer follows the offer's order, as the browser does anyway.
- **Keypad tones from them** show on the keypad ("Bob sent 159#") as on the desktop. Tones in
  their audio (RFC 4733) the browser plays but doesn't report, so each audio receiver gets an
  encoded transform too (the simulated-loss worker, passing every frame on) that looks for
  frames with the call's `telephone-event` payload type and reports each event once, by its
  RTP timestamp. SIP INFO tones (`application/dtmf-relay` or `application/dtmf`) are answered
  200 and shown, other INFO 415. Like simulated loss, it needs `RTCRtpScriptTransform`.
- **Not here:** call transfer (the page hides Transfer: the host says `canTransfer` only on the
  desktop) and *Manage accounts* / *Call from* (one account, the chip's).
- **Being transferred** (a REFER on a call) is turned down at once, `603 Decline`, and the call
  header's status says so for 8 s: "Transfer to Dave failed: this phone can't take transfers"
  (the full sentence its tooltip; × drops it sooner). Without an `onRefer`
  handler SIP.js accepts a REFER (202) and quietly places a call of its own the page never sees,
  sending no NOTIFYs, so the transferor waited a minute for "No word from … on how the transfer
  went". Now theirs says straight away that the transfer was turned down.
- The desktop shows up as `sip:thisis@anonymous.invalid` when it calls without an account: that's
  the From SIPSorcery sends by default.
- **On a phone** (360–430 px): making calls works from a page kept open; receiving calls, or a
  call surviving a locked screen, needs a native app.
  - **The layout** (500 px and under, in the desktop's `styles.css`; the desktop window is never
    that narrow): the header is the mark, "SIPSorcery" and its buttons; the dial bar is two rows,
    People and the address (its hint shortened to "Name, number or SIP address"), then Audio ◯━
    Video and a wide Call; the control bar's buttons are icons only (their words kept for screen
    readers), 44 px tall for a finger. Text fields are 16 px, so iOS doesn't zoom in on them, and
    the page is the height that's really visible (`100dvh`), so the address bar can't push the
    control bar off the bottom. With the privacy switch armed or on, the name goes to make room
    for its words, which are cut short with "…" if they still don't fit ("Mic and Cameras
    Bloc…"). Diagnostics' bar is the icon (no "Diagnostics"), the four tabs and ×, without the F12
    key (a phone has none); the tabs scroll sideways if they must. Call quality's charts are the
    screen's width, one above the other.
  - **The screen stays on** while a call is calling, ringing or connected (the Screen Wake Lock
    API, any browser that has it, desktops too). A browser drops the lock while the page is
    hidden; it's asked for again on coming back, and let go when the last call ends. The SIP log
    says each.
  - **Keep the page open**: a phone's browser stops the microphone, and soon the page, when you
    switch apps or lock the screen, so the call goes one way or drops, and no web page can stop
    that. On a phone (a touch screen and no mouse: `pointer: coarse`, no `hover`), once a call
    connects, a note over the bottom of the stage says so ("Keep this page open during the call:
    switching apps or locking the phone can cut off your audio or end the call.") until *Got it*,
    remembered in `localStorage` (`backgroundNoteSeen`). Back from the background mid-call, it
    says how long ("This page was in the background for 25 s, so they may not have heard you
    then."), with a red edge, whether or not you'd seen the first.
  - **Not here yet**: STUN and TURN (`host.js` gives the connection no ICE servers), so over mobile
    data, behind the carrier's NAT, media will often fail or go one way; the page needs HTTPS and
    `wss://` on a phone (the microphone needs a secure page, and a secure page can't open `ws://`);
    and the browser picks the speaker, earpiece or Bluetooth.

## What's been checked

Desktop app with `--fake-media`, the web page in Edge with Chromium's fake camera and
microphone, both on one PC.

| Test | Result |
|---|---|
| A saved GitHub guest turned down: a fake registrar (`ws://127.0.0.1:8395`, every REGISTER 403) for a saved account with `github` set; then the same without `github`; then Sign in with GitHub from that dialog (the fake portal posting `octocat` with a new password) over a saved `oldname` | GitHub guest: the chip "failed", the sign-in dialog open with "The server didn't take octocat's password. If you've renamed on GitHub…", focus on Sign in with GitHub; the chip's menu: Copy my address, *Sign in with GitHub again*, Show account details, Sign out; the item opens the same dialog. Captcha guest: no dialog, no item. Signing in with GitHub: "Your GitHub test account", the chip "octocat", the saved account now `octocat` with the new password and WebSocket. |
| Sign in with GitHub against a fake portal (a page on another origin, `127.0.0.1:5190`, standing in for the server's completion page: posts to `window.opener` and closes), `api=http://127.0.0.1:5190`, Edge headless; the click with and without a user gesture; the portal posting an account, an error, a message of another `type`, nothing; with the popup open, a forged message from the page itself; Sign out; then `api=stub&github=Hubot` | No gesture: no popup, "Allow pop-ups for this page to sign in with GitHub." With one: the popup at `…/api/guest-accounts/github/?origin=http%3A%2F%2F127.0.0.1%3A5181`, the button "Waiting for GitHub…" and both disabled. An account: "Your GitHub test account", "Signed in with GitHub as octocat…", saved with `github: "octocat"`, `guest`, `api`. An error: its sentence under the buttons, the buttons back. Another `type`, or nothing: once the popup closes, the dialog as before with no error. The forged message: ignored (still waiting, nothing saved). Sign out: no confirm, account forgotten. Stub: no popup, "hubot", connected (calls out only). Not tested against the real server, which isn't deployed. |
| Phone layout: Edge headless emulating a touch phone (375×812 and 360×740, mobile, touch), `?server=ws://127.0.0.1:8392` to a desktop copy (`--ws-port 8392 --auto-answer`); idle, then call `vtB`, light and dark | Idle: no sideways scroll (393 px wide before); the header fits with the gear (it was cut off), the address field full width (before: squeezed to an empty box) with the short hint, Audio ◯━ Video and a wide Call under it; the control bar icons only, End red, all 6 within 348 px at 360. Connected: the call header, the bars, the control bar at the bottom. |
| Phone, the rest: a call at 375 px; the privacy switch armed, then on; then Diagnostics (F12) on Call quality | Before: the page 496 px wide armed, 470 on, and 467 with Diagnostics open (its bar's tabs wrapping to two lines, pushing the charts off the right). After: 375 px each time. Armed or on, "SIPSorcery" goes and the chip reads "Mic and Cameras Bloc…" with Settings still on screen; Diagnostics' bar the icon, SIP log, Call details, Call quality, Network and ×, on one line; the charts 351 px, the screen less its margins. |
| Screen on and the background note: the same, with `backgroundNoteSeen` cleared; *Got it*; the page faked hidden for 3 s (`visibilityState`, `visibilitychange`) mid-call; End | Log "keeping the screen on for the call" as it rang; once connected the note over the bottom of the stage, above the control bar. *Got it* hides it and sets `backgroundNoteSeen`. Back after 3 s: "the page went to the background during a call", "the page is back from the background (3 s)", and the note's "This page was in the background for 3 s…" with a red edge. End: the note goes, "the screen can turn off again". Not tested on a real phone: what iOS and Android actually do to the call in the background. |
| Active calls, `?server=ws://127.0.0.1:8092` to a desktop copy with `--auto-answer` (Claude's browser pane, mic blocked) | "B · Audio call" with the timer; collapsed to the rail it keeps ticking, and after hanging up and a hard reload it's still the rail. At 375 px it's the rail, with no sideways scroll; expanding lays it over the stage and Esc folds it back. |
| Web `dial=desktop&call=video`, desktop `--auto-answer` | Registered, INVITE with the page's offer, ICE and DTLS connected in 0.2 s. Video both ways (the desktop receives 20 fps, then 1280×720; the web 30 fps), audio 50 pkt/s each way with no loss. Web's Call quality ↓ 4.4. |
| Desktop dials `web` (video), web `auto=1`, with the desktop's old test registrar (since removed) | The desktop's offer goes to the page; answered, media both ways. Desktop End: BYE, 200; desktop "You hung up", web "They hung up". |
| No account: Create a test account | Turnstile's test key passes; the stub's account (`brave-falcon-5128`) is shown with Copy buttons, and registered with the desktop's old test registrar (now it connects without registering). The chip goes green. |
| Reload | The kept account registers again without the dialog. |
| Chip menu, Sign out | The menu shows the address and "Ready for calls"; Sign out unregisters, empties `localStorage` and shows the dialog. |
| Sign in with the WebSocket address `ws://localhost:8099` (nothing there), then `:8081` | "Couldn't reach ws://localhost:8099." and nothing kept; then registered and kept. |
| Web `dial=desktop&hangup=12` (audio only) | Audio only both ways; after 12 s the web sends BYE: web "You hung up", desktop "They hung up". |
| Local sipsorcery-servers2, `api=https://localhost:7443&ws=ws://localhost:8090`: Create a test account | Turnstile's test key passes; the server returns a guest account (`brisk-gibbon-8621@sipsorcery.com`), shown with Copy buttons; it registers through the proxy's `ws://localhost:8090` and the chip goes green. |
| Headless Edge on a guest account dials the desktop app's guest account (registered over UDP, `--webrtc`), video | Through the proxy: the INVITE reaches the desktop over UDP with a `UDP/TLS/RTP/SAVPF` offer, so it answers with WebRTC media. ICE and DTLS connected in 0.15 s; the desktop receives 20 fps 640×360 video, audio 50 pkt/s with no loss. Web BYE after 10 s: "They hung up". |
| The desktop (`--webrtc --video`) dials the browser's guest account, browser `auto=1` | Through the proxy to the browser's WebSocket; answered, WebRTC media connected in 0.2 s, video both ways. Web BYE after 10 s. |
| Web `dial=desktop&call=video`, desktop `--auto-answer --loss-audio 10`, Call quality after 30 s | ↑ Sent: MOS 4.4, loss 0%, 20 fps, from the desktop's RTCP ("their RTCP on our audio: loss 0%… rtt 1 ms" in the log); ↓ Received: MOS 3.6, 8% audio loss; RTT 1.0 ms. Before, ↑ Sent stayed empty and said "No RTCP reports from them". |
| Headless Edge `server=ws://127.0.0.1:8392&dial=vtB`, a desktop copy with `--fake-media --auto-answer --dtmf 159#`, keypad opened as the call connects | Web log "DTMF '1' received (RFC 4733)", then 5, 9, # 400 ms apart, each once; the keypad says "VtB sent 159#". Over the next 25 s no "isn't reaching them" (each of their reports lists our audio). |
| Same call, the connection's `getStats` patched to leave out our audio's `remote-inbound-rtp`, then restored | Their next report but one: "their RTCP says our audio isn't reaching them", the bars' note "VtB isn't getting your audio"; restored, "…reaching them again" and the note clears. (Before, Edge's missing `reportsReceived` made every report look like it left us out.) |
| SIP INFO parsing, `onInfo` on its own (the desktop sends INFO only when `telephone-event` isn't agreed, and the browser always offers it) | `dtmf-relay` "Signal=5", "Signal= #" and `dtmf` "9": 200 and shown; `media_control+xml` and a `dtmf-relay` with no Signal: 415. |
| Web `server=ws://127.0.0.1:8392&dial=vtB`, the padlock on both ends | "Media encrypted (DTLS + SRTP); SIP unencrypted (WS)" on the web page and on the desktop copy: the host's details say how the media's encrypted (`mediaSecurity`), always DTLS + SRTP here. |
| Web `dial=desktop&call=video`, Call details | "Outgoing · web@localhost", "Media encrypted (SRTP); SIP unencrypted (WS)", video "VP8 640×360", SIP "WS localhost:8091". (It said "Incoming" and showed the WebSocket URL before.) |
| Settings (`server=ws://127.0.0.1:8099`, nothing there): the gear, Dark, auto-answer on, camera on at start off, Open diagnostics, then a hard reload | Settings opens over the page; the page goes dark at once; Open diagnostics closes Settings and opens the panel, whose log shows each change. After the reload the theme and both choices are kept. The device lists say *System default* with the note about names (the browser pane allows no microphone). |
| Web `server=ws://127.0.0.1:8382&dial=setb`, the desktop copy with *Answer calls automatically* ticked in its Settings | The desktop answers after a second: "answering automatically, camera off", audio only. Settings on the web page shows `web@127.0.0.1` under Account. |
| A second browser dials the desktop while it's on a call | Before several calls: "486 BusyHere". (It said "They hung up" before: SIP.js ends the session before handing over the rejection.) Now it rings as a second call. |
| Several calls: `?server=ws://localhost:8192&user=web&diag=1` in Claude's browser pane, dial `b` (a desktop copy with `--auto-answer`), then End | No console errors. Call 1 listed, its offer and answer passed with its id; the call connects and ends ("You hung up"). The media fails as before in the pane (mic blocked: "Incompatible send direction"). |
| Focus and grid: the same page after the views were added | No console errors; registered (`web@localhost`); the switch hidden with no call, Settings has *Show all calls in a grid*. (The views were tried on the desktop copies: the pane has no microphone for a call with media.) |
| Web `server=ws://localhost:8095&diag=1&call=video&video=test&dial=desktop`, desktop `--fake-media --auto-answer --test-pattern`; Network sliders to 10% mid-call, then 0% | Web log: "dropped 27 of 250 audio frames, 15 of 151 video frames in 5 s" and so on. The desktop receives 44–45 audio pkt/s instead of 50, with concealment climbing, and 14–17 fps video instead of 30; its page asks for keyframes (55, then 75 PLIs). Their RTCP still says 0% loss. Back to 0%: 30 fps and 50 pkt/s again. |
| Web dials the desktop (`server=`), then reloads mid-call | The page hangs up as it unloads: the desktop gets a BYE ("They hung up") and answers the reloaded page's new call at once. Before, the page sent no BYE: the desktop stayed in a call with closed media and turned every new call down with 486. |
| Web dials the desktop (`server=`), then its `RTCPeerConnection` is closed with no BYE | The desktop logs "WebRTC media (ICE, DTLS) closed", hangs up a second later ("Media connection closed") and its BYE reaches the page over its WebSocket connection (200 OK; web "They hung up"). Before, it stayed in the call, and its BYEs, End button's included, went to the page's `.invalid` Contact and never arrived. |
| Headless Edge (fake devices), `server=ws://localhost:8291&diag=1&dial=desktop&say=hi_from_web`, desktop `--auto-answer --say hi_from_desktop` | The offer's `m=application` has `a=dcmap:1 label="t140";subprotocol="t140"`, and so does the desktop's answer. Web log: "real-time text on (RFC 8865, T.140 data channel on stream 1)" 40 ms after connecting, then "real-time text in: 17 characters"; the popover shows "You: hi_from_web" and "Desktop: hi_from_desktop". The desktop logs text on and "real-time text in: 13 characters". |
| Same, desktop `--no-text` | Its answer has `m=application` but no dcmap: web "they didn't take text (no T.140 data channel in their SDP)", and the popover says their phone doesn't support it. |
| Answering: in the page, a plain `RTCPeerConnection` offers a negotiated `t140` channel on stream 3 (dcmap added) to the wrapped one | The wrapper opens stream 3 before answering, the answer keeps `m=application`, it counts as agreed, and "abc" + U+2028 arrives on it. (A real incoming call with text isn't checked: `server=` takes no calls, and it'd need the desktop to call a registered browser.) |
| In the browser pane (microphone blocked) | The call connects but the page's answer fails, "Incompatible send direction" (its offer is receive-only audio; nothing to do with text), so no media and no text: the T.140 channel times out after 10 s and shows refused. Use a browser with a microphone, or fake devices. |
| The desktop without `--webrtc` dials it (RTP/AVP offer) | Turned down with 488 + Warning 302 + SDP, no 180 from the browser ("turned down a call from … its offer isn't WebRTC"). The local app server sends its own 180 when it starts dialling, and passes on only the 488's status, so the desktop doesn't retry yet. |

Not tested yet: a real Turnstile site key and secret, a wrong password, the live sipsorcery.com
over `wss`, and a browser on another network (we offer host ICE candidates only).

## Things learned building it

- **The page tore its connection down on every render before the call connected.** On the
  desktop the media offer only comes once the call is up, so closing media whenever the call
  wasn't "connected" was harmless. Here the page sets up its connection while calling or
  ringing, and `render()` closed it a moment later. `createOffer` on a connection closed under it
  never settles, so the call just sat at Calling. Media now closes only when idle or ended.
- **A WebRTC answer can't add a stream.** The desktop answered the browser's audio-only offer
  with its video too (an extra m-line), and the browser rejected the answer ("the order of
  m-lines… doesn't match"). The desktop now answers browser calls with video only if offered.
