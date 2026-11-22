# T961: QA of B044 HUD minimap

**Verdict: PASS.** There are 0 P0, 0 P1 and 2 P2 bugs. One P2 is a wrong test assertion, which QA fixed.

Unity is open, so the tests were compiled but not run.

## Checks run
- **`python tools/compile_check.py`:** PASS Biplanes, PASS Biplanes.Editor, exit 0.
- **Test compile (B018 scratch method):**
  - The runtime DLL was built with compile_check's helpers.
  - A temp copy of Biplanes.Tests.EditMode.csproj was retargeted from v4.7.1 to v4.7.2, with all 31 `Tests/EditMode/*.cs` files listed explicitly, including the new MinimapQaTests.cs.
  - Result: 0 errors. No `zz_*` files are left in the project.
- **Static checks:**
  - The `Biplanes.UI` namespace matches the UI/ folder.
  - Every API used exists in the repo: `MissionRunner.MissionData`, `Player`, `ObjectivesOpen`, `EnemyAircraft`, `BomberAI.SpawnId`, `ZeppelinAI.Id/ScoreKey`, `EnemyBase.Id/TypeKey`, `ScoreKeeper.ReinforcementFighterKey`, `TerrainStrip.Instance/GroundBaseY`, `ScrollController.Camera`, `PlayerPlane.Facing`, `GameManager.CurrentState`, and `ObjectiveEntry.failAtX` (default -1).
  - No `UnityEngine.Input` and no `rb.velocity`.
  - Each file has one public class. `MarkerPool` is a private nested class.
  - The .meta files are Unity-generated (59-byte Unity 6 format). No hand-written assets.

## UI_SPEC §3.3.1 conformance
| Item | Result |
|---|---|
| Frame | Top-centre, anchor and pivot (0.5,1), 12 px from the top, 480x44. Paper backing at 70% alpha. 1 px ink border, sliced, drawn last. |
| X mapping | `MapX` maps world x 0..length to the strip and clamps it (tested). |
| Terrain | Pencil polyline from `HeightAt` every 5 u (8 to 120 segments). Rebuilt on a new mission, terrain or width. |
| Band | From the lowest sampled ground to the camera top (decision f). Clamped (tested). |
| Camera bracket | Pencil outline over the camera span, clamped. |
| Player | 7 px ink triangle. 0 or 180 degrees from Facing. Drawn after every marker except the border. |
| Aircraft | Fighter and reinforcement: 3 px ink dot. Bomber: 5x3. Zeppelin: 12x4. Drawn by position, so right-flying fighters and reinforcements show (new test). |
| Ground | AAA: pencil tick on the terrain line. Balloon: 5 px pencil ring (decision a). Base: 6 px solid pencil square (decision b). |
| Upcoming | Pencil ticks at 40% alpha, at atX on the bottom edge, for enemy types only. Removed once `IsSpawnPending` is false (tested). |
| Diamonds | destroyById and bombTarget diamonds also show on the pending tick (decision c). destroyAllOfType diamonds go on live enemies only, and reinforcements are excluded (new test). Completed or ended missions show none (tested). |
| failAtX | Dashed ink line, drawn only for open destroyById objectives. Drops on completion and at mission end (tested). |
| HQ flag | From the JSON playerHQ spawns, before the HQ spawns (new test). |
| Health balloons | Never drawn: they are not enemy types and not in the runner lists (tested). |
| Palette | Only ink, pencil or paper, with alpha. raycastTarget is off on every Image (tested). |
| Update | 10 Hz on `unscaledTime`. Content shows only in Playing or Paused, and is hidden in Menu, Briefing, Won and Lost. |
| Pooling | Begin/Next/End pools. Unused markers are deactivated, not destroyed. 20 refreshes add no GameObjects (new test). |
| Lookups | `FindAnyObjectByType<GameManager>` and `GetComponentInParent<MissionRunner>` run once, in Create. |

## MissionRunner
- **Read-only:** B044 adds only `_groundEnemies` and its registration (AAA, barrage balloon, enemy base), plus `MissionLength`, `SpawnEntryCount` and `IsSpawnPending`. Nothing outside MinimapView reads them, so EnemyPressure, objectives and spawns are unchanged.
- **Cleanup:** the list is cleared in `TearDownPreviousMission` (Retry and Next) and in `OnDestroy` (scene reload), and pruned of destroyed entries at each spawn. The new test checks the teardown path.
- **Null-safety:** `IsLive` skips Unity-null, inactive and `IsDead` entities. The new test destroys a fighter between refreshes, and nothing throws.

## Layout (constant-pixel canvas)
- **Left reserve:** the DamageFrame reaches 16+208 = 224 px. The DAMAGE label reaches 216 px.
- **Right reserve:** the score and objective column reaches 316 px from the right edge.

