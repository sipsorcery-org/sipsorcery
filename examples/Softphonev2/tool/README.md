# SIPSorcery Softphone

A SIP softphone for Windows, built on the [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery)
library: audio and video calls, several at once, with hold, transfer, keypad tones (RFC 4733 and
SIP INFO), real-time text (RFC 4103), call history and contacts, SRTP and SIP over TLS.

It's a preview: expect changes between releases.

## Install

Needs Windows 10 or 11 with the .NET 10 SDK. The WebView2 runtime, which Windows 10 and 11 already
have, shows the app's window.

```
dotnet tool install -g SIPSorcery.Softphone --prerelease
sipsorcery-softphone
```

Or run it once without installing:

```
dnx SIPSorcery.Softphone --prerelease
```

Update with `dotnet tool update -g SIPSorcery.Softphone --prerelease`.

## Use

Add your SIP account with *Manage accounts* (the server icon beside the address bar, also in
Settings), or call a SIP address directly. Accounts, contacts, call history and logs are kept in
`%LocalAppData%\SIPSorcery\Softphonev2`.

More: the [Softphone v2 README](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/Softphonev2/desktop).
