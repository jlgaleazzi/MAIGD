# Biplanes task board

Owner: orchestrator. Statuses: TODO, IN-PROGRESS, DONE, PARTIAL, BLOCKED.
Bug tasks start with `B`, for example `B007 [P1]`.

## Milestone summaries
_(The orchestrator adds a 5-line summary here at the end of each milestone.)_

**M6 Audio (2026-11-17): PASS (code), pending Unity run.**
- 23 generated placeholder WAVs (tools/synth_sfx.py, stdlib only, seeded); real recordings can replace them by name.
- AudioManager (voice pool, priorities, pan, jitter, loops, pause, single listener) + hooks in gameplay, FX and UI.
- QA T953 found a P0 (no AudioListener: silent game) and 7 P2s; all fixed (B032-B034) and re-verified in T955.
- Open: run EditMode tests with Unity closed, listening checklist (docs/qa/T953-verify.md), optional music T954.

## Board
| ID | Milestone | Owner | Task | Deps | Status |
|---|---|---|---|---|---|
| T001 | M0 | orchestrator | Write docs/UI_SPEC.md | none | DONE |
| T002 | M0 | art | bg_farmland_far tileable background (1920×1080 SVG+PNG) | none | DONE |
| T003 | M0 | art | bg_farmland_mid tileable background (1920×1080 SVG+PNG) | none | DONE |
| T004 | M0 | art | bg_farmland_near tileable background (1920×1080 SVG+PNG) | none | DONE |
| T005 | M0 | art | player_biplane sprite (160×80 SVG+PNG, wireframe duotone) | none | DONE |
| T006 | M0 | art | player_biplane_frame2 propeller animation frame (160×80 SVG+PNG) | none | DONE |
| T007 | M0 | mechanics | Core: TeamTag enum + component | none | DONE |
| T008 | M0 | mechanics | Core: IDamageable interface + DamageSource enum | T007 | DONE |
| T009 | M0 | mechanics | Core: Health class (IDamageable impl, percent dmg, death hook) | T008 | DONE |
| T010 | M0 | mechanics | Core: GameEvents static event bus + Raise* helpers | T008 | DONE |
| T011 | M0 | mechanics | Core: GameBalance JSON loader (Resources/Balance.json) | T008 | DONE |
| T012 | M0 | mechanics | Core: SaveSystem (PlayerPrefs: unlocked_mission, best_score) | T010 | DONE |
| T013 | M0 | mechanics | Core: GameManager state machine (Menu/Briefing/Playing/Paused/Won/Lost) | T010,T012 | DONE |
| T014 | M0 | mechanics | Core: ScrollController (camera x = mission distance, scrollSpeed from mission) | T010 | DONE |
| T015 | M0 | mechanics | Core: ScoreKeeper (add score on EntityDestroyed, tie to Balance) | T010,T011 | DONE |
| T016 | M0 | mechanics | Weapons: Projectile base class (speed, team, damage, lifetime) | T008 | DONE |
| T017 | M0 | mechanics | Weapons: ObjectPool<T> generic pool (no per-frame Instantiate) | T016 | DONE |
| T018 | M0 | mechanics | Weapons: Bullet class (player projectile, inherits Projectile) | T016,T011 | DONE |
| T019 | M0 | mechanics | Weapons: Bomb class (gravity, inherits x-velocity, collides ground) | T016 | DONE |
| T020 | M0 | mechanics | Weapons: FlakShell class (enemy AA projectile) | T016 | DONE |
| T021 | M0 | mechanics | Player: PlayerInput (WASD + Space hold autofire + Enter bomb drop) | T018,T019 | DONE |
| T022 | M0 | mechanics | Player: PlayerPlane (Rigidbody2D Kinematic, takeoff state onRunway, smoke trail ≥70%) | T021,T009 | DONE |
| T023 | M0 | mechanics | Player: BombBay (capacity 7, drop logic) | T019,T022 | DONE |
| T024 | M0 | mechanics | Enemies: StaticObstacle (building/tree trigger collider → collision damage) | none | DONE |
| T025 | M0 | mechanics | Enemies: HealthBalloon pickup (restore health, Heal color) | T009 | DONE |
| T026 | M0 | mechanics | Enemies: FighterAI (sine/straight patterns, shoot at player) | T018,T016 | DONE |
| T027 | M0 | mechanics | Enemies: AAAGun (ground-bound, fires flak up, damageable by Bomb only) | T020 | DONE |
| T028 | M0 | mechanics | Enemies: BomberAI (targetX reach = mission fail hook, escort pattern) | T016 | DONE |
| T029 | M0 | mechanics | Enemies: BarrageBalloon (tether visual, damageable) | none | DONE |
| T030 | M0 | mechanics | Missions: MissionData serializable classes (matches JSON schema §5) | none | DONE |
| T031 | M0 | mechanics | Missions: MissionRunner (load JSON, spawn atX, track objectives) | T030,T026,T027,T028 | DONE |
| T032 | M0 | mechanics | Missions: ObjectiveTracker (eval destroyAllOfType/destroyById/bombTarget/surviveToEnd) | T031 | DONE |
| T033 | M0 | mechanics | UI: HudView (damage bar, smoke tint at 70%, bomb icons ×7, score, objective text) | T010,T015,T023 | DONE |
| T034 | M0 | mechanics | UI: MainMenuView (mission select limited to unlocked_mission) | T012 | DONE via T934 (QA PASS in T936) |
| T035 | M0 | mechanics | UI: BriefingView (show mission title + briefing text from JSON) | T030 | DONE |
| T036 | M0 | mechanics | UI: PauseView (Esc toggle, resume button) | none | DONE |
| T037 | M0 | mechanics | UI: ResultView (win/lose, score, retry, next mission) | T015,T012 | DONE |
| T038 | M0 | mechanics | Editor: ProjectBuilder (BuildScenes, ConfigureProject, build targets) | none | TODO |
| T039 | M0 | qa | Compile + SceneBuilder smoke test for M0 | T038,T015,T022 | TODO |
| T040 | M1 | art | enemy_fighter sprite (150×75 SVG+PNG) | none | DONE |
| T041 | M1 | mission-design | Mission 1 JSON: takeoff, fighter wave, health balloon, terrain (mission_01.json) | none | DONE |
| T042 | M1 | mechanics | Airfield and player_hq ground tiles for M1 | T024 | DONE |
| T043 | M1 | qa | Playtest mission_01 full vertical slice in WebGL build | T040,T041,T039 | TODO |
| T044 | M2 | art | enemy_bomber sprite (320×140 SVG+PNG) | none | DONE |
| T045 | M2 | art | health_balloon pickup sprite (70×100 SVG+PNG) | none | DONE |
| T046 | M2 | art | tree_a and tree_b sprites (80×120 each, SVG+PNG) | none | DONE |
| T047 | M2 | mechanics | BomberAI targetX failAtX integration (needs BomberAI.SetTargetX or a Spawn overload + MissionRunner wiring; until then bombers never drop bombs, per B004 handoff) + escort pattern support | T028,T026 | TODO |
| T048 | M2 | mission-design | Mission 2 JSON: bomber + escort, failAtX (mission_02.json) | T044,T045 | DONE |
| T049 | M3 | art | aaa_gun sprite (100×80 SVG+PNG) | none | DONE |
| T050 | M3 | art | barrage_balloon sprite (90×140 SVG+PNG) | none | DONE |
| T051 | M3 | art | building_barn and building_house sprites (140×110 each, SVG+PNG) | none | DONE |
| T052 | M3 | mission-design | Mission 3 JSON: AAA defenses + enemy planes in territory (mission_03.json) | T049,T050,T051 | DONE |
| T053 | M3 | qa | Playtest missions 2 and 3 in WebGL build | T048,T052,T047 | TODO |
| T054 | M4 | art | zeppelin_hull sprite (900×220 SVG+PNG) | none | DONE |
| T055 | M4 | art | zeppelin_turret sprite (60×40 SVG+PNG) | none | DONE |
| T056 | M4 | art | enemy_base hangar sprite (260×120 SVG+PNG) | none | DONE |
| T057 | M4 | art | airfield and player_hq ground sprites (400×100 each, SVG+PNG) | T042 | DONE |
| T058 | M4 | mechanics | ZeppelinAI hull + N ZeppelinTurret children (turret destruction → hull vulnerable) | T054,T055 | TODO |
| T059 | M4 | mission-design | Mission 4 JSON: barrage balloons, AAA, enemy planes, bomb target base (mission_04.json) | T056,T057 | DONE |
| T060 | M5 | art | zeppelin_hull and zeppelin_turret already done in M4; add stormsky backgrounds for M5 | none | DONE |
| T061 | M5 | art | title_logo (1200×300 SVG+PNG, blueprint stencil lettering) | none | DONE |
| T062 | M5 | art | ui_bomb_icon, ui_damage_frame, ui_panel, ui_button sprites (various sizes) | none | DONE |
| T063 | M5 | art | explosion_0..5 sprites (128×128 6-frame ink burst PNG sequence) | none | DONE |
| T064 | M5 | art | smoke_puff sprite (48×48 pencil-hatched puff PNG) | none | DONE |
| T065 | M5 | art | flak_burst_0..3 sprites (96×96 hostile-tinted burst PNG) | none | DONE |
| T066 | M5 | art | bg_enemyland_far/mid/near tileable backgrounds (1920×1080) | none | DONE |
| T067 | M5 | art | bg_frontline_far/mid/near tileable backgrounds (1920×1080) | none | DONE |
| T068 | M5 | art | bg_stormsky_far/mid/near tileable backgrounds (1920×1080) | none | DONE |
| T069 | M5 | mission-design | Mission 5 JSON: zeppelin + turrets + fighter escort, destroy before HQ (mission_05.json) | T054,T055 | DONE |
| T070 | M5 | mechanics | Terrain crash integration (static obstacles at ground level trigger collision damage) | T024,T014 | DONE |
| T071 | M5 | qa | Playtest mission_05 full vertical slice | T069,T058 | TODO |
| T072 | M5 | qa | Pause, retry, results flow verification | T035,T036,T037 | TODO |
| T073 | M5 | mechanics | Balance pass: tune all mission JSONs + Balance.json for pacing | T041,T048,T052,T059,T069,T011 | DONE |
| T074 | M5 | qa | Final QA milestone pass: Windows build, Mac build, WebGL build | none | TODO |
| T075 | M5 | art | project-wide rasterization check (all SVG→PNG, preview verification) | T068,T061,T062 | TODO |
| T076 | M1 | mechanics | Missions: TerrainStrip (HeightAt interpolation + chunked runtime-texture ground, ARCHITECTURE §3 Missions). Files: TerrainStrip.cs | none | DONE |
| T077 | M1 | mechanics | Ground contact: PlayerPlane crashes when collider bottom ≤ TerrainStrip.HeightAt (not while OnRunway); Bomb explodes + recycles at ground. Files: PlayerPlane.cs, Bomb.cs | T076 | DONE |
| T078 | M1 | mechanics | Missions: ParallaxBackground (3 layers × 2 tiles, LateUpdate offset formula) + Balance.json `background` factors + GameBalance BackgroundData. Files: ParallaxBackground.cs, Balance.json, GameBalance.cs | none | DONE |
| T079 | M1 | mechanics | Wire world into missions: MissionRunner creates ParallaxBackground + TerrainStrip after ScrollController and uses HeightAt for spawns; ProjectBuilder camera clears to paper #0F2A4A. Files: MissionRunner.cs, ProjectBuilder.cs | T076,T078 | DONE |
| T080 | M1 | qa | Verify ground + background: EditMode tests for TerrainStrip.HeightAt (before/between/after keyframes, empty) and the parallax offset formula; playtest checklist for missions 1–5 (theme, scrolling, crash on ground, bombs burst on ground) | T077,T079 | TODO |
| T906 | M1 | mechanics | Launch crash, controls, sprite import and UI paths fix (direct fix) | none | DONE |
| T907 | M1 | mechanics | Bullet hits, ramming, enemy HP, enemySpeed, randomized plane entry, ground spawn timing (direct fix) | T906 | DONE |
| T908 | M1 | art | Player and enemy fighter sprites from user blueprints (tools/import_blueprints.py) | none | DONE |
| T909 | M1 | art | Blueprint importer: palette faction marks (roundel friendly, Iron Cross hostile), Fokker line weight matched to player, player_biplane_frame2 from blueprint, ART_STYLE "blueprint detail" rules | T908 | DONE |
| T910 | M1 | art | Restyle enemies to blueprint detail: aaa_gun, zeppelin_hull, zeppelin_turret (bomber waits for user blueprint) | T908 | DONE |
| T911 | M1 | art | Restyle obstacles and pickups to blueprint detail: barrage_balloon, building_barn, building_house, tree_a, tree_b, health_balloon | T908 | DONE |
| T912 | M1 | art | Restyle targets to blueprint detail: airfield, enemy_base, player_hq | T908 | DONE |
| T913 | M1 | art | Restyle fx to blueprint detail: explosion_0..5, flak_burst_0..3, smoke_puff | T908 | DONE |
| T914 | M2 | art | enemy_bomber from user blueprint (blueprints/enemy_bomber_blueprint_clean_256px.png, supplied 2026-11-12) | T909 | DONE |
| T915 | M1 | mechanics | Propeller animation: alternate player_biplane / player_biplane_frame2 (same pivot, from T909) on PlayerPlane | T909 | TODO |
| T916 | M1 | mechanics | Check zeppelin_turret placement on the hull (curved saddle, base at y≈37 of 40) and enemy_base ground line (now y=116 of 120) against spawn alignment | T910,T912 | TODO |
| T917 | M1 | qa | PNG palette spot-check for blueprint-sourced sprites (rasterize --check covers SVGs only); verify the fx sequences animate smoothly in game | T909,T913 | TODO |
| T918 | M1 | art | Faction marks back to ink white (user decision): importer roundel/crosses, aaa_gun, zeppelin, bomber, enemy_base, airfield, player_hq | T909 | DONE |
| T919 | M1 | art | Engraved-sketch backgrounds: farmland far/mid/near + ART_STYLE background rules (mid/near transparent; fixes the opaque-layer bug) | T918 | DONE |
| T920 | M1 | art | Engraved-sketch backgrounds: frontline far/mid/near | T918 | DONE |
| T921 | M1 | art | Engraved-sketch backgrounds: enemyland far/mid/near | T918 | DONE |
| T922 | M1 | art | Engraved-sketch backgrounds: stormsky far/mid/near | T918 | DONE |
| T923 | M1 | art | Opaque paper backing under player and enemy plane sprites (importer add_backing; rasterize BACKED for enemy_bomber) | T919 | DONE |
| T924 | M1 | mechanics | Reversible flight (Defender-style): A/D set facing, camera scrolls in facing direction with eased turns and mission limits, plane eases to trailing third, bullets fire in facing direction, spawns/progress use farthest x, terrain window both ways | T079 | DONE |
| T925 | M1 | qa | Verify T924 (compile, static review, EditMode tests for scroll direction/limits and facing anchor) | T924 | DONE (PASS, 30 tests, not yet run) |
| B001 [P1] | M1 | mechanics | "Next mission" without scene reload keeps the old camera x, player, enemies and ObjectiveTracker (limits/FarthestX/spawns start at old end; kills double-counted). Reset camera to mission start and tear down previous mission objects in MissionRunner.StartMission | T924 | DONE |
| B002 [P2] | M1 | mechanics | T925 P2s: W+A on runway takes off facing left; length <= start x should pin the camera; first-use pooled bullets fly at speed 10 (Bullet.Start after Init); add com.unity.test-framework to manifest explicitly | T924 | DONE |
| T926 | M1 | qa | Verify B001 + B002 (compile, static review, 7 more EditMode tests incl. MissionRestartTests) | B001,B002 | DONE (PASS, 37 tests, not yet run) |
| B003 [P2] | M1 | mechanics | A held through takeoff turns the plane left on the next tick (B002a fix lasts 1 tick). Orchestrator decision: ignore A after takeoff until it is released and pressed again | B002 | DONE |
| B004 [P1] | M1 | mechanics | FlakShell.InitPool is never called, so AAA guns never fire. Call it in MissionRunner.StartMission or on first AAAGun.Spawn (pre-existing, found by T926) | none | DONE |
| B005 [P1] | M2 | mechanics | BomberAI drops Team.Player bombs from the player's pool, so they can't hurt the player. Give it an enemy bomb (Team.Enemy) with its own pool (pre-existing, found by T926) | none | DONE |
| B006 [P2] | M1 | mechanics | Mission restart leftovers: Bullet/Bomb pool roots (HideAndDontSave) leak on Next and Retry; ScoreKeeper.CurrentScore carries over on Next; HandleObjectiveComplete/SaveBestScore runs every frame after a win (add a mission-ended guard) | B001 | DONE |
| B007 [P2] | M2 | mechanics | Health.GetDamageMultiplier maps DamageSource.Bomb on the player to damageTaken.fighterBullet (8); only bomber bombs reach the player now, so map it to damageTaken.bomberGunner (10) | B005 | DONE |
| T927 | M1 | qa | Verify B003–B006 and their interaction (compile, static review, 17 more EditMode tests incl. EnemyWeaponsAndPoolTests) | B003,B004,B005,B006 | DONE (PASS, 54 tests, not yet run) |
| B008 [P1] | M1 | mechanics | Player bombs can pass through the thin AAA trigger (about 50–60% register, static estimate). Bomb ground burst must damage nearby non-player IDamageables (OverlapCircle ≈ 0.5) | B004 | DONE (reopened by T928, fixed and verified by T929) |
| B009 [P2] | M1 | mechanics | T927 P2s: AAA kill scores twice (remove AddPoints in AAAGun.HandleDeath); AAA aim never leads (expose PlayerPlane world velocity, use in PredictPosition); FlakShell/EnemyBomb EnsurePool rebuild must Dispose the old pool | B004,B005 | DONE |
| T928 | M1 | qa | Verify B007–B009 (compile, static review, 7 more EditMode tests; consolidated playtest checklist in docs/qa/B008-verify.md) | B007,B008,B009 | DONE (B008 partial fail, 61 tests, not yet run) |
| B010 [P2] | M1 | mechanics | AAA lead uses dy / ShellSpeed as flight time, so shells pass about 1.5 units behind the plane; compute flight time from distance to the predicted point (iterate 1–2 times) | B009 | DONE |
| B011 [P2] | M1 | mechanics | Player Bullet and Bomb hit and recycle on a HealthBalloon (Neutral, ApplyDamage does nothing); they should pass through it (pre-existing) | none | DONE |
| T929 | M1 | qa | Verify reopened B008, B010, B011 (compile, static review and simulation, 3 more EditMode tests; playtest checklist in docs/qa/B010-verify.md) | B008,B010,B011 | DONE (PASS, 64 tests, not yet run) |
| T930 | M1 | qa | Needs the user in Unity: run Test Runner EditMode (70 tests) and the playtest checklists in docs/qa/B010-verify.md, B008-verify.md and B012-verify.md (bomber); step 6 of B003-verify.md decides R1 (ObjectPool.Dispose on quit: switch to DestroyImmediate if pool roots survive leaving Play mode) | T929 | TODO |
| B012 [P2] | M2 | mechanics | Refit BomberAI's trigger collider (now 2 x 1.5) to the blueprint bomber from T914: opaque bbox 305x136 px of 320x140; art suggests about 3.0 x 0.8 units at offset (0, +0.05), plus an optional 1.0 x 1.1 box over the wings at x +0.6 | T914 | DONE (3 fitted boxes; wheel band left uncovered on purpose, orchestrator decision: forgiving hitbox) |
| T931 | M2 | qa | Verify B012 (hitboxes measured on the sprite alpha mask, flip, recycled-projectile guard; 6 EditMode tests in BomberHitboxTests) | B012 | DONE (PASS, 70 tests, not yet run) |
| T932 | M4 | mechanics | Goal-agent gaps: EnemyBase (bomb target), PlayerHQ (friendly ground structure), and remove the NotImplementedException in HealthBalloon.Heal | none | DONE (QA PASS in T936) |
| T933 | M5 | mechanics | Goal-agent gap: ZeppelinAI hull + ZeppelinTurret (hull only damageable after all turrets are down, or hullVulnerableEarly) | none | DONE (QA PASS in T936) |
| T934 | M0 | mechanics | Goal-agent gap: MainMenuView with mission select limited to the unlocked mission (same as T034) | none | DONE (QA PASS in T936) |
| T935 | M4 | mechanics | MissionRunner spawn cases "enemybase", "playerhq", "zeppelin" (one agent: all touch MissionRunner.cs) + bombTarget wiring so mission 4 can complete: subscribe EnemyBase.Destroyed, ObjectiveTracker completes bombTarget by targetId when KilledByBomb (no extra points); see T932 handoff REQUESTS | T932,T933 | DONE (QA PASS in T936) |
| T936 | M4 | qa | Verify T932–T935, B014, M05-ESCORT (compile, ARCHITECTURE conformance, ObjectiveTracker rewrite: missions 1–3 can now be won for the first time, mission 4 and 5 content, EditMode tests incl. ObjectiveTracker) | T935 | DONE (PASS; 6 bugs filed; 105 tests, not yet run) |
| B013 [P1] | M0 | mechanics | Retry loads scene "MainScene", which doesn't exist (GameManager.cs:219 RetryMission, ResultView.cs:308); ProjectBuilder builds "Game". Since GameManager now boots into the menu, Retry must reload "Game" and start the current mission via the _pendingMission path, not land on the menu (found by T934) | T934 | TODO |
| T937 | M0 | mechanics | UI flow wiring found by T934: Briefing not in the flow (menu -> ShowBriefing creates BriefingView, TriggerTransition calls GameManager.StartMission); PauseView and ResultView are never created and nothing handles Esc; other views' legacy Text has no font set (check in play mode, MainMenuView sets LegacyRuntime.ttf). Overlaps T035–T037 | T934  ResultView also: CreateButton never adds a Button component (Retry/Next unclickable), no fonts, title/score/best all at y 0 (from B017). | DONE (QA PASS T941) |
| B014 [P2] | M5 | mechanics | Zeppelin follow-ups from T933: enemySpeed.zeppelin in Balance (ZeppelinSpeed is a magic number), FlakShell overload that doesn't force the climb, turret flak applies damageTaken.zeppelinTurret (new DamageSource), no velocity overwrite in ZeppelinTurret.Fire | T933 | DONE (QA PASS in T936) |
| M05-ESCORT | M5 | mission-design | Mission 5 escort fighters spawn at atX 40 before the zeppelin at atX 60, so FighterAI's one-time escort lookup misses it; move escorts to atX >= 60 | T933 | DONE (QA PASS in T936) |
| B015 [P2] | M5 | mechanics | Escort fighters stack on the zeppelin's exact position (escort pattern has no per-fighter offset); add an offset in FighterAI | M05-ESCORT | TODO |
| V001 [P2] | M1 | mission-design | tools/validate_missions.py fails missions 1–4 on two rules (negative plane y, id on groups with count > 1; M3 also 8 bomb targets) that predate this work, and mission 5 only passed after the escort groups' ids were cleared and plane y made positive. ARCHITECTURE §3 says plane y is ignored at runtime, so decide: relax the rules, or fix the missions. Also mission 5 duration warning (350 s vs 270 s target) | none | TODO |
| B016 [P2] | M2 | mechanics | A failAtX loss shows "Player crashed": GameManager.SetState(Lost) has no reason parameter (add LoseMission(string reason) + ResultView text); and the player keeps input after a failAtX loss because only a crash calls UnsubscribeInput (disable input on any MissionEnded) | T935 | TODO |
| B017 [P1] | M1 | mechanics | Mission end rules (T936 P1-1, decision in GDD log 2026-11-13): mission is LOST when the camera reaches `length` with any non-surviveToEnd objective still open. Also: B013 (Retry: load "Game" + _pendingMission), B016 (LoseMission(reason) + disable input on any end), guard GameManager.SetState against Won->Lost after an end (P2-1), add a Heal case to Health.GetDamageMultiplier (P2-3, no more unknown-source warning per balloon) | T935 | DONE (QA PASS T940) |
| B018 [P1] | M2 | mechanics | Entity-based failAtX (T936 P1-3): failAtX is the WORLD x the target entity must not reach (original ARCHITECTURE §5 meaning; T935 made it camera-based, which the player can dodge by turning back). Give ZeppelinAI and BomberAI a targetX set by MissionRunner from the objective; entity reaching targetX (flying left) = mission lost with reason "reached its target". Covers T047. Update ARCHITECTURE §3/§5 | B017 | DONE (QA pending; V003 must fix mission geometry) |
| V002 [P1] | M3 | mission-design | Mission 3 needs 8 bomb-only AAA with 7 bombs and no refill, so it can't be won: cut to 5 AAA required (margin for missed bombs), keep difficulty with fighters and flak density. Check M4 and M5 bomb budgets too. Folds the M3 item of V001 | T935 | DONE (QA PASS T940) |
| V003 [P1] | M5 | mission-design | Mission 5 geometry (T936 P1-3): HQ must be where the zeppelin is heading (HQ at the start side, zeppelin enters from the right flying left, failAtX = HQ world x); fix the zeppelin y (centre is about 0.9, not 3.7); same check for mission 2's bomber and its failAtX (370). Needs B018 for the fail rule | B018 | TODO |
| B019 [P2] | M5 | mechanics | A rammed ZeppelinTurret may survive depending on trigger callback order (T936 P2-2) | T933 | TODO |

