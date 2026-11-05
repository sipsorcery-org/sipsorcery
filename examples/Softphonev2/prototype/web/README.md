# Softphone v2 – web UX prototype

A static HTML/CSS/JS mock of the "Quiet workspace" design. There's no SIP or media here. Every
call is simulated so the layout and interaction model can be tried out before choosing the real
web and WPF stacks.

## Run

Any static file server works, for example:

```
python -m http.server 5179 --directory examples/Softphonev2/prototype/web
```

Then open http://localhost:5179.

## What's modelled

- **Active calls.** The left-hand list shows only calls that exist, newest at the top, up to 8.
  A call is dialing, incoming, connected, held or ended. Ended calls drop off the list after a
  moment. Calls are only started from the bar at the top; the main view never dials.
  Next to each connected or held call's name, a mic and a camera icon show what you're sending
  on that call. Red means you turned it off (muted, camera off, or *Mute all* / *Camera off*).
  Faint means it isn't flowing for another reason: audio is on another call, the call is on
  hold, or it's an audio-only call. Hovering over an icon says which. A third icon appears when
  real-time text is on: it pulses while the other person types, and gets a red dot for text you
  haven't read. Meanwhile the status line shows "Typing…" and then their last line.
- **Audio focus.** Only one connected call has mic and speaker at a time. Selecting another
  connected call moves audio to it. The previous call stays up with its video and shows
  "Audio off". **Hold** is separate: it's an explicit SIP re-INVITE and shows "On hold".
- **Focus and grid views.** By default the main view shows only the selected call. The
  *Grid* toggle, or `G`, shows every active call as an equal tile. The tiles have no buttons:
  clicking anywhere on one opens that call in Focus view, and so does clicking it in the Active
  calls list or pressing its number key while the grid is showing. A ringing call shows its
  answer view and leaves the grid choice alone, so answering goes back to the grid. The choice
  is remembered in the browser.
- **Self view.** Your camera preview ("You") sits in the bottom-right of the selected call's
  tile. *Self view* in the info bar under the call, or `V`, hides or shows it. This is
  remembered too.
- **Call controls.** The controls are part of the call card in Focus view, directly under its
  info bar. Grid view is an overview with no controls, so there's never any doubt which call
  a button acts on. Click a call to open it in Focus view and act on it.
  For the same reason `M` and `H` do nothing in Grid view.
- **Mute all / Camera off.** These are in the top bar. They apply to every call, including
  new ones, and turn red while on. A call's own *Unmute* or *Camera* button, pressed while one
  of these is on, re-enables just that call; the others stay muted or off.
- **SIP accounts.** *Call from* shows the chosen account's registration state next to its
  label. *Manage accounts*, under the picker and apart from the new-call controls, switches the
  main view to Accounts; it isn't added to the Active calls list. The view starts on a table
  of every account and its status. Clicking an account opens its settings as a full page;
  *All accounts* or `Esc` goes back. *Back to …* at the top, or clicking any call, steps away
  without losing edits, and an incoming call takes over the same way. Viewing accounts never
  moves audio away from your current call. The × at the top, *Manage accounts* again, or
  `Esc` from the table closes the view. If there are unsaved edits, a dialog asks before
  discarding them.
  *Manage accounts* turns red with an alert icon when an account isn't registered, and gets
  an amber dot when you've left unsaved edits behind. A domain containing `fail` times out on
  register, and Diagnostics can break a registration.
- **Settings.** The gear in the top-right opens a dialog for audio and video devices
  (microphone, speaker, ringtone, camera) and the self-view and grid defaults.
- **Incoming calls.** These appear as a banner on top of the selected call, with
  *Answer & hold current* or *Decline*. Selecting the ringing call gives a full-screen answer
  view, which also offers *Answer, keep current*. When several calls ring at once they share
  one card, "3 calls waiting", instead of a pile of banners. The caller who has waited longest
  is on top, and each row shows how long that caller has been waiting. *+N more* expands the
  queue, and *Decline all* clears it. `A` answers the caller who has waited longest.
- **Hold time.** A held call shows "On hold · 02:10" in the list, on its tile and in the grid.
  After a minute a gentle toast says who's been waiting, with **Resume**, and repeats every two
  minutes. The hold time also turns amber.
