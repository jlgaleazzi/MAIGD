# Biplanes: UI Specification

## 1. Screens and flow

| Screen | View class | Shown when GameManager = |
|---|---|---|
| **Main Menu** | `MainMenuView` | Menu |
| **Briefing** | `BriefingView` | Briefing |
| **HUD overlay** | `HudView` (child of Playing canvas) | Playing |
| **Pause menu** | `PauseView` | Paused |
| **Results** | `ResultView` | Won / Lost |

Flow: MainMenu → pick mission → Briefing → Playing → (Esc) → Pause → Resume or Quit → Results.

## 2. Blueprint styling

All UI uses the white-on-blueprint palette (GDD Style Guide; no colour accents since 2026-11-18):
- Background panels (`ui_panel` sprite): paper `#0F2A4A` fill, ink `#E8F1FA` wireframe border (2px).
- Buttons (`ui_button` sprite): hover state = pencil `#9DB4CC` glow ring; active = full ink `#E8F1FA` with the label in paper.
- Damage bar: frame `ui_damage_frame` containing a fill that reads 0→100%. Fill is solid ink `#E8F1FA` up to ~69%; at ≥ 70% (smoke warning) the fill becomes hatched ink and the bar flashes (alpha 1.0 ↔ 0.4, about 3 Hz).
- Bomb icons (`ui_bomb_icon`): 7 small biplane silhouettes; ink `#E8F1FA` when available, pencil `#9DB4CC` at 50% alpha when spent (same icon sprite, tinted).
- Text uses the game font fallback (no special font assets). Colour: ink `#E8F1FA`.
- Emphasis without colour (B038b): a completed objective's text turns from pencil to full ink; a won result title is paper text on an ink plate, a lost one plain ink; a pressed button fills with ink and its label turns paper.

## 3. Screen layouts

### 3.1 Main Menu (MainMenuView)

Layout anchored to canvas center:

```
┌──────────────────────────────────────┐
│     [ title_planes illustration ]    │
│   (player biplane vs enemy fighter)  │
│          [ title_logo ]              │
│     ("Biplanes", stencil letters)    │
│                                     │
│    ┌─────────┐    ┌─────────┐      │
│    │Mission 1│    │Mission 2│ ...  │
│    └─────────┘    └─────────┘      │
│   (locked: dim)  (unlocked: bright) │
│                                     │
│           [ How to Play ]            │
│                                     │
└──────────────────────────────────────┘
```

- Title screen art (2026-11-20): `ui/title_planes` (about 960x300 px) is centred above the
  title logo (centre about y 390 at the 1920x1080 reference, never overlapping the logo or the
  buttons), and `ui/title_logo` reads "Biplanes". Both white-only blueprint art; missing sprites
  are skipped (logo falls back to the text title).
  Built (B058): logo at y 145, 520x130 reference units (1280x720 reference); `title_planes` fills the band from 8 units above the logo top to 12 units below the canvas top, about 660x210 px at 1080p, preserveAspect, never overlapping the logo or buttons.
- All 5 mission buttons are shown; missions above `SaveSystem.GetUnlockedMission()` are dimmed and can't be clicked (T934, replaces "only shown if unlocked").
- Each button shows the mission title from `MissionData.title`.
- Clicking a locked button does nothing.
- Clicking an unlocked button transitions to BriefingView with that mission's ID.

### 3.2 Briefing (BriefingView)

The briefing freezes the world, fades in over 0.5 s and starts the mission after 5 s or on Enter, Space or the button. It also appears after Next (not after Retry).

```
┌──────────────────────────────────────┐
│                                      │
│   Mission N: [title from JSON]       │
│                                      │
│   [briefing text from JSON]          │
│                                      │
│        [  Begin Mission  ]           │
│                                      │
└──────────────────────────────────────┘
```

- Displayed for ~5 seconds then auto-transitions to Playing, or the player presses Start.
- The canvas background fades in over 0.5s.