| T938 | M1-M5 | qa | Verify B017/B018/V002/V003/V001a: FAIL (0 P0, 1 P1, 5 P2). B017, B018, V001a, V002 PASS. V003 FAIL: M5 zeppelin y 0.9 puts the hull at ground level, so it cannot be destroyed. See docs/qa/T938-verify.md | DONE |
| V004 [P1] | M5 | mission-design | T938 P1-1/P2-2/P2-4: zeppelin y 4.9, id that cannot match turret names, escorts at atX>=422, map length so the camera cannot end the map before the zeppelin reaches the HQ | T938 | DONE (QA T939) |
| B020 [P2] | M2/M5 | mechanics | T938 P2-1/P2-3/P2-5: bomber name carries its id for escort lookup, whole-id escort match, readable objective text, validator bomber offset 8 and zeppelin y>=3 rule | T938 | DONE (QA T939) |
| T939 | M5 | qa | Re-verify V004/B020: FAIL (0 P0, 1 P1, 1 P2). T938 findings closed. New P1: M5 zeppelin shield can never drop (underside turret level with gondolas that absorb bullets). P2: zeppelin pops in on screen. See docs/qa/T939-verify.md | DONE |
| B021 [P1] | M5 | mechanics | T939: lower the underside turret (offset y about -1.10) so a clear bullet band exists, and widen the zeppelin spawn look-ahead | T939 | DONE (QA T940) |
| T940 | M5 | qa | Verify B021: PASS (0 bugs). Underside turret band 0.31, zeppelin spawns off screen, HQ loss before map end with 5.8 s margin at 4:3. Tests compiled, not run. Checklist M5-1..M5-15 in docs/qa/T940-verify.md | DONE |
| A-ZEP-PYLON | M5 | art | Mount pylon under the zeppelin hull for the lowered turret; zeppelin_hull.png re-rasterized (900x220) | DONE (check M5-2 in play) |
| B022 [P1] | M1 | mechanics | User playtest: white bar bottom-left instead of 7 bomb icons. Fixed in HudView and BombBay (icons built with sprite, start-up state independent of event order). Also fixed: damage fill drawn full white, HUD texts had no font, negative widths. New HudBombIconsTests (6), asmdef references UnityEngine.UI. QA pending, tests not run | DONE (QA PASS T941) |
| B023 [P1] | M1 | mechanics | GDD gap found 2026-11-14: enemy airplanes 'shoot at the player' (GDD Enemies 1) but FighterAI and BomberAI have no gun code; Balance damageTaken.fighterBullet (8) and bomberGunner (10) are never used by an enemy. Needs an enemy bullet pool (EnemyBullet, damage 1 hit unit, DamageSource.Bullet) plus fighter firing rules (range, cadence, aim) tuned in Balance.json; goal_agent spec.json should gain this requirement | DONE (QA PASS T941) |
| B024 [P1] | M1 | mechanics | User playtest bug 3: 'a single enemy bullet stops the player plane'. Not reproducible from the code (no enemy fires, flak is 15%, bombs 10%). Need the mission, the projectile's look and the Console line | DONE (cause: fatal plane collision, see B025) |
| B025 [P1] | M1 | mechanics | Playtest bug 3 resolved as design: mid-air contact with an enemy fighter/bomber (PlayerPlane.OnTriggerEnter2D) must be a 50% hit (new Balance damageTaken.planeCollision), enemy still destroyed, no TriggerCrash unless damage reaches 100%; terrain/buildings/trees/zeppelin ramming stay fatal. Update tests, ARCHITECTURE §3/§4. Waits for T937 (same PlayerPlane.cs) | T937 | DONE (QA PASS T941) |
| B026 [P1] | M1 | mechanics | User request 2026-11-14: left mouse = fire (hold autofire), Space = drop bomb, Enter no longer bombs. UI clicks must not fire; Space on the briefing must not drop a bomb. GDD/ARCHITECTURE/README already updated | DONE (QA pending; stale comments in PlayerPlane.cs:508 and GameManager.cs:74 to fix) |
| T941 | all | qa | Verify B022/T937/B023/B025/B026: PASS (0 P0, 0 P1, 3 P2). 10 GameFlowTests added. Checklist in docs/qa/T941-verify.md §9. Tests compiled, not run | DONE |
| B027 [P2] | M1 | mechanics | T941: BriefingView Begin button clickable during the 0.5 s fade (double-click on Next skips the briefing): block raycasts until the fade ends | TODO |
| B028 [P2] | M3 | mechanics | T941: AAAGun keeps firing at the wreck during the 1 s crash fall: skip the burst when PlayerRef.HasCrashed (AAAGun.cs:156) | TODO |
| T942 | all | qa | Review of the goal agent's pitch code (gpt-oss:120b, 6 steps, all compiled): FAIL (1 P0, 2 P1, 6 P2). P0: facing left the player's gun fires right from the tail. See docs/qa/T942-verify.md | DONE |
| B029 [P0] | all | mechanics | Fix all T942 bugs; decision: fighters fire along their nose (pitch eases toward the player, fire only within aimToleranceDegrees), ease rate and fighter max pitch in Balance | DONE (QA T943) |
| T943 | all | qa | Re-verify B029: PASS (0 P0, 0 P1, 2 P2). All 9 T942 bugs fixed; M5 turret bands still >= 0.31 in level flight. P2: fighters fire on the first tick the nose is within tolerance (can be ~14 deg off while turning; balance only, tune after playtest E-5). Latent: easeDegreesPerSecond must stay below ~72 or runway takeoff crashes (test guards it) | DONE |
| T944 | all | art | Redraw FX in pencil-sketch style (explosion_0..5, smoke_puff, flak_burst_0..3), same names/sizes; generator art-src/fx/gen_pencil_fx.py; rasterize --check 40/40; palette checked; orchestrator viewed sheet docs/art/fx_preview.png | DONE |
| B030 [P2] | all | mechanics | From T944: FxManager plays explosion_0..5 at ~0.08 s/frame instead of scaling explosion_0 0.1x->2x (blurs hatching; if growing keep 0.6x-1.3x); flak shells play flak_burst_0..3; optional smoke_puff trail from damaged planes | DONE (QA T945) |
| T945 | all | qa | Verify B030: PASS (0 P0, 0 P1, 3 P2). FxManagerTests (11) added; QA fixed a FlakBurst leak in ZeppelinAndBaseTests TearDown. Orchestrator fixed ART_STYLE explosion row. Tests compiled, not run | DONE |
| B031 [P2] | all | mechanics | From T945: AAA lifetime airbursts happen off-screen (6 u/s x 3 s climb) and use flak-cap slots; consider shorter fuse or proximity burst. Decide after playtest | TODO |
| T946 | M6 | orchestrator | Audio spec: GDD Style Guide "Sound effects" + ARCHITECTURE §3 Audio, clip list, ownership (art owns tools/synth_sfx.py and Resources/Audio/**), Balance `audio` block | none | DONE |
| T947 | M6 | orchestrator | Extend .claude/agents/art.md and .opencode/agents/art.md with the audio role (synth_sfx.py, WAV format, listen-check via waveform/spectrum report) | T946 | DONE |
| T948 | M6 | art | tools/synth_sfx.py: seeded procedural generator for all 23 clips in ARCHITECTURE §3 Audio (period WWI flavour: rotary engine, MG rattle, thumps, paper/pencil foley), writes Resources/Audio/sfx/*.wav; placeholder quality, swappable for recordings later Done: 23 clips, 1.7 MB, --check 0 errors. | T947 | DONE |
| T949 | M6 | mechanics | AudioManager core: Scripts/Audio/{AudioManager,SfxId,AudioMix}.cs, AudioData + `audio` block in GameBalance/Balance.json, voice pool, pan, jitter, loops, pause handling; EditMode tests for AudioMix | T946 | DONE (QA T953/T955) |
| T950 | M6 | mechanics | Gameplay hooks 1: player gun, bomb release/impact, enemy guns, flak fire/burst, explosion/explosion_big (FxManager, FlakShell, AAAGun, BombBay/Bomb, PlayerPlane fire, EnemyBullet spawners) | T949 | DONE (QA T953/T955) |
| T951 | M6 | mechanics | Gameplay hooks 2: engine loop with pitch (climb/dive, sputter at smokeThreshold), smoke crackle loop, hit_player vs hit_enemy, crash, pickup, zeppelin hum, damage warning on crossing smokeThreshold | T949 | DONE (QA T953/T955) |
| T952 | M6 | mechanics | UI hooks: ui_click, ui_page, ui_pause, briefing typewriter + stamp on Begin, win/lose on MissionEnded | T949 | DONE (QA T953/T955) |
| T953 | M6 | qa | Verify M6 audio: hooks fire once per event, no loops survive Retry/scene reload, pause behaviour, voice cap, missing clips safe; manual listening checklist FAIL: 1 P0 (no AudioListener, game silent) + 7 P2; AudioManagerQaTests (16) added. | T948,T950,T951,T952 | DONE |
| B032 [P0] | M6 | mechanics | From T953 B1/B2/B3/B7/B8: AudioManager keeps one AudioListener; crash cue 0.1 s before landing (fallback at result screen); crash tail stopped on unload; no manager on quit; gun/bomb cues only for spawned projectiles | T953 | DONE (QA T955) |
| B033 [P2] | M6 | mechanics | From T953 B4/B5/B6: one explosion cue per bomb kill; AAA death explosion; turret FlakFire + throttled HitEnemy | T953 | DONE (QA T955) |
| B034 [P2] | M6 | art | Re-cut crash.wav: 1.0 s, crunch at 0.103 s; other 22 clips byte-identical | T953 | DONE (QA T955) |
| T955 | M6 | qa | Re-verify B032-B034: PASS, all 8 T953 bugs closed, 0 new bugs. AudioFixesQaTests (12) added. M6 PASS pending the EditMode run and the listening checklist in docs/qa/T953-verify.md | B032,B033,B034 | DONE |
| T954 | M6 | orchestrator | Optional: music (briefing/result screens only, quiet or none in flight); decide source after T953 playtest | T953 | TODO |
| T956 | all | orchestrator | Style loop (tools/style_loop): Generator -> Evaluator -> Refiner enforcing the GDD Style Guide (palette, accents, fills, line weight, background quiet, narrative voice, vocabulary, grounding). GDD gained Narrative voice + Enforced rules table. Applied: 2 SVG line-weight fixes (PNGs re-rendered), 5 mission briefings rewritten by gpt-oss:120b on the Spark; 35 offline tests | T946 | DONE |
| B035 | M7 | mechanics | Fighters take 3 hits (enemyHealth.fighter 75); EnemyPressure reinforcement waves after 2 s without enemy aircraft on screen (Balance `pressure`) | none | DONE (QA T957-T959) |
| V005 | M7 | mission-design | More fighters M1-M3 (37/29/46), waves timed for ~2 s gaps; V001 negative-y fixed | none | DONE (QA T957) |
| V006 | M7 | mission-design | More fighters M4-M5 (48/62), waves timed for <2 s gaps; V001 fixed | none | DONE (QA T957) |
| A-BOMB | M7 | art | player_bomb sprite: 32x14 horizontal copy of the HUD bomb icon, ink white | none | DONE |
| B037 | M7 | mechanics | AAA destroyable by 5 player bullets (enemyHealth.aaa 125); one bomb still kills outright; throttled HitEnemy | none | DONE (QA T957) |
| B036 | M7 | mechanics | Parabolic bombs (BombBallistics, full plane velocity, gravity 10, drag 0.15, nose along path), player_bomb sprite, white damage tint, Balance `bomb` | B035 | DONE (QA T957) |
| A-WHITE | M7 | art | White only: accents removed from flak (gen_pencil_fx.py), health balloon, rasterizer palette, ART_STYLE | none | DONE (QA T957) |
| B038a | M7 | mechanics | White only in world code: bullets, tracers (shape cue), flak, enemy bomb, health cross, tether | none | DONE (QA T957) |
| B038b | M7 | mechanics | White only in UI: HUD bomb icons, hatched flashing damage bar, ink press inversion on buttons, result title plate | none | DONE (QA T957) |
| B039 | M7 | mechanics | Bullets as 3x1 px line bursts (player solid, enemy dot-gap-dot), colliders unchanged | none | DONE (QA T957) |
| T957 | M7 | qa | Verify the M7 round: FAIL (0 P0, 4 P1, 8 P2); fixed 3 broken tests, added WhiteOnlyAndPressureQaTests | all above | DONE |
| B040 [P1] | M7 | mechanics | T957 BUG 1/2/3/5/6/7: no aircraft cast, reinforcements on the facing side, escaped fighters re-enter while a destroy-all needs them, FighterAI 75 fallback; fighterBullet 8 -> 5 | T957 | DONE (QA T958) |
| V007 [P2] | M7 | mission-design | T957 BUG 9: duplicate health balloons removed in M2/M3 | T957 | DONE (QA T958) |
| T958 | M7 | qa | Re-verify: code fixes PASS; P1 BUG 4 (M4/M5 unsurvivable on paper) open; N1/N2 P2 | B040,V007 | DONE |
| V008 [P1] | M7 | mission-design | M4 +2 balloons (220% vs 168%); M5 +3 balloons and 2 AAA removed (280% vs 235%) | T958 | DONE |
| B041 [P2] | M7 | mechanics | T958 N1: reinforcement cap counts only fighters on screen or closing; N2 FighterAI comment | T958 | DONE |
| B042 [P2] | M7 | mechanics | Deferred cleanup T957 BUG 8/10: remove dead 6x2 bullet placeholder in PlayerPlane; Color.white 1x1 fallbacks -> ink in 8 enemy files + PlayerPlane | T957 | TODO |
| T959 | M7 | qa | Final check B041 + V008: PASS (0 P0, 0 P1, 3 P2 deferred: T957 BUG 8, 10, 12). M4 168% vs 220%, M5 235% vs 280% on paper. 2 mission-data tests added | B041,V008 | DONE |
| B043 | M7 | orchestrator | HUD warning label "SMOKE" -> "DAMAGE" (user request, direct fix) | none | DONE |
| V009 | M7 | mission-design | +3 health balloons in M1-M3 (4/5/6) | none | DONE (QA T960) |
| V010 | M7 | mission-design | +3 balloons M4 (7), +2 M5 (8) | none | DONE (QA T960) |
| T960 | M7 | qa | Verify extra balloons: PASS (0 P0, 0 P1, 1 P2: M5 balloon at 470 off the zeppelin fight). Heal headroom +105..+163 points on all missions | V009,V010 | DONE |
| V011 [P2] | M7 | mission-design | Move M5 balloon 470 -> 405 (zeppelin fight); orchestrator checked JSON + validator | T960 | DONE |
| B044 | M7 | mechanics | HUD minimap (UI_SPEC 3.3.1): whole mission span, player, camera window, live/upcoming enemies, objective diamonds, failAtX line, HQ flag | none | DONE (QA T961) |
| B045 [P2] | M7 | mechanics | HUD canvas: switch CanvasScaler to ScaleWithScreenSize 1920x1080 so the HUD and minimap scale on 4K (from B044) | B044 | TODO |
| T961 | M7 | qa | Verify minimap B044: PASS (0 P0, 0 P1, 2 P2: a wrong test value fixed by QA; per-refresh gameObject.name check makes small garbage). MinimapQaTests (5) added | B044 | DONE |
| B046 [P2] | M7 | mechanics | Minimap: cache a FighterAI.IsReinforcement flag instead of reading gameObject.name each refresh (T961) | T961 | DONE (in B053) |
| B047 [P1] | M7 | mechanics | First real Unity test run (405/418): EditMode-safe teardown (DestroyImmediate), GameManager.GetOrNull, ScrollController.Create sets Instance, EnemyBullet stats without Awake -> 415/418 | none | DONE |
| B048 [P2] | M7 | mechanics | EnemyPressure.cs:117 GameManager.Instance -> GetOrNull; :179 Destroy -> Play-mode-guarded destroy (from B047) | B047 | TODO |
| T962 | M7 | qa | Stale tests made data-driven (M1 37 fighters, M4 balloons), fixtures bind their own ScrollController. Real Unity EditMode run: 418/418 pass | B047 | DONE |
| B049 [P1] | M7 | mechanics | Every mission waits for the first input (ScrollController.Hold/Release, PlayerPlane.IsWaitingForStart, MissionRunner.HasStarted) | none | DONE (tests pending) |
| B050 [P1] | M7 | mechanics | ObjectiveTracker counts reinforcement kills (CountsAsKillOf, RemainingKills) | none | DONE |
| B053 [P1] | M7 | mechanics | HUD objective number from ObjectiveTracker.RemainingKills; minimap diamonds on reinforcements; old HUD start count used spawn entries not counts | B050 | DONE (tests pending) |
| B051 | M7 | mechanics | Damaged enemy fighters trail smoke (0.25 s after 1 hit, 0.12 s from 2), own FX cap 32 | none | DONE (tests pending) |
| B052 | M7 | mechanics | Player 50% tougher: Balance player.resistance 1.5 divides non-fatal hits; crashes stay fatal | none | DONE (tests pending) |
| T963 | M7 | qa | Verify B049-B053: PASS (0 P0, 0 P1, 4 P2); tests updated for counted reinforcements and player resistance; FighterCounterQaTests (8). Real Unity run pending (editor open) | B049-B053 | DONE |
| B054 [P2] | M7 | mechanics | HudView.OnObjectiveCompleted: only the shown objective shows "Objective Complete!" (M4 base-first cleared the balloon counter); stale "reinforcements never count" comments in EnemyPressure/MissionRunner/ScoreKeeper | T963 | DONE (QA T964) |
| T964 | M7 | qa | B054 tests (HudObjectiveQaTests 5); real Unity EditMode run 473/473; one ram test updated for B052 | B054 | DONE |
| B055 [P2] | M7 | mechanics | HUD singular at 1 left ("Destroy 1 more barrage balloon"): HudView.cs:522 ReadableType(type, remaining != 1) + test strings | T964 | TODO |
| V012 | M7 | mission-design | +2 first-half health balloons M1-M3 (6/7/8) | none | DONE |
| V013 | M7 | mission-design | +2 first-half health balloons M4-M5 (9/10) | none | DONE |
| T965 | M7 | orchestrator | Check V012/V013: validate_missions all OK, spawns sorted, real Unity EditMode run 473/473 (incl. MissionDataRulesTests balloon rules) | V012,V013 | DONE |
| B056 | M7 | mechanics | player.speed 5 -> 6.5 (climb/dive, post-turn slide); fallback 6.5 | none | DONE (tests pending) |
| V014 | M7 | mission-design | scrollSpeed x1.3 all missions; M5 length 580; gap/bomber/zeppelin timing re-checked | B056 | DONE |
| B057 [P1] | M7 | mechanics | Bombs always leave along the facing (BombBallistics.InheritedVelocity); cause: post-turn slide runs backward. Real Unity run 482/482 (incl. B056, V014) | none | DONE |
| A-TITLE | M7 | art | title_logo redrawn to read "Biplanes" (was "SIPIAPAE"); new title_planes composite from the user blueprints (player chasing a Fokker) | none | DONE (orchestrator viewed both) |
| B058 | M7 | mechanics | Main menu shows title_planes above the logo (logo y 145, 520x130); fallback text "Biplanes"; Unity 482/482 | none | DONE |
| A-GUN | M7 | art | Realistic gun sounds: player_gun/enemy_gun/hit_enemy rebuilt from noise + falling sine (no metallic ringing): player 9.6/54.7/35.7 -> 66.9/28.8/4.3 low/mid/high %; enemy -6 dBFS; others byte-identical. Agent ran git stash/pop (line endings only); orchestrator re-rendered PNGs (identical), all checks OK | none | DONE |
| B059 | M7 | mechanics | Fighters always face and chase the player (eased turn, standoff, separation, escort leash, Balance fighterChase); Unity 498/498 | none | DONE (QA T966) |
| B060 | M7 | mechanics | Attack slots (max 3 shooters) + per-fighter standoff 3.5-6; pack worst case 5.6%/s (>= 16 s to crash); Unity 514/514 | B059 | DONE (QA T966) |
| B061 [P1] | M7 | mechanics | T966 fixes: no tail-first flight (MinForwardSpeed), avoid-box hysteresis (no overtake clip), escort formation slots, standoffMax 5; Unity 525/530 (5 stale tests) | T966 | DONE (QA T967) |
| B062 | M7 | mechanics | Health balloons restore 100% (Balance healthBalloonRestore 100, HealthBalloon.FallbackRestore); heal budget test replaced; HealthBalloonFullHealTests (4). Real Unity run pending (editor open) | none | DONE (tests pending) |
| T966 | M7 | qa | Verify chase + attack slots: FAIL 1 P1 (overtake clip), 5 P2 | B059,B060 | DONE |
| T967 | M7 | qa | Re-verify after B061: PASS (0 P0, 0 P1, 2 P2); real Unity run 536/536 incl. B062 | B061 | DONE |
| B063 [P2] | M7 | mechanics | T967: escort formation slots kept inside the flight band; DefaultStandoffMax 5; stale comment; Unity 544/544 | T967 | DONE |
