TASK: B026b
AGENT: mechanics
STATUS: DONE
FILES: Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs, Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs
SUMMARY: Changed only comments. PlayerPlane.OnDropBomb summary now reads "Drop one bomb per Space press". The GameManager._pausedInputs summary now says Space (not Enter) would still drop bombs at timeScale 0, and adds the real re-enable behaviour: PlayerInput.OnEnable calls _gate.Reset(), so the click or Space still held after closing pause is spent until released.
  Grep of Scripts/ for "Enter": no other bomb-related mentions remain. The other hits are OnTriggerEnter2D, GameManager's EnterMenu/EnterBriefing/"Entering a state", and BriefingView's "Space (Enter also works)" for starting a mission (correct as written: Enter/numpad Enter still start the briefing).
VERIFY: python tools/compile_check.py -> "PASS Biplanes (+2 new file(s) not yet in the csproj)", "PASS Biplanes.Editor", exit 0. (The first run failed with MSBUILD MSB1009 "Project file does not exist", most likely because Unity was regenerating the csproj files at the time. The rerun with no changes passed.)
REQUESTS: none
