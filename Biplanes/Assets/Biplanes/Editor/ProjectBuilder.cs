using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

namespace Biplanes.EditorTools {

    /// <summary>
    /// Editor automation for Biplanes project setup and builds.
    /// Each public static method is also accessible as a MenuItem under "Biplanes/".
    ///
    /// ARCHITECTURE.md §6 — ProjectBuilder contract.
    /// </summary>
    public static class ProjectBuilder {

        // ── Paths ───────────────────────────────────────────────────────

        private const string ProjectRoot       = "Assets/Biplanes/";
        private const string ScenesDir         = "Assets/Biplanes/Scenes/";
        private const string SpritesDir        = "Assets/Biplanes/Resources/Sprites/";
        private const string BuildsDir         = "Biplanes/Builds/";

        // ── Scene creation helpers ──────────────────────────────────────

        /// <summary>Creates a minimal scene with camera, managers, canvas and EventSystem.
        /// Saves it to *relativePath* (relative to Assets/) and returns the path.</summary>
        private static string CreateScene(string sceneName, bool addManagers) {
            // Ensure Scenes directory exists using AssetDatabase (required for Unity assets).
            const string scenesRel = "Assets/Biplanes/Scenes";
            if (!AssetDatabase.IsValidFolder(scenesRel)) {
                // First ensure the parent Biplanes folder exists.
                if (!AssetDatabase.IsValidFolder("Assets/Biplanes"))
                    AssetDatabase.CreateFolder("Assets", "Biplanes");
                AssetDatabase.CreateFolder(scenesRel.Replace("/Scenes",""), "Scenes");
            }

            // Build the save path — Unity 6 uses EditorSceneManager.SaveScene for .unity.
            string scenePath = $"{scenesRel}/{sceneName}.unity";

            // If it already exists, load it instead of recreating.
            string fullPath = Application.dataPath + "/" + scenePath;
            if (System.IO.File.Exists(fullPath)) {
                Debug.LogWarning($"ProjectBuilder: scene '{sceneName}' already exists at {scenePath}, loading.");
                Scene existing = EditorSceneManager.OpenScene(scenePath);
                // Still register it in build settings below.
            } else {
                var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                EditorSceneManager.SaveScene(newScene, scenePath);
            }

            // Open the scene so we can add GameObjects to it.
            var scene = EditorSceneManager.OpenScene(scenePath);
            var root  = scene.GetRootGameObjects().FirstOrDefault();
            if (root != null) DestroyObjectImmediate(root);

            ClearSceneRoots(scene);

            // ── Main Camera ───────────────────────────────────────
            var mainCamGO = new GameObject("MainCamera");
            mainCamGO.tag = "MainCamera"; // Required for Camera.main (ScrollController).
            // Sit behind the z=0 sprite plane so the near clip plane doesn't cull it.
            mainCamGO.transform.position = new Vector3(0f, 0f, -10f);
            var cam = mainCamGO.AddComponent<Camera>();
            cam.orthographic             = true;
            cam.orthographicSize         = 5f;
            cam.clearFlags               = CameraClearFlags.SolidColor;
            cam.backgroundColor          = new Color32(0x0F, 0x2A, 0x4A, 0xFF); // ART_STYLE paper #0F2A4A
            cam.nearClipPlane           = 0.1f;
            cam.farClipPlane            = 100f;
            mainCamGO.layer              = LayerMask.NameToLayer("Default");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(mainCamGO);

            

            if (addManagers) {
                // GameManager singleton placeholder — the actual script will be added by MissionRunner builds.
                var gmGO = new GameObject("GameManager");

                // SaveSystem is static — no GameObject needed.

                // ── UI Canvas ─────────────────────────────────────
                var canvasGO = new GameObject("UI");
                canvasGO.AddComponent<Canvas>();
                var canvas = canvasGO.GetComponent<Canvas>();
                canvas.renderMode           = RenderMode.ScreenSpaceOverlay;
               // canvas.screenMatchMode     = ScreenMatchMode.MatchWidthOrHeight;
                canvas.pixelPerfect         = false;

                var scaler = canvasGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution  = new Vector2(1280f, 720f);
                scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight   = 0.5f;

                var graphicRaycaster = canvasGO.AddComponent<GraphicRaycaster>();

                // EventSystem for uGUI input.
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<EventSystem>();
                esGO.AddComponent<InputSystemUIInputModule>(); // Input System only; StandaloneInputModule throws

                // Parent canvas under EventSystem hierarchy is not required;
                // keep it at root level.  However, we parent the UI to GameManager
                // for convenience during editing.
                canvasGO.transform.SetParent(gmGO.transform, false);
            }

            EditorSceneManager.SaveScene(scene, scenePath);
            return scenePath;

        }