- **Call quality and encryption.** Each connected call has signal bars, plus a lock when media
  is encrypted (SRTP). The bars appear in the call list, beside the selected call's timer and on
  grid tiles. Hovering over them, or focusing or tapping the one in the call header, shows packet
  loss and jitter *from them* and *to them*, plus the round-trip time. A plain-words verdict
  answers "is it me or them?": poor only coming from them means their connection, poor going to
  them means your upload, and poor both ways (especially on every call) means your connection.
  The popover also says why a call isn't encrypted (SRTP off for the account, or not accepted by
  the other side) and which codecs are in use. Stats are simulated: Bob's connection is poor on
  his side and isn't encrypted, and calls on an account with SRTP *Off* aren't encrypted.
- **Real-time text (RFC 4103).** Text belongs to a call, like its audio and video, and only
  goes to that call. Every call offers text unless the account's *Offer real-time text* is off.
  The text panel is only shown in Focus view, beside the video (below it on narrow screens); in
  Grid view the tiles' status lines show "Typing…" and unread lines instead, and *Text* or `T`
  switches to Focus view with the panel open. The panel collapses, with its chevron, *Text* or
  `T`, to a slim rail (a one-line bar on narrow screens). The rail shows typing dots, a count of
  lines you haven't read, and a tint; and a new line pops up over the video for a few seconds.
  Clicking the rail or the pop-up expands the panel. It also opens by itself the first time the
  other person types on the call you're looking at. Each person's unfinished line shows letter by letter, backspaces included, with a
  dashed outline and a blinking caret. Your own line is sent the same way, which the panel says
  under the input. Enter finishes the line, and `Esc` leaves the field so shortcuts work again.
  On hold the input is paused. If text wasn't set up, *Add text to this call* sends a re-INVITE.
  If their phone can't do text (tel: addresses, or any address containing `notext`), the panel
  says so. In Settings, *Open the text panel on every call* is for people who mainly talk by text.
  *Save text conversations in call history* is off by default. When it's on, the transcript is
  shown in the call's history details (Grace's call from yesterday has one as an example).
- **Video, or not.** Video is something a call has or doesn't, and it can change. The call is
  shown as soon as it connects, and it never waits for video.
  - **One Call button, with an Audio | Video switch beside it.** The switch picks the kind of
    call: *Audio* leaves video out of the offer, *Video* offers it (and the button reads "Video
    call"). It starts on Audio, because most SIP calls go to phones and an audio offer never
    trips anything up, and then remembers your last choice. It's locked to Audio when the
    account has video turned off. Phone numbers are always audio.
    (Earlier tries: separate *Video call* and *Audio call* buttons, which looked odd side by
    side; then a camera toggle where every call offered video and the toggle only decided
    whether you sent yours, which made "off" ambiguous.)
  - **Add video.** An audio call has an *Add video* button where Camera would be. It sends a
    re-INVITE with `m=video`. If their phone refuses (`m=video 0`, try *Lobby desk phone*),
    a notice says so and the call carries on as audio, with a disabled *No video* button.
  - On a video call, *Camera* turns your camera off and on with no re-INVITE.
  - **Servers that can't take video:** each account has *Offer video on calls* (Accounts →
    Network & media). Off leaves video out of every offer from that account. That's where the
    "no `m=video`" choice belongs, because it's about the server, not the call.
  - An incoming video call offers *Camera off* next to *Answer*. It answers with your camera
    off, and you still see them.
  - A call with no video at all (their phone, a phone number, or the account) keeps the same card as a video call. A small *Audio only* label sits in the
    corner, and the conversation takes the picture's place.
    - **Two rows of frequency bars.** Their audio grows up from the centre line and yours grows
      down from it, in the accent colour. Their initial and "You" mark each row. The bars stay
      in place and only change height: they rise quickly and sink slowly, so the picture
      settles rather than flickers. Voice energy sits in the lower bands, as real speech does.
      You can see who's talking, who talks over whom, and that your mic works. (An earlier
      scrolling waveform was too busy.) In the real app the bands would come from an
      `AnalyserNode` on the received track and on the microphone.
    - **Muted** is a flat lower lane with "You're muted".
    - **Quiet and broken look different.** Quiet is a row of small resting pills. With no
      packets at all, the bars vanish and leave one thin line. After 2 s a note says which way
      it's broken: "No audio arriving from Dana", or "Dana isn't getting your audio", with your
      bars in red. Try *No audio from them* and *They can't hear me* in Diagnostics. In the
      real app these come from the relay's packet counts and from RTCP receiver reports.
    - A call you aren't listening to is drawn flat and dimmed, and a held call freezes.
    - With reduced motion on, each row is a single level bar instead.
    - The Camera button becomes *Add video*, or a disabled **No video** with a tooltip
      saying why, when video isn't possible.
  - If their phone turns video down, the label reads "Audio only · their phone has no video". This happens with `phone` in the address, as with *Lobby desk phone*, and with
    `tel:` numbers, which never offer video.
  - Video agreed but not arriving shows their initials with "Their camera is off". This
    happens with `nocam` in the address (*Sam*), or with the *Their camera off / on* diagnostic.
    The mock waits 2 seconds before showing this, so a short gap doesn't flicker.
  - If they take video but don't receive yours (`noview`, *Kiosk*), your self view goes dashed
    with "They can't see you".
  - *I have no camera* in Diagnostics: calls still offer video, receive only, so you can see
    them. The self view says "No camera", and the Camera button is disabled.
  - The camera icon in the Active calls list says which of these applies when you hover over it.
