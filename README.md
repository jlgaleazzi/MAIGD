# Biplanes — M0 Content & Assets Inventory

This document tracks every file, asset, and system delivered during **M0 Foundation**, plus the remaining work needed before the milestone can be signed off.

---

## ✅ Delivered Code (`Assets/Biplanes/Scripts/`)

### Core Systems
| File | Purpose |
|---|---|
| `TeamTag.cs` | `Team` enum (Friendly, Enemy) |
| `IDamageable.cs` + `DamageSource.cs` | Damage contract & source typing |
| `Health.cs` | Health state machine, percent damage, death hook |
| `GameEvents.cs` | Static event bus (`PlayerDamageChanged`, `ScoreChanged`, `EntityDestroyed`, etc.) |
| `SaveSystem.cs` | `PlayerPrefs` persistence (unlocked mission, best score) |
| `GameManager.cs` | High-level state machine (Menu → Briefing → Playing → Paused/Won/Lost) |
| `ScrollController.cs` | Camera x-axis progression tied to mission length |
| `ScoreKeeper.cs` | Score aggregation tied to `Balance.json` |

### Weapons & Combat
| File | Purpose |
|---|---|
| `Projectile.cs` + `ObjectPool<T>.cs` | Base class + generic pool (zero runtime Instantiate/Destroy) |
| `Bullet.cs` | Player projectile |
| `Bomb.cs` | Gravity-driven bomb with ground burst |
| `FlakShell.cs` | Enemy AA projectile |

### Player Systems
| File | Purpose |
|---|---|
| `PlayerInput.cs` | WASD/LeftStick movement, Space hold autofire, Enter bomb drop |
| `PlayerPlane.cs` | Kinematic Rigidbody2D, runway/takeoff state, smoke trail logic, ground collision |
| `BombBay.cs` | Capacity 7, drop logic, event raising |

### Enemies & Obstacles
| File | Purpose |
|---|---|
| `FighterAI.cs`, `BomberAI.cs` | Sine/straight patterns, escort logic, shooting at player |
| `AAAGun.cs` | Ground-bound, fires flak upward, damageable by Bomb only |
| `BarrageBalloon.cs` | Tether visual, damageable obstacle |
| `StaticObstacle.cs` | Building/tree trigger colliders → collision damage |
| `HealthBalloon.cs` | Pickup that restores health |

### Mission Framework
| File | Purpose |
|---|---|
| `MissionData.cs` | Serializable JSON schema for mission config |
| `MissionRunner.cs` | Loads JSON, drives `ScrollController`, instantiates `TerrainStrip` + `ParallaxBackground`, spawns entities, evaluates objectives |
| `ObjectiveTracker.cs` | Evaluates `destroyAllOfType`, `bombTarget`, `surviveToEnd`, `failAtX` |

### UI Views (Built programmatically, no prefabs)
| File | Purpose |
|---|---|
| `HudView.cs` | Damage bar with smoke warning at 70%, score, bomb row (×7), objective text |
| `BriefingView.cs` | Fade-in briefing screen with auto-transition & manual skip |
| `PauseView.cs`, `ResultView.cs` | Pause overlay & Win/Loss results screen |
| ❌ **`MainMenuView.cs`** | *Missing — mission select screen not yet implemented* |

### Editor Tools
| File | Purpose |
|---|---|
| `ProjectBuilder.cs` | Scene generation, sprite importer defaults, active input mode config, build pipelines (WebGL/Windows/Mac) |
| `SpriteImportSettings.cs` | TextureImporter helper utilities |

---

## 🎨 Delivered Assets (`Assets/Biplanes/Resources/Sprites/`)

| Category | Files | Count |
|---|---|---|
| **Player** | `player_biplane.png`, `player_biplane_frame2.png` | 2 |
| **Enemies** | `enemy_fighter.png`, `enemy_bomber.png`, `aaa_gun.png`, `zeppelin_hull.png`, `zeppelin_turret.png` | 5 |
| **Obstacles** | `building_house.png`, `building_barn.png`, `tree_a.png`, `tree_b.png`, `barrage_balloon.png` | 5 |
| **Targets** | `enemy_base.png`, `airfield.png`, `player_hq.png` | 3 |
| **Backgrounds** | `farmland`(3), `enemyland`(3), `frontline`(3), `stormsky`(3) + extras | 14 |
| **UI** | `ui_button.png`, `ui_bomb_icon.png`, `title_logo.png`, `ui_damage_frame.png`, `ui_panel.png` | 5 |
| **FX** | `explosion_0..5.png` (6), `smoke_puff.png`, `flak_burst_0..3.png` (4) | 11 |

---

## 📄 Delivered Data

| File | Purpose |
|---|---|
| `Resources/Balance.json` | Tuned stats: player, enemies, damage values, scores, parallax factors, enemy speeds |
| `Resources/Missions/mission_01.json` → `mission_05.json` | All 5 missions populated with spawns, objectives, terrain keyframes, and background themes |

---

## 🚧 Remaining TODOs (Post-M0)

### 🔴 Critical / Code Missing
- **T034**: Implement `MainMenuView.cs` (mission select limited to unlocked levels)
- **T047**: Verify/Fix `BomberAI` failAtX integration & escort pattern support
- **T058**: Implement `ZeppelinAI` hull + turret hierarchy logic (turret destruction → hull vulnerability)

### 🟡 QA & Build Verification
- **T038/T039**: Fix `ProjectBuilder.cs` InputSystem compile error & run M0 compile + scene smoke test
- **T075**: Project-wide rasterization check (verify all SVGs correctly rasterized to PNG)
- **M1–M5 QA Passes**: 
  - T043: Mission 1 vertical slice playtest
  - T053: Missions 2–3 playtest
  - T071: Mission 5 vertical slice
  - T072: Pause / Retry / Results flow verification
  - T074: Windows / Mac / WebGL build verification

### 🟢 Polish & Balance
- **T080**: EditMode tests for `TerrainStrip.HeightAt` & parallax formula; manual playtest checklist for missions 1–5
- Global balance tuning pass across all JSONs if pacing feels off during QA.

---
*Milestone M0 codebase is ~94% complete on disk. Remaining work focuses on the missing Main Menu view, compile/QA passes, and Zepplin/Bomber mechanics verification.*
