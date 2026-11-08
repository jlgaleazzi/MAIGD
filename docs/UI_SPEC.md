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

All UI uses the duotone palette from ART_STYLE.md:
- Background panels (`ui_panel` sprite): paper `#0F2A4A` fill, ink `#E8F1FA` wireframe border (2px).
- Buttons (`ui_button` sprite): hover state = pencil `#9DB4CC` glow ring; active = hostile `#E4572E` tint.
- Damage bar: frame `ui_damage_frame` containing a fill that reads 0→100%. Fill color is ink `#E8F1FA` up to ~69%, then transitions to hostile `#E4572E` (smoke warning at ≥ 70%).
- Bomb icons (`ui_bomb_icon`): 7 small biplane silhouettes; filled friendly `#F2C14E` when available, greyed-out `#9DB4CC` when empty.
- Text uses the game font fallback (no special font assets). Colour: ink `#E8F1FA`.

## 3. Screen layouts

### 3.1 Main Menu (MainMenuView)

Layout anchored to canvas center:

```
┌──────────────────────────────────────┐
│                                     │
│          [ title_logo ]              │
│       (Blueprint stencil letter)     │
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

- Mission buttons are only shown if the player has unlocked that mission (`SaveSystem.GetUnlockedMission() ≥ N`).
- Each button shows the mission title from `MissionData.title`.
- Clicking a locked button does nothing.
- Clicking an unlocked button transitions to BriefingView with that mission's ID.

### 3.2 Briefing (BriefingView)

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
│ Smoke ⚠️                               ──────────  │
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
| Damage bar fill | Top-left, 16px from corner | `GameEvents.PlayerDamageChanged` | Ink → hostile transition at 70% (smoke threshold). Bar height: 24px. Width: 200px max. Fill is a scaled sprite or rect. |
| Smoke warning icon | Directly below damage bar | Same event, ≥ 70% = visible | Pencil ⚠️ symbol, visible when smoke active |
| Score display | Top-right corner | `GameEvents.ScoreChanged` from ScoreKeeper | Right-anchored, large font (24pt) |
| Objective text | Below score | `ObjectiveTracker.GetDescription()` left-anchors under score | Shows remaining count; updates on EntityDestroyed |
| Bomb icons row | Bottom-left, 16px padding | `GameEvents.BombsChanged` from BombBay | 7 slots; filled = friendly yellow; empty = grey pencil color |

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
- Resume sets GameManager back to Playing. Quit loads MainMenu scene.

### 3.5 Results (ResultView)

Shown on MissionEnded:

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
