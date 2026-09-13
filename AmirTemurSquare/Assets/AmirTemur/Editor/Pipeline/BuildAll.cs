using System;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Orchestrator entry points (run with -batchmode -executeMethod AmirTemur.Editor.BuildAll.Run, without -nographics):
    ///   Run()                - assets, character, scene, city, landmarks, player, day/night, reflection probes, save, screenshots
    ///   RunScreenshotsOnly() - opens the saved scene and captures the default shots
    ///   BuildPlayer()        - Windows x64 player to D:/Tashkent city/Build/AmirTemurSquare.exe
    /// </summary>
    public static class BuildAll
    {
        public const string ScenePath = "Assets/AmirTemur/Scenes/AmirTemurSquare.unity";
        public const string ScenesFolder = "Assets/AmirTemur/Scenes";
        public const string ScreenshotDir = "D:/Tashkent city/screenshots";
        public const string BuildPath = "D:/Tashkent city/Build/AmirTemurSquare.exe";
        public static readonly Vector3 PlayerSpawn = new Vector3(-20f, 0.1f, -45f);

        [MenuItem("AmirTemur/Build All")]
        public static void Run()
        {
            var total = Stopwatch.StartNew();
            Debug.Log("[BuildAll] ===== start =====");

            Stage("AssetImportSetup", () => AssetImportSetup.Run());
            Stage("CharacterImportSetup", () => CharacterImportSetup.Run());

            Scene scene = default;
            Stage("SceneSetup", () => scene = SceneSetup.CreateScene());

            Transform root = null;
            Stage("CityRoot", () => root = new GameObject("City").transform);
            Stage("CityBuilder", () => CityBuilder.Build(root));
            Stage("Landmarks", () => Landmarks.Build(root));
            Stage("PlayerSetup", () =>
            {
                var player = PlayerSetup.Create(PlayerSpawn);
                Debug.Log($"[BuildAll] player: {(player != null ? player.name : "null")}");
            });
            Stage("DayNightCycle", AttachDayNightCycle);
            Stage("ReflectionProbes", () => { foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) if (rp.mode == UnityEngine.Rendering.ReflectionProbeMode.Baked) Lightmapping.BakeReflectionProbe(rp, $"Assets/AmirTemur/Settings/ReflectionProbe_{rp.name}.exr"); });
            Stage("SaveScene", () =>
            {
                MeshUtil.EnsureFolder(ScenesFolder);
                if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new Exception("SaveScene returned false");
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            });
            Stage("Screenshots", () => ScreenshotTool.Capture(ScreenshotDir, ScreenshotTool.DefaultShots));
            Stage("SaveAssets", () => AssetDatabase.SaveAssets());

            Debug.Log($"[BuildAll] ===== done in {total.Elapsed.TotalMinutes:F1} min =====");
        }

        public static void RunScreenshotsOnly()
        {
            Stage("OpenScene", () =>
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!scene.IsValid()) throw new Exception("scene not found: " + ScenePath);
            });
            Stage("Screenshots", () => ScreenshotTool.Capture(ScreenshotDir, ScreenshotTool.DefaultShots));
        }

        public static void BuildPlayer()
        {
            Stage("PlayerSettings", () =>
            {
                PlayerSettings.productName = "Amir Temur Square";
                PlayerSettings.companyName = "Tashkent City";
                PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                PlayerSettings.defaultScreenWidth = 1920;
                PlayerSettings.defaultScreenHeight = 1080;
                PlayerSettings.runInBackground = true;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.colorSpace = ColorSpace.Linear;
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
                AssetDatabase.SaveAssets();
            });

            Stage("BuildPlayer", () =>
            {
                string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) scenes = new[] { ScenePath };
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(BuildPath));
                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = BuildPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                string msg = $"[BuildAll] build {summary.result}: {summary.totalSize / (1024.0 * 1024.0):F1} MB, {summary.totalErrors} errors, {summary.totalWarnings} warnings, {summary.totalTime.TotalMinutes:F1} min -> {summary.outputPath}";
                if (summary.result == BuildResult.Succeeded) Debug.Log(msg); else Debug.LogError(msg);
            });
        }

        // ------------------------------------------------------------------ helpers

        static void Stage(string name, Action action)
        {
            var sw = Stopwatch.StartNew();
            Debug.Log($"[BuildAll] stage {name} ...");
            try
            {
                action();
                Debug.Log($"[BuildAll] stage {name} done in {sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception e)
            {
                Debug.LogError($"[BuildAll] stage {name} FAILED after {sw.Elapsed.TotalSeconds:F1}s: {e}");
            }
        }

        /// <summary>Adds the runtime AmirTemur.DayNightCycle (owned by the character/runtime module) to an "Environment" object and
        /// points its sun reference at the Sun light. Resolved by name through TypeCache so this module compiles without it.</summary>
        static void AttachDayNightCycle()
        {
            var sun = FindSun();
            var type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().FirstOrDefault(t => t.FullName == "AmirTemur.DayNightCycle");
            if (type == null) { Debug.LogWarning("[BuildAll] AmirTemur.DayNightCycle not found; skipped"); return; }

            var env = GameObject.Find("Environment") ?? new GameObject("Environment");
            Component cycle = Object.FindFirstObjectByType(type) as Component;
            if (cycle != null && cycle.gameObject != env)
            {
                cycle.transform.SetParent(env.transform, true);   // PlayerSetup may already have created one
            }
            if (cycle == null) cycle = env.AddComponent(type);

            var so = new SerializedObject(cycle);
            bool assigned = false;
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                bool nameMatches = it.name.IndexOf("sun", StringComparison.OrdinalIgnoreCase) >= 0 || it.name.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!nameMatches) continue;
                if (sun == null) break;
                if (it.type.Contains("Transform")) it.objectReferenceValue = sun.transform;
                else if (it.type.Contains("HDAdditionalLightData")) it.objectReferenceValue = sun.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                else if (it.type.Contains("GameObject")) it.objectReferenceValue = sun.gameObject;
                else it.objectReferenceValue = sun;
                assigned = true;
                break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(env);
            Debug.Log($"[BuildAll] DayNightCycle on '{env.name}' (sun reference {(assigned ? "assigned" : "not assigned")})");
        }

        static Light FindSun()
        {
            var sunGo = GameObject.Find("Sun");
            if (sunGo != null && sunGo.TryGetComponent<Light>(out var l)) return l;
            return Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(x => x.type == LightType.Directional);
        }
    }
}