| Canvas | Frame | Gap to left widgets | Gap to right widgets |
|---|---|---|---|
| 1920 | 480 px (720..1200) | 496 | 404 |
| 1280 | 480 px (400..880) | 176 | 84 |
| 1024 | 368 px (328..696) | 104 | 12 |

No overlap at any of the three. The new test checks this against the real widget RectTransforms.

## HUD tests
- HudBombIconsTests still holds by inspection:
  - Every minimap Image has a sprite.
  - Every minimap colour has ink, pencil or paper RGB.
  - The bomb-row lookups use `Find("BombRow/...")`, which the minimap doesn't affect.

## Tests added or changed by QA
- **MinimapTests.cs:** the LeftReserve assertion was corrected from 232 to 224 (BUG 1).
- **MinimapQaTests.cs (new), 5 tests:**
  - Layout_FrameClearsDamageBarLabelScoreAndObjective_At1920_1280_1024
  - LiveAircraft_AllTypesDrawn_ReinforcementsGetNoDiamond_NullSafe_EndClearsDiamonds
  - HqFlag_FromJson_HealthBalloonsNeverShown
  - RepeatedRefresh_CreatesNoNewGameObjects
  - Teardown_ClearsGroundEnemies

## Bugs
```
BUG: MinimapTests asserted LeftReserve == 232; the real value is 224 (test would fail)
PRIORITY: P2
OWNER: mechanics
WHERE: Tests/EditMode/MinimapTests.cs:102 (FrameWidth_Is480_At1920_AndNeverOverlapsTheCornerWidgets)
STEPS: HudView.LeftReserve = _padding 16 + _barWidth 200 + FramePadX 4*2 = 224. The B044 handoff also quotes 232.
EXPECTED: the test matches HudView (UI_SPEC §3.3: damage bar 200 px, 16 px from the corner)
ACTUAL: Assert.AreEqual(232f, left) fails. The layout is unaffected: the right reserve (316) sets the width.
STATUS: fixed by QA (assertion now 224)
```
```
BUG: The minimap allocates a string per fighter per refresh while a fighter objective is open
PRIORITY: P2
OWNER: mechanics
WHERE: Scripts/UI/MinimapView.cs:451-452 (a.gameObject.name compared against ReinforcementFighterKey)
STEPS: M1 (destroyAllOfType fighter) with about 10 live fighters at 10 Hz gives about 100 small string allocations per second.
EXPECTED: no steady per-refresh garbage (B044 "a steady refresh allocates nothing")
ACTUAL: small GC churn. There is no GameObject allocation, and the impact on WebGL is negligible. A cached reinforcement flag on FighterAI would remove it.
```

## Notes (not bugs)
- At Create, the canvas RectTransform may not be sized yet, so the first layout can use a small width. `LayoutIfNeeded` corrects it on the next 10 Hz refresh, and the strip is hidden during the Briefing anyway.
- The band top is sampled from the camera once per static rebuild. That's fine while the camera y is fixed.

## Minimap playtest checklist (user, Unity Play or a build)
1. **M1 to M5:** the strip shows at the top centre once Playing starts, and is hidden on the menu, briefing and result screens. It stays visible and still updates behind Pause (Esc).
2. **No overlap:** at 1920x1080, 1280x720 and 1024x768 (Game view sizes), the strip doesn't touch the damage bar, the DAMAGE label (take 70% damage), the score or the objective text.
3. **Camera and player:** the camera bracket tracks the view as you fly forward and when you turn back. The triangle flips on A/D and rides the plane's height.
4. **Ticks:** the faint ticks disappear as groups spawn.
5. **Health balloons:** they never appear on the strip.
6. **M1:** diamonds appear on live fighters only. A reinforcement wave shows dots without diamonds.
7. **M2:**
   - The bomber's diamond sits on its tick at x 80, then follows the bomber.
   - The dashed line is near the left edge (x 10).
   - Killing the bomber removes both.
8. **M3:** AAA ticks sit on the terrain line, each with a diamond.
9. **M4:**
   - The balloon rings have diamonds.
   - The base square shows a diamond on its tick before it spawns.
   - After the base is bombed, its diamond is gone.
10. **M5:**
    - The HQ flag is at x 360, with a dashed line there.
    - The zeppelin bar has a diamond.
    - Losing (the zeppelin reaches the HQ) or winning hides the strip on the results screen.
11. **Retry and Next:** the strip resets, with no stale markers from the previous mission.
12. **WebGL:** check in Chrome, Firefox and Safari that the strip is crisp and that clicks on pause and results buttons are never blocked.