        private static void ClearSceneRoots(Scene scene) {
            var roots = scene.GetRootGameObjects().ToArray();
            foreach (var go in roots) {
                if (!Application.isPlaying) {
                    // Can't use DestroyImmediate during play mode.
                    GameObject.DestroyImmediate(go);
                } else {
                    GameObject.Destroy(go);
                }
            }
        }

        private static void DestroyObjectImmediate(Object obj) {
#if UNITY_EDITOR
            if (Application.isPlaying) {
                Object.Destroy(obj);
            } else {
                Object.DestroyImmediate(obj);
            }
#else
            Object.Destroy(obj);
#endif
        }


        // GetOpenedScenes -TODO- helper to get all openScenes


        // ── MenuItem: BuildScenes ───────────────────────────────────────

        /// <summary>Creates MainMenu.unity and Game.unity, adds them to Build Settings.</summary>
        [MenuItem("Biplanes/BuildScenes")]
        public static void BuildScenes() {
            Debug.Log("ProjectBuilder: creating scenes...");

            string mainMenuPath = CreateScene("MainMenu", false);
            string gamePath     = BuildGameScene();

            // Register in build settings.
            //var scenes = UnityEditor.SceneManagement.EditorSceneManager.GetOpenedScenes().ToList();
            var scenes = SceneManager.GetAllScenes();


            // BuildSettings uses relative paths from Assets/.
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            bool hasMainMenu = current.Any(s => s.path == mainMenuPath);
            bool hasGame     = current.Any(s => s.path == gamePath);

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(current);
            if (!hasMainMenu) list.Add(new EditorBuildSettingsScene(mainMenuPath, true));
            if (!hasGame)     list.Add(new EditorBuildSettingsScene(gamePath, true));

            // Scene order: Game (0), MainMenu (1).
            // Game boots first because MainMenu has no MainMenuView yet (T034) and would
            // open on an empty screen. GameManager.Start() auto-starts mission 1 when no
            // BriefingView exists. Swap back to MainMenu first once T034 really lands.
            var ordered = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            foreach (var s in list) {
                if (s.path == mainMenuPath || s.path == gamePath) continue; // re-add after
                ordered.Add(s);
            }
            ordered.Insert(0, new EditorBuildSettingsScene(gamePath, true));
            ordered.Insert(1, new EditorBuildSettingsScene(mainMenuPath, true));

            EditorBuildSettings.scenes = ordered.ToArray();

            Debug.Log($"ProjectBuilder: scenes built — MainMenu at {mainMenuPath}, Game at {gamePath}");
        }

        // ── MenuItem: ConfigureProject ──────────────────────────────────

        /// <summary>Sets sprite import settings under Resources/Sprites, input handling, and company/product name.</summary>
        [MenuItem("Biplanes/ConfigureProject")]
        public static void ConfigureProject() {
            Debug.Log("ProjectBuilder: configuring project...");

            // ── Sprite importer defaults for every subdirectory ───────
            var spriteFolders = AssetDatabase.FindAssets("t:folder", new[] { "Assets/Biplanes/Resources/Sprites" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Directory.Exists(Application.dataPath + "/" + p));

            foreach (var folderPath in spriteFolders.Concat(new[] { "Assets/Biplanes/Resources/Sprites" })) {
                string fullPath = Application.dataPath + "/" + folderPath.Replace("Assets/", "");
                if (!Directory.Exists(fullPath)) continue;

                var files = Directory.GetFiles(fullPath, "*.png", SearchOption.AllDirectories)
                    .Select(f => f.Replace(Application.dataPath, "Assets"))
                    .ToArray();

                foreach (var assetPath in files) {
                    var importer = AssetImporter.GetAtPath(assetPath);
                    if (importer == null) continue;

#if UNITY_EDITOR
                    if (importer is TextureImporter texImporter) {
                        texImporter.spriteImportMode = SpriteImportMode.Single;
                        texImporter.spritePixelsPerUnit = 100;
                        texImporter.filterMode        = FilterMode.Bilinear;
                        texImporter.textureCompression = TextureImporterCompression.Uncompressed;
                        texImporter.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                        texImporter.npotScale           = TextureImporterNPOTScale.None;
                        texImporter.alphaIsTransparency = true;
                        texImporter.maxTextureSize      = 2048;
                        texImporter.SaveAndReimport();
                    }
#endif
                }
            }

            // ── Active Input Handling (Input System package) ───────────
#if UNITY_EDITOR
            var assembly = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "UnityEditor.InputSystem");
            if (assembly != null) {
                var type = assembly.GetType("UnityEditor.InputSystem.Editor.InputSettings");
                if (type != null) {
                    var prop = type.GetProperty("activeInputMode");
                    if (prop != null) {
                        prop.SetValue(null, 1); // InputActionMap-based (Input System)
                        Debug.Log("ProjectBuilder: active input handling set to Input System.");
                    }
                }
            }
#endif

            // ── Company / Product name ────────────────────────────────
            UnityEditor.EditorUserBuildSettings.activeBuildTargetChanged += null; // ensure target aware
            PlayerSettings.companyName = "Biplanes Studio";
            PlayerSettings.productName = "Biplanes";
            PlayerSettings.bundleVersion = "0.1.0";

            Debug.Log("ProjectBuilder: project configured — company, product, sprite defaults applied.");
        }