- **New call.** This adds a call to the top of the list and holds the current call. Focusing
  the address field (or `/`) lists recent calls and contacts; typing filters them. Typing part
  of a name highlights the first match. Enter or a click calls the highlighted entry, ↑/↓ move
  through the list, and `Esc` closes it. Calls that didn't connect show their outcome, and
  missed calls are red.
  Words in the address decide how the mock call goes: `busy` gives 486, `nobody` gives 404,
  `timeout` gives 408, `away` gives 480 and `decline` gives 603.
- **When a call ends.** If it was the selected call, the main view shows what happened: who
  hung up and how long the call lasted. For a failure it shows the reason in plain words
  ("Busy · They're on another call."), with the SIP code underneath. **Redial** calls again;
  *Back to …* (or `Esc`) returns to another call. A call ending in the background shows a
  toast with **Redial** instead. Every ended call goes into the call history.
- **Call history.** The sidebar has two sections: *Active calls*, shown only while there are
  calls, and *History* underneath. History lists the latest calls with a call-back button and a
  red badge for missed calls you haven't looked at yet. It can be collapsed, and that choice is
  remembered. *See all history* opens History in the main view, which works like Accounts: it
  isn't a list entry, *Back to …* and the × step away, and `Esc` closes it. The view has search,
  *All / Missed / Outgoing / Incoming* filters and day groups. Clicking a row, or its chevron,
  shows its details (direction, time, duration, account, outcome and SIP code), with
  **Call back** and **Copy address**; the chevron turns over and collapses it again. Nothing in
  the list removes a call by accident: the only way is the **Remove** button in a row's details,
  or *Remove all N from this address* next to it, which deals with a flood of spam calls. *Clear history* in the toolbar asks before clearing. With a
  search or filter on, it becomes *Clear N shown* and only removes those calls. Every removal
  shows an **Undo** toast, and removals made while the toast is still up are undone together.
  An unanswered incoming call that takes over Accounts or History hands back
  to that view when it stops ringing. On phones, the sidebar strip has *Calls* and *History*
  tabs.
- **Audio levels.** Speech is simulated. A level meter in the *Mute* button shows your
  microphone. A ring around the other person's avatar grows while they talk. Only the call
  with the audio focus gets these. Talking while muted shows a "you're muted" nudge with
  **Unmute**, at most every 30 seconds.
- **Quick device switch.** The ▾ on *Mute* switches microphone and speaker, and the ▾ on
  *Camera* switches camera, without leaving the call. Both share their choice with Settings.
- **Transfer.** Attended to another active call, or blind to an address.
- **Keypad.** Sends DTMF, which is logged. While the keypad is open, number keys work too.
- **Diagnostics.** The footer toggle opens a mock SIP log, plus buttons to simulate an
  incoming call, a remote hang-up of the selected call or another call, a missed call, three
  calls ringing at once, held calls skipping a minute ahead, your own network going bad, the other
  person's camera stopping or starting, having no camera yourself, one-way audio in either
  direction, the other
  person typing a message, the other side dropping text, or a broken registration. Bob also
  types a message in the background a few seconds after the page loads.

Keyboard: `/` focuses the address field, `1`–`8` select a call by its position in the list (`1` is the newest), `A` answers, `M` mutes, `Shift+M` mutes all, `Shift+C` turns all cameras off, `H`
holds or resumes, `T` opens or closes the text panel, `G` toggles grid view, `V` toggles self view, and `Esc` closes dialogs.
