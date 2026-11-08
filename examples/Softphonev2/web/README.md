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
  ("Save these somewhere…"), keeps them in this browser and registers. A new account is kept
  even if registering fails, since its password exists nowhere else.
- **Sign in**: user name and password (and, under Server, the domain and WebSocket address) for
  an account you already have, such as a test account saved earlier. SIP.js answers the
  registrar's digest challenge. It's kept only once registering works; otherwise the dialog says
  why ("Couldn't reach …", "Wrong user name or password.").
- **The header chip** shows the account with a dot (green registered, amber registering, red
  failed, with the reason in its tooltip and menu). Its menu has Copy my address, Show account
  details (the saved password again) and Sign out, which unregisters and forgets the account.
  Signing out of a test account asks first, since its password may not be saved anywhere.
- The account lives in `localStorage` (`softphonev2.account`), not a cookie: a cookie would go
  to the web server with every request, and the page's own script needs the password for the
  SIP digest.

Guest accounts are created and limited on the server (sipsorcery-servers2): all owned by one
`guest` owner with no providers or dial plans, so they can call SIPSorcery users and SIP
addresses but nothing that costs money, a few per IP per hour, deleted after 30 days unused.
**Until that endpoint is live, the page uses a stub** that makes the account up in the browser
and connects it to the desktop app's WebSocket (`ws://localhost:8081`) without registering: the
desktop isn't a registrar, so it can call the desktop but nothing can call it ("Calls out only"
in the chip's menu). Pass `api=https://…` to use the real endpoint.

## Running it

1. Start the desktop app. Its SIP log starts with "browser phones: connect to ws://…:8081".
2. Serve this folder's parent (`examples/Softphonev2`) over HTTP, from the same PC:

   ```bash
   python -m http.server 5181 --bind 127.0.0.1 --directory examples/Softphonev2
   ```

3. Open <http://localhost:5181/web/index.html> and click **Create a test account**. With the
   stub, it connects to the desktop app as e.g. `sip:brave-falcon-5128@localhost`; the header
   chip turns green ("Calls out only").
4. From the web page, call `desktop` (any user name reaches the desktop app). The desktop can
   only call the web page once both are registered with a SIP server, and with `--webrtc`.

Or skip accounts: `?server=ws://localhost:8081` connects straight away as `sip:web@localhost`
(`user=` to change it), calls out only.

The page has to be on `localhost` (or `https`) for the browser to give it the camera and
microphone, and the desktop app only listens on `ws://` for now, so the browser runs on the
same PC as the desktop app.

Query parameters (the page's own, like `diag=1`, `camera=off` or `call=video`, work too):

| Parameter | What it does |
|---|---|
| `server=ws://host:port` | No account: connect to the desktop app at this address, without registering (calls out only). |
| `user=web` | With `server=`, our user name: we're `sip:<user>@<server host>`. |
| `api=https://…` | The guest-account API. Left out, the stub. |
| `captcha=<site key>` | Turnstile's site key (default: Turnstile's test key, which always passes). |
| `stubws=ws://…` | Where the stub's accounts connect (default `ws://<this page's host>:8081`). |
| `wsdefault=wss://…` | The sign-in form's default WebSocket address (default `wss://sipsorcery.com`). |
| `name=…` | Our display name. |
| `dial=desktop` | Call this once registered or connected (with `call=video`, a video call). |
| `auto=1` | Answer incoming calls after a second. |
| `hangup=<s>` | Hang up calls this many seconds after they connect. |

## What works, and what doesn't yet

- Audio and video calls both ways, with the desktop's controls: mic and camera on and off, the
  device menus, self view, ringing, answer, decline, cancel, hang up, the SIP log (F12).
- **Not yet:** reconnecting when the WebSocket drops (the chip goes red and stays red until a reload), real-time text (the popover says it isn't available; it'll be a data channel),
  adding video to an audio call (refused either way), the Network tab's loss sliders, and
  Call quality's ↑ Sent column (the desktop host fills it from RTCP; here it would come from the
  page's own `remote-inbound-rtp` stats).
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

Not tested yet: the real guest-account endpoint and captcha, a wrong password, registering with
sipsorcery.com over `wss`, and calls made with a test account (the call path itself is the one
tested above).

## Things learned building it

- **The page tore its connection down on every render before the call connected.** On the
  desktop the media offer only comes once the call is up, so closing media whenever the call
  wasn't "connected" was harmless. Here the page sets up its connection while calling or
  ringing, and `render()` closed it a moment later. `createOffer` on a connection closed under it
  never settles, so the call just sat at Calling. Media now closes only when idle or ended.
- **A WebRTC answer can't add a stream.** The desktop answered the browser's audio-only offer
  with its video too (an extra m-line), and the browser rejected the answer ("the order of
  m-lines… doesn't match"). The desktop now answers browser calls with video only if offered.