        // ── MenuItem: BuildWebGL ────────────────────────────────────────

        /// <summary>Builds the project for WebGL to Biplanes/Builds/WebGL/.</summary>
        [MenuItem("Biplanes/BuildWebGL")]
        public static void BuildWebGL() {
            Debug.Log("ProjectBuilder: starting WebGL build...");
            var targetDir = GetBuildPath("WebGL");
            CreateBuildDirectory(targetDir);

            PlayerSettings.WebGL.compressionFormat     = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.dataCaching            = true;
            PlayerSettings.WebGL.memorySize             = 256;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;

            var result = UnityEditor.BuildPipeline.BuildPlayer(
                GetBuildScenes(),
                targetDir,
                UnityEditor.BuildTarget.WebGL,
                UnityEditor.BuildOptions.None);

            if (result.summary.totalErrors > 0 || result.summary.totalWarnings > 0) {
                Debug.LogError($"ProjectBuilder: WebGL build reported issues — {result.summary.totalErrors} errors, {result.summary.totalWarnings} warnings.");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
            } else {
                Debug.Log("ProjectBuilder: WebGL build succeeded.");
            }
        }

        // ── MenuItem: BuildWindows ──────────────────────────────────────

        /// <summary>Builds the project for Windows (standalone 64-bit) to Biplanes/Builds/Windows/.</summary>
        [MenuItem("Biplanes/BuildWindows")]
        public static void BuildWindows() {
            Debug.Log("ProjectBuilder: starting Windows build...");
            var targetDir = GetBuildPath("Windows");
            CreateBuildDirectory(targetDir);

            // x86_64 is the default for standalone Windows.

            var result = UnityEditor.BuildPipeline.BuildPlayer(
                GetBuildScenes(),
                targetDir,
                UnityEditor.BuildTarget.StandaloneWindows64,
                UnityEditor.BuildOptions.None);

            if (result.summary.totalErrors > 0 || result.summary.totalWarnings > 0)
            {
                Debug.LogError($"ProjectBuilder: Windows build reported issues — {result.summary.totalErrors} errors, {result.summary.totalWarnings} warnings.");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
            } else {
                Debug.Log("ProjectBuilder: Windows build succeeded.");
            }
        }

        // ── MenuItem: BuildMac ──────────────────────────────────────────

        /// <summary>Builds the project for macOS (standalone 64-bit) to Biplanes/Builds/Mac/.</summary>
        [MenuItem("Biplanes/BuildMac")]
        public static void BuildMac() {
            Debug.Log("ProjectBuilder: starting Mac build...");
            var targetDir = GetBuildPath("Mac");
            CreateBuildDirectory(targetDir);

            ///PlayerSettings.macOSTargetArchitecture = UnityEditor.MacOSTargetArchitecture.x86_64 | UnityEditor.MacOSTargetArchitecture.ARM64;
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
            var result = UnityEditor.BuildPipeline.BuildPlayer(
                GetBuildScenes(),
                targetDir,
                UnityEditor.BuildTarget.StandaloneOSX,
                UnityEditor.BuildOptions.None);

            if (result.summary.totalErrors > 0 || result.summary.totalWarnings > 0)
            {
                Debug.LogError($"ProjectBuilder: Mac build reported issues — {result.summary.totalErrors} errors, {result.summary.totalWarnings} warnings.");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
            } else {
                Debug.Log("ProjectBuilder: Mac build succeeded.");
            }
        }

        // ── MenuItem: BuildAll ──────────────────────────────────────────

