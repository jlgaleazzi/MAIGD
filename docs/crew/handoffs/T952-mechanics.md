TASK: T952
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/UI/MainMenuView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/PauseView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs
- Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs
- Biplanes/Assets/Biplanes/Tests/EditMode/AudioHooksUiTests.cs (new, 5 tests)
SUMMARY:
- Every UI cue goes through `AudioManager.GetOrNull()` + null check + `PlayUi` (private static `PlayUiCue(SfxId)` per view). Page turn (UiPage) once when MainMenuView, BriefingView or ResultView is built. UiClick on mission select, How to Play, Pause Resume/Quit, Result Retry/Next/Menu (after the existing `_clicked` guard, so one click per press). UiPause when PauseView opens (Esc or any pause). Resume: the Resume button plays UiClick; Esc-resume plays nothing.
- Briefing: the text appears at once, so 5 Typewriter clacks 0.06 s apart, the first 0.12 s after opening (after the page turn). They run from Update on unscaled time (no coroutine), so they stop after Begin (`_transitioned`) and when the view is destroyed. At most one clack per frame, so a long frame never stacks a burst. The schedule is the pure `BriefingView.TypewriterClacksDue(elapsed)` (public consts TypewriterClacks/TypewriterInterval/TypewriterStartDelay). Stamp plays once in `TriggerTransition` (button, Space/Enter, or the 5 s auto-start). It is Begin's click cue, so there is no UiClick on top.
- Win/Lose: in `GameManager.OpenResultView`, only when no ResultView is showing yet. It plays once per mission end, together with the result screen (after the 1 s crash fall, not at the moment of impact). Retry/Next/Menu never reopen the screen; a mission ends only once (SetState guard). ResultView itself plays only UiPage. Quitting during the 1 s crash delay cancels the screen and the Lose sting.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes and Biplanes.Editor, exit 0, on the first run. A later rerun FAILED only on `Scripts/Enemies/ZeppelinAI.cs(261,41): CS0619 GetInstanceID() is obsolete`. That file belongs to parallel task T951 (in progress) and is not mine. `tools/unity.sh compile` was not run because Unity has the project open.
- Scratch build (session temp dir, nothing left in the project), B018 method:
  - The runtime DLL was built with compile_check helpers, using a scratch copy of ZeppelinAI.cs with `GetInstanceID()` -> `GetHashCode()` only to get past the T951 error. Result: 0 errors.
  - Then a temp copy of Biplanes.Tests.EditMode.csproj, with ProjectReferences and Compile items stripped and retargeted v4.7.1 -> v4.7.2, built all 19 Tests/EditMode/*.cs. Result: 0 errors. The tests were not run.
- With Unity closed: `bash tools/unity.sh test`, then AudioHooksUiTests (5): the typewriter schedule (none before the delay, then 1 per interval, capped at 4-6 within 0.05-0.07 s), and PauseView/ResultView build and hide with no AudioManager.
- Play check (needs T948 WAVs):
  - Menu opens: page turn. Mission click: tick, then page turn plus about 5 clacks.
  - Space, Enter or Begin: stamp, and no clacks after it.
  - Esc: hush; Esc again: silent; the Resume button: tick.
  - Mission end: win or lose sting plus page turn when the screen opens. Retry, Next and Menu: tick only, no replayed sting.
REQUESTS:
- orchestrator: ARCHITECTURE.md §3 Audio "Hooks": replace "GameManager (win/lose on MissionEnded)" with "GameManager (Win/Lose via PlayUi when the result screen opens, once per mission end)". Add the UI detail: UiPage on MainMenu/Briefing/Result open; UiClick on every button (Begin plays Stamp instead); UiPause when PauseView opens; Esc-resume is silent; the briefing plays 5 Typewriter clacks 0.06 s apart starting 0.12 s after opening (`BriefingView.TypewriterClacksDue`), and Stamp on Begin, Space/Enter or the auto-start.
- mechanics/T951 owner: `ZeppelinAI.cs:261` uses obsolete `GetInstanceID()` (CS0619 under compile_check). Use `GetEntityId()` or a per-instance counter.
