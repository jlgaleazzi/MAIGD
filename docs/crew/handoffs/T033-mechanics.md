TASK: T033
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs (created)
SUMMARY:
  Built the in-game HUD overlay view with all five widgets from UI_SPEC §3.3:
  damage bar fill (ink→hostile lerp at 70% threshold), smoke warning text,
  score display (top-right bold), objective text countdown, and bomb icon row (×7).
  All widgets update reactively via GameEvents subscriptions — zero polling in Update.
VERIFY:
  1. Open Unity project Biplanes/ and ensure HudView.cs compiles without errors.
  2. In Game scene with Playing state active: confirm HUD canvas is visible showing:
     - Damage bar (top-left, ink colour fill scaling 0→100%)
     - "SMOKE" label appears when player damage >= 70% (transition to hostile red)
     - Score text updates on top-right as kills occur
     - Bomb icons row at bottom-left: yellow filled for available, grey pencil for empty
     - Objective text below score decrements on each EntityDestroyed event
  3. Fire GameEvents.RaisePlayerDamageChanged(85f) from console → smoke visible, fill half-orange
  4. Fire GameEvents.RaiseBombsChanged(3) → first 3 icons yellow, last 4 grey
REQUESTS:
  - art agent: ensure sprites exist at Resources/Sprites/UI/damage_frame.png and Resources/Sprites/UI/bomb_icon.png (HudView falls back to tinted rectangles if missing)
  - mission-design agent: MissionRunner must call HudView.SetObjective(targetType, count) during mission init so the objective text has data to display
