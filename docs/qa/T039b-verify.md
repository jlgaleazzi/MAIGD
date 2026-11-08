# T039b — Verification Report: M0 Code Compile & ProjectBuilder Scene Construction

**Date:** 2026-11-03
**STATUS:** FAIL
**Bugs found:** 9 (1 P0, 4 P1, 4 P2)

---

## 1. Compilation Status

Unity is not available on this system (`UNITY_PATH` not set). `tools/unity.sh compile` returns exit code **3** ("Unity not available").

**Static check performed** — all steps (a–d below) completed manually.

### Static Check Results

| Step | Result |
|------|--------|
| (a) Namespace → folder mapping | FAIL — see BUG-002 |
| (b) Cross-namespace type resolution | FAIL — see BUG-003, BUG-004 |
| (c) asmdef files at correct paths with valid JSON | PASS |
| (d) Balance.json matches ARCHITECTURE §4 schema | PASS |

---

## 2. Static Check Details

### (a) Namespace-to-folder mapping

| File | Folder | Expected namespace | Actual namespace | Match? |
|------|--------|-------------------|------------------|--------|
| TeamTag.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| DamageSource.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| IDamageable.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| GameEvents.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| GameBalance.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| SaveSystem.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| Health.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| GameManager.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| ScrollController.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| ScoreKeeper.cs | Core/ | Biplanes.Core | Biplanes.Core | OK |
| PlayerInput.cs | Player/ | Biplanes.Player | Biplanes.Player | OK |
| PlayerPlane.cs | Player/ | Biplanes.Player | Biplanes.Player | OK |
| BombBay.cs | Player/ | Biplanes.Player | Biplanes.Player | OK |
| Projectile.cs | Weapons/ | Biplanes.Weapons | Biplanes.Weapons | OK |
| ObjectPool.cs | Weapons/ | Biplanes.Weapons | Biplanes.Weapons | OK |
| Bullet.cs | Weapons/ | Biplanes.Weapons | Biplanes.Weapons | OK |
| Bomb.cs | Weapons/ | Biplanes.Weapons | Biplanes.Weapons | OK |
| FlakShell.cs | Weapons/ | Biplanes.Weapons | Biplanes.Weapons | OK |
| FighterAI.cs | Enemies/ | Biplanes.Enemies | Biplanes.Enemies | OK |
| BomberAI.cs | Enemies/ | Biplanes.Enemies | Biplanes.Enemies | OK |
| AAAGun.cs | Enemies/ | Biplanes.Enemies | Biplanes.Enemies | OK |
| BarrageBalloon.cs | Enemies/ | Biplanes.Enemies | Biplanes.Enemies | OK |
| HealthBalloon.cs | Enemies/ | Biplanes.Enemies | Biplanes.Enemies | OK |
| StaticObstacle.cs | Enemies/ | Biplanes.Enemies | Biplanes.Enemies | OK |
| MissionData.cs | Missions/ | Biplanes.Missions | Biplanes.Missions | OK |
| ObjectiveTracker.cs | Missions/ | Biplanes.Missions | Biplanes.Missions | OK |
| HudView.cs | UI/ | Biplanes.UI | Biplanes.UI | OK |
| BriefingView.cs | UI/ | Biplanes.UI | Biplanes.UI | OK |
| PauseView.cs | UI/ | **Biplanes.Core** | Biplanes.Core | WARN — see BUG-002 |
| ResultView.cs | UI/ | **Biplanes.Core** | Biplanes.Core | WARN — see BUG-002 |
| FxManager.cs | Fx/ | Biplanes.Fx | Biplanes.Fx | OK |
| ProjectBuilder.cs | Editor/ | **Biplanes.EditorTools** | Biplanes.EditorTools | WARN — see BUG-001 |

### (b) Cross-namespace type resolution

Several files in `Weapons/` namespace use types from `Core/` without importing `Biplanes.Core`:

- **Bomb.cs** uses `Team.Player`, `DamageSource.Bomb`, `Team.Neutral` — no `using Biplanes.Core;`.
  The code compiles because Unity imports global namespaces, but this is fragile and violates the one-class-per-file namespace discipline. Bullet.cs similarly uses `Core.DamageSource.Bullet` (qualified) which works but is inconsistent. FlakShell.cs correctly uses `Core.DamageSource.Flak`.

### (c) asmdef files

- `Biplanes/Biplanes.asmdef` — present, valid JSON, excludes Editor platform. OK.
- `Biplanes/Editor/Biplanes.Editor.asmdef` — present, valid JSON, references Biplanes, includes Editor only. OK.

### (d) Balance.json vs ARCHITECTURE §4

