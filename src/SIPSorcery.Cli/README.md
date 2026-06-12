# sipsorcery CLI

SIP and WebRTC diagnostics from the command line, built on the
[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) library.

## Install

```bash
dotnet tool install -g SIPSorcery.Cli --prerelease
```

## Usage

```bash
# SIP ping: send an OPTIONS request and report the response.
sipsorcery sip options music@iptel.org
sipsorcery sip options tcp:sip.example.com:5060
sipsorcery sip options sips:secure.example.com -t 10 -v

# SIP call: place a call, send a test audio source and report on the media received.
# No audio devices are used; received audio renders via ffplay, a WAV file, or raw PCM
# on stdout. ffmpeg/ffplay act as the cross platform audio device layer
# (winget/brew/apt install ffmpeg).
sipsorcery sip call music@iptel.org --audio play           # listen via ffplay
sipsorcery sip call music@iptel.org --scope                # live spectrum + level in the terminal
sipsorcery sip call music@iptel.org --audio play --scope   # listen AND watch: --scope renders on
                                                           # stderr so it composes with any --audio
sipsorcery sip call music@iptel.org --audio rx.wav -d 10   # record 10s to a WAV file
sipsorcery sip call music@iptel.org --audio - > rx.pcm     # raw s16le PCM on stdout
                                                           # (the result moves to stderr)
sipsorcery sip call 100@pbx.example.com -u user --password pass --play tone --send-dtmf 123

# Both SIP verbs can mirror their traffic to a HEPv3 capture server (HOMER, heplify-server,
# sipcapture.org) so the exchange shows up as a call ladder diagram:
sipsorcery sip options music@iptel.org --hep 192.168.0.10
sipsorcery sip call music@iptel.org --hep "192.168.0.10:9060;myHep;42"   # host:port;password;agentId

# STUN lookup: report this machine's public IP address and port.
sipsorcery stun lookup stun.cloudflare.com
sipsorcery stun lookup stun:stun.l.google.com:19302

# ICE probe: gather candidates and verify STUN/TURN connectivity.
sipsorcery ice probe
sipsorcery ice probe --stun stun:stun.cloudflare.com
sipsorcery ice probe --turn "turn:turn.example.com;user;pass" --relay-only

# WebRTC WHEP: full connection (ICE, DTLS, SRTP) to a WHEP endpoint, verifies media arrives.
# Publish to the same stream key first, e.g. with OBS's WHIP output, to get media flowing.
sipsorcery webrtc whep https://b.siobud.com/api/whep --token mystreamkey

# Received video can be rendered or captured (decode is delegated to the consumer, so no
# video codecs are needed in-process). H264 is written as Annex B, VP8 in an IVF container.
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key --video play       # ffplay window
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key --video rx.h264    # capture to file
sipsorcery webrtc whep https://b.siobud.com/api/whep --token key --video - \
  | mpv --vo=tct -                                   # bitstream on stdout: video IN the terminal
                                                     # (the result moves to stderr)
# mpv is a media player with terminal renderers (https://mpv.io/installation/):
#   winget install mpv          (Windows)
#   brew install mpv            (macOS)
#   sudo apt install mpv        (Debian/Ubuntu)
# Windows PowerShell 5.1 corrupts binary data in pipes; run pipelines like the above
# under cmd or PowerShell 7.4+. If mpv does not detect the format from a pipe, add
# --demuxer-lavf-format=h264 (or ivf for VP8).

# WebRTC WHIP server: accept a publish directly from ffmpeg/OBS and report on the media,
# including sequence anomalies. Useful for isolating where stream problems originate.
sipsorcery webrtc whip-server --listen http://localhost:8080/whip --token test -d 10
ffmpeg -re -f lavfi -i testsrc=size=640x360 -f lavfi -i sine=frequency=440 \
  -pix_fmt yuv420p -c:v libx264 -profile:v baseline -r 25 -g 50 \
  -c:a libopus -ar 48000 -ac 2 -f whip -authorization "test" "http://localhost:8080/whip"
```

Every verb supports `--json` for a machine readable result on stdout (logs always go to
stderr, so JSON output is pipeable):

```bash
sipsorcery sip options music@iptel.org --json
```

```json
{
  "success": true,
  "destination": "sip:music@iptel.org",
  "statusCode": 200,
  "reasonPhrase": "OK",
  "server": "kamailio",
  "remoteEndPoint": "udp:212.79.111.155:5060",
  "durationMs": 86
}
```

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success. |
| 1 | The operation completed but failed, e.g. an error SIP response. |
| 2 | An argument value was invalid. |
| 3 | No response within the timeout. |
| 4 | A network send failed. |

## Status

Early preview. The planned command surface covers SIP (calls, registrations, load testing),
WebRTC (offer/answer exchange, echo test peers, ICE connectivity probes), STUN/TURN checks,
SIP DNS resolution and integrations for services such as LiveKit and the OpenAI Realtime API.
