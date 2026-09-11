# SIPSORCERY001

**`TurnServer` is not hardened for production use.**

`SIPSorcery.Net.TurnServer` is a lightweight TURN relay (RFC 5766) written for development, testing,
and small scale or embedded scenarios. It is marked with
[`ExperimentalAttribute`](https://learn.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.experimentalattribute),
so referencing it is a compile error until this diagnostic is suppressed. That is deliberate: the
limitations below should be an explicit decision rather than something discovered after deployment.

For a production TURN deployment use [coturn](https://github.com/coturn/coturn) or an equivalent
hardened server.

## What is missing

- **No nonce validation or expiry.** Nonces are generated but never verified on subsequent requests,
  so replay attacks are possible within the allocation lifetime.
- **No rate limiting and no per-IP allocation caps.** A misbehaving or hostile client can exhaust
  server resources.
- **No TLS/DTLS on the control channel.** Credentials travel in the clear unless the transport is
  already secured. The default listen address is loopback for this reason.
- **Allocation lifetime is not capped.** Clients can request arbitrarily long lifetimes.
- **Weak default credentials** (`turn-user` / `turn-pass`), intentionally, to encourage replacement.
- **No per-user credential database** outside of REST-style ephemeral credentials.
- **UDP-only relay.** No TCP relay (RFC 6062) and no TURN-over-TLS.
- **IPv4 only.** No IPv6 relay addresses.
- **Unimplemented:** REQUESTED-TRANSPORT validation, EVEN-PORT, RESERVATION-TOKEN, ALTERNATE-SERVER.

The XML documentation on the type carries the same list and stays authoritative if the two drift.

## Suppressing it

For a whole project, in the csproj:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);SIPSORCERY001</NoWarn>
</PropertyGroup>
```

For a single file or region:

```csharp
#pragma warning disable SIPSORCERY001
```

`examples/TurnServerExample/Program.cs` shows the file scoped form.

## Reporting issues

Bugs in `TurnServer` are welcome as ordinary issues. Because it is not a supported production
component, defects in it are not treated as security advisories against the SIPSorcery library.
