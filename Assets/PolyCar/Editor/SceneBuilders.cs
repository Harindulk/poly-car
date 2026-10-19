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

            ConformTerrainToRoad(road);
            FixStaticConvexColliders(scene);
            int stripped = StripMissingScripts(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Poly Car: Green Forest converted. Track length {path.Length:0} m, {path.points.Count} points, {stripped} missing scripts removed.");
        }

        /// <summary>Static scenery doesn't need convex colliders (the cliff exceeded the 255-triangle convex limit).</summary>
        public static int FixStaticConvexColliders(UnityEngine.SceneManagement.Scene scene)
        {
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                    if (mc.convex && !mc.isTrigger && mc.attachedRigidbody == null && mc.sharedMesh != null && mc.sharedMesh.triangles.Length / 3 > 255) { mc.convex = false; n++; }
            return n;
        }

        /// <summary>
        /// Roadside dressing (marker poles, crowds, hay bales) and the stage grade for Green Forest, the same pass the
        /// generated stages get. Re-runnable: replaces the previous dressing.
        /// </summary>
        [MenuItem("Poly Car/Setup/Dress Green Forest")]
        public static void DressGreenForest()
        {
            var scene = EditorSceneManager.OpenScene(GreenForestPath, OpenSceneMode.Single);
            var path = Object.FindAnyObjectByType<TrackPath>();
            var old = GameObject.Find("StageDressing");
            if (old) Object.DestroyImmediate(old);
            var root = new GameObject("StageDressing");
            int cleared = StageDressing.ClearTerrainTreesNearRoad(path, 2.5f);
            int thinned = StageDressing.ThinTerrainDetail(path, 110f, 500f);
            Physics.SyncTransforms();
            StageDressing.Dress(path, root.transform, 0.6f, 6, 8, 5, "Assets/PolyCar/Maps/Green Forest_Meshes");
            const string prof = "Assets/PolyCar/Maps/Materials/forest_PostFX.asset";
            AssetDatabase.DeleteAsset(prof);
            var profile = MapBuilder.StageProfile(prof, 10f);
            var volumes = Object.FindObjectsByType<Volume>();
            var global = System.Array.Find(volumes, v => v.isGlobal);
            if (global == null) { global = new GameObject("Post FX").AddComponent<Volume>(); global.isGlobal = true; }
            global.sharedProfile = profile;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"Poly Car: Green Forest dressed ({cleared} overhanging trees cleared, {thinned} far grass/flower instances dropped).");
        }

        [MenuItem("Poly Car/Setup/Fix Green Forest Colliders")]
        public static void FixGreenForestColliders()
        {
            var scene = EditorSceneManager.OpenScene(GreenForestPath, OpenSceneMode.Single);
            int n = FixStaticConvexColliders(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Poly Car: {n} static convex colliders made non-convex.");
        }

        /// <summary>Removes components whose script no longer exists (the 2022 ReSpawner triggers, the old camera script).</summary>
        public static int StripMissingScripts(UnityEngine.SceneManagement.Scene scene)
        {
            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            return removed;
        }

        [MenuItem("Poly Car/Setup/Strip Missing Scripts In Build Scenes")]
        public static void StripMissingScriptsInBuildScenes()
        {
            foreach (var s in EditorBuildSettings.scenes)
            {
                var scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
                int n = StripMissingScripts(scene);
                if (n > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
                Debug.Log($"Poly Car: {s.path}: removed {n} missing script component(s).");
            }
        }

        /// <summary>
        /// Conforms every terrain tile to the road (what EasyRoads3D does for art of rally): heightmap samples
        /// within the road half-width + 1 m go to just under the road surface, then blend back to the
        /// original ground over 12 m. Trees are re-seated on the new surface. Removes the raised-road lip.
        /// </summary>
        [MenuItem("Poly Car/Setup/Conform Green Forest Terrain To Road")]
        public static void ConformGreenForestTerrainMenu()
        {
            var scene = EditorSceneManager.OpenScene(GreenForestPath, OpenSceneMode.Single);
            int changed = ConformTerrainToRoad(Object.FindAnyObjectByType<RoadArchitect.Road>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Poly Car: conformed {changed} terrain samples to the Green Forest road.");
        }

        public static int ConformTerrainToRoad(RoadArchitect.Road road)
        {
            if (road == null) return 0;
            var pts = SampleRoad(road, 2f);
            // Road Architect smooths heights when it builds the mesh, so take the real surface from the mesh.
            for (int i = 0; i < pts.Count; i++)
            {
                var hits = Physics.RaycastAll(pts[i] + Vector3.up * 6f, Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore);
                float bestY = float.NaN, bestGap = float.MaxValue;
                foreach (var h in hits)
                {
                    if (!h.collider.transform.IsChildOf(road.transform)) continue;
                    float gap = Mathf.Abs(h.point.y - pts[i].y);
                    if (gap < bestGap) { bestGap = gap; bestY = h.point.y; }
                }
                if (!float.IsNaN(bestY)) pts[i] = new Vector3(pts[i].x, bestY, pts[i].z);
            }
            float hw = road.RoadWidth() * 0.5f + (road.isShouldersEnabled ? road.shoulderWidth : 0f);
            const float edge = 1f, blend = 12f;
            float reach = hw + edge + blend;
            int changed = 0;
            foreach (var terrain in Object.FindObjectsByType<Terrain>())
            {
                var data = terrain.terrainData;
                Vector3 origin = terrain.transform.position, size = data.size;
                int res = data.heightmapResolution;
                float cell = size.x / (res - 1);
                var heights = data.GetHeights(0, 0, res, res);
                var bestDist = new float[res, res];
                var bestY = new float[res, res];
                for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) bestDist[z, x] = float.MaxValue;
                bool any = false;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    float minX = Mathf.Min(a.x, b.x) - reach, maxX = Mathf.Max(a.x, b.x) + reach;
                    float minZ = Mathf.Min(a.z, b.z) - reach, maxZ = Mathf.Max(a.z, b.z) + reach;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - origin.x) / cell)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((maxX - origin.x) / cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - origin.z) / cell)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((maxZ - origin.z) / cell));
                    if (x0 > x1 || z0 > z1) continue;
                    float abx = b.x - a.x, abz = b.z - a.z, len2 = abx * abx + abz * abz;
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float wx = origin.x + x * cell, wz = origin.z + z * cell;
                            float t = len2 > 1e-6f ? Mathf.Clamp01(((wx - a.x) * abx + (wz - a.z) * abz) / len2) : 0f;
                            float dx = a.x + abx * t - wx, dz = a.z + abz * t - wz;
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d < bestDist[z, x]) { bestDist[z, x] = d; bestY[z, x] = Mathf.Lerp(a.y, b.y, t); any = true; }
                        }
                }
                if (!any) continue;
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        float d = bestDist[z, x];
                        if (d >= reach) continue;
                        float original = origin.y + heights[z, x] * size.y;
                        float roadLevel = bestY[z, x] - 0.06f;
                        float k = d <= hw + edge ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - hw - edge) / blend);
                        float h = Mathf.Lerp(original, roadLevel, k);
                        heights[z, x] = Mathf.Clamp01((h - origin.y) / size.y);
                        changed++;
                    }
                data.SetHeights(0, 0, heights);
                // Re-seat trees on the new ground.
                var trees = data.treeInstances;
                for (int i = 0; i < trees.Length; i++)
                {
                    var tp = trees[i].position;
                    trees[i].position = new Vector3(tp.x, data.GetInterpolatedHeight(tp.x, tp.z) / size.y, tp.z);
                }
                data.treeInstances = trees;
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            return changed;
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
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 130f;
            RenderSettings.fogEndDistance = 320f;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            RenderSettings.sun = sun;

            // One floating diorama per track theme; the menu shows the selected track's.
            var root = new GameObject("Dioramas");
            var diorama = root.AddComponent<MenuDiorama>();
            var variants = new List<MenuDiorama.Variant>();
            foreach (var look in DioramaBuilder.Looks())
            {
                var t = DioramaBuilder.Build(look, root.transform);
                variants.Add(new MenuDiorama.Variant
                {
                    mapId = look.mapId, root = t.gameObject, carAnchor = t.Find("CarAnchor"),
                    background = look.background, sunColor = look.sun, sunEuler = look.sunEuler, sunIntensity = look.sunIntensity,
                    ambientSky = look.ambientSky, ambientEquator = look.ambientEquator, ambientGround = look.ambientGround,
                });
                t.gameObject.SetActive(variants.Count == 1);
            }
            diorama.variants = variants.ToArray();
            var first = variants[0];
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = first.ambientSky;
            RenderSettings.ambientEquatorColor = first.ambientEquator;
            RenderSettings.ambientGroundColor = first.ambientGround;
            RenderSettings.fogColor = first.background;
            sun.transform.rotation = Quaternion.Euler(first.sunEuler);
            sun.color = first.sunColor;
            sun.intensity = first.sunIntensity;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = first.background;
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 600f;
            camGo.transform.position = new Vector3(-60f, 50f, -60f);
            camGo.transform.LookAt(Vector3.zero);
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var vol = new GameObject("Post FX").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = MenuProfile();

            diorama.cam = cam;
            diorama.sun = sun;
            diorama.volume = vol;
            var menu = new GameObject("Menu").AddComponent<MainMenu>();
            menu.diorama = diorama;

            EditorSceneManager.SaveScene(scene, MenuPath);
            Debug.Log("Poly Car: menu scene built.");
        }

        /// <summary>Soft, slightly warm grade with a long-lens bokeh depth of field (the miniature look).</summary>
        private static VolumeProfile MenuProfile()
        {
            const string path = "Assets/PolyCar/Materials/MenuDioramaPostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.1f);
            var vig = p.Add<Vignette>(true); vig.intensity.Override(0.22f); vig.smoothness.Override(0.5f);
            var ca = p.Add<ColorAdjustments>(true); ca.postExposure.Override(0.1f); ca.contrast.Override(8f); ca.saturation.Override(8f);
            var dof = p.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(90f);
            dof.focalLength.Override(260f);
            dof.aperture.Override(1.4f);
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
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PolyCar/Branding/Icon.png");
            if (icon) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            PlayerSettings.bundleVersion = "2.1.0";
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
