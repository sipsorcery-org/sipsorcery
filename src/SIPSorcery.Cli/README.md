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