### 3.3 HUD (HudView) — top-left and bottom anchor

```
┌──────────────┐                       ┌─────────────┐
│ Damage: ████░░ 70%                Score: 1200    │
│ DAMAGE ⚠️                              ──────────  │
│                                      Objective:   │
│                                     Destroy 3     │
│                                     fighters       │
│                              (left-anchored text)  │
├──────────────────────────────────────┴─────────────┤
│ [♢] [♢] [♢] [♢] [░] [░] [░]   ← bomb icons (×7) │
└────────────────────────────────────────────────────┘
```

**Widgets:**

| Widget | Anker | Data source | Notes |
|---|---|---|---|
| Damage bar fill | Top-left, 16px from corner | `GameEvents.PlayerDamageChanged` | Solid ink, hatched and flashing from 70% (smoke threshold). Bar height: 24px. Width: 200px max. Fill is a scaled sprite or rect. |
| Smoke warning icon | Directly below damage bar | Same event, ≥ 70% = visible | Ink "DAMAGE" label (was "SMOKE", user 2026-11-18) that flashes with the bar, visible when smoke active (decision 2026-11-14: stands out better than pencil grey) |
| Score display | Top-right corner | `GameEvents.ScoreChanged` from ScoreKeeper | Right-anchored, large font (24pt) |
| Objective text | Below score | `ObjectiveTracker.RemainingKills(i)` (B053; the HUD keeps no counter of its own) | "Destroy N more <ReadableType>", refreshed after every counted Team.Enemy kill (reinforcements included); pencil while N > 0, ink when complete. Shows one objective at a time; only that objective's ObjectiveCompleted changes it, then the HUD moves to the next open objective (counter for destroyAllOfType, else `ObjectiveTracker.GetDescription`) and shows "Objective Complete!" only when none remain (B054) |
| Minimap | Top-centre, 12px from the top | MissionRunner (mission length, spawns, live entities, objectives), PlayerPlane, camera | See §3.3.1 |
| Bomb icons row | Bottom-left, 16px padding | `GameEvents.BombsChanged` from BombBay | 7 slots; available = ink; spent = pencil at 50% alpha |

#### 3.3.1 Minimap (MinimapView)
A thin blueprint strip at the top centre showing the **whole mission span**, start (x 0) on the
left to the map end (`length`) on the right. White only.
The strip shows while Playing or Paused, and in Won until the result screen opens (the B069 kill-win window), using `MinimapView.VisibleIn(state, ResultView.IsShowing)`.
- **Frame:** 480 x 44 px at the 1920x1080 reference (scales with the canvas), paper `#0F2A4A` at
  70% alpha with a 1 px ink border, like a small `ui_panel`. Horizontal axis = world x 0..length;
  vertical axis = the flight band, terrain line at the bottom (a pencil polyline sampled from
  `TerrainStrip.HeightAt` every few units), flight ceiling at the top.
- **Camera window:** a pencil bracket outline showing the part of the map currently on screen.
- **Player:** a 7 px ink triangle at the plane's x/y, pointing the way it faces; always drawn
  on top.
- **Enemy aircraft (live):** ink dots: fighter (and reinforcement) 3 px, bomber 5 x 3 px,
  zeppelin a 12 x 4 px bar. **Ground enemies (live):** AAA 3 px pencil ticks on the terrain line,
  barrage balloons 3 px pencil circles, enemy base a 6 px pencil square.
- **Upcoming enemies:** spawns from the mission JSON that haven't appeared yet are faint pencil
  ticks (40% alpha) on the bottom edge at their `atX`, so the whole span reads at a glance; each
  disappears when it spawns.
- **Objectives:** each open objective target gets a hollow ink diamond (9 px) around its marker
  (the `destroyById` bomber or zeppelin, the `bombTarget` base; for `destroyAllOfType` the
  diamonds go on the live enemies of that type, matched with `ObjectiveTracker.CountsAsKillOf`, so reinforcements get them too). A `failAtX` line is a dashed ink vertical line
  across the strip, and the player HQ a 6 px ink flag at its x. Completed objectives lose their
  diamonds.
