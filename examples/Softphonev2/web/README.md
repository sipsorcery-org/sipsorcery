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
`api=stub` uses a stub instead, which makes the account up in the browser and connects it to
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
  button switching an Audio call to Video before dialling, the device menus, self view,
  ringing, answer, decline, cancel, hang up, the SIP log (F12).
- **Settings** (the gear, top right): the desktop page's own (see the desktop README), kept in
  this browser's `localStorage` (`softphonev2.settings`, and `devices` and `selfView` as before).
  Devices, camera on at the start of video calls, self view, auto-answer (camera off), theme and
  a button for diagnostics, handy since a browser may keep F12 for its own tools. `host.js` sets
  `data-host="web"` on the page, so the Account section points at the account button instead of
  the desktop's `softphone.json`. The speaker list is hidden where the browser can't pick one
  (no `setSinkId`). Device names show once the page has had the microphone or camera.
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
- **Call quality** works as on the desktop. There the host reads the far end's RTCP on the SIP
  leg; here the page's own `RTCPeerConnection` is the media leg and the browser reads their
  reports, so `host.js` finds that connection (it wraps the `RTCPeerConnection` constructor
  before `app.js` loads) and posts the same `quality` messages from `getStats()`: each new
  `remote-inbound-rtp` report for ↑ Sent (loss, jitter, round trip), and our received video's
  loss every 5 s for ↓ Received.
- The desktop shows up as `sip:thisis@anonymous.invalid` when it calls without an account: that's
  the From SIPSorcery sends by default.

## What's been checked

Desktop app with `--fake-media`, the web page in Edge with Chromium's fake camera and
microphone, both on one PC.

| Test | Result |
|---|---|
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
| Web `dial=desktop&call=video`, Call details | "Outgoing · web@localhost", "Media encrypted (SRTP); SIP not (WS)", video "VP8 640×360", SIP "WS localhost:8091". (It said "Incoming" and showed the WebSocket URL before.) |
| Settings (`server=ws://127.0.0.1:8099`, nothing there): the gear, Dark, auto-answer on, camera on at start off, Open diagnostics, then a hard reload | Settings opens over the page; the page goes dark at once; Open diagnostics closes Settings and opens the panel, whose log shows each change. After the reload the theme and both choices are kept. The device lists say *System default* with the note about names (the browser pane allows no microphone). |
| Web `server=ws://127.0.0.1:8382&dial=setb`, the desktop copy with *Answer calls automatically* ticked in its Settings | The desktop answers after a second: "answering automatically, camera off", audio only. Settings on the web page shows `web@127.0.0.1` under Account. |
| A second browser dials the desktop while it's on a call | "486 BusyHere". (It said "They hung up" before: SIP.js ends the session before handing over the rejection.) |
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
