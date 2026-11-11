# Biplanes task board

Owner: orchestrator. Statuses: TODO, IN-PROGRESS, DONE, PARTIAL, BLOCKED.
Bug tasks start with `B`, for example `B007 [P1]`.

## Milestone summaries
_(The orchestrator adds a 5-line summary here at the end of each milestone.)_

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
| T034 | M0 | mechanics | UI: MainMenuView (mission select limited to unlocked_mission) | T012 | TODO |
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
| T047 | M2 | mechanics | BomberAI targetX failAtX integration + escort pattern support | T028,T026 | TODO |
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
| T914 | M2 | art | enemy_bomber from user blueprint (waiting on the user's file) | T909 | BLOCKED |
| T915 | M1 | mechanics | Propeller animation: alternate player_biplane / player_biplane_frame2 (same pivot, from T909) on PlayerPlane | T909 | TODO |
| T916 | M1 | mechanics | Check zeppelin_turret placement on the hull (curved saddle, base at y≈37 of 40) and enemy_base ground line (now y=116 of 120) against spawn alignment | T910,T912 | TODO |
| T917 | M1 | qa | PNG palette spot-check for blueprint-sourced sprites (rasterize --check covers SVGs only); verify the fx sequences animate smoothly in game | T909,T913 | TODO |
| T918 | M1 | art | Faction marks back to ink white (user decision): importer roundel/crosses, aaa_gun, zeppelin, bomber, enemy_base, airfield, player_hq | T909 | DONE |
