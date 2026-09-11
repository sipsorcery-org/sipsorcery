# Unity smoke test

This folder contains a minimal Unity 6 project used by the
[`unity-smoke-test`](../../.github/workflows/unity-smoke-test.yml) GitHub
Actions workflow to guard against regressions like
[issue #1614](https://github.com/sipsorcery-org/sipsorcery/issues/1614),
where SIPSorcery's static initializers throw on Unity's Mono runtime.

The project is intentionally tiny: one PlayMode test that constructs an
`RTCPeerConnection` and asserts no exception is raised.

## Two jobs, two reasons

The workflow runs two jobs in parallel:

- **`smoke-test` (Linux)** — fast canary. Uses GameCI's Docker image on
  `ubuntu-latest`. Proves the SIPSorcery DLLs load on Unity's Linux Mono
  and the `NetServices` type initializer doesn't crash there. Catches
  general init regressions but **does not reproduce #1614**, because
  the bug is in Unity's Windows-shipped Mono (`NetworkChange` is
  unsupported on Win32 Mono but works on Linux Mono).
- **`smoke-test-windows`** — reproduction job. Runs on `windows-latest`
  with a native Unity Editor install via
  [`RageAgainstThePixel/unity-setup`](https://github.com/RageAgainstThePixel/unity-setup)
  and license activation via
  [`RageAgainstThePixel/activate-unity-license`](https://github.com/RageAgainstThePixel/activate-unity-license).
  This is the job that actually fails when #1614 is open and turns
  green when it's fixed.

## Layout

```
test/Unity/SipSorceryUnitySmokeTest/
  Assets/
    Editor/         PluginImportSettings editor script (local-dev safety net)
    Plugins/        SIPSorcery.dll + deps; staged here by CI (gitignored)
    Tests/          PlayMode test + asmdef
  Packages/         Unity package manifest + lockfile
  ProjectSettings/  Unity project settings (committed)
```

## How the CI workflow runs

1. `dotnet publish` builds `src/SIPSorcery/SIPSorcery.csproj` for
   `netstandard2.1` and copies the DLLs into `Assets/Plugins/`. A small
   block-list filters out assemblies Unity already provides as built-in
   references (e.g. `Microsoft.CSharp.dll` — leaving it causes CS1703).
2. An inline shell step generates a `.dll.meta` file for every staged
   plugin. The `.meta` sets `validateReferences: 0` and enables the
   plugin on the Editor platform. Without this, Unity's plugin importer
   rejects assemblies whose transitive references can't be resolved
   (notably `Makaretu.Dns.Multicast → Tmds.LibC`, which is Linux-only and
   not pulled in by a `netstandard2.1` publish on Windows), and refuses
   to load SIPSorcery itself.
3. [`game-ci/unity-test-runner`](https://github.com/game-ci/unity-test-runner)
   opens the project and runs the PlayMode test on a Linux container.
4. Results are uploaded as a workflow artifact.

## Running it locally

1. Install Unity 6000.4.7f1 (or update
   `ProjectSettings/ProjectVersion.txt` to whatever Unity 6 LTS you have
   and pass the same to the workflow matrix).
2. From the repo root, stage the DLLs:
   ```bash
   dotnet publish src/SIPSorcery/SIPSorcery.csproj \
     -c Release -f netstandard2.1 \
     -o test/Unity/SipSorceryUnitySmokeTest/Assets/Plugins
   rm test/Unity/SipSorceryUnitySmokeTest/Assets/Plugins/Microsoft.CSharp.dll
   ```
3. Open `test/Unity/SipSorceryUnitySmokeTest` in Unity Hub. The
   `Assets/Editor/PluginImportSettings.cs` script will fix any
   newly-imported plugins automatically (`validateReferences = false`,
   Editor compatibility on). On first import you may need to close and
   reopen the project once so the test runner re-discovers the test
   assembly after the reimport.
4. `Window → General → Test Runner`, switch to **PlayMode**, run all.

Or to run the same single-invocation command CI uses:

```bash
PROJ=test/Unity/SipSorceryUnitySmokeTest
"C:/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Unity.exe" \
  -batchmode -projectPath "$PROJ" -runTests -testPlatform PlayMode \
  -testResults "$PROJ/test-results.xml" -logFile "$PROJ/unity.log"
```

(Unity reports the test-runner internal code in the log — `code 2` means
a test failed. The shell exit code from `-runTests` is unreliable; parse
`test-results.xml` to know the real outcome. GameCI's `unity-test-runner`
action does this for you in CI.)

## CI setup (one-time)

The two jobs need different combinations of secrets, both gated by an
early `license_check` step that logs a warning and skips the job (no
build failure) when secrets are missing.

| Secret | Used by | Required for |
| --- | --- | --- |
| `UNITY_LICENSE` | Linux job (GameCI) | the `.ulf` file contents |
| `UNITY_EMAIL` | both | the Unity ID email |
| `UNITY_PASSWORD` | both | the Unity ID password |
| `UNITY_SERIAL` | — | only needed if you switch the Windows job to a Pro/Plus license |

For a Personal Unity account, follow
[GameCI's activation guide](https://game.ci/docs/github/activation/) to
generate the `Unity_v*.ulf` and paste its contents into `UNITY_LICENSE`.
Add `UNITY_EMAIL` and `UNITY_PASSWORD` from the Unity ID account that
owns that license. The Windows job uses email+password directly via
[`RageAgainstThePixel/activate-unity-license`](https://github.com/RageAgainstThePixel/activate-unity-license),
so it doesn't need the `.ulf` file at all.

Until the secrets are set, each job logs a warning and exits without
failing the build, so PRs are not blocked.

## Triggers

The workflow runs on:

- `workflow_dispatch` — manually from the Actions tab
- Pull requests touching `src/SIPSorcery/sys/Net/**`, `test/Unity/**`,
  or the workflow file itself

Expand the path filter in
[`.github/workflows/unity-smoke-test.yml`](../../.github/workflows/unity-smoke-test.yml)
if you want broader coverage.