- **Health balloons:** not shown (pickups stay a surprise).
- Visible only while Playing or Paused (hidden on menus, briefing and results); updates at
  10 Hz on unscaled time; never blocks clicks (raycastTarget off).
- Build decisions (B044, accepted): barrage balloons are 5 px pencil rings (3 px read as dots);
  the enemy base is a solid pencil square; a `destroyById`/`bombTarget` target not yet spawned
  shows its diamond on its upcoming tick; the `failAtX` line disappears when its objective
  completes; the frame shrinks to stay 12 px clear of the corner widgets on canvases narrower
  than about 1288 px (minimum 120 px); the vertical band runs from the lowest ground to the
  camera top; the HQ flag comes from the mission JSON, so it shows before the HQ spawns.

### 3.4 Pause menu (PauseView)

```
┌───────────────────────────────┐
│        [ PAUSED ]             │
│                               │
│      ┌──────────────┐        │
│      │   Resume     │        │
│      └──────────────┘        │
│                               │
│      ┌──────────────┐        │
│      │    Quit      │        │
│      └──────────────┘        │
│                               │
└───────────────────────────────┘
```

- Shown when Esc is pressed during Playing. Hides the canvas (or sets alpha 0) to reveal Playing HUD.
- Resume sets GameManager back to Playing. Quit calls `GameManager.QuitToMenu` (loads the MainMenu scene, or shows the menu in place when that scene is not in the build). Esc toggles pause; PlayerInput is off while paused.

### 3.5 Results (ResultView)

Shown on MissionEnded (1 s after a crash, while the wreck falls). A Menu button is always shown; Next only after a win below mission 5.

**Won state:**
```
┌───────────────────────────────┐
│      MISSION COMPLETE!        │
│                               │
│   Score: 2500                 │
│                               │
│   ┌──────────┐  ┌──────────┐ │
│   │  Retry   │  │  Next    │ │
│   └──────────┘  └──────────┘ │
└───────────────────────────────┘
```

**Lost state:**
```
┌───────────────────────────────┐
│      MISSION FAILED           │
│                               │
│   Score: 800                  │
│                               │
│   ┌──────────┐               │
│   │  Retry   │               │
│   └──────────┘               │
└───────────────────────────────┘
```

- On win, `SaveSystem.UnlockNextMission()` increments unlocked_mission.
- "Next" button is hidden if this was the last mission (no Mission N+1).
- "Retry" resets the mission: clears state, reloads MissionRunner, restores HUD.
- Score comparison: `best_score_N` from SaveSystem shows previous best above current score.

## 4. Event wiring summary

| Event | Listens on | Widget |
|---|---|---|
| `ScoreChanged` | HudView, ResultView | Score display / result score |
| `PlayerDamageChanged` | HudView | Damage bar fill + smoke warning icon |
| `BombsChanged` | HudView, PauseView | Bomb icons row |
| `EntityDestroyed` | HudView (objective text), ScoreKeeper (score) | Objective text count-down |
| `ObjectiveCompleted` | HudView (flash/hide objective briefly) | Brief visual feedback |
| `MissionEnded` | GameManager → ResultsView | Triggers ResultView show |

## 5. Construction order

All UI is built programmatically via uGUI components (`RectTransform`, `Image`, `TextMeshProUGUI` or Unity's legacy `Text` — prefer `TextMeshProUGUI` if available).
- `HudView.OnEnable()`: create the Canvas child hierarchy (damage bar, score text, objective text, bomb row), assign sprites by path via `Resources.Load<Sprite>()`.
- `MainMenuView`, `BriefingView`, `PauseView`, `ResultView` follow the same pattern: instantiate panel prefab → add children programmatically.
- No `.prefab` files. The `ProjectBuilder.BuildScenes()` method creates the canvas + EventSystem and attaches the initial view at scene load time.
