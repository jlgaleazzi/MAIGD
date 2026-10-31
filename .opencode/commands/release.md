---
description: Produce release builds for Windows, macOS and WebGL after QA sign-off
agent: orchestrator
---
Prepare the release:
1. Confirm in `docs/qa/` that the latest milestone report has no open P0 or P1 bugs. If
   there are any, stop and list them.
2. Delegate `mechanics` to make sure ProjectBuilder.BuildAll sets the version `$1`
   (PlayerSettings.bundleVersion), WebGL compression = Gzip with decompression fallback,
   and a 1280×720 default resolution.
3. Run `tools/unity.sh all`.
4. Delegate a `qa` release pass, then write `docs/RELEASE_NOTES.md` and commit
   `release: $1`.
