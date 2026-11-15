TASK: B026
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerInput.cs (changed)
- Biplanes/Assets/Biplanes/Scripts/Player/PlayerInputGate.cs (new)
- Biplanes/Assets/Biplanes/Scripts/UI/MainMenuView.cs (help text; HelpPanelHeight 130 → 150 so 5 lines fit)
- Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs (hint "SPACE  begin", doc comments; Enter still starts)
- Biplanes/Assets/Biplanes/Tests/EditMode/PlayerInputGateTests.cs (new, 12 tests)
SUMMARY:
- Fire = Mouse.current.leftButton.isPressed (hold, same autofire pacing). Bomb = kb.spaceKey.wasPressedThisFrame (one per press). Enter and numpad Enter are no longer read by PlayerInput. W/S/A/D unchanged. If there is no mouse, fire reads as released. If there is no keyboard, move and bomb read as released.
- New pure PlayerInputGate does the mapping and the UI guard. While a menu, briefing, pause or result view is showing (the views' static IsShowing), or while timeScale is 0, nothing moves, fires or drops. A held button is "spent" until it is released: this covers a hold across a UI screen, a mouse press that lands on UI (EventSystem.current.IsPointerOverGameObject, checked only at press time), and a button already down at OnEnable (spawn or pause resume). Space on the briefing never drops a bomb, whichever of BriefingView and PlayerInput runs first that frame. The gate remembers a blocked previous step, so the same-frame press counts as spent.
- How to Play now lists W/S climb/dive, A/D face left/right, LEFT MOUSE (hold) machine gun, SPACE drop a bomb (7 per mission), ESC pause. The briefing hint reads "SPACE  begin".
VERIFY:
- `python tools/compile_check.py` → PASS Biplanes (+2 new file(s) not yet in the csproj), PASS Biplanes.Editor, exit 0. I didn't run `tools/unity.sh` because the editor has the project open.
- Scratch build of all 12 Tests/EditMode/*.cs against a freshly built Biplanes.dll: 0 errors. The method is the same as B018, with one extra step: the scratch test csproj needs TargetFrameworkVersion v4.7.2, because nunit.framework is built for 4.7.2 and the generated csproj says 4.7.1, so NUnit won't resolve without it. Nothing was left in the project. These were compiled only, not run.
- Run with Unity closed: `bash tools/unity.sh test`, PlayerInputGateTests (12 tests).
- Manual playtest checklist (Unity → Biplanes → BuildScenes, then Play):
  1. Main menu → How to Play: the 5 lines above, all inside the panel.
  2. Click a mission button. The briefing shows "SPACE  begin". Press Space: the mission starts and no bomb drops (HUD still shows 7 bombs). Repeat with Enter, which also starts it, and then with the Begin Mission button click: no shot fires.
  3. In play: hold left mouse to autofire at the old rate, release to stop. Space drops exactly one bomb per press, and holding Space doesn't repeat. Enter does nothing.
  4. Esc → click Resume: no shot on resume. Esc → Esc while holding left mouse: firing resumes only after a release and a new press.
  5. Win or lose → click Next or Retry: no shot or bomb on the next mission's first frames.
  6. WebGL: before the canvas has focus, nothing fires.
- Old-control mentions found:
  - Fixed: MainMenuView HelpText, BriefingView hint and comments, PlayerInput comments.
  - Still stale, outside my write scope:
    - Scripts/Player/PlayerPlane.cs:508 says "Drop one bomb per Enter press" (comment only).
    - Scripts/Core/GameManager.cs:74 says "Enter would still drop bombs at timeScale 0" (comment only; should now say Space or a mouse click).
    - .claude/agents/mechanics.md:59 and .opencode/agents/mechanics.md:64 still list the old controls.
    - docs/qa/T940-verify.md:136 still lists the old controls (historical QA record).
    - tools/goal_agent/spec.json:13, "Keyboard controls W/A/S/D, Space, Enter".
    - README.md:32 says "WASD/LeftStick": there is no gamepad stick support.
  - No test relied on Space fire or Enter bomb. docs/UI_SPEC.md:53 ("Enter, Space or the button" start the briefing) is still accurate.
REQUESTS:
- orchestrator: in ARCHITECTURE.md §3 Player input, add: "`PlayerInputGate` (pure, EditMode-testable) maps raw keys and mouse to `InputState`. It blocks while MainMenuView/BriefingView/PauseView/ResultView.IsShowing or timeScale 0. A button held across a UI screen, pressed over UI (`EventSystem.IsPointerOverGameObject` at press time) or held at OnEnable must be released before it counts." Also update the stale lines listed above: the agent prompts, the README "LeftStick" line, goal_agent spec.json and UI_SPEC if desired.
- mechanics (follow-up, trivial): fix the stale comments in PlayerPlane.cs:508 and GameManager.cs:74 once B023 releases those files.
