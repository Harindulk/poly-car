using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace PolyCar.Editor
{
    /// <summary>
    /// One-click (and batch-mode) project setup: UI panel settings, shared materials, car prefabs built from
    /// the bundled models, the car and map catalogs, and the build scene list. Idempotent - safe to re-run.
    /// </summary>
    public static class PolyCarSetup
    {
        private const string Root = "Assets/PolyCar";
        private const string CarsDir = Root + "/Cars";
        private const string Res = Root + "/Resources";
        private const string MatDir = Root + "/Materials";

        [MenuItem("Poly Car/Setup/Run Full Setup")]
        public static void RunAll()
        {
            EnsureFolders();
            PlayerSettings.bundleVersion = "2.0.0";
            CreatePanelSettings();
            CreateMaterials();
            BuildCars();
            BuildMapCatalog();
            ConfigureAudioImport();
            WriteCredits();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Poly Car: setup complete.");
        }

        /// <summary>Music streams (Vorbis), effects decompress on load; engine/tyre loops mono.</summary>
        [MenuItem("Poly Car/Setup/Audio Import Settings")]
        public static void ConfigureAudioImport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Res + "/Audio" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;
                bool music = path.Contains("/Music/");
                var s = imp.defaultSampleSettings;
                var load = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                float q = music ? 0.55f : 0.75f;
                if (s.loadType == load && s.compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Approximately(s.quality, q) && imp.forceToMono == !music) continue;
                s.loadType = load;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = q;
                s.preloadAudioData = !music;
                imp.defaultSampleSettings = s;
                imp.forceToMono = !music;
                imp.loadInBackground = music;
                imp.SaveAndReimport();
            }
        }

        private static void EnsureFolders()
        {
            foreach (var d in new[] { CarsDir, Res, MatDir, Root + "/Maps" })
                if (!AssetDatabase.IsValidFolder(d)) Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------- UI

        [MenuItem("Poly Car/Setup/Panel Settings")]
        public static void CreatePanelSettings()
        {
            string path = Res + "/PolyCarPanel.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            ps.clearColor = false;
            EditorUtility.SetDirty(ps);
        }

        // ---------------------------------------------------------------- Materials

        [MenuItem("Poly Car/Setup/Materials")]
        public static void CreateMaterials()
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            var ghost = LoadOrCreateMat(Res + "/GhostMaterial.mat", unlit);
            MakeTransparent(ghost, false);
            ghost.SetColor("_BaseColor", new Color(0.45f, 0.9f, 1f, 0.33f));

            var locked = LoadOrCreateMat(Res + "/LockedMaterial.mat", lit);
            locked.SetColor("_BaseColor", new Color(0.07f, 0.075f, 0.09f));
            locked.SetFloat("_Smoothness", 0.75f);

            var soft = SoftParticleTexture();
            var flame = LoadOrCreateMat(MatDir + "/BoostFlame.mat", particles);
            MakeTransparent(flame, true);
            flame.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f, 0.9f));
            flame.SetTexture("_BaseMap", soft);

            var smoke = LoadOrCreateMat(MatDir + "/TyreSmoke.mat", particles);
            MakeTransparent(smoke, false);
            smoke.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.94f, 0.55f));
            smoke.SetTexture("_BaseMap", soft);

            foreach (var m in new[] { ghost, locked, flame, smoke }) EditorUtility.SetDirty(m);
        }

        /// <summary>Soft round puff (radial falloff) for smoke and flames.</summary>
        private static Texture2D SoftParticleTexture()
        {
            string path = MatDir + "/SoftPuff.png";
            if (!File.Exists(path))
            {
                const int S = 64;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture FindSmokeTexture()
        {
            var smoke = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TireSmoke_PS.prefab");
            var r = smoke ? smoke.GetComponent<ParticleSystemRenderer>() : null;
            var m = r ? r.sharedMaterial : null;
            if (m == null) return null;
            return m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
        }

        private static Material LoadOrCreateMat(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        private static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        // ---------------------------------------------------------------- Cars

        private class CarSource
        {
            public string id, name, blurb, source;
            public string[] wheels; // FL, FR, RL, RR renderer names; null = auto by position
            public bool flip;       // model faces -Z
            public float yaw;       // extra turn so the model faces +Z
            public float top, accel, grip, drift, turn, mass;
            public int unlock;
            public float enginePitch = 1f;
        }

        private static readonly CarSource[] Sources =
        {
            new CarSource { id = "muscle", name = "Stallion GT", source = "Assets/Prefabs/DriftCar.prefab",
                wheels = new[] { "FrontLeftWheel", "FrontRightWheel", "RearLeftWheel", "RearRightWheel" },
                blurb = "The original Poly Car. Big torque, a lazy rear end and happiest going sideways.",
                top = 182, accel = 10.5f, grip = 6.6f, drift = 2.1f, turn = 94, mass = 1350, unlock = 0, enginePitch = 0.9f },
            new CarSource { id = "outlaw", name = "Outlaw 01", source = "Assets/Low Poly Cars/Prefabs/car_1.prefab", yaw = -90f,
                blurb = "A loud orange muscle car with a habit of jumping things. Loose, loud and forgiving.",
                top = 176, accel = 10f, grip = 7f, drift = 2.0f, turn = 100, mass = 1300, unlock = 0, enginePitch = 0.85f },
            new CarSource { id = "scoop", name = "Scoop Van", source = "Assets/News Truck - Stylized Vehicle/Prefabs/News_Truck.prefab",
                wheels = new[] { "FLW", "FRW", "RLW", "RRW" },
                blurb = "Channel 7's news van. Heavy, slow and somehow always first on the scene.",
                top = 148, accel = 8f, grip = 6.2f, drift = 2.0f, turn = 84, mass = 1900, unlock = 0, enginePitch = 0.8f },
            new CarSource { id = "interceptor", name = "Interceptor", source = "Assets/Low Poly Cars/Prefabs/car_2.prefab", yaw = -90f,
                blurb = "Retired from the force, not from chasing. Stable, quick and great on the brakes.",
                top = 186, accel = 10.6f, grip = 7.8f, drift = 2.4f, turn = 96, mass = 1500, unlock = 2, enginePitch = 0.95f },
            new CarSource { id = "apex", name = "Apex S", source = "Assets/HyperCars/Prefabs/Hypercar2_Simple.prefab",
                wheels = new[] { "WHEEL_FL", "WHEEL_FR", "WHEEL_RL", "WHEEL_RR" },
                blurb = "Mid-engined and glued to the road. The grip car - brake late, turn hard.",
                top = 196, accel = 12f, grip = 9.2f, drift = 3.0f, turn = 102, mass = 1220, unlock = 3, enginePitch = 1.05f },
            new CarSource { id = "vortex", name = "Vortex R", source = "Assets/HyperCars/Prefabs/Hypercar1_Simple.prefab",
                wheels = new[] { "WHEEL_FL", "WHEEL_FR", "WHEEL_RL", "WHEEL_RR" },
                blurb = "A twin-turbo missile. Huge top speed, needs a careful right foot in the bends.",
                top = 206, accel = 12.6f, grip = 8.4f, drift = 2.6f, turn = 96, mass = 1260, unlock = 5, enginePitch = 1f },
            new CarSource { id = "zenith", name = "Zenith X", source = "Assets/HyperCars/Prefabs/Hypercar3_Simple.prefab",
                wheels = new[] { "WHEEL_FL", "WHEEL_FR", "WHEEL_RL", "WHEEL_RR" },
                blurb = "The fastest thing in Poly Car. Earn it, then hold on.",
                top = 214, accel = 13.5f, grip = 8.9f, drift = 2.5f, turn = 100, mass = 1180, unlock = 7, enginePitch = 1.08f },
        };

        [MenuItem("Poly Car/Setup/Build Cars")]
        public static void BuildCars()
        {
            EnsureFolders();
            var bodyMat = LoadOrCreatePhysics(Root + "/CarBody.physicMaterial");
            var specs = new List<CarSpec>();
            var prevActive = EditorSceneManager.GetActiveScene();
            bool untitled = !prevActive.IsValid() || string.IsNullOrEmpty(prevActive.path);
            // Batch mode starts in an unsaved untitled scene, which can't host an additive scene.
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            if (untitled) prevActive = default;
            EditorSceneManager.SetActiveScene(temp);
            try
            {
                foreach (var src in Sources)
                {
                    var prefab = BuildCar(src, bodyMat);
                    if (prefab == null) continue;
                    specs.Add(new CarSpec
                    {
                        id = src.id, displayName = src.name, blurb = src.blurb, prefab = prefab, unlockMedals = src.unlock,
                        topSpeedKmh = src.top, acceleration = src.accel, grip = src.grip, driftGrip = src.drift, turnRate = src.turn, mass = src.mass,
                    });
                }
            }
            finally
            {
                if (prevActive.IsValid())
                {
                    EditorSceneManager.SetActiveScene(prevActive);
                    EditorSceneManager.CloseScene(temp, true);
                }
            }

            string path = Res + "/CarCatalog.asset";
            var cat = AssetDatabase.LoadAssetAtPath<CarCatalog>(path);
            if (cat == null) { cat = ScriptableObject.CreateInstance<CarCatalog>(); AssetDatabase.CreateAsset(cat, path); }
            cat.cars = specs.ToArray();
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
        }

        private static PhysicsMaterial LoadOrCreatePhysics(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null) { m = new PhysicsMaterial("CarBody"); AssetDatabase.CreateAsset(m, path); }
            m.dynamicFriction = 0.05f;
            m.staticFriction = 0.05f;
            m.bounciness = 0.12f;
            m.frictionCombine = PhysicsMaterialCombine.Minimum;
            m.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject BuildCar(CarSource src, PhysicsMaterial bodyMat)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(src.source);
            if (source == null) { Debug.LogError("Poly Car: missing car model " + src.source); return null; }

            var root = new GameObject("Car_" + src.id);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var model = (GameObject)Object.Instantiate(source);
            model.name = "Model";
            // Keep the model's own root rotation (some FBX roots carry the Z-up fix); only optionally turn it around.
            model.transform.position = Vector3.zero;
            float turn = (src.flip ? 180f : 0f) + src.yaw;
            if (turn != 0f) model.transform.rotation = Quaternion.Euler(0f, turn, 0f) * model.transform.rotation;
            model.transform.SetParent(body.transform, true);

            // Strip the old rigs: scripts (including missing ones), physics, sounds, effects.
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var c in model.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                Object.DestroyImmediate(c);
            }
            foreach (var t in model.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null) continue;
                if (t.name == "Effects" || t.name == "Sounds" || t.name == "Colliders" || t.name == "Collider") Object.DestroyImmediate(t.gameObject);
            }

            var renderers = model.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).ToList();
            var wheelRs = new MeshRenderer[4];
            if (src.wheels != null)
            {
                for (int i = 0; i < 4; i++) wheelRs[i] = renderers.FirstOrDefault(r => r.name == src.wheels[i]);
            }
            else
            {
                var ws = renderers.Where(r => r.name.ToLower().Contains("whell") || r.name.ToLower().Contains("wheel")).ToList();
                if (ws.Count == 4)
                {
                    var byZ = ws.OrderByDescending(r => r.bounds.center.z).ToList();
                    var front = byZ.Take(2).OrderBy(r => r.bounds.center.x).ToList();
                    var rear = byZ.Skip(2).OrderBy(r => r.bounds.center.x).ToList();
                    wheelRs = new[] { front[0], front[1], rear[0], rear[1] };
                }
            }
            if (wheelRs.Any(w => w == null)) { Debug.LogError("Poly Car: could not find 4 wheels on " + src.source); Object.DestroyImmediate(root); return null; }

            var bodyRs = renderers.Where(r => !wheelRs.Contains(r)).ToList();
            Bounds bodyB = bodyRs[0].bounds;
            foreach (var r in bodyRs) bodyB.Encapsulate(r.bounds);
            float wheelRadius = wheelRs.Average(w => w.bounds.extents.y);
            float wheelBottom = wheelRs.Min(w => w.bounds.min.y);

            // Centre on x/z and stand the wheels on y = 0.
            Vector3 shift = new Vector3(-bodyB.center.x, -wheelBottom, -bodyB.center.z);
            model.transform.position += shift;
            bodyB.center += shift;

            var controller = root.AddComponent<PolyCarController>();
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                var wr = wheelRs[i];
                var pivot = new GameObject(names[i]).transform;
                pivot.SetParent(root.transform, false);
                Vector3 c = wr.bounds.center;
                pivot.position = new Vector3(c.x, wheelRadius, c.z);
                wr.transform.SetParent(pivot, true);
                controller.wheels[i] = new PolyCarController.Wheel { pivot = pivot, front = i < 2, left = i % 2 == 0 };
            }
            controller.bodyVisual = body.transform;
            controller.wheelRadius = wheelRadius;
            controller.suspensionTravel = Mathf.Clamp(wheelRadius * 0.85f, 0.22f, 0.36f);
            controller.centerOfMass = new Vector3(0f, wheelRadius * 0.95f, 0.05f);

            var rb = root.GetComponent<Rigidbody>();
            rb.mass = src.mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Body collider from wheel-centre height to the roof; wheels are raycasts.
            var box = root.AddComponent<BoxCollider>();
            float bottom = wheelRadius * 0.9f;
            float top = bodyB.max.y;
            box.center = new Vector3(0f, (bottom + top) * 0.5f, 0f);
            box.size = new Vector3(bodyB.size.x * 0.92f, Mathf.Max(0.3f, top - bottom), bodyB.size.z * 0.96f);
            box.sharedMaterial = bodyMat;

            var audio = root.AddComponent<CarAudio>();
            audio.car = controller;
            audio.enginePitch = src.enginePitch;

            var fx = root.AddComponent<CarFx>();
            fx.car = controller;
            var skidPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TireSkid.prefab");
            var smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TireSmoke_PS.prefab");
            var skids = new List<TrailRenderer>();
            var smokes = new List<ParticleSystem>();
            for (int i = 2; i < 4; i++)
            {
                Vector3 p = controller.wheels[i].pivot.localPosition;
                p.y = 0.03f;
                if (skidPrefab)
                {
                    var s = (GameObject)PrefabUtility.InstantiatePrefab(skidPrefab);
                    PrefabUtility.UnpackPrefabInstance(s, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    s.name = i == 2 ? "Skid_L" : "Skid_R";
                    s.transform.SetParent(root.transform, false);
                    s.transform.localPosition = p;
                    var tr = s.GetComponent<TrailRenderer>();
                    if (tr)
                    {
                        tr.emitting = false; tr.time = 7f; tr.minVertexDistance = 0.35f;
                        tr.widthMultiplier = 0.32f; tr.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
                        tr.startColor = new Color(0.06f, 0.06f, 0.07f, 0.7f); tr.endColor = new Color(0.06f, 0.06f, 0.07f, 0f);
                        tr.alignment = LineAlignment.TransformZ;
                        s.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat on the road
                        tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false; skids.Add(tr);
                    }
                }
                if (smokePrefab)
                {
                    var s = (GameObject)PrefabUtility.InstantiatePrefab(smokePrefab);
                    PrefabUtility.UnpackPrefabInstance(s, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    s.name = i == 2 ? "Smoke_L" : "Smoke_R";
                    s.transform.SetParent(root.transform, false);
                    s.transform.localPosition = p + Vector3.up * 0.1f;
                    var ps = s.GetComponent<ParticleSystem>();
                    if (ps)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        var main = ps.main;
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                        main.playOnAwake = false;
                        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
                        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
                        main.startColor = new Color(0.95f, 0.95f, 0.97f, 0.75f);
                        main.gravityModifier = -0.04f;
                        main.maxParticles = 160;
                        var em = ps.emission;
                        em.rateOverTimeMultiplier = 0f;
                        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
                        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 3.2f));
                        var col = ps.colorOverLifetime; col.enabled = true;
                        var g = new Gradient();
                        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                  new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
                        col.color = g;
                        var pr = s.GetComponent<ParticleSystemRenderer>();
                        pr.renderMode = ParticleSystemRenderMode.Billboard; // the 2022 prefab drew mesh particles
                        pr.mesh = null;
                        pr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/TyreSmoke.mat");
                        pr.shadowCastingMode = ShadowCastingMode.Off;
                        pr.receiveShadows = false;
                        smokes.Add(ps);
                    }
                }
            }
            fx.skids = skids.ToArray();
            fx.smoke = smokes.ToArray();
            fx.boostFlames = new[] { MakeFlame(root.transform, new Vector3(0f, wheelRadius * 0.9f, bodyB.min.z + 0.05f)) };

            string path = CarsDir + "/Car_" + src.id + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        private static ParticleSystem MakeFlame(Transform parent, Vector3 localPos)
        {
            var go = new GameObject("BoostFlame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = 0.22f;
            main.startSpeed = 7f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.25f), new Color(0.4f, 0.75f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 90f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.12f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/BoostFlame.mat");
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // ---------------------------------------------------------------- Maps

        [MenuItem("Poly Car/Setup/Map Catalog")]
        public static void BuildMapCatalog()
        {
            string path = Res + "/MapCatalog.asset";
            var cat = AssetDatabase.LoadAssetAtPath<MapCatalog>(path);
            if (cat == null) { cat = ScriptableObject.CreateInstance<MapCatalog>(); AssetDatabase.CreateAsset(cat, path); }
            var old = cat.maps ?? new MapSpec[0];
            MapSpec Keep(string id, MapSpec fresh)
            {
                // Preserve tuned par times if the map already exists in the catalog.
                var prev = old.FirstOrDefault(m => m.id == id);
                if (prev != null && string.IsNullOrEmpty(fresh.lengthLabel)) fresh.lengthLabel = prev.lengthLabel;
                return fresh;
            }
            var list = new List<MapSpec>
            {
                Keep("green_forest", new MapSpec
                {
                    id = "green_forest", displayName = "Green Forest", sceneName = "Green Forest", laps = 0,
                    blurb = "The original stage: a winding forest road past lakes and cliffs, start gate to finish arch.",
                    thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/thumbnails/car nature.png"),
                    goldTime = 148f, silverTime = 165f, bronzeTime = 190f, lengthLabel = "4.1 KM", accent = new Color(0.35f, 0.8f, 0.35f),
                }),
            };
            foreach (var extra in MapRegistry.Extra)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/PolyCar/Maps/" + extra.sceneName + ".unity") == null) continue;
                extra.thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PolyCar/Maps/" + extra.sceneName + "_thumb.png");
                list.Add(Keep(extra.id, extra));
            }
            cat.maps = list.ToArray();
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            SyncBuildScenes();
        }

        [MenuItem("Poly Car/Setup/Sync Build Scenes")]
        public static void SyncBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene("Assets/_Scenes/Menu.unity", true) };
            var cat = AssetDatabase.LoadAssetAtPath<MapCatalog>(Res + "/MapCatalog.asset");
            if (cat != null)
                foreach (var m in cat.maps)
                {
                    string p = m.sceneName == "Green Forest" ? "Assets/_Scenes/Green Forest.unity" : "Assets/PolyCar/Maps/" + m.sceneName + ".unity";
                    if (File.Exists(p)) scenes.Add(new EditorBuildSettingsScene(p, true));
                }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------------------------------------------------------------- Credits

        public static void WriteCredits()
        {
            const string text =
@"POLY CAR
Created by Harindu Fonseka (StarFun Games) - originally for the Microsoft Virtual Hackathon 2022.
Remastered for Unity 6 in 2026.

PROGRAMMING, DESIGN
Harindu Fonseka

MODELS AND ART (Unity Asset Store packages included in the project)
HyperCars, Low Poly Cars, News Truck - Stylized Vehicle, Stylized Grass Shader and Stylized Water (Staggart Creations / Alexander Ameye),
Skybox Cubemap Extended (BOXOPHOBIC), Vegetation Spawner (Staggart Creations), Road Architect (open source, MIT).

MUSIC
Pixabay Content License tracks: Embrace, Energetic Funky Groove, Fashionable Deep House, Flashes, Once in Paris.
Stargazer (original Poly Car soundtrack).

SOUND
Rally car engine layers, gear change, backfire and landing: Pol, Foundation Sound Effects (Freesound, CC0).
Tyre squeal: audible-edge (Freesound, CC0). Whoosh: BennettFilmTeacher (Freesound, CC0). Wind loop: SketchMan3 (OpenGameArt, CC0).
Jingle: Kenney Music Jingles (CC0). UI sounds: ZapSplat.

FONTS
Teko, League Spartan, Fredoka One, Fira Sans (SIL Open Font License).

Thanks for playing!";
            File.WriteAllText(Res + "/Credits.txt", text);
            AssetDatabase.ImportAsset(Res + "/Credits.txt");
        }
    }

    /// <summary>Extra maps added by generators; registered here so the catalog picks them up once built.</summary>
    public static class MapRegistry
    {
        public static readonly List<MapSpec> Extra = new List<MapSpec>
        {
            new MapSpec
            {
                id = "sakura_ring", displayName = "Sakura Ring", sceneName = "Sakura Ring", laps = 3,
                blurb = "A lakeside circuit under cherry blossoms and torii gates, with Mount Fuji watching.",
                goldTime = 150f, silverTime = 168f, bronzeTime = 195f, lengthLabel = "1.7 KM", accent = new Color(1f, 0.55f, 0.75f),
            },
            new MapSpec
            {
                id = "dust_canyon", displayName = "Dust Canyon", sceneName = "Dust Canyon", laps = 2,
                blurb = "Fast sweepers through red-rock desert mesas. Flat out, if you dare.",
                goldTime = 158f, silverTime = 178f, bronzeTime = 205f, lengthLabel = "3.2 KM", accent = new Color(1f, 0.5f, 0.2f),
            },
        };
    }
}
