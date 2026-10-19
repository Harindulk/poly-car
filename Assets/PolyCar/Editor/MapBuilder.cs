using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PolyCar.Editor
{
    /// <summary>
    /// Generates a complete circuit scene from a closed spline: faceted low-poly terrain carved to the road,
    /// road ribbon with markings and kerbs, tyre walls on tight corners, start gantry and grid, themed scenery,
    /// lighting, fog, post FX, the race layer and a menu thumbnail. Deterministic per seed.
    /// </summary>
    public static class MapBuilder
    {
        private const string Dir = "Assets/PolyCar/Maps";
        private const string MatDir = "Assets/PolyCar/Maps/Materials";

        public class Theme
        {
            public string key;
            public Color grassA, grassB, rock, dirt, snow;
            public float snowLine = 9999f;
            public Color skyTint, groundColor, fog, sun, ambientSky, ambientEquator, ambientGround;
            public float fogDensity = 0.0028f, sunIntensity = 1.3f, exposure = 1.2f, atmosphere = 1f;
            public Vector3 sunEuler = new Vector3(42f, -30f, 0f);
            public (string prefab, float weight, float minScale, float maxScale, float clearance)[] scenery;
            public int sceneryCount = 1200;
            public float mountainHeight = 70f, hillNoise = 8f;
            public float saturation = 15f;
        }

        public class Track
        {
            public string sceneName;
            public Vector3[] control;
            public float halfWidth = 7f;
            public int seed = 1;
            public Vector2 lakeCenter; public float lakeRadius; public float waterY;
        }

        // ------------------------------------------------------------------ public entry points

        [MenuItem("Poly Car/Maps/Build Sakura Ring")]
        public static void BuildSakura() => Build(SakuraTrack(), SakuraTheme(), DecorateSakura);

        [MenuItem("Poly Car/Maps/Build Dust Canyon")]
        public static void BuildCanyon() => Build(CanyonTrack(), CanyonTheme(), DecorateCanyon);

        [MenuItem("Poly Car/Maps/Build Frost Peak")]
        public static void BuildFrost() => Build(FrostTrack(), FrostTheme(), DecorateFrost);

        [MenuItem("Poly Car/Maps/Build All")]
        public static void BuildAllMaps()
        {
            BuildSakura();
            BuildCanyon();
            BuildFrost();
            PolyCarSetup.BuildMapCatalog();
        }

        // ------------------------------------------------------------------ track + theme data

        private static Track SakuraTrack() => new Track
        {
            sceneName = "Sakura Ring", seed = 7, halfWidth = 7f,
            control = new[]
            {
                new Vector3(0, 4, 0), new Vector3(170, 4, -6), new Vector3(300, 6, 30), new Vector3(372, 9, 130),
                new Vector3(352, 12, 236), new Vector3(276, 13, 282), new Vector3(205, 11, 246), new Vector3(150, 9, 262),
                new Vector3(70, 8, 336), new Vector3(-60, 10, 352), new Vector3(-190, 13, 296), new Vector3(-262, 11, 182),
                new Vector3(-250, 8, 72), new Vector3(-150, 5, 14),
            },
            lakeCenter = new Vector2(60f, 160f), lakeRadius = 95f, waterY = 1.2f,
        };

        private static Theme SakuraTheme() => new Theme
        {
            key = "sakura",
            grassA = new Color(0.44f, 0.62f, 0.27f), grassB = new Color(0.52f, 0.68f, 0.31f), rock = new Color(0.47f, 0.45f, 0.47f),
            dirt = new Color(0.63f, 0.52f, 0.41f), snow = new Color(0.95f, 0.96f, 1f), snowLine = 230f,
            skyTint = new Color(0.55f, 0.65f, 0.85f), groundColor = new Color(0.55f, 0.5f, 0.55f), fog = new Color(0.86f, 0.82f, 0.88f),
            sun = new Color(1f, 0.9f, 0.82f), ambientSky = new Color(0.72f, 0.74f, 0.9f), ambientEquator = new Color(0.78f, 0.7f, 0.74f), ambientGround = new Color(0.35f, 0.36f, 0.3f),
            fogDensity = 0.0022f, sunEuler = new Vector3(36f, -125f, 0f), sunIntensity = 1.35f,
            scenery = new[]
            {
                ("Assets/Prefabs/SGS_Tree.prefab", 5f, 0.9f, 1.4f, 9f),
                ("Assets/Prefabs/DemoBroadleaf.prefab", 2f, 0.8f, 1.3f, 9f),
                ("Assets/Prefabs/DemoSpruce.prefab", 2.5f, 0.9f, 1.5f, 9f),
                ("Assets/Prefabs/Shrub.prefab", 2f, 0.6f, 1.1f, 5f),
                ("Assets/Prefabs/SGS_DemoRock.prefab", 0.8f, 0.4f, 1.1f, 6f),
            },
            sceneryCount = 1400, mountainHeight = 55f, hillNoise = 7f,
        };

        private static Track CanyonTrack() => new Track
        {
            sceneName = "Dust Canyon", seed = 21, halfWidth = 7.5f,
            control = new[]
            {
                new Vector3(0, 10, 0), new Vector3(260, 10, -20), new Vector3(520, 12, 30), new Vector3(700, 16, 170),
                new Vector3(760, 20, 380), new Vector3(690, 22, 580), new Vector3(520, 20, 700), new Vector3(330, 18, 690),
                new Vector3(250, 17, 580), new Vector3(310, 15, 460), new Vector3(200, 14, 380), new Vector3(30, 20, 430),
                new Vector3(-130, 13, 440), new Vector3(-300, 12, 360), new Vector3(-360, 11, 200), new Vector3(-270, 10, 60),
                new Vector3(-140, 10, 5),
            },
        };

        private static Theme CanyonTheme() => new Theme
        {
            key = "canyon",
            grassA = new Color(0.86f, 0.62f, 0.38f), grassB = new Color(0.82f, 0.56f, 0.33f), rock = new Color(0.66f, 0.33f, 0.2f),
            dirt = new Color(0.9f, 0.72f, 0.5f), snow = new Color(0.66f, 0.33f, 0.2f),
            skyTint = new Color(0.62f, 0.62f, 0.75f), groundColor = new Color(0.7f, 0.5f, 0.35f), fog = new Color(0.95f, 0.8f, 0.62f),
            sun = new Color(1f, 0.86f, 0.68f), ambientSky = new Color(0.78f, 0.74f, 0.82f), ambientEquator = new Color(0.9f, 0.72f, 0.56f), ambientGround = new Color(0.45f, 0.3f, 0.2f),
            fogDensity = 0.0018f, sunEuler = new Vector3(30f, 60f, 0f), sunIntensity = 1.5f, exposure = 1.1f,
            scenery = new[]
            {
                ("Assets/Prefabs/SGS_DemoRock.prefab", 3f, 0.6f, 2.2f, 7f),
                ("Assets/Prefabs/LowPoly - Rock B.prefab", 2f, 0.6f, 1.8f, 7f),
                ("Assets/Prefabs/Shrub.prefab", 1.2f, 0.4f, 0.8f, 6f),
            },
            sceneryCount = 500, mountainHeight = 95f, hillNoise = 5f, saturation = 10f,
        };

        private static Track FrostTrack() => new Track
        {
            sceneName = "Frost Peak", seed = 33, halfWidth = 7f,
            control = new[]
            {
                new Vector3(0, 20, 0), new Vector3(180, 22, 10), new Vector3(300, 28, 80), new Vector3(330, 34, 200),
                new Vector3(250, 40, 300), new Vector3(120, 44, 320), new Vector3(40, 40, 250), new Vector3(-40, 34, 300),
                new Vector3(-160, 30, 330), new Vector3(-280, 26, 250), new Vector3(-300, 22, 120), new Vector3(-220, 20, 30),
                new Vector3(-100, 18, -10),
            },
        };

        private static Theme FrostTheme() => new Theme
        {
            key = "frost",
            grassA = new Color(0.9f, 0.93f, 0.98f), grassB = new Color(0.83f, 0.87f, 0.95f), rock = new Color(0.42f, 0.45f, 0.52f),
            dirt = new Color(0.62f, 0.63f, 0.67f), snow = new Color(0.97f, 0.98f, 1f),
            skyTint = new Color(0.5f, 0.6f, 0.82f), groundColor = new Color(0.7f, 0.72f, 0.8f), fog = new Color(0.82f, 0.86f, 0.94f),
            sun = new Color(1f, 0.86f, 0.74f), ambientSky = new Color(0.7f, 0.76f, 0.92f), ambientEquator = new Color(0.78f, 0.8f, 0.88f), ambientGround = new Color(0.5f, 0.52f, 0.6f),
            fogDensity = 0.0026f, sunEuler = new Vector3(18f, -60f, 0f), sunIntensity = 1.25f, exposure = 1.15f,
            scenery = new[]
            {
                ("Assets/Prefabs/SGS_SpruceTree.prefab", 6f, 0.9f, 1.6f, 9f),
                ("Assets/Prefabs/DemoSpruce.prefab", 2f, 0.9f, 1.5f, 9f),
                ("Assets/Prefabs/SGS_DemoRock.prefab", 1f, 0.5f, 1.4f, 7f),
            },
            sceneryCount = 1300, mountainHeight = 110f, hillNoise = 9f, saturation = 5f,
        };

        // ------------------------------------------------------------------ build

        public class Ctx
        {
            public Track track; public Theme theme;
            public List<Vector3> center;       // 2 m spacing, closed loop
            public List<float> curvature;      // signed rad/m per centre point
            public Vector3[] coarse;            // for distance queries
            public float minX, minZ, maxX, maxZ;
            public System.Random rnd;
            public Transform root;
            public Dictionary<string, Material> mats = new Dictionary<string, Material>();
            public System.Func<float, float, float> heightAt;
            public Rect bounds;
        }

        public static void Build(Track track, Theme theme, System.Action<Ctx> decorate)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) Directory.CreateDirectory(Dir);
            if (!AssetDatabase.IsValidFolder(MatDir)) Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var c = new Ctx { track = track, theme = theme, rnd = new System.Random(track.seed) };
            c.root = new GameObject("Map").transform;

            BuildCenterline(c);
            CreateMaterials(c);
            BuildTerrain(c);
            BuildRoad(c);
            BuildVerges(c);
            BuildKerbsAndWalls(c);
            BuildStart(c);
            if (track.lakeRadius > 0f) BuildLake(c);
            decorate?.Invoke(c);
            Scatter(c);
            Atmosphere(c);
            RaceLayer(c);
            var cam = MakeCamera(c);

            string scenePath = $"{Dir}/{track.sceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Thumbnail(c, cam);
            Debug.Log($"Poly Car: built map {track.sceneName}: track {TrackLength(c):0} m.");
        }

        private static float TrackLength(Ctx c)
        {
            float len = 0f;
            for (int i = 0; i < c.center.Count; i++) len += Vector3.Distance(c.center[i], c.center[(i + 1) % c.center.Count]);
            return len;
        }

        // ------------------------------------------------------------------ centreline

        private static void BuildCenterline(Ctx c)
        {
            var cp = c.track.control;
            var dense = new List<Vector3>();
            int n = cp.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = cp[(i - 1 + n) % n], p1 = cp[i], p2 = cp[(i + 1) % n], p3 = cp[(i + 2) % n];
                int sub = Mathf.Max(8, Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.5f));
                for (int k = 0; k < sub; k++) dense.Add(SceneBuilders.CatmullRom(p0, p1, p2, p3, k / (float)sub));
            }
            // Resample at 2 m.
            var pts = new List<Vector3> { dense[0] };
            float acc = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                acc += Vector3.Distance(dense[i - 1], dense[i]);
                if (acc >= 2f) { pts.Add(dense[i]); acc = 0f; }
            }
            // Smooth heights (no bumps), keep plan shape.
            int m = pts.Count;
            for (int pass = 0; pass < 6; pass++)
            {
                var ys = pts.Select(p => p.y).ToArray();
                for (int i = 0; i < m; i++)
                {
                    float s = 0f;
                    for (int k = -10; k <= 10; k++) s += ys[(i + k + m) % m];
                    pts[i] = new Vector3(pts[i].x, s / 21f, pts[i].z);
                }
            }
            c.center = pts;
            c.curvature = new List<float>(m);
            for (int i = 0; i < m; i++)
            {
                Vector3 a = pts[(i - 4 + m) % m], b = pts[i], d = pts[(i + 4) % m];
                Vector3 d1 = (b - a); d1.y = 0; Vector3 d2 = (d - b); d2.y = 0;
                float ang = Vector3.SignedAngle(d1, d2, Vector3.up) * Mathf.Deg2Rad;
                c.curvature.Add(ang / 16f);
            }
            c.coarse = pts.Where((p, i) => i % 3 == 0).ToArray();
            c.minX = pts.Min(p => p.x); c.maxX = pts.Max(p => p.x);
            c.minZ = pts.Min(p => p.z); c.maxZ = pts.Max(p => p.z);
        }

        private static Vector3 Heading(Ctx c, int i)
        {
            int m = c.center.Count;
            Vector3 d = c.center[(i + 1) % m] - c.center[(i - 1 + m) % m];
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>
        /// Exact XZ distance to the road centreline and the road height at the nearest point (coarse search
        /// over every 3rd point, then projection onto the 2 m segments around it, height interpolated).
        /// </summary>
        private static float NearestRoad(Ctx c, float x, float z, out float roadY)
        {
            var pts = c.coarse;
            float best = float.MaxValue; int bestK = 0;
            for (int k = 0; k < pts.Length; k++)
            {
                float dx = pts[k].x - x, dz = pts[k].z - z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; bestK = k; }
            }
            int m = c.center.Count;
            int fi = bestK * 3;
            float bestD = float.MaxValue; roadY = pts[bestK].y;
            for (int o = -5; o <= 5; o++)
            {
                Vector3 a = c.center[(fi + o + m) % m], b = c.center[(fi + o + 1 + m) % m];
                float abx = b.x - a.x, abz = b.z - a.z;
                float len2 = abx * abx + abz * abz;
                float t = len2 > 1e-6f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.z) * abz) / len2) : 0f;
                float px = a.x + abx * t - x, pz = a.z + abz * t - z;
                float d2 = px * px + pz * pz;
                if (d2 < bestD) { bestD = d2; roadY = Mathf.Lerp(a.y, b.y, t); }
            }
            return Mathf.Sqrt(bestD);
        }

        // ------------------------------------------------------------------ materials

        private static Material Mat(Ctx c, string name, Color color, float smooth = 0.05f, Texture tex = null, bool instancing = true)
        {
            string path = $"{MatDir}/{c.theme.key}_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_SpecularHighlights", smooth > 0.2f ? 1f : 0f);
            if (smooth <= 0.2f) m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); else m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (tex) m.SetTexture("_BaseMap", tex);
            m.enableInstancing = instancing;
            EditorUtility.SetDirty(m);
            c.mats[name] = m;
            return m;
        }

        private static void CreateMaterials(Ctx c)
        {
            var t = c.theme;
            Mat(c, "grassA", t.grassA); Mat(c, "grassB", t.grassB); Mat(c, "rock", t.rock); Mat(c, "dirt", t.dirt); Mat(c, "snow", t.snow, 0.3f);
            Mat(c, "road", Color.white, 0.25f, RoadTexture());
            Mat(c, "kerbRed", new Color(0.85f, 0.12f, 0.12f), 0.2f);
            Mat(c, "kerbWhite", new Color(0.95f, 0.95f, 0.95f), 0.2f);
            Mat(c, "checker", Color.white, 0.2f, CheckerTexture());
            Mat(c, "gantry", new Color(0.12f, 0.13f, 0.16f), 0.4f);
            Mat(c, "accent", new Color(1f, 0.42f, 0.1f), 0.3f);
            Mat(c, "lineWhite", new Color(0.95f, 0.95f, 0.95f), 0.1f);
        }

        private static Texture2D SaveTexture(string name, Texture2D tex, TextureWrapMode wrap, int aniso = 8)
        {
            string path = $"{MatDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = wrap;
            imp.anisoLevel = aniso;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D RoadTexture()
        {
            // 256 x 512: width of the road across u, two road-widths along v. Edge lines, dashed centre line, grain.
            const int W = 256, H = 512;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
            var rnd = new System.Random(3);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    float grain = (float)rnd.NextDouble() * 0.035f + Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.03f;
                    Color col = new Color(0.25f + grain, 0.26f + grain, 0.29f + grain);
                    if ((u > 0.035f && u < 0.06f) || (u > 0.94f && u < 0.965f)) col = new Color(0.92f, 0.92f, 0.9f);
                    if (u > 0.49f && u < 0.51f && (v % 0.5f) < 0.28f) col = new Color(0.95f, 0.82f, 0.25f);
                    if (u < 0.02f || u > 0.98f) col *= 0.8f;
                    col.a = 1f;
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            return SaveTexture("RoadAsphalt", tex, TextureWrapMode.Repeat);
        }

        private static Texture2D CheckerTexture()
        {
            const int S = 64;
            var tex = new Texture2D(S, S / 4, TextureFormat.RGBA32, true);
            for (int y = 0; y < S / 4; y++)
                for (int x = 0; x < S; x++)
                    tex.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? Color.white : new Color(0.08f, 0.08f, 0.1f));
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            var t = SaveTexture("Checker", tex, TextureWrapMode.Repeat, 4);
            var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
            imp.filterMode = FilterMode.Point; imp.SaveAndReimport();
            return t;
        }

        // ------------------------------------------------------------------ terrain

        private const float Cell = 7f;
        private const float Margin = 380f;

        private static float NaturalHeight(Ctx c, float x, float z, float distToRoad, float roadY)
        {
            var t = c.theme;
            float n = Mathf.PerlinNoise(x * 0.004f + 13.1f, z * 0.004f + 7.7f) * 2f - 1f;
            float n2 = Mathf.PerlinNoise(x * 0.012f + 3.3f, z * 0.012f + 1.9f) * 2f - 1f;
            float h = roadY + n * t.hillNoise * 2.2f + n2 * t.hillNoise * 0.6f;
            // Hills grow away from the circuit, framing it like a valley.
            float ring = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(120f, 420f, distToRoad));
            float peaks = Mathf.PerlinNoise(x * 0.006f + 50f, z * 0.006f + 20f);
            h += ring * t.mountainHeight * (0.45f + peaks);
            return h;
        }

        private static void BuildTerrain(Ctx c)
        {
            float x0 = c.minX - Margin, z0 = c.minZ - Margin, x1 = c.maxX + Margin, z1 = c.maxZ + Margin;
            int nx = Mathf.CeilToInt((x1 - x0) / Cell), nz = Mathf.CeilToInt((z1 - z0) / Cell);
            var heights = new float[nx + 1, nz + 1];
            var dists = new float[nx + 1, nz + 1];
            // Ground meets the road edge almost flush (art of rally / EasyRoads-style terrain conform),
            // then eases into the natural hills; verge strips cover the last few metres.
            float flat = c.track.halfWidth + 0.8f;
            float blend = 30f;
            var tr = c.track;
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float x = x0 + i * Cell, z = z0 + j * Cell;
                    float d = NearestRoad(c, x, z, out float ry);
                    float nat = NaturalHeight(c, x, z, d, ry);
                    float h;
                    if (d < flat) h = ry - 0.1f;
                    else h = Mathf.Lerp(ry - 0.1f, nat, Mathf.SmoothStep(0f, 1f, (d - flat) / blend));
                    if (tr.lakeRadius > 0f)
                    {
                        float ld = Vector2.Distance(new Vector2(x, z), tr.lakeCenter);
                        if (ld < tr.lakeRadius && d > flat + 8f)
                        {
                            float depth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((tr.lakeRadius - ld) / 35f));
                            h = Mathf.Lerp(h, tr.waterY - 4f, depth * Mathf.Clamp01((d - flat - 8f) / 12f));
                        }
                    }
                    heights[i, j] = h;
                    dists[i, j] = d;
                }

            // Chunks of 32 x 32 cells, flat-shaded, one submesh per surface.
            const int chunk = 32;
            var parent = new GameObject("Terrain").transform;
            parent.SetParent(c.root, false);
            var mats = new[] { c.mats["grassA"], c.mats["grassB"], c.mats["rock"], c.mats["dirt"], c.mats["snow"] };
            for (int ci = 0; ci < nx; ci += chunk)
                for (int cj = 0; cj < nz; cj += chunk)
                {
                    var verts = new List<Vector3>();
                    var subs = new List<int>[5];
                    for (int s = 0; s < 5; s++) subs[s] = new List<int>();
                    for (int i = ci; i < Mathf.Min(ci + chunk, nx); i++)
                        for (int j = cj; j < Mathf.Min(cj + chunk, nz); j++)
                        {
                            Vector3 a = new Vector3(x0 + i * Cell, heights[i, j], z0 + j * Cell);
                            Vector3 b = new Vector3(x0 + (i + 1) * Cell, heights[i + 1, j], z0 + j * Cell);
                            Vector3 cc = new Vector3(x0 + i * Cell, heights[i, j + 1], z0 + (j + 1) * Cell);
                            Vector3 d = new Vector3(x0 + (i + 1) * Cell, heights[i + 1, j + 1], z0 + (j + 1) * Cell);
                            bool flip = ((i + j) & 1) == 0;
                            float dist = Mathf.Min(dists[i, j], dists[i + 1, j + 1]);
                            if (flip) { Tri(c, verts, subs, a, cc, b, dist); Tri(c, verts, subs, b, cc, d, dist); }
                            else { Tri(c, verts, subs, a, cc, d, dist); Tri(c, verts, subs, a, d, b, dist); }
                        }
                    var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = $"Terrain_{ci}_{cj}" };
                    mesh.SetVertices(verts);
                    mesh.subMeshCount = 5;
                    for (int s = 0; s < 5; s++) mesh.SetTriangles(subs[s], s);
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    string mp = $"{Dir}/{c.track.sceneName}_Meshes";
                    if (!AssetDatabase.IsValidFolder(mp)) AssetDatabase.CreateFolder(Dir, $"{c.track.sceneName}_Meshes");
                    AssetDatabase.CreateAsset(mesh, $"{mp}/{mesh.name}.asset");
                    var go = new GameObject(mesh.name);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
                }
            c.heightAt = (x, z) =>
            {
                float fi = (x - x0) / Cell, fj = (z - z0) / Cell;
                int i = Mathf.Clamp(Mathf.FloorToInt(fi), 0, nx - 1), j = Mathf.Clamp(Mathf.FloorToInt(fj), 0, nz - 1);
                float u = Mathf.Clamp01(fi - i), v = Mathf.Clamp01(fj - j);
                return Mathf.Lerp(Mathf.Lerp(heights[i, j], heights[i + 1, j], u), Mathf.Lerp(heights[i, j + 1], heights[i + 1, j + 1], u), v);
            };
            c.bounds = new Rect(x0, z0, x1 - x0, z1 - z0);
        }

        private static void Tri(Ctx c, List<Vector3> verts, List<int>[] subs, Vector3 a, Vector3 b, Vector3 d, float distToRoad)
        {
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            float h = (a.y + b.y + d.y) / 3f;
            int sub;
            if (h > c.theme.snowLine) sub = 4;
            else if (n.y < 0.8f) sub = 2;
            else if (distToRoad < c.track.halfWidth + 2.5f) sub = 3;
            else
            {
                Vector3 m = (a + b + d) / 3f;
                sub = Mathf.PerlinNoise(m.x * 0.03f, m.z * 0.03f) + (float)c.rnd.NextDouble() * 0.25f > 0.62f ? 1 : 0;
            }
            int baseIndex = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(d);
            subs[sub].Add(baseIndex); subs[sub].Add(baseIndex + 1); subs[sub].Add(baseIndex + 2);
        }

        // ------------------------------------------------------------------ road

        private static void BuildRoad(Ctx c)
        {
            var parent = new GameObject("Road").transform;
            parent.SetParent(c.root, false);
            int m = c.center.Count;
            float hw = c.track.halfWidth;
            const int segLen = 100; // centre points per mesh piece (~200 m)
            float vScale = 1f / (hw * 4f);
            float v = 0f;
            for (int s0 = 0; s0 < m; s0 += segLen)
            {
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
                var colVerts = new List<Vector3>(); var colTris = new List<int>();
                int end = Mathf.Min(s0 + segLen, m);
                for (int i = s0; i <= end; i++)
                {
                    int k = i % m;
                    Vector3 p = c.center[k];
                    Vector3 right = Vector3.Cross(Vector3.up, Heading(c, k));
                    Vector3 l = p - right * hw, r = p + right * hw;
                    if (i > s0) v += Vector3.Distance(c.center[(i - 1) % m], p) * vScale;
                    int b = verts.Count;
                    verts.Add(l + Vector3.down * 0.6f); uvs.Add(new Vector2(0f, v));   // skirt
                    verts.Add(l); uvs.Add(new Vector2(0f, v));
                    verts.Add(r); uvs.Add(new Vector2(1f, v));
                    verts.Add(r + Vector3.down * 0.6f); uvs.Add(new Vector2(1f, v));
                    colVerts.Add(l); colVerts.Add(r);
                    if (i > s0)
                    {
                        int a = b - 4;
                        // top
                        tris.AddRange(new[] { a + 1, b + 1, a + 2, a + 2, b + 1, b + 2 });
                        // skirts
                        tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                        tris.AddRange(new[] { a + 2, b + 2, a + 3, a + 3, b + 2, b + 3 });
                        int cb = colVerts.Count - 2, ca = cb - 2;
                        colTris.AddRange(new[] { ca, cb, ca + 1, ca + 1, cb, cb + 1 });
                    }
                }
                var mesh = new Mesh { name = $"Road_{s0}" };
                mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                var col = new Mesh { name = $"RoadCol_{s0}" };
                col.SetVertices(colVerts); col.SetTriangles(colTris, 0); col.RecalculateNormals();
                string mp = $"{Dir}/{c.track.sceneName}_Meshes";
                AssetDatabase.CreateAsset(mesh, $"{mp}/{mesh.name}.asset");
                AssetDatabase.CreateAsset(col, $"{mp}/{col.name}.asset");
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = c.mats["road"];
                go.AddComponent<MeshCollider>().sharedMesh = col;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        /// <summary>
        /// Gravel verges: from the asphalt edge (road height) out 3.2 m to the terrain surface, so there is
        /// no step or gap when a car leaves the road. Collider included; chunked like the road.
        /// </summary>
        private static void BuildVerges(Ctx c)
        {
            var parent = new GameObject("Verges").transform;
            parent.SetParent(c.root, false);
            var mat = Mat(c, "verge", Color.Lerp(c.theme.dirt, c.theme.grassA, 0.25f), 0.05f);
            int m = c.center.Count;
            float hw = c.track.halfWidth;
            const float width = 3.2f;
            const int segLen = 100;
            for (int s0 = 0; s0 < m; s0 += segLen)
            {
                var verts = new List<Vector3>(); var tris = new List<int>();
                int end = Mathf.Min(s0 + segLen, m);
                for (int i = s0; i <= end; i++)
                {
                    int k = i % m;
                    Vector3 p = c.center[k];
                    Vector3 right = Vector3.Cross(Vector3.up, Heading(c, k));
                    foreach (int side in new[] { -1, 1 })
                    {
                        Vector3 inner = p + right * side * (hw - 0.05f) + Vector3.down * 0.01f;
                        Vector3 outer = p + right * side * (hw + width);
                        outer.y = Mathf.Min(c.heightAt(outer.x, outer.z) - 0.03f, p.y + 2.5f);
                        outer.y = Mathf.Max(outer.y, p.y - 2.5f);
                        verts.Add(inner); verts.Add(outer);
                    }
                    if (i > s0)
                    {
                        int b = verts.Count - 4, a = b - 4;
                        // left verge (a, a+1) -> (b, b+1); right verge (a+2, a+3) -> (b+2, b+3)
                        tris.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                        tris.AddRange(new[] { a + 2, b + 2, a + 3, a + 3, b + 2, b + 3 });
                    }
                }
                var mesh = new Mesh { name = $"Verge_{s0}" };
                mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{Dir}/{c.track.sceneName}_Meshes/{mesh.name}.asset");
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        private static void BuildKerbsAndWalls(Ctx c)
        {
            int m = c.center.Count;
            float hw = c.track.halfWidth;
            var kerbParent = new GameObject("Kerbs").transform; kerbParent.SetParent(c.root, false);
            var wallParent = new GameObject("TyreWalls").transform; wallParent.SetParent(c.root, false);
            var red = new List<Vector3>(); var white = new List<Vector3>();
            var redT = new List<int>(); var whiteT = new List<int>();
            int step = 1; // 2 m kerb blocks
            for (int i = 0; i < m; i += step)
            {
                float k = c.curvature[i];
                if (Mathf.Abs(k) < 0.011f) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p0 = c.center[i], p1 = c.center[(i + step) % m];
                    Vector3 r0 = Vector3.Cross(Vector3.up, Heading(c, i)), r1 = Vector3.Cross(Vector3.up, Heading(c, (i + step) % m));
                    Vector3 a = p0 + r0 * side * (hw - 0.05f), b = p0 + r0 * side * (hw - 1.1f);
                    Vector3 cc = p1 + r1 * side * (hw - 0.05f), d = p1 + r1 * side * (hw - 1.1f);
                    Vector3 up = Vector3.up * 0.035f;
                    var list = (i / step) % 2 == 0 ? red : white;
                    var tl = list == red ? redT : whiteT;
                    int bi = list.Count;
                    list.Add(a + up); list.Add(b + up); list.Add(cc + up); list.Add(d + up);
                    if (side > 0) tl.AddRange(new[] { bi, bi + 2, bi + 1, bi + 1, bi + 2, bi + 3 });
                    else tl.AddRange(new[] { bi, bi + 1, bi + 2, bi + 1, bi + 3, bi + 2 });
                }
            }
            MakeMesh(c, "Kerbs", kerbParent, new[] { red, white }, new[] { redT, whiteT }, new[] { c.mats["kerbRed"], c.mats["kerbWhite"] }, false);

            // Tyre walls on the outside of tight corners.
            var wr = new List<Vector3>(); var ww = new List<Vector3>(); var wrT = new List<int>(); var wwT = new List<int>();
            var colV = new List<Vector3>(); var colT = new List<int>();
            float off = hw + 5.5f;
            for (int i = 0; i < m; i += 2)
            {
                float k = c.curvature[i];
                if (Mathf.Abs(k) < 0.014f) continue;
                float side = -Mathf.Sign(k); // outside of the turn
                int j = (i + 2) % m;
                Vector3 r0 = Vector3.Cross(Vector3.up, Heading(c, i)), r1 = Vector3.Cross(Vector3.up, Heading(c, j));
                Vector3 a = c.center[i] + r0 * side * off, b = c.center[j] + r1 * side * off;
                a.y = c.heightAt(a.x, a.z) - 0.2f; b.y = c.heightAt(b.x, b.z) - 0.2f;
                var list = (i / 2) % 3 == 0 ? wr : ww;
                var tl = list == wr ? wrT : wwT;
                AddWallSegment(list, tl, a, b, 1.0f, 0.7f);
                AddWallSegment(colV, colT, a, b, 1.6f, 0.7f);
            }
            var wallGo = MakeMesh(c, "TyreWall", wallParent, new[] { wr, ww }, new[] { wrT, wwT }, new[] { c.mats["kerbRed"], c.mats["kerbWhite"] }, false);
            if (colV.Count > 0)
            {
                var col = new Mesh { name = "TyreWallCol" };
                col.SetVertices(colV); col.SetTriangles(colT, 0); col.RecalculateNormals();
                AssetDatabase.CreateAsset(col, $"{Dir}/{c.track.sceneName}_Meshes/TyreWallCol.asset");
                var mc = wallGo.AddComponent<MeshCollider>(); mc.sharedMesh = col;
                var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/PolyCar/CarBody.physicMaterial");
                if (pm) mc.sharedMaterial = pm;
            }
        }

        private static void AddWallSegment(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float height, float thick)
        {
            Vector3 dir = (b - a); dir.y = 0f;
            Vector3 n = Vector3.Cross(Vector3.up, dir.normalized) * (thick * 0.5f);
            Vector3[] q = { a - n, a + n, b + n, b - n };
            int bi = v.Count;
            foreach (var p in q) v.Add(p);
            foreach (var p in q) v.Add(p + Vector3.up * height);
            int[] faces = { 0, 4, 1, 1, 4, 5, 1, 5, 2, 2, 5, 6, 2, 6, 3, 3, 6, 7, 3, 7, 0, 0, 7, 4, 4, 7, 5, 5, 7, 6 };
            foreach (var f in faces) t.Add(bi + f);
        }

        private static GameObject MakeMesh(Ctx c, string name, Transform parent, List<Vector3>[] verts, List<int>[] tris, Material[] mats, bool collider)
        {
            var all = new List<Vector3>();
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            var offsets = new int[verts.Length];
            for (int s = 0; s < verts.Length; s++) { offsets[s] = all.Count; all.AddRange(verts[s]); }
            // Flat shading: duplicate per triangle.
            var flatV = new List<Vector3>(); var flatT = new List<int>[verts.Length];
            for (int s = 0; s < verts.Length; s++)
            {
                flatT[s] = new List<int>();
                for (int i = 0; i < tris[s].Count; i++) { flatT[s].Add(flatV.Count); flatV.Add(verts[s][tris[s][i]]); }
            }
            mesh.SetVertices(flatV);
            mesh.subMeshCount = verts.Length;
            for (int s = 0; s < verts.Length; s++) mesh.SetTriangles(flatT[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{Dir}/{c.track.sceneName}_Meshes/{name}.asset");
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        // ------------------------------------------------------------------ start / finish

        private static void BuildStart(Ctx c)
        {
            var parent = new GameObject("StartFinish").transform; parent.SetParent(c.root, false);
            Vector3 p = c.center[0];
            Vector3 fwd = Heading(c, 0);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            float hw = c.track.halfWidth;
            // Checkered line.
            Box(parent, "FinishLine", p + Vector3.up * 0.04f, rot, new Vector3(hw * 2f, 0.02f, 1.6f), c.mats["checker"], false, new Vector2(4f, 1f));
            // Gantry.
            float pillarX = hw + 1.6f;
            foreach (int s in new[] { -1, 1 })
                Box(parent, "Pillar", p + rot * new Vector3(s * pillarX, 3.6f, 0f), rot, new Vector3(0.9f, 7.2f, 0.9f), c.mats["gantry"], true);
            Box(parent, "Beam", p + rot * new Vector3(0f, 7.6f, 0f), rot, new Vector3(pillarX * 2f + 0.9f, 1.4f, 1f), c.mats["gantry"], false);
            Box(parent, "Banner", p + rot * new Vector3(0f, 7.6f, -0.52f), rot, new Vector3(pillarX * 2f - 0.6f, 1.0f, 0.05f), c.mats["checker"], false, new Vector2(6f, 1f));
            Box(parent, "BannerBack", p + rot * new Vector3(0f, 7.6f, 0.52f), rot, new Vector3(pillarX * 2f - 0.6f, 1.0f, 0.05f), c.mats["checker"], false, new Vector2(6f, 1f));
            Box(parent, "AccentL", p + rot * new Vector3(-pillarX, 7.2f, 0f), rot, new Vector3(1.0f, 0.25f, 1.05f), c.mats["accent"], false);
            Box(parent, "AccentR", p + rot * new Vector3(pillarX, 7.2f, 0f), rot, new Vector3(1.0f, 0.25f, 1.05f), c.mats["accent"], false);
            // Grid boxes behind the line (circuits start just behind it).
            int m = c.center.Count;
            for (int slot = 0; slot < 8; slot++)
            {
                int row = slot / 2, col = slot % 2;
                float dist = 7f + row * 9f;
                int idx = (m - Mathf.RoundToInt(dist / 2f)) % m;
                Vector3 cp = c.center[idx];
                var r = Quaternion.LookRotation(Heading(c, idx), Vector3.up);
                Vector3 lat = r * Vector3.right * ((col == 0 ? -1f : 1f) * Mathf.Min(2.6f, hw * 0.4f));
                Box(parent, "Grid", cp + lat + r * new Vector3(0f, 0.04f, 2.6f), r, new Vector3(2.4f, 0.015f, 0.18f), c.mats["lineWhite"], false);
            }
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material mat, bool collider, Vector2? tiling = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ lake

        private static void BuildLake(Ctx c)
        {
            var water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Stylized/Water/Materials/Beach.mat");
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Lake";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(c.root, false);
            go.transform.position = new Vector3(c.track.lakeCenter.x, c.track.waterY, c.track.lakeCenter.y);
            float s = c.track.lakeRadius * 2.3f / 10f;
            go.transform.localScale = new Vector3(s, 1f, s);
            go.GetComponent<MeshRenderer>().sharedMaterial = water ? water : Mat(c, "water", new Color(0.3f, 0.6f, 0.8f), 0.9f);
        }

        // ------------------------------------------------------------------ scenery

        private static void Scatter(Ctx c)
        {
            var parent = new GameObject("Scenery").transform; parent.SetParent(c.root, false);
            var items = c.theme.scenery;
            if (items == null || items.Length == 0) return;
            float total = items.Sum(i => i.weight);
            var prefabs = items.Select(i => AssetDatabase.LoadAssetAtPath<GameObject>(i.prefab)).ToArray();
            int placed = 0, tries = 0;
            while (placed < c.theme.sceneryCount && tries < c.theme.sceneryCount * 25)
            {
                tries++;
                // Bias toward the track: pick a random centre point and an offset distance.
                int ci = c.rnd.Next(c.center.Count);
                Vector3 cp = c.center[ci];
                float side = c.rnd.NextDouble() < 0.5 ? -1f : 1f;
                float dist = Mathf.Lerp(c.track.halfWidth + 6f, 260f, Mathf.Pow((float)c.rnd.NextDouble(), 1.8f));
                Vector3 right = Vector3.Cross(Vector3.up, Heading(c, ci));
                Vector3 p = cp + right * side * dist + new Vector3((float)c.rnd.NextDouble() - 0.5f, 0f, (float)c.rnd.NextDouble() - 0.5f) * 20f;
                double pick = c.rnd.NextDouble() * total;
                int idx = 0;
                for (float acc = 0f; idx < items.Length; idx++) { acc += items[idx].weight; if (pick <= acc) break; }
                idx = Mathf.Min(idx, items.Length - 1);
                if (prefabs[idx] == null) continue;
                float d = NearestRoad(c, p.x, p.z, out _);
                if (d < c.track.halfWidth + items[idx].clearance) continue;
                if (c.track.lakeRadius > 0f && Vector2.Distance(new Vector2(p.x, p.z), c.track.lakeCenter) < c.track.lakeRadius + 4f) continue;
                if (!c.bounds.Contains(new Vector2(p.x, p.z))) continue;
                p.y = c.heightAt(p.x, p.z) - 0.1f;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[idx], parent);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(0f, (float)c.rnd.NextDouble() * 360f, 0f);
                go.transform.localScale *= Mathf.Lerp(items[idx].minScale, items[idx].maxScale, (float)c.rnd.NextDouble());
                foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.DestroyImmediate(rb);
                placed++;
            }
        }

        // ------------------------------------------------------------------ theme decorations

        private static void DecorateSakura(Ctx c)
        {
            var parent = new GameObject("Sakura").transform; parent.SetParent(c.root, false);
            var vermilion = Mat(c, "torii", new Color(0.85f, 0.2f, 0.12f), 0.3f);
            var black = Mat(c, "toriiTop", new Color(0.1f, 0.1f, 0.12f), 0.3f);
            var stone = Mat(c, "stone", new Color(0.62f, 0.6f, 0.58f), 0.1f);
            var lamp = Mat(c, "lamp", new Color(1f, 0.85f, 0.6f), 0.1f);
            lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.35f) * 1.5f);
            int m = c.center.Count;
            float hw = c.track.halfWidth;
            // Torii gates over the road on straights.
            foreach (float frac in new[] { 0.12f, 0.43f, 0.71f })
            {
                int i = Mathf.RoundToInt(frac * m) % m;
                Vector3 p = c.center[i];
                var rot = Quaternion.LookRotation(Heading(c, i), Vector3.up);
                float px = hw + 2.2f;
                foreach (int s in new[] { -1, 1 })
                {
                    var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pillar.name = "ToriiPillar"; pillar.transform.SetParent(parent, false);
                    pillar.transform.SetPositionAndRotation(p + rot * new Vector3(s * px, 4.5f, 0f), rot);
                    pillar.transform.localScale = new Vector3(0.9f, 4.5f, 0.9f);
                    pillar.GetComponent<MeshRenderer>().sharedMaterial = vermilion;
                    pillar.isStatic = true;
                }
                Box(parent, "Nuki", p + rot * new Vector3(0f, 7.2f, 0f), rot, new Vector3(px * 2f + 1.6f, 0.6f, 0.6f), vermilion, false);
                Box(parent, "Kasagi", p + rot * new Vector3(0f, 8.9f, 0f), rot, new Vector3(px * 2f + 4.2f, 0.7f, 1.1f), black, false);
                Box(parent, "KasagiRed", p + rot * new Vector3(0f, 8.35f, 0f), rot, new Vector3(px * 2f + 3.4f, 0.5f, 0.9f), vermilion, false);
                Box(parent, "Gakuzuka", p + rot * new Vector3(0f, 7.9f, 0f), rot, new Vector3(0.6f, 1.2f, 0.4f), black, false);
            }
            // Stone lanterns along the road.
            for (int i = 0; i < m; i += 22)
            {
                if (Mathf.Abs(c.curvature[i]) > 0.01f) continue;
                Vector3 p = c.center[i];
                var rot = Quaternion.LookRotation(Heading(c, i), Vector3.up);
                int s = (i / 22) % 2 == 0 ? 1 : -1;
                Vector3 b = p + rot * new Vector3(s * (hw + 3.2f), 0f, 0f);
                b.y = c.heightAt(b.x, b.z);
                Box(parent, "LanternBase", b + Vector3.up * 0.4f, rot, new Vector3(0.9f, 0.8f, 0.9f), stone, true);
                Box(parent, "LanternPost", b + Vector3.up * 1.2f, rot, new Vector3(0.35f, 1.0f, 0.35f), stone, false);
                Box(parent, "LanternLight", b + Vector3.up * 1.95f, rot, new Vector3(0.7f, 0.55f, 0.7f), lamp, false);
                Box(parent, "LanternRoof", b + Vector3.up * 2.35f, rot * Quaternion.Euler(0f, 45f, 0f), new Vector3(1.1f, 0.25f, 1.1f), stone, false);
            }
            // Mount Fuji on the horizon.
            var fuji = Cone(c, "Fuji", 520f, 260f, 18, new Vector3(c.maxX + 650f, -10f, c.maxZ + 900f), c.mats["rock"], c.mats["snow"], 0.72f);
            fuji.transform.SetParent(parent, false);
        }

        private static void DecorateCanyon(Ctx c)
        {
            var parent = new GameObject("Canyon").transform; parent.SetParent(c.root, false);
            var bands = new[] { Mat(c, "mesaA", new Color(0.72f, 0.36f, 0.2f)), Mat(c, "mesaB", new Color(0.82f, 0.48f, 0.27f)), Mat(c, "mesaC", new Color(0.6f, 0.28f, 0.17f)) };
            var cactus = Mat(c, "cactus", new Color(0.3f, 0.52f, 0.25f));
            // Mesas: flat-topped rock towers, mostly away from the road.
            int placed = 0, tries = 0;
            while (placed < 46 && tries < 2000)
            {
                tries++;
                float x = Mathf.Lerp(c.bounds.xMin + 40f, c.bounds.xMax - 40f, (float)c.rnd.NextDouble());
                float z = Mathf.Lerp(c.bounds.yMin + 40f, c.bounds.yMax - 40f, (float)c.rnd.NextDouble());
                float d = NearestRoad(c, x, z, out _);
                float radius = Mathf.Lerp(14f, 48f, (float)c.rnd.NextDouble());
                if (d < c.track.halfWidth + radius + 18f) continue;
                float height = Mathf.Lerp(25f, 85f, (float)c.rnd.NextDouble()) * (d > 250f ? 1.4f : 1f);
                var go = Mesa(c, radius, height, new Vector3(x, c.heightAt(x, z) - 3f, z), bands);
                go.transform.SetParent(parent, true);
                placed++;
            }
            // Cacti near the road.
            for (int i = 0; i < 260; i++)
            {
                int ci = c.rnd.Next(c.center.Count);
                Vector3 right = Vector3.Cross(Vector3.up, Heading(c, ci));
                float side = c.rnd.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 p = c.center[ci] + right * side * Mathf.Lerp(c.track.halfWidth + 6f, 90f, (float)c.rnd.NextDouble());
                if (NearestRoad(c, p.x, p.z, out _) < c.track.halfWidth + 5f) continue;
                p.y = c.heightAt(p.x, p.z);
                Cactus(parent, p, Mathf.Lerp(2.5f, 5.5f, (float)c.rnd.NextDouble()), cactus, c.rnd);
            }
        }

        private static void DecorateFrost(Ctx c)
        {
            var parent = new GameObject("Frost").transform; parent.SetParent(c.root, false);
            var red = Mat(c, "poleRed", new Color(0.85f, 0.15f, 0.12f), 0.3f);
            var white = Mat(c, "poleWhite", new Color(0.95f, 0.95f, 0.95f), 0.3f);
            int m = c.center.Count;
            float hw = c.track.halfWidth;
            // Red/white snow marker poles along both edges.
            for (int i = 0; i < m; i += 10)
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 right = Vector3.Cross(Vector3.up, Heading(c, i));
                    Vector3 p = c.center[i] + right * side * (hw + 2.4f);
                    p.y = c.heightAt(p.x, p.z);
                    var rot = Quaternion.LookRotation(Heading(c, i), Vector3.up);
                    Box(parent, "PoleW", p + Vector3.up * 0.6f, rot, new Vector3(0.12f, 1.2f, 0.12f), white, false);
                    Box(parent, "PoleR", p + Vector3.up * 1.55f, rot, new Vector3(0.13f, 0.7f, 0.13f), red, false);
                }
            // Snow-capped peaks around the valley.
            var peaks = new[] { new Vector3(c.maxX + 520f, -20f, c.maxZ + 380f), new Vector3(c.minX - 600f, -20f, c.maxZ + 520f), new Vector3(c.minX - 420f, -20f, c.minZ - 520f), new Vector3(c.maxX + 300f, -20f, c.minZ - 640f) };
            for (int k = 0; k < peaks.Length; k++)
            {
                var cone = Cone(c, "Peak" + k, 380f + k * 40f, 300f + (k % 2) * 90f, 14 + k, peaks[k], c.mats["rock"], c.mats["snow"], 0.55f);
                cone.transform.SetParent(parent, true);
            }
        }

        private static GameObject Cone(Ctx c, string name, float radius, float height, int sides, Vector3 pos, Material rock, Material snow, float snowFrom)
        {
            var low = new List<Vector3>(); var lowT = new List<int>(); var high = new List<Vector3>(); var highT = new List<int>();
            var rnd = new System.Random(5);
            var ring = new Vector3[sides];
            var mid = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                float jr = 1f + ((float)rnd.NextDouble() - 0.5f) * 0.18f;
                ring[i] = new Vector3(Mathf.Cos(a) * radius * jr, 0f, Mathf.Sin(a) * radius * jr);
                mid[i] = new Vector3(ring[i].x * (1f - snowFrom), height * snowFrom * (0.92f + (float)rnd.NextDouble() * 0.12f), ring[i].z * (1f - snowFrom));
            }
            Vector3 top = new Vector3(0f, height, 0f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                int b = low.Count;
                low.AddRange(new[] { ring[i], mid[i], ring[j], ring[j], mid[i], mid[j] });
                lowT.AddRange(new[] { b, b + 1, b + 2, b + 3, b + 4, b + 5 });
                int hb = high.Count;
                high.AddRange(new[] { mid[i], top, mid[j] });
                highT.AddRange(new[] { hb, hb + 1, hb + 2 });
            }
            var go = MakeMesh(c, name, c.root, new[] { low, high }, new[] { lowT, highT }, new[] { rock, snow }, false);
            go.transform.position = pos;
            return go;
        }

        private static GameObject Mesa(Ctx c, float radius, float height, Vector3 pos, Material[] bands)
        {
            int sides = 7 + c.rnd.Next(5);
            var v = new List<Vector3>[bands.Length]; var t = new List<int>[bands.Length];
            for (int b = 0; b < bands.Length; b++) { v[b] = new List<Vector3>(); t[b] = new List<int>(); }
            var rad = new float[sides];
            for (int i = 0; i < sides; i++) rad[i] = radius * (0.75f + (float)c.rnd.NextDouble() * 0.5f);
            int layers = bands.Length;
            for (int L = 0; L < layers; L++)
            {
                float y0 = height * L / layers, y1 = height * (L + 1) / layers;
                float shrink0 = 1f - 0.12f * L, shrink1 = 1f - 0.12f * (L + 1) + 0.04f;
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    float ai = i / (float)sides * Mathf.PI * 2f, aj = j / (float)sides * Mathf.PI * 2f;
                    Vector3 a0 = new Vector3(Mathf.Cos(ai) * rad[i] * shrink0, y0, Mathf.Sin(ai) * rad[i] * shrink0);
                    Vector3 b0 = new Vector3(Mathf.Cos(aj) * rad[j] * shrink0, y0, Mathf.Sin(aj) * rad[j] * shrink0);
                    Vector3 a1 = new Vector3(Mathf.Cos(ai) * rad[i] * shrink1, y1, Mathf.Sin(ai) * rad[i] * shrink1);
                    Vector3 b1 = new Vector3(Mathf.Cos(aj) * rad[j] * shrink1, y1, Mathf.Sin(aj) * rad[j] * shrink1);
                    int bi = v[L].Count;
                    v[L].AddRange(new[] { a0, a1, b0, b0, a1, b1 });
                    t[L].AddRange(new[] { bi, bi + 1, bi + 2, bi + 3, bi + 4, bi + 5 });
                }
            }
            // Flat top.
            Vector3 centreTop = new Vector3(0f, height, 0f);
            float shrinkTop = 1f - 0.12f * layers + 0.04f;
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                float ai = i / (float)sides * Mathf.PI * 2f, aj = j / (float)sides * Mathf.PI * 2f;
                int bi = v[0].Count;
                v[0].AddRange(new[] { centreTop, new Vector3(Mathf.Cos(aj) * rad[j] * shrinkTop, height, Mathf.Sin(aj) * rad[j] * shrinkTop), new Vector3(Mathf.Cos(ai) * rad[i] * shrinkTop, height, Mathf.Sin(ai) * rad[i] * shrinkTop) });
                t[0].AddRange(new[] { bi, bi + 1, bi + 2 });
            }
            var go = MakeMesh(c, "Mesa_" + c.rnd.Next(100000), c.root, v, t, bands, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, (float)c.rnd.NextDouble() * 360f, 0f);
            return go;
        }

        private static void Cactus(Transform parent, Vector3 p, float h, Material mat, System.Random rnd)
        {
            var root = new GameObject("Cactus").transform;
            root.SetParent(parent, false);
            root.position = p;
            root.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            void Part(Vector3 lp, Vector3 s)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(root, false);
                g.transform.localPosition = lp; g.transform.localScale = s;
                g.GetComponent<MeshRenderer>().sharedMaterial = mat;
                g.isStatic = true;
            }
            Part(new Vector3(0f, h * 0.5f, 0f), new Vector3(0.7f, h * 0.5f, 0.7f));
            if (rnd.NextDouble() < 0.8) { Part(new Vector3(0.6f, h * 0.45f, 0f), new Vector3(0.45f, 0.5f, 0.45f)); Part(new Vector3(0.85f, h * 0.62f, 0f), new Vector3(0.42f, h * 0.18f, 0.42f)); }
            if (rnd.NextDouble() < 0.6) { Part(new Vector3(-0.6f, h * 0.35f, 0f), new Vector3(0.45f, 0.5f, 0.45f)); Part(new Vector3(-0.85f, h * 0.5f, 0f), new Vector3(0.42f, h * 0.15f, 0.42f)); }
            var col = root.gameObject.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, h * 0.5f, 0f); col.height = h; col.radius = 0.4f;
        }

        // ------------------------------------------------------------------ atmosphere, race layer, camera

        private static void Atmosphere(Ctx c)
        {
            var t = c.theme;
            string skyPath = $"{MatDir}/{t.key}_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
            sky.SetColor("_SkyTint", t.skyTint);
            sky.SetColor("_GroundColor", t.groundColor);
            sky.SetFloat("_AtmosphereThickness", t.atmosphere);
            sky.SetFloat("_Exposure", t.exposure);
            sky.SetFloat("_SunSize", 0.045f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = t.sun;
            sun.intensity = t.sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(t.sunEuler);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = t.ambientSky;
            RenderSettings.ambientEquatorColor = t.ambientEquator;
            RenderSettings.ambientGroundColor = t.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = t.fogDensity;
            RenderSettings.fogColor = t.fog;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            string profPath = $"{MatDir}/{t.key}_PostFX.asset";
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, profPath);
                var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
                var bloom = p.Add<Bloom>(true); bloom.intensity.Override(0.45f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
                var vig = p.Add<Vignette>(true); vig.intensity.Override(0.22f);
                var ca = p.Add<ColorAdjustments>(true); ca.saturation.Override(t.saturation); ca.contrast.Override(8f); ca.postExposure.Override(0.1f);
                foreach (var comp in p.components) { comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(comp, p); }
                EditorUtility.SetDirty(p);
            }
            var vol = new GameObject("Post FX").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = p;
        }

        private static void RaceLayer(Ctx c)
        {
            var race = new GameObject("Race");
            var path = race.AddComponent<TrackPath>();
            path.points = c.center.Where((p, i) => i % 2 == 0).Select(p => p + Vector3.up * 0.05f).ToList();
            path.loop = true;
            path.halfWidth = c.track.halfWidth;
            var rm = race.AddComponent<RaceManager>();
            rm.Path = path;
            rm.checkpointSpacing = 110f;
            rm.fallbackMapId = c.track.sceneName.ToLower().Replace(' ', '_');
            // Ambient bed.
            var amb = new GameObject("Ambience").AddComponent<AudioSource>();
            amb.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/forest.wav");
            amb.loop = true; amb.playOnAwake = true; amb.volume = 0.25f; amb.spatialBlend = 0f;
        }

        private static Camera MakeCamera(Ctx c)
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 2500f; cam.fieldOfView = 60f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Vector3 p = c.center[0];
            Vector3 fwd = Heading(c, 0);
            camGo.transform.position = p - fwd * 30f + Vector3.up * 14f + Vector3.Cross(Vector3.up, fwd) * 18f;
            camGo.transform.LookAt(p + fwd * 40f);
            return cam;
        }

        private static void Thumbnail(Ctx c, Camera cam)
        {
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            string path = $"{Dir}/{c.track.sceneName}_thumb.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
        }
    }
}