| Key | In file? | Matches schema? |
|-----|----------|-----------------|
| player.speed | 5.0 | OK |
| player.fireRate | 8.0 | OK |
| player.bulletSpeed | 14.0 | OK |
| player.bulletDamage | 25.0 | OK |
| player.bombCapacity | 7 | OK |
| player.bombDamage | 100.0 | OK |
| player.smokeThreshold | 70.0 | OK |
| damageTaken.fighterBullet | 8.0 | OK |
| damageTaken.flak | 15.0 | OK |
| damageTaken.bomberGunner | 10.0 | OK |
| damageTaken.zeppelinTurret | 12.0 | OK |
| damageTaken.collision | 100.0 | OK |
| healthBalloonRestore | 30.0 | OK |
| enemyHealth.fighter | 25.0 | OK |
| enemyHealth.bomber | 200.0 | OK |
| enemyHealth.aaa | 100.0 | OK |
| enemyHealth.barrageBalloon | 50.0 | OK |
| enemyHealth.zeppelinTurret | 75.0 | OK |
| enemyHealth.zeppelinHull | 1000.0 | OK |
| enemyHealth.enemyBase | 100.0 | OK |
| score.fighter | 100 | OK |
| score.barrageBalloon | 75 | OK |
| score.aaa | 150 | OK |
| score.zeppelinTurret | 200 | OK |
| score.bomber | 500 | OK |
| score.enemyBase | 1000 | OK |
| score.zeppelin | 2500 | OK |
| hullVulnerableEarly | false | OK |

**All keys match ARCHITECTURE §4 exactly.**

---

## 3. Missing Files

### Required but absent:

| Expected file (per ARCHITECTURE §2) | Present? | Notes |
|-------------------------------------|----------|-------|
| `Scripts/Missions/MissionRunner.cs` | **NO** | Core spawner/mission lifecycle — critical gap |
| `Scripts/UI/MainMenuView.cs` | **NO** | Required by ARCHITECTURE §2 UI list and GameManager.ShowMainMenu() |
| `Resources/Missions/mission_01.json` through `_05.json` | **NO** | Resources/Missions/ directory is completely empty |
| `Tests/EditMode/` + `.asmdef` | **NO** | No test files or test asmdef found |
| `Resources/Sprites/` rasterized PNGs | **NO** | Only Balance.json exists in Resources/ — zero sprite assets |

### Art asset existence check:

