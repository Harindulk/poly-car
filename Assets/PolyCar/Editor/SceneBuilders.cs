using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PolyCar.Editor
{
    /// <summary>Scene conversion/creation and the Windows build. All idempotent.</summary>
    public static class SceneBuilders
    {
        public const string GreenForestPath = "Assets/_Scenes/Green Forest.unity";
        public const string MenuPath = "Assets/_Scenes/Menu.unity";
        private const string SkyboxGuid = "0390510663f4d494ab4f3b949c7a01b8"; // Green Forest's sky

        // ------------------------------------------------------------ Green Forest

        [MenuItem("Poly Car/Setup/Convert Green Forest")]
        public static void ConvertGreenForest()
        {
            var scene = EditorSceneManager.OpenScene(GreenForestPath, OpenSceneMode.Single);
            GameObject Find(string n) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == n);

            // Where the 2022 game started and finished.
            var oldCar = Find("Car");
            var end = Find("end");
            Vector3 startPos = oldCar ? oldCar.transform.position : new Vector3(272.55f, 22.46f, -222.73f);
            var gm = end ? end.transform.Find("GameManager") : null;
            Vector3 finishPos = gm ? gm.position : end ? end.transform.position : Vector3.zero;

            // Remove the old game layer: HUD canvas, PlayFab, old car, stopwatch trigger, broken prefab, stray truck.
            foreach (var name in new[] { "Canvas", "EventSystem", "PlayFab", "Car", "Cube", "News_Truck" })
            {
                var go = Find(name);
                if (go) Object.DestroyImmediate(go);
            }
            foreach (var go in scene.GetRootGameObjects().Where(g => g.name.StartsWith("News_Truck_Blue")).ToArray()) Object.DestroyImmediate(go);
            if (gm) Object.DestroyImmediate(gm.gameObject);
            var cam = Find("Main Camera");
            if (cam)
            {
                foreach (var mb in cam.GetComponents<MonoBehaviour>()) if (mb == null || mb.GetType().Name == "CameraController") Object.DestroyImmediate(mb);
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data) { data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; }
            }

            // Race layer.
            var race = Find("Race") ?? new GameObject("Race");
            var path = race.GetComponent<TrackPath>() ?? race.AddComponent<TrackPath>();
            var road = Object.FindAnyObjectByType<RoadArchitect.Road>();
            var pts = SampleRoad(road, 4f);
            if (pts.Count < 2) { Debug.LogError("Poly Car: no road spline found in Green Forest."); return; }
            // Orient start -> finish and trim to the old start / finish.
            if (Vector3.Distance(pts[0], startPos) > Vector3.Distance(pts[pts.Count - 1], startPos)) pts.Reverse();
            int si = Nearest(pts, startPos), fi = Nearest(pts, finishPos);
            si = Mathf.Max(0, si - 4); // a little run-up behind the old spawn for the grid
            if (fi <= si + 10) fi = pts.Count - 1;
            path.points = pts.GetRange(si, fi - si + 1);
            path.loop = false;
            path.halfWidth = road ? road.RoadWidth() * 0.5f + (road.isShouldersEnabled ? road.shoulderWidth * 0.5f : 0f) : 6f;
            path.widths.Clear();
            path.Build();
            var rm = race.GetComponent<RaceManager>() ?? race.AddComponent<RaceManager>();
            rm.Path = path;
            rm.fallbackMapId = "green_forest";
            rm.checkpointSpacing = 120f;
            rm.gridStartDistance = 6f;

            // Finish arch stays as scenery; make sure no trigger or collider blocks the road under it.
            if (end) foreach (var c in end.GetComponentsInChildren<Collider>()) if (c.isTrigger) Object.DestroyImmediate(c);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Poly Car: Green Forest converted. Track length {path.Length:0} m, {path.points.Count} points.");
        }

        private static int Nearest(List<Vector3> pts, Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pts.Count; i++) { float d = (pts[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Catmull-Rom through the RoadArchitect spline nodes, resampled every <paramref name="step"/> metres.</summary>
        public static List<Vector3> SampleRoad(RoadArchitect.Road road, float step)
        {
            var result = new List<Vector3>();
            if (road == null || road.spline == null) return result;
            var nodes = road.spline.nodes.Where(n => n != null).Select(n => n.pos).ToList();
            if (nodes.Count < 2) return result;
            var dense = new List<Vector3>();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                Vector3 p0 = nodes[Mathf.Max(0, i - 1)], p1 = nodes[i], p2 = nodes[i + 1], p3 = nodes[Mathf.Min(nodes.Count - 1, i + 2)];
                int sub = Mathf.Max(4, Mathf.CeilToInt(Vector3.Distance(p1, p2) / 1f));
                for (int k = 0; k < sub; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / (float)sub));
            }
            dense.Add(nodes[nodes.Count - 1]);
            // Resample at even spacing.
            result.Add(dense[0]);
            float acc = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                acc += Vector3.Distance(dense[i - 1], dense[i]);
                if (acc >= step) { result.Add(dense[i]); acc = 0f; }
            }
            if (result[result.Count - 1] != dense[dense.Count - 1]) result.Add(dense[dense.Count - 1]);
            return result;
        }

        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // ------------------------------------------------------------ Menu

        [MenuItem("Poly Car/Setup/Build Menu Scene")]
        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sky = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(SkyboxGuid));
            if (sky) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.76f, 0.86f);
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 140f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, -140f, 0f);
            RenderSettings.sun = sun;


            // Showroom: ground, a turntable plinth with an accent ring, trees and track props behind.
            var groundMat = MakeLit("Assets/PolyCar/Materials/MenuGround.mat", new Color(0.33f, 0.52f, 0.28f), 0.1f);
            var plinthMat = MakeLit("Assets/PolyCar/Materials/MenuPlinth.mat", new Color(0.14f, 0.15f, 0.18f), 0.55f);
            var accentMat = MakeLit("Assets/PolyCar/Materials/MenuAccent.mat", new Color(1f, 0.42f, 0.1f), 0.4f, new Color(1f, 0.35f, 0.05f) * 1.6f);
            var asphaltMat = MakeLit("Assets/PolyCar/Materials/MenuAsphalt.mat", new Color(0.2f, 0.21f, 0.24f), 0.2f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = "Road";
            road.transform.position = new Vector3(0f, 0.01f, 6f);
            road.transform.localScale = new Vector3(9f, 0.02f, 200f);
            road.transform.rotation = Quaternion.Euler(0f, 62f, 0f);
            road.GetComponent<Renderer>().sharedMaterial = asphaltMat;

            var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plinth.name = "Plinth";
            plinth.transform.position = new Vector3(0f, 0.12f, 0f);
            plinth.transform.localScale = new Vector3(7.2f, 0.12f, 7.2f);
            plinth.GetComponent<Renderer>().sharedMaterial = plinthMat;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Ring";
            ring.transform.position = new Vector3(0f, 0.06f, 0f);
            ring.transform.localScale = new Vector3(7.7f, 0.06f, 7.7f);
            ring.GetComponent<Renderer>().sharedMaterial = accentMat;
            foreach (var g in new[] { ground, road, plinth, ring }) Object.DestroyImmediate(g.GetComponent<Collider>());

            var turntable = new GameObject("Turntable").transform;
            turntable.position = new Vector3(0f, 0.245f, 0f);
            turntable.rotation = Quaternion.Euler(0f, -35f, 0f);

            Scatter("Assets/Prefabs/LowPoly - FirTree A.prefab", 30, 24f, 70f, 0.8f, 1.4f, 11);
            Scatter("Assets/Prefabs/LowPoly - FirTree B.prefab", 26, 26f, 75f, 0.7f, 1.3f, 23);
            Scatter("Assets/Prefabs/LowPoly - Rock A.prefab", 8, 14f, 40f, 0.5f, 1.0f, 37);
            PlaceProp("Assets/Prefabs/TrafficConeStatic.prefab", new Vector3(4.6f, 0f, 2.2f), 20f);
            PlaceProp("Assets/Prefabs/TrafficConeStatic.prefab", new Vector3(5.4f, 0f, 0.6f), 70f);
            PlaceProp("Assets/Prefabs/TrafficConeStatic.prefab", new Vector3(-4.8f, 0f, 3.6f), 10f);
            PlaceProp("Assets/Prefabs/BarrierKRail.prefab", new Vector3(-6.5f, 0f, 6.5f), 40f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            camGo.transform.position = new Vector3(0.6f, 3.1f, -15.5f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(-2.9f, 0.2f, 0f) - camGo.transform.position);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var volGo = new GameObject("Post FX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = MenuProfile();

            var menu = new GameObject("Menu").AddComponent<MainMenu>();
            menu.turntable = turntable;

            EditorSceneManager.SaveScene(scene, MenuPath);
            Debug.Log("Poly Car: menu scene built.");
        }

        private static Material MakeLit(string path, Color c, float smooth, Color? emission = null)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void Scatter(string prefabPath, int count, float minR, float maxR, float minS, float maxS, int seed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab) return;
            var rnd = new System.Random(seed);
            var parent = GameObject.Find("Scenery") ?? new GameObject("Scenery");
            for (int i = 0; i < count; i++)
            {
                // Keep the camera's view of the car clear: only place in the back half-ring.
                float a = Mathf.Lerp(-30f, 210f, (float)rnd.NextDouble()) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(minR, maxR, (float)rnd.NextDouble());
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r + 4f);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                go.transform.localScale = Vector3.one * Mathf.Lerp(minS, maxS, (float)rnd.NextDouble());
                go.isStatic = true;
            }
        }

        private static void PlaceProp(string prefabPath, Vector3 pos, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab) return;
            var parent = GameObject.Find("Scenery") ?? new GameObject("Scenery");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
        }

        private static VolumeProfile MenuProfile()
        {
            const string path = "Assets/PolyCar/Materials/MenuPostFX.asset";
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p != null) return p;
            p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.6f); bloom.threshold.Override(1f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.3f); vig.smoothness.Override(0.45f);
            var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(0.15f); ca.contrast.Override(12f); ca.saturation.Override(12f);
            var dof = p.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Gaussian); dof.gaussianStart.Override(18f); dof.gaussianEnd.Override(60f);
            foreach (var c in p.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, p); }
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        // ------------------------------------------------------------ Build

        [MenuItem("Poly Car/Build/macOS (test)")]
        public static void BuildMac()
        {
            ConfigurePlayer();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string outPath = Path.GetFullPath("Builds/Mac/PolyCar.app");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = outPath, target = BuildTarget.StandaloneOSX, targetGroup = BuildTargetGroup.Standalone });
            File.WriteAllText("Builds/mac-build.txt", $"{report.summary.result} {report.summary.totalSize / (1024 * 1024)} MB");
        }

        [MenuItem("Poly Car/Build/Windows (64-bit)")]
        public static void BuildWindows()
        {
            ConfigurePlayer();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string outDir = Path.GetFullPath("Builds/Windows");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true); // no stale files from older builds
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "PolyCar.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            string msg = $"Poly Car Windows build: {s.result}, {s.totalErrors} errors, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:0}s -> {opts.locationPathName}";
            File.WriteAllText("Builds/windows-build.txt", msg);
            Debug.Log(msg);
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        public static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Poly Car";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.harindulkgames.polycar");
            PlayerSettings.bundleVersion = "2.0.0";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Disabled);
            // SRP Batcher handles the draw calls; static batching only duplicated every tree into huge combined meshes.
            PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.StandaloneOSX, false);
            PolyCarSetup.ConfigureAudioImport();
        }
    }
}
