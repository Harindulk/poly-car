using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PolyCar.Editor
{
    /// <summary>
    /// Builds the main menu dioramas: a floating square of a track's world (faceted terrain, layered earth sides,
    /// an S-bend of road with gravel verges and rally marker poles, themed trees and props) with a parking spot
    /// for the selected car. One per track theme; the menu shows the one for the selected track.
    /// </summary>
    public static class DioramaBuilder
    {
        private const string MatDir = "Assets/PolyCar/Materials/Diorama";
        private const float Half = 21f, Cell = 1.5f, Depth = 7f, RoadHalf = 3.3f, Verge = 1.3f;

        public class Look
        {
            public string mapId, key;
            public Color grassA, grassB, verge, road, roadLine, soil, rockA, rockB;
            public bool lines = true;
            public Color background, sun, ambientSky, ambientEquator, ambientGround;
            public Vector3 sunEuler = new Vector3(40f, -40f, 0f);
            public float sunIntensity = 1.3f;
            /// <summary>Prefab, pick weight, and the height range (m) it is scaled to, whatever its native size.</summary>
            public (string prefab, float weight, float minHeight, float maxHeight)[] trees;
            public int treeCount = 34;
            public System.Action<Transform, Look, System.Random> extras;
            public int seed;
        }

        public static List<Look> Looks()
        {
            var sakura = MapBuilder.SakuraTheme();
            var frost = MapBuilder.FrostTheme();
            var canyon = MapBuilder.CanyonTheme();
            return new List<Look>
            {
                new Look
                {
                    mapId = "green_forest", key = "forest", seed = 3,
                    grassA = new Color(0.36f, 0.55f, 0.26f), grassB = new Color(0.42f, 0.6f, 0.29f), verge = new Color(0.6f, 0.52f, 0.4f),
                    road = new Color(0.25f, 0.26f, 0.29f), roadLine = new Color(0.92f, 0.9f, 0.82f),
                    soil = new Color(0.42f, 0.3f, 0.2f), rockA = new Color(0.52f, 0.45f, 0.38f), rockB = new Color(0.4f, 0.36f, 0.33f),
                    background = new Color(0.78f, 0.86f, 0.84f), sun = new Color(1f, 0.93f, 0.82f), sunEuler = new Vector3(42f, -150f, 0f),
                    ambientSky = new Color(0.66f, 0.74f, 0.8f), ambientEquator = new Color(0.62f, 0.68f, 0.6f), ambientGround = new Color(0.3f, 0.32f, 0.26f),
                    trees = new[] { ("Assets/Prefabs/LowPoly - FirTree A.prefab", 3f, 6f, 9.5f), ("Assets/Prefabs/LowPoly - FirTree B.prefab", 3f, 6f, 9.5f), ("Assets/Prefabs/LowPoly - Rock A.prefab", 0.8f, 0.8f, 1.6f) },
                    extras = ForestExtras,
                },
                new Look
                {
                    mapId = "sakura_ring", key = "sakura", seed = 7,
                    grassA = sakura.grassA, grassB = sakura.grassB, verge = sakura.dirt, road = new Color(0.27f, 0.27f, 0.31f), roadLine = new Color(0.95f, 0.93f, 0.9f),
                    soil = new Color(0.45f, 0.33f, 0.25f), rockA = sakura.rock, rockB = sakura.rock * 0.8f,
                    background = new Color(0.93f, 0.86f, 0.88f), sun = sakura.sun, sunEuler = new Vector3(38f, -120f, 0f), sunIntensity = 1.35f,
                    ambientSky = sakura.ambientSky, ambientEquator = sakura.ambientEquator, ambientGround = sakura.ambientGround,
                    trees = Trees(sakura), extras = SakuraExtras,
                },
                new Look
                {
                    mapId = "frost_peak", key = "frost", seed = 33, lines = false,
                    grassA = frost.grassA, grassB = frost.grassB, verge = new Color(0.78f, 0.8f, 0.86f), road = new Color(0.62f, 0.64f, 0.7f), roadLine = Color.white,
                    soil = new Color(0.4f, 0.36f, 0.36f), rockA = frost.rock, rockB = frost.rock * 0.82f,
                    background = new Color(0.8f, 0.85f, 0.93f), sun = frost.sun, sunEuler = new Vector3(24f, -70f, 0f), sunIntensity = 1.25f,
                    ambientSky = frost.ambientSky, ambientEquator = frost.ambientEquator, ambientGround = frost.ambientGround,
                    trees = Trees(frost), extras = FrostExtras,
                },
                new Look
                {
                    mapId = "dust_canyon", key = "canyon", seed = 21, lines = false, treeCount = 18,
                    grassA = canyon.grassA, grassB = canyon.grassB, verge = canyon.dirt, road = new Color(0.78f, 0.6f, 0.42f), roadLine = Color.white,
                    soil = new Color(0.7f, 0.42f, 0.26f), rockA = canyon.rock, rockB = new Color(0.78f, 0.5f, 0.32f),
                    background = new Color(0.96f, 0.86f, 0.72f), sun = canyon.sun, sunEuler = new Vector3(34f, 60f, 0f), sunIntensity = 1.45f,
                    ambientSky = canyon.ambientSky, ambientEquator = canyon.ambientEquator, ambientGround = canyon.ambientGround,
                    trees = Trees(canyon), extras = CanyonExtras,
                },
            };
        }

        private static (string, float, float, float)[] Trees(MapBuilder.Theme t)
        {
            var list = new List<(string, float, float, float)>();
            foreach (var s in t.scenery)
            {
                string n = s.prefab.ToLower();
                var h = n.Contains("rock") ? (0.9f, 2.4f) : n.Contains("shrub") ? (1.2f, 2.2f) : (5.5f, 9f);
                list.Add((s.prefab, s.weight, h.Item1, h.Item2));
            }
            return list.ToArray();
        }

        // ------------------------------------------------------------------ geometry

        private static float RoadZ(float x) => 5f * Mathf.Sin(x * 0.085f + 0.6f) - 2f;

        private static float RoadDist(float x, float z)
        {
            float best = float.MaxValue;
            for (float sx = -Half - 4f; sx <= Half + 4f; sx += 0.5f)
            {
                float dx = x - sx, dz = z - RoadZ(sx);
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return Mathf.Sqrt(best);
        }

        private static float Hill(float x, float z, float cx, float cz, float h, float r)
        {
            float d2 = (x - cx) * (x - cx) + (z - cz) * (z - cz);
            return h * Mathf.Exp(-d2 / (r * r));
        }

        private static float Height(float x, float z, int seed)
        {
            float n = (Mathf.PerlinNoise(x * 0.09f + seed * 3.1f, z * 0.09f + seed * 1.7f) - 0.5f) * 2.6f
                      + Hill(x, z, -12f, 12f, 4.5f, 8f) + Hill(x, z, 14f, -14f, 2.6f, 7f) + Hill(x, z, 15f, 13f, 1.6f, 6f);
            float t = Mathf.SmoothStep(0f, 1f, (RoadDist(x, z) - RoadHalf - Verge) / 5f);
            return Mathf.Lerp(-0.06f, Mathf.Max(n, -0.4f), t);
        }

        public static Transform Build(Look look, Transform parent)
        {
            var root = new GameObject("Diorama_" + look.key).transform;
            root.SetParent(parent, false);
            var rnd = new System.Random(look.seed);

            Material grassA = Mat(look.key + "_grassA", look.grassA), grassB = Mat(look.key + "_grassB", look.grassB);
            Material verge = Mat(look.key + "_verge", look.verge), road = Mat(look.key + "_road", look.road, 0.15f), line = Mat(look.key + "_line", look.roadLine, 0.2f);
            Material soil = Mat(look.key + "_soil", look.soil), rockA = Mat(look.key + "_rockA", look.rockA), rockB = Mat(look.key + "_rockB", look.rockB);

            // Terrain top: faceted grid (flat-shaded triangles), interior points jittered so it doesn't look gridded.
            int n = Mathf.RoundToInt(Half * 2f / Cell);
            var pts = new Vector3[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float x = -Half + i * Cell, z = -Half + j * Cell;
                    if (i > 0 && i < n && j > 0 && j < n) { x += ((float)rnd.NextDouble() - 0.5f) * 0.6f; z += ((float)rnd.NextDouble() - 0.5f) * 0.6f; }
                    pts[i, j] = new Vector3(x, Height(x, z, look.seed), z);
                }
            var top = new MeshParts(3);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    Vector3 a = pts[i, j], b = pts[i + 1, j], c = pts[i + 1, j + 1], d = pts[i, j + 1];
                    TopTri(top, a, d, c, rnd);
                    TopTri(top, a, c, b, rnd);
                }
            MakeMesh("Terrain", root, top, new[] { grassA, grassB, verge }, true);

            // Earth sides in bands (soil, two rock strata) and the underside.
            var sides = new MeshParts(3);
            for (int e = 0; e < 4; e++)
                for (int k = 0; k < n; k++)
                {
                    Vector3 p0, p1, outward;
                    switch (e)
                    {
                        case 0: p0 = pts[k, 0]; p1 = pts[k + 1, 0]; outward = Vector3.back; break;
                        case 1: p0 = pts[n, k]; p1 = pts[n, k + 1]; outward = Vector3.right; break;
                        case 2: p0 = pts[k, n]; p1 = pts[k + 1, n]; outward = Vector3.forward; break;
                        default: p0 = pts[0, k]; p1 = pts[0, k + 1]; outward = Vector3.left; break;
                    }
                    float s0 = -1.1f + Mathf.Sin(k * 1.3f + e) * 0.25f, s1 = -1.1f + Mathf.Sin((k + 1) * 1.3f + e) * 0.25f;
                    Quad(sides, 0, p0, p1, Y(p1, s1), Y(p0, s0), outward);
                    Quad(sides, 1, Y(p0, s0), Y(p1, s1), Y(p1, -3.8f), Y(p0, -3.8f), outward);
                    Quad(sides, 2, Y(p0, -3.8f), Y(p1, -3.8f), Y(p1, -Depth), Y(p0, -Depth), outward);
                }
            Quad(sides, 2, new Vector3(-Half, -Depth, -Half), new Vector3(Half, -Depth, -Half), new Vector3(Half, -Depth, Half), new Vector3(-Half, -Depth, Half), Vector3.down);
            MakeMesh("Sides", root, sides, new[] { soil, rockA, rockB }, false);

            // Road ribbon with verges (and centre dashes on tarmac themes).
            var rd = new MeshParts(3);
            float step = 1f;
            for (float x = -Half; x < Half - 0.01f; x += step)
            {
                float x1 = Mathf.Min(Half, x + step);
                Vector3 c0 = new Vector3(x, 0f, RoadZ(x)), c1 = new Vector3(x1, 0f, RoadZ(x1));
                Vector3 r0 = Right(x), r1 = Right(x1);
                Strip(rd, 0, c0, c1, r0, r1, -RoadHalf, RoadHalf, 0.03f);
                Strip(rd, 1, c0, c1, r0, r1, RoadHalf, RoadHalf + Verge, 0.015f);
                Strip(rd, 1, c0, c1, r0, r1, -RoadHalf - Verge, -RoadHalf, 0.015f);
                if (look.lines && Mathf.Repeat(x, 4f) < 2f) Strip(rd, 2, c0, c1, r0, r1, -0.09f, 0.09f, 0.04f);
            }
            MakeMesh("Road", root, rd, new[] { road, verge, line }, false);

            // Rally marker poles along the verges.
            var pole = Mat("pole_white", new Color(0.95f, 0.95f, 0.93f), 0.3f);
            var cap = Mat("pole_red", new Color(0.86f, 0.18f, 0.12f), 0.3f);
            for (float x = -Half + 3f; x < Half - 2f; x += 7f)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = new Vector3(x, 0f, RoadZ(x)) + Right(x) * (side * (RoadHalf + Verge + 0.5f));
                    Prim(PrimitiveType.Cylinder, root, "Pole", p + Vector3.up * 0.6f, Quaternion.identity, new Vector3(0.1f, 0.6f, 0.1f), pole);
                    Prim(PrimitiveType.Cylinder, root, "PoleCap", p + Vector3.up * 1.12f, Quaternion.identity, new Vector3(0.11f, 0.1f, 0.11f), cap);
                }

            // The car's parking spot: right lane, facing along the road.
            var anchor = new GameObject("CarAnchor").transform;
            anchor.SetParent(root, false);
            float ax = 2f;
            anchor.position = new Vector3(ax, 0.03f, RoadZ(ax)) + Right(ax) * 1.4f;
            anchor.rotation = Quaternion.LookRotation(Tangent(ax), Vector3.up);

            // Trees and rocks, clear of the road, the edges and the car.
            float total = 0f;
            foreach (var t in look.trees) total += t.weight;
            int placed = 0;
            for (int tries = 0; tries < look.treeCount * 30 && placed < look.treeCount; tries++)
            {
                float x = Mathf.Lerp(-Half + 1.6f, Half - 1.6f, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-Half + 1.6f, Half - 1.6f, (float)rnd.NextDouble());
                if (RoadDist(x, z) < RoadHalf + Verge + 2f) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(anchor.position.x, anchor.position.z)) < 11f) continue;
                float pick = (float)rnd.NextDouble() * total;
                var choice = look.trees[0];
                foreach (var t in look.trees) { if (pick < t.weight) { choice = t; break; } pick -= t.weight; }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(choice.prefab);
                if (!prefab) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                go.transform.position = new Vector3(x, Height(x, z, look.seed) - 0.1f, z);
                go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                float height = Mathf.Lerp(choice.minHeight, choice.maxHeight, (float)rnd.NextDouble());
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    if (b.size.y > 0.01f) go.transform.localScale *= height / b.size.y;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                placed++;
            }
            look.extras?.Invoke(root, look, rnd);
            foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            return root;
        }

        private static Vector3 Y(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        private static Vector3 Tangent(float x)
        {
            float dz = (RoadZ(x + 0.1f) - RoadZ(x - 0.1f)) / 0.2f;
            return new Vector3(1f, 0f, dz).normalized;
        }

        private static Vector3 Right(float x) => Vector3.Cross(Vector3.up, Tangent(x)).normalized;

        // ------------------------------------------------------------------ themed props

        private static void ForestExtras(Transform root, Look look, System.Random rnd)
        {
            var wood = Mat("log", new Color(0.45f, 0.3f, 0.18f));
            var hay = Mat("hay", new Color(0.86f, 0.74f, 0.42f));
            Vector3 p = new Vector3(-6f, 0f, RoadZ(-6f)) + Right(-6f) * (RoadHalf + Verge + 2.5f);
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cylinder, root, "Log", p + new Vector3(0f, 0.3f + (i == 2 ? 0.5f : 0f), i == 2 ? 0f : (i - 0.5f) * 0.6f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.55f, 1.6f, 0.55f), wood);
            for (int i = 0; i < 2; i++)
            {
                Vector3 q = new Vector3(9f + i * 1.6f, 0.55f, RoadZ(9f + i * 1.6f)) - Right(9f) * (RoadHalf + Verge + 1.6f);
                Prim(PrimitiveType.Cylinder, root, "HayBale", q, Quaternion.Euler(90f, 0f, 0f), new Vector3(1.1f, 0.55f, 1.1f), hay);
            }
        }

        private static void SakuraExtras(Transform root, Look look, System.Random rnd)
        {
            var red = Mat("torii_red", new Color(0.82f, 0.16f, 0.1f), 0.3f);
            var black = Mat("torii_black", new Color(0.12f, 0.1f, 0.1f), 0.3f);
            float x = -11f;
            Vector3 c = new Vector3(x, 0f, RoadZ(x));
            Vector3 r = Right(x);
            var rot = Quaternion.LookRotation(Tangent(x));
            float w = RoadHalf + Verge + 0.6f;
            foreach (int s in new[] { -1, 1 })
                Prim(PrimitiveType.Cylinder, root, "ToriiPost", c + r * (s * w) + Vector3.up * 2.6f, Quaternion.identity, new Vector3(0.45f, 2.6f, 0.45f), red);
            Prim(PrimitiveType.Cube, root, "ToriiBeam", c + Vector3.up * 4.4f, rot, new Vector3(w * 2f + 1.2f, 0.35f, 0.45f), red);
            Prim(PrimitiveType.Cube, root, "ToriiTop", c + Vector3.up * 5.25f, rot, new Vector3(w * 2f + 2.4f, 0.4f, 0.6f), black);
        }

        private static void FrostExtras(Transform root, Look look, System.Random rnd)
        {
            var snow = Mat("snowbank", new Color(0.97f, 0.98f, 1f));
            for (float x = -Half + 2f; x < Half - 2f; x += 5f)
                foreach (int s in new[] { -1, 1 })
                {
                    Vector3 p = new Vector3(x, 0.2f, RoadZ(x)) + Right(x) * (s * (RoadHalf + Verge + 1.4f));
                    Prim(PrimitiveType.Sphere, root, "SnowBank", p, Quaternion.LookRotation(Tangent(x)), new Vector3(1.6f, 0.7f, 4.2f), snow);
                }
        }

        private static void CanyonExtras(Transform root, Look look, System.Random rnd)
        {
            var bands = new[] { Mat(look.key + "_rockA", look.rockA), Mat(look.key + "_rockB", look.rockB), Mat(look.key + "_soil", look.soil) };
            foreach (var spot in new[] { new Vector3(-13f, 0f, 12f), new Vector3(14f, 0f, -13f) })
            {
                float y = Height(spot.x, spot.z, look.seed) - 0.3f;
                float radius = 4.2f;
                for (int i = 0; i < 4; i++)
                {
                    float h = 1.6f - i * 0.15f;
                    Prim(PrimitiveType.Cylinder, root, "Mesa", new Vector3(spot.x, y + h * 0.5f, spot.z), Quaternion.Euler(0f, i * 23f, 0f), new Vector3(radius * 2f, h * 0.5f, radius * 2f), bands[i % 3]);
                    y += h;
                    radius *= 0.9f;
                }
            }
        }

        // ------------------------------------------------------------------ mesh helpers

        private class MeshParts
        {
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<int>[] subs;
            public MeshParts(int count) { subs = new List<int>[count]; for (int i = 0; i < count; i++) subs[i] = new List<int>(); }
            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
            }
        }

        private static void TopTri(MeshParts m, Vector3 a, Vector3 b, Vector3 c, System.Random rnd)
        {
            Vector3 mid = (a + b + c) / 3f;
            int sub = RoadDist(mid.x, mid.z) < RoadHalf + Verge + 0.8f ? 2 : rnd.NextDouble() < 0.5 ? 0 : 1;
            if (Vector3.Cross(b - a, c - a).y < 0f) (b, c) = (c, b);
            m.Tri(sub, a, b, c);
        }

        private static void Quad(MeshParts m, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { (b, d) = (d, b); }
            m.Tri(sub, a, b, c);
            m.Tri(sub, a, c, d);
        }

        private static void Strip(MeshParts m, int sub, Vector3 c0, Vector3 c1, Vector3 r0, Vector3 r1, float from, float to, float y)
        {
            Vector3 up = Vector3.up * y;
            Quad(m, sub, c0 + r0 * from + up, c0 + r0 * to + up, c1 + r1 * to + up, c1 + r1 * from + up, Vector3.up);
        }

        private static void MakeMesh(string name, Transform parent, MeshParts parts, Material[] mats, bool collider)
        {
            var mesh = new Mesh { name = parent.name + "_" + name };
            if (parts.verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(parts.verts);
            mesh.subMeshCount = parts.subs.Length;
            for (int i = 0; i < parts.subs.Length; i++) mesh.SetTriangles(parts.subs[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string dir = MatDir + "/Meshes";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(MatDir, "Meshes");
            string path = dir + "/" + mesh.name + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static Material Mat(string name, Color color, float smooth = 0.05f)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/PolyCar/Materials", "Diorama");
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
