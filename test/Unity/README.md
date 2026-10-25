# Unity smoke test

This folder contains a minimal Unity 6 project used by the
[`unity-smoke-test`](../../.github/workflows/unity-smoke-test.yml) GitHub
Actions workflow to guard against regressions like
[issue #1614](https://github.com/sipsorcery-org/sipsorcery/issues/1614),
where SIPSorcery's static initializers throw on Unity's Mono runtime.

The project is intentionally tiny: one PlayMode test that constructs an
`RTCPeerConnection` and asserts no exception is raised.

## Why Windows only, and manual only

The workflow has a single `smoke-test-windows` job. It runs on
`windows-latest` with a native Unity Editor install via
[`RageAgainstThePixel/unity-setup`](https://github.com/RageAgainstThePixel/unity-setup)
and license activation via
[`RageAgainstThePixel/activate-unity-license`](https://github.com/RageAgainstThePixel/activate-unity-license).

#1614 only reproduces on Unity's Windows-shipped Mono (`NetworkChange` is
unsupported on Win32 Mono but works on Linux Mono). A Linux job using
GameCI's Docker image used to run on every push and PR, but it could not
reproduce the bug and broke whenever the machine-bound `.ulf` license
stopped matching the runner ("Machine bindings don't match"), so it was
removed.

A cold Windows run takes ~20 min, so the workflow is `workflow_dispatch`
only. Run it from the Actions tab before a release or after changes to
`SIPSorcery.Sys.NetServices` / other static initializers.

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
   (for example platform-conditional references that a `netstandard2.1`
   publish on Windows does not include), and refuses to load SIPSorcery
   itself.
3. Unity is installed and activated, then run in `-batchmode -runTests`
   for PlayMode; the job parses `playmode-results.xml` and fails on any
   failed test or if zero tests were discovered.
4. Results and the Unity log are uploaded as a workflow artifact.

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
`test-results.xml` to know the real outcome. The CI job does this for
you.)

## CI setup (one-time)

| Secret | Required for |
| --- | --- |
| `UNITY_EMAIL` | the Unity ID email |
| `UNITY_PASSWORD` | the Unity ID password |
| `UNITY_SERIAL` | only needed if you switch the job to a Pro/Plus license |

The job activates a Personal license with email+password via
[`RageAgainstThePixel/activate-unity-license`](https://github.com/RageAgainstThePixel/activate-unity-license),
so no `.ulf` file is needed (the old `UNITY_LICENSE` secret is unused and
can be deleted). If the secrets are missing, an early `license_check`
step logs a warning and skips the job instead of failing.