        /// <summary>Runs ConfigureProject → BuildScenes → all three builds in sequence.</summary>
        [MenuItem("Biplanes/BuildAll")]
        public static void BuildAll() {
            Debug.Log("ProjectBuilder: running full build pipeline...");
            bool failed = false;

            try   { ConfigureProject(); } catch (System.Exception ex)  { Debug.LogError($"ProjectBuilder: ConfigureProject failed — {ex.Message}"); failed = true; }
            if (!failed) try { BuildScenes(); } catch (System.Exception ex) { Debug.LogError($"ProjectBuilder: BuildScenes failed — {ex.Message}"); failed = true; }

            if (failed) {
                Debug.LogError("ProjectBuilder: pipeline aborted due to earlier errors.");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
                return;
            }

            try   { BuildWebGL();  } catch (System.Exception ex)    { Debug.LogError($"ProjectBuilder: BuildWebGL failed — {ex.Message}"); failed = true; }
            try   { BuildWindows(); } catch (System.Exception ex)   { Debug.LogError($"ProjectBuilder: BuildWindows failed — {ex.Message}"); failed = true; }
            try   { BuildMac();     } catch (System.Exception ex)   { Debug.LogError($"ProjectBuilder: BuildMac failed — {ex.Message}"); failed = true; }

            if (failed) {
                Debug.LogError("ProjectBuilder: one or more builds failed.");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(1);
            } else {
                Debug.Log("ProjectBuilder: all builds completed successfully.");
            }
        }

        // ── Game scene builder (M0 test scene) ────────────────────────

        /// <summary>Creates a fully wired Game scene with managers, player, enemy, and HUD for M0 verification.</summary>
        private static string BuildGameScene() {
            const string scenesRel = "Assets/Biplanes/Scenes";
            if (!AssetDatabase.IsValidFolder(scenesRel)) {
                if (!AssetDatabase.IsValidFolder("Assets/Biplanes"))
                    AssetDatabase.CreateFolder("Assets", "Biplanes");
                AssetDatabase.CreateFolder(scenesRel.Replace("/Scenes",""), "Scenes");
            }

            string scenePath = $"{scenesRel}/Game.unity";
            var existingScene = EditorSceneManager.OpenScene(scenePath);
            ClearSceneRoots(existingScene);

            // ── Camera ────────────────────────────────────────────────
            var camGO = new GameObject("MainCamera");
            camGO.tag = "MainCamera"; // Required for Camera.main (ScrollController).
            // Sit behind the z=0 sprite plane so the near clip plane doesn't cull it.
            camGO.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic             = true;
            cam.orthographicSize         = 5f;
            cam.clearFlags               = CameraClearFlags.SolidColor;
            cam.backgroundColor          = new Color32(0x0F, 0x2A, 0x4A, 0xFF); // ART_STYLE paper #0F2A4A
            cam.nearClipPlane           = 0.1f;
            cam.farClipPlane            = 100f;


            // ── Managers (with scripts attached) ──────────────────────
            var gmGO = new GameObject("GameManager");
            gmGO.AddComponent<Biplanes.Core.GameManager>();


            var skGO = new GameObject("ScoreKeeper");
            skGO.AddComponent<Biplanes.Core.ScoreKeeper>();

            // ── MissionRunner (core game orchestrator) ───────
            var mrGO = new GameObject("MissionRunner");
            mrGO.AddComponent<Biplanes.Missions.MissionRunner>();


            // ── Player biplane ────────────────────────────────────────
            // Not placed in the scene: MissionRunner.StartMission creates it with
            // PlayerPlane.Create at the mission's playerStart. A scene copy would be a
            // second, input-less plane.

            // ── UI Canvas with blank HUD panel ───────────────────────
            var canvasGO = new GameObject("UI");
            canvasGO.transform.SetParent(gmGO.transform, false);
            var cvs = canvasGO.AddComponent<Canvas>();
            cvs.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            var raycaster = canvasGO.AddComponent<GraphicRaycaster>();

            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>(); // Input System only; StandaloneInputModule throws

            EditorSceneManager.SaveScene(existingScene, scenePath);
            return scenePath;
        }

        private static Sprite CreatePlaceholderSprite(Color color) {
            Texture2D tex = new Texture2D(64, 64);
            for (int x = 0; x < 64; x++)
                for (int y = 0; y < 64; y++) {
                    // Circle mask.
                    float dx = x - 32f;
                    float dy = y - 32f;
                    tex.SetPixel(x, y, (dx*dx + dy*dy <= 32f*32f) ? color : Color.clear);
                }
            tex.Apply();
            Sprite s = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
            Object.DontDestroyOnLoad(tex); // prevent GC; scene will own it.
            return s;
        }

        // ── Internal helpers ────────────────────────────────────────────

        private static string GetBuildPath(string target) {
            return System.IO.Path.GetFullPath($"{Application.dataPath}/../{BuildsDir}{target}/");
        }

        private static void CreateBuildDirectory(string path) {
            if (!System.IO.Directory.Exists(path)) {
                System.IO.Directory.CreateDirectory(path);
                Debug.Log($"ProjectBuilder: created build directory {path}");
            }
        }

        private static string[] GetBuildScenes() {
            return EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
        }
    }

}