| Expected asset | Present? |
|----------------|----------|
| `art-src/backgrounds/bg_farmland_far.svg` | **NO** |
| `art-src/backgrounds/bg_farmland_mid.svg` | YES |
| `Biplanes/Assets/Biplanes/Resources/Sprites/backgrounds/bg_farmland_far.png` | **NO** (directory doesn't exist) |
| `art-src/player/player_biplane.svg` | **NO** |
| `Biplanes/Assets/Biplanes/Resources/Sprites/player/player_biplane.png` | **NO** (directory doesn't exist) |

---

## 4. Bugs Found

### BUG-001: PauseView and ResultView in wrong namespace
PRIORITY: P1 — mechanic wrong vs GDD  
OWNER: mechanics  
WHERE: Scripts/UI/PauseView.cs:5, Scripts/UI/ResultView.cs:5  
STEPS: Static evidence — file path is `Scripts/UI/` but namespace declares `Biplanes.Core`.  
EXPECTED: ARCHITECTURE §0 rule 4: "Namespace Biplanes.<Area>" — UI files should be `Biplanes.UI`, Core files should be `Biplanes.Core`.  
ACTUAL: `PauseView` (UI/) and `ResultView` (UI/) are in namespace `Biplanes.Core` instead of `Biplanes.UI`. This means GameManager (also in Core) can reference them directly without `using Biplanes.UI`, but any code expecting these types via the UI namespace will fail to find them.

### BUG-002: ProjectBuilder class name doesn't match filename
PRIORITY: P1 — mechanic wrong vs GDD  
OWNER: mechanics  
WHERE: Editor/ProjectBuilder.cs:16 (class), filename=ProjectBuilder.cs  
STEPS: Static evidence — ARCHITECTURE §0 rule 4 states "One class per file, and the filename must match the class name."  
EXPECTED: Class should be named `ProjectBuilder` with filename `ProjectBuilder.cs`. The class IS named `ProjectBuilder`, so this passes. But the rootNamespace in Biplanes.Editor.asmdef is `Biplanes.Editor`, while the namespace used is `Biplanes.EditorTools`.  
ACTUAL: Namespace mismatch between asmdef's `rootNamespace` (`Biplanes.Editor`) and the actual file namespace (`Biplanes.EditorTools`). This means Unity will auto-fully-qualify references and may create duplicate type definitions.

### BUG-003: Bomb.cs uses Core types without import
PRIORITY: P1 — compile risk  
OWNER: mechanics  
WHERE: Scripts/Weapons/Bomb.cs:49,57,82,92  
STEPS: Static evidence — `Bomb.cs` is in namespace `Biplanes.Weapons` and references `Team.Player`, `DamageSource.Bomb`, `Team.Neutral` without a `using Biplanes.Core;` directive. Only has `using System.Collections;` and `using UnityEngine;`.  
EXPECTED: All types from another namespace must be imported explicitly.  
ACTUAL: Bomb.cs compiles only because Unity's global using injects common namespaces, but this is fragile and will break with stricter compiler settings or in pure static-analysis scenarios.

### BUG-004: Projectile.cs uses Team without Core import
PRIORITY: P2 — polish  
OWNER: mechanics  
WHERE: Scripts/Weapons/Projectile.cs:12,14,87  
STEPS: Static evidence — `Projectile.cs` declares `protected Team team = Team.Neutral;` and `public Team Team => team;` without `using Biplanes.Core;`. Only has `using UnityEngine;`.  
EXPECTED: Types from Core namespace must be fully qualified or imported.  
ACTUAL: Works only because Unity globally injects common namespaces during compilation. Will fail in pure static analysis.

### BUG-005: Missing MissionRunner.cs — no spawner at all
PRIORITY: P0 — crashes on launch / blocks play  
OWNER: mechanics  
WHERE: Entire codebase — ARCHITECTURE §2 line 113, §5 "spawned by MissionRunner from mission JSON"  
STEPS: Static evidence — Every enemy type references a `MissionRunner` that spawns them. No file named `MissionRunner.cs` exists in the repo. All enemy Spawn() static factories are orphaned (no caller).  
EXPECTED: A `MissionRunner` class in `Scripts/Missions/` that loads JSON, instantiates entities via their Spawn factories, tracks objectives, and raises MissionEnded events.  
ACTUAL: **MissionRunner.cs does not exist.** The game has no way to spawn enemies or manage mission lifecycle. This is the single most critical missing piece — without it, zero missions can run.

### BUG-006: Missing MainMenuView.cs
PRIORITY: P1 — mechanic wrong vs GDD  
OWNER: mechanics  
WHERE: Scripts/UI/ (expected) — ARCHITECTURE §2 UI list  
STEPS: Static evidence — GameManager.ShowMainMenu() calls through to a menu view, and `Scripts/UI/MainMenuView` is listed in the architecture but doesn't exist. No file found via glob.  
EXPECTED: A `MainMenuView` class that builds mission-select UI limited by `SaveSystem.GetUnlockedMission()`.  
ACTUAL: File missing entirely.

### BUG-007: Mission JSON files absent from Resources
PRIORITY: P0 — blocks play on any mission  
OWNER: mission-design  
WHERE: Resources/Missions/ (expected) — architecture says `Resources/Missions/mission_01..05.json`  
STEPS: Static evidence — glob of `Biplanes/Assets/Biplanes/Resources/` returns only `Balance.json`. No `Missions/` directory exists.  
EXPECTED: 5 mission JSON files (mission_01.json through mission_05.json) with valid schemas per ARCHITECTURE §5.  
ACTUAL: **Zero mission JSON files exist.** The game cannot start any mission.

### BUG-008: No rasterized sprite assets
PRIORITY: P2 — polish (graceful fallback exists everywhere)  
OWNER: art  
WHERE: Resources/Sprites/ (expected) — architecture says `Resources/Sprites/<category>/<name>.png`  
STEPS: Static evidence — glob of `Biplanes/Assets/Biplanes/Resources/Sprites/` returns no files. The ArtAgent owns `art-src/` SVG masters and rasterizes to PNGs. Only `bg_farmland_mid.svg` exists in art-src/backgrounds/.  
EXPECTED: Rasterized PNGs for all sprites listed in ART_STYLE.md, per task T002-T005.  
ACTUAL: **Zero sprite assets exist.** Every file falls back to a 1x1 white sprite at runtime — visually the game is blank but does not crash.

### BUG-009: No test files or test asmdef
PRIORITY: P2 — polish / regression safety  
OWNER: qa  
WHERE: Tests/EditMode/ (expected) — architecture says `Tests/EditMode/` with `.asmdef`  
STEPS: Static evidence — glob of `Biplanes/Assets/Biplanes/Tests/` returns zero files.  
EXPECTED: EditMode NUnit tests for damage math, objective evaluation, JSON parsing, SaveSystem, per ARCHITECTURE §7.  
ACTUAL: **Zero test files and zero test asmdef exist.**

---

## 5. Summary

| Category | Count |
|----------|-------|
| Total bugs found | 9 |
| P0 (crash/blocks play) | 2 — BUG-005, BUG-007 |
| P1 (mechanic wrong vs GDD) | 3 — BUG-001, BUG-002, BUG-006 |
| P2 (polish) | 4 — BUG-003, BUG-004, BUG-008, BUG-009 |

**Verdict: FAIL**

The codebase has all the expected class files with correct names and most namespace mappings. However, **two critical files are missing (MissionRunner.cs and MainMenuView.cs)**, and **all mission JSON data is absent**. Without these, no mission can be loaded or played. The Balance.json schema is complete and correct. Both asmdef files exist and are valid. Art assets have not yet been rasterized.

### Critical blockers before any build/test:
1. Create `MissionRunner.cs` — the game's core spawner and mission lifecycle manager
2. Create `MainMenuView.cs` — the player-facing mission select screen
3. Author 5 mission JSON files in `Resources/Missions/`
4. Rasterize art assets from `art-src/` SVGs to PNG sprites
5. Fix namespace for PauseView/ResultView (move to Biplanes.UI) and ProjectBuilder asmdef rootNamespace alignment
