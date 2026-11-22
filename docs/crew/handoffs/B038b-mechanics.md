TASK: B038b
AGENT: mechanics
STATUS: DONE
FILES:
- Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/MainMenuView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/PauseView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs
- Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs
- docs/crew/handoffs/B038b-mechanics.md
SUMMARY:
- The UI is white only. HostileCol, FriendlyCol and HealCol are removed from HudView, and Hostile/Friendly from the four views. The only colours left are ink, pencil and paper (alpha allowed), plus `NoTint = Color.white`. NoTint is the identity multiplier for sprites that carry their own ink lines, and the base texel of the procedural sprites. It is never a displayed hue.
- HUD:
  - Bomb icons: available = `HudView.BombAvailableCol` (ink), spent = `HudView.BombSpentCol` (pencil at alpha 0.5). Both are new public statics.
  - Damage fill: solid ink below smokeThreshold. From the threshold it swaps to a procedural diagonal-hatch sprite, built once at the bar size so the Filled image crops it without stretching. It then flashes from alpha 1.0 down to 0.4 and back at 3 Hz on `Time.unscaledTime`, starting at alpha 1.
  - The SMOKE warning text is ink and gets the same alpha in the same Update, so they flash in sync.
  - Below the threshold the HUD goes back to the solid sprite at alpha 1 and nothing flashes.
  - New public members: `DamageFlashing` (bool) and `static FlashAlpha(float elapsed)`.
  - The completed objective text now turns from pencil to full ink. It used to be heal green.
- Buttons in Menu, Pause, Result and Briefing:
  - `pressedColor` is now ink (was hostile).
  - New `MainMenuView.AddPressInversion(Button, Text, Sprite)` (internal static, shared the same way as EnsureEventSystem). It adds an inset ink plate ("<name>ActiveFill") under the label. A built-in EventTrigger shows the plate and turns the label paper on PointerDown, and restores both on PointerUp or PointerExit. Non-interactable (locked) buttons never invert.
  - onClick, audio cues and keyboard handling are unchanged.
- Result title: a win is paper text on an ink plate ("ResultTitlePlate"). A loss is ink text with no plate. This used to be friendly/hostile hue.
VERIFY:
- `python tools/compile_check.py`: PASS Biplanes (+2 new file(s) not yet in the csproj, from parallel tasks), PASS Biplanes.Editor, exit 0. `tools/unity.sh` was not run because Unity has the project open.
- Tests scratch build (B018 method):
  - Runtime DLL built from source. A temp copy of Biplanes.Tests.EditMode.csproj was retargeted from v4.7.1 to v4.7.2, with all 26 Tests/EditMode/*.cs files added. The temp csproj was removed afterwards.
  - Result: 2 errors, both expected and listed under REQUESTS: `HudBombIconsTests.cs(70,41)` and `(92,49)` CS0117, 'HudView' does not contain a definition for 'FriendlyCol'. There were no other errors.
- Grep check: `grep -n "Hostile\|Friendly\|Heal" Biplanes/Assets/Biplanes/Scripts/UI/{HudView,MainMenuView,PauseView,ResultView,BriefingView}.cs` finds nothing.
- Playtest (needs Unity: BuildScenes, then Play):
  - Take damage past 70%: the bar turns hatched, and the bar and SMOKE flash together about 3 times per second. This continues while paused.
  - Drop bombs: spent icons go faint pencil.
  - Hold the mouse on any menu, pause, briefing or result button: it fills with ink and the label turns paper. Drag off and it restores.
  - Win a mission: the title is stamped paper on ink. Lose one: the title is plain ink.
REQUESTS:
- qa: update `Biplanes/Assets/Biplanes/Tests/EditMode/HudBombIconsTests.cs`. Until then the EditMode test assembly doesn't compile.
  - Line 70: `HudView.FriendlyCol` -> `HudView.BombAvailableCol`. Also update the message, "not friendly at start" -> "not filled ink at start".
  - Line 92: `i < 4 ? HudView.FriendlyCol : HudView.PencilCol` -> `i < 4 ? HudView.BombAvailableCol : HudView.BombSpentCol`. PencilCol alone would compile but fail, because spent is now alpha 0.5.
  - Optional new tests:
    - At damage 69 `DamageFlashing` is false and the DamageFill sprite is the 1x1 solid one.
    - At damage 70 `DamageFlashing` is true, the fill sprite differs, and SmokeWarning is active.
    - Back at 50 the fill alpha is 1.
    - `HudView.FlashAlpha(0) == 1` and `FlashAlpha(1f/6f) ≈ 0.4`.
- orchestrator: update ARCHITECTURE.md (UI section, HudView/views):
  - "HudView exposes `BombAvailableCol`/`BombSpentCol`, `DamageFlashing` and `static FlashAlpha(elapsed)`. FriendlyCol/HostileCol/HealCol are gone."
  - "Code-built buttons get their active state from `MainMenuView.AddPressInversion(btn, label, sprite)`: an ink plate plus a paper label while the pointer is held."
  - UI_SPEC §3.3/§3.5 could note two things: the completed objective turns pencil -> ink, and a won result title is paper on an ink plate while a lost one is plain ink.
- art (A-WHITE, FYI): bomb icons are tinted ink (available) or pencil at 50% (spent). ui_bomb_icon should be a white/ink filled silhouette, so the tint reads correctly. A true "outline when spent" would need a separate outline sprite, for example `ui_bomb_icon_empty`. Ask mechanics to wire it if you add one.
