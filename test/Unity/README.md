# Unity smoke test

This folder contains a minimal Unity 6 project used by the
[`unity-smoke-test`](../../.github/workflows/unity-smoke-test.yml) GitHub
Actions workflow to guard against regressions like
[issue #1614](https://github.com/sipsorcery-org/sipsorcery/issues/1614),
where SIPSorcery's static initializers throw on Unity's Mono runtime.

The project is intentionally tiny: one PlayMode test that constructs an
`RTCPeerConnection` and asserts no exception is raised.

## Layout

```
test/Unity/SipSorceryUnitySmokeTest/
  Assets/
    Plugins/        SIPSorcery.dll + deps are dropped here by CI
    Tests/          PlayMode test + asmdef
  Packages/         Minimal Unity package manifest
  ProjectSettings/  ProjectVersion.txt; Unity regenerates the rest on first open
```

`Assets/Plugins/` is intentionally empty in source control — the workflow
runs `dotnet publish` on `src/SIPSorcery/SIPSorcery.csproj` (netstandard2.1)
and copies the resulting DLLs into that folder before Unity opens the
project. This avoids depending on NuGetForUnity in CI while still exercising
the same code paths the issue reporter hit.

## Running it locally

1. Install Unity 6000.0.32f1 (or update `ProjectSettings/ProjectVersion.txt`
   to whatever Unity 6 LTS you have).
2. From the repo root, build SIPSorcery and stage the DLLs:
   ```bash
   dotnet publish src/SIPSorcery/SIPSorcery.csproj \
     -c Release -f netstandard2.1 \
     -o test/Unity/SipSorceryUnitySmokeTest/Assets/Plugins
   ```
3. Open `test/Unity/SipSorceryUnitySmokeTest` in Unity Hub.
4. `Window → General → Test Runner`, switch to **PlayMode**, run all.

## CI setup (one-time)

The workflow runs only if a `UNITY_LICENSE` secret is configured on the
repository. To activate one against a personal Unity account, follow
[GameCI's activation guide](https://game.ci/docs/github/activation/), then
add the resulting `Unity_v*.ulf` contents as `UNITY_LICENSE`. If you use a
Plus/Pro seat, additionally set `UNITY_EMAIL` and `UNITY_PASSWORD`.

Until the secret is set, the workflow logs a warning and exits without
failing the build, so PRs are not blocked.

## Triggers

The workflow runs on:

- `workflow_dispatch` — manually from the Actions tab
- Pull requests touching `src/SIPSorcery/sys/Net/**`, `test/Unity/**`, or
  the workflow file itself

Expand the path filter in
[`.github/workflows/unity-smoke-test.yml`](../../.github/workflows/unity-smoke-test.yml)
if you want broader coverage.
