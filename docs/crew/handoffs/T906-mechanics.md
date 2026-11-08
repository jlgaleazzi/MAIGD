TASK: T906
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Missions/MissionRunner.cs
  - Biplanes/Assets/Biplanes/Scripts/Core/GameManager.cs
  - Biplanes/Assets/Biplanes/Editor/ProjectBuilder.cs
  - Biplanes/Assets/Biplanes/Editor/SpriteImportSettings.cs (new)
  - Biplanes/Assets/Biplanes/Editor/Biplanes.Editor.asmdef
  - Biplanes/Assets/Biplanes/Biplanes.asmdef
  - Biplanes/Assets/Biplanes/Scripts/Player/PlayerInput.cs
  - Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
  - Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
  - Biplanes/Assets/Biplanes/Scripts/UI/BriefingView.cs
  - Biplanes/Assets/Biplanes/Scripts/UI/HudView.cs
  - Biplanes/Assets/Biplanes/Scripts/UI/PauseView.cs
  - Biplanes/Assets/Biplanes/Scripts/UI/ResultView.cs
SUMMARY: Part 1, launch crash: GameManager.StartMission and MissionRunner.StartMission called each
         other forever (stack overflow, introduced in T905). MissionRunner no longer calls back.
         Both cameras ProjectBuilder creates are now tagged "MainCamera" at z=-10. Game is build scene 0.
         Part 2, controls: PlayerInput was never attached, used legacy UnityEngine.Input (the project
         is Input System only), and its autofire timer counted down so it never fired. It now uses
         Keyboard.current and PlayerPlane.Create attaches it. PlayerPlane now wires OnFire to Bullet.Spawn
         and OnDropBomb to BombBay, initialises the Bullet/Bomb pools (nothing called InitPool), adds a
         trigger BoxCollider2D, adds TeamTag before Health (the plane was Neutral, so its own bullets could
         hit it), and moves along with the scrolling camera. Bomb.Spawn now uses the parent velocity as
         given, not multiplied by speed. EventSystem uses InputSystemUIInputModule (StandaloneInputModule
         threw every frame). The extra input-less PlayerPlane in Game.unity is removed.
         Part 3, sprites: every PNG imported as "Multiple", so Resources.Load<Sprite> returned null and the
         player showed as a white box. SpriteImportSettings (AssetPostprocessor) forces Single/PPU 100 on
         Resources/Sprites. The UI views used non-existent paths (Sprites/UI/damage_frame etc.); they now
         use Sprites/ui/ui_*.
VERIFY: Static: runtime and editor assemblies compiled with dotnet against Library/ScriptAssemblies
        (0 errors). Unity itself not run here: the editor had the project open.
        1) Focus the Unity editor so it recompiles and reimports textures (postprocessor v1).
        2) Biplanes > BuildScenes, then Biplanes > BuildWindows, run Biplanes/Builds/Windows/Biplanes.exe
        3) Expect: biplane sprite on the runway; W or D takes off; WASD moves; Space autofires; Enter drops
           a bomb (HUD bomb count drops). Player.log has no InvalidOperationException and no
           "missing TeamTag on 'PlayerPlane'" warning.
REQUESTS: orchestrator: add T906 to TASKS.md and note in ARCHITECTURE.md that MissionRunner must never
          call GameManager.StartMission (one-way: GameManager -> MissionRunner).
          orchestrator: re-open T021 (PlayerInput) and T034 (MainMenuView). T021 was marked DONE but
          was never attached and used legacy input. MainMenuView.cs does not exist. Workaround in T906:
          BuildScenes puts Game at build index 0 and MainMenu at 1; restore MainMenu first when T034 lands.
          orchestrator: ARCHITECTURE §3 requires TerrainStrip (ground at y=-4.5, terrain crash), but it
          does not exist and no task covers it. Nothing renders the 12 bg_* backgrounds; add a background
          class to ARCHITECTURE and a task for it.
          mechanics: GameManager.RetryMission and ResultView load "MainScene", but that scene does not
          exist (it is named "Game"). BomberAI drops bombs via the player's Bomb pool, so its bombs are
          Team.Player. PauseView has no Esc key handling.
