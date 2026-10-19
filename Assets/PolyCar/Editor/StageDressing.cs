using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PolyCar.Editor
{
    /// <summary>
    /// Roadside dressing generated along a stage's TrackPath, the way art of rally dresses its stages with road
    /// tools: knockable marker poles every 20 m on both sides, spectator crowds (cube people, as in art of rally)
    /// behind tape on the outside of the biggest corners, and hay bales lining the tightest bends. Poles and bales
    /// are sleeping rigidbodies on the Ignore Raycast layer, so wheels and the camera don't ride on them.
    /// Shared flat-colour palette materials with GPU instancing.
    /// </summary>
    public static class StageDressing
    {
        private const string SharedDir = "Assets/PolyCar/Maps/Shared";
        private const int PropLayer = 2; // Ignore Raycast: game raycasts use Physics.DefaultRaycastLayers

        private static readonly Color[] Shirts =
        {
            new Color(0.86f, 0.2f, 0.16f), new Color(0.15f, 0.36f, 0.72f), new Color(0.95f, 0.78f, 0.2f), new Color(0.2f, 0.55f, 0.3f),
            new Color(0.93f, 0.93f, 0.9f), new Color(0.22f, 0.22f, 0.26f), new Color(0.95f, 0.5f, 0.15f),
        };

        public struct KeepOut { public Vector3 center; public float radius; }

        /// <summary>Dresses the stage; returns areas that scenery scattering must keep clear.</summary>
        public static List<KeepOut> Dress(TrackPath path, Transform parent, float verge, int crowds, int hayCorners, int seed, string meshDir)
        {
            path.Build();
            Physics.SyncTransforms();
            var rnd = new System.Random(seed);
            var keep = new List<KeepOut>();
            var root = new GameObject("Dressing").transform;
            root.SetParent(parent, false);

            Material white = Mat("Prop_White", new Color(0.94f, 0.94f, 0.92f)), red = Mat("Prop_Red", new Color(0.8f, 0.14f, 0.12f));
            Material straw = Mat("Prop_Straw", new Color(0.86f, 0.73f, 0.4f)), wood = Mat("Prop_Wood", new Color(0.36f, 0.27f, 0.17f));
            var shirtMats = new Material[Shirts.Length];
            for (int i = 0; i < Shirts.Length; i++) shirtMats[i] = Mat("Person_" + i, Shirts[i]);
            Mesh pole = PoleMesh(), bale = BaleMesh();

            // Marker poles.
            var poles = new GameObject("MarkerPoles").transform; poles.SetParent(root, false);
            float len = path.loop ? path.Length : path.Length - 10f;
            for (float d = 10f; d < len; d += 20f)
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 p = Edge(path, d, side, path.HalfWidthAt(d) + verge + 0.4f);
                    if (!Ground(p, out Vector3 g) || TooCloseToRoad(path, g, path.HalfWidthAt(d) + verge)) continue;
                    var go = Prop("Pole", poles, pole, new[] { white, red }, g, Quaternion.Euler(0f, rnd.Next(360), 0f));
                    var col = go.AddComponent<CapsuleCollider>(); col.radius = 0.09f; col.height = 1.3f; col.center = new Vector3(0f, 0.65f, 0f);
                    Body(go, 4f);
                }

            // Corners by sharpness, at least 160 m apart.
            var corners = new List<(float d, float k)>();
            for (float d = 30f; d < len - 30f; d += 4f) corners.Add((d, Curvature(path, d)));
            corners.Sort((a, b) => Mathf.Abs(b.k).CompareTo(Mathf.Abs(a.k)));
            var picked = new List<(float d, float k)>();
            foreach (var c in corners)
            {
                if (Mathf.Abs(c.k) < 0.006f || picked.Count >= Mathf.Max(crowds, hayCorners)) break;
                bool near = false;
                foreach (var p in picked) if (Mathf.Abs(Wrap(path, p.d - c.d)) < 160f) near = true;
                if (!near) picked.Add(c);
            }

            // Hay bales along the outside of the tightest bends.
            var bales = new GameObject("HayBales").transform; bales.SetParent(root, false);
            for (int i = 0; i < picked.Count && i < hayCorners; i++)
            {
                var (cd, k) = picked[i];
                int outside = k > 0f ? 1 : -1;   // left-hand turn: outside is the right
                for (float o = -12f; o <= 12f; o += 2.7f)
                {
                    float d = Wrap(path, cd + o);
                    Vector3 p = Edge(path, d, outside, path.HalfWidthAt(d) + verge + 1.4f);
                    if (!Ground(p, out Vector3 g) || TooCloseToRoad(path, g, path.HalfWidthAt(d) + verge + 0.6f)) continue;
                    var rot = Quaternion.LookRotation(path.DirectionAt(d), Vector3.up);
                    var go = Prop("HayBale", bales, bale, new[] { straw }, g + Vector3.up * 0.6f, rot);
                    var col = go.AddComponent<CapsuleCollider>(); col.radius = 0.6f; col.height = 1.3f; col.direction = 2;
                    Body(go, 90f);
                }
            }

            // Spectators: a crowd behind tape on the outside of the big corners.
            var crowdRoot = new GameObject("Crowds").transform; crowdRoot.SetParent(root, false);
            for (int i = 0; i < picked.Count && i < crowds; i++)
            {
                var (cd, k) = picked[i];
                int outside = k > 0f ? 1 : -1;
                float hw = path.HalfWidthAt(cd);
                float front = hw + verge + 5f;
                Crowd(path, crowdRoot, cd, outside, front, rnd, shirtMats, white, red, wood, keep, meshDir);
            }
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.GetComponent<Rigidbody>() == null && t.GetComponentInParent<Rigidbody>() == null) t.gameObject.isStatic = true;
            return keep;
        }

        private static void Crowd(TrackPath path, Transform parent, float cd, int side, float front, System.Random rnd, Material[] shirts, Material white, Material red, Material wood, List<KeepOut> keep, string meshDir)
        {
            var verts = new List<Vector3>[shirts.Length];
            var tris = new List<int>[shirts.Length];
            for (int i = 0; i < shirts.Length; i++) { verts[i] = new List<Vector3>(); tris[i] = new List<int>(); }
            var go = new GameObject("Crowd");
            go.transform.SetParent(parent, false);
            Vector3 centre = Edge(path, cd, side, front + 1.5f);
            go.transform.position = centre;
            int people = 0;
            for (float o = -11f; o <= 11f; o += 0.75f)
                for (int row = 0; row < 3; row++)
                {
                    if (rnd.NextDouble() < 0.18) continue;
                    float d = Wrap(path, cd + o + (float)(rnd.NextDouble() - 0.5) * 0.4f);
                    Vector3 p = Edge(path, d, side, front + 0.8f + row * 0.9f + (float)rnd.NextDouble() * 0.3f);
                    if (!Ground(p, out Vector3 g) || TooCloseToRoad(path, g, front - 0.5f)) continue;
                    if (Physics.CheckBox(g + Vector3.up * 1.1f, new Vector3(0.3f, 0.6f, 0.3f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    float h = Mathf.Lerp(1.5f, 1.9f, (float)rnd.NextDouble());
                    Vector3 face = -SideDir(path, d, side);
                    var rot = Quaternion.LookRotation(face, Vector3.up) * Quaternion.Euler(0f, (float)(rnd.NextDouble() - 0.5) * 50f, 0f);
                    int s = rnd.Next(shirts.Length);
                    AddBox(verts[s], tris[s], g - centre + Vector3.up * (h * 0.5f), rot, new Vector3(0.46f, h, 0.3f));
                    people++;
                }
            if (people == 0) { Object.DestroyImmediate(go); return; }
            var mesh = new Mesh { name = "Crowd" };
            var all = new List<Vector3>();
            mesh.subMeshCount = shirts.Length;
            var subTris = new List<int>[shirts.Length];
            for (int s = 0; s < shirts.Length; s++) { int off = all.Count; all.AddRange(verts[s]); subTris[s] = tris[s].ConvertAll(t => t + off); }
            mesh.SetVertices(all);
            for (int s = 0; s < shirts.Length; s++) mesh.SetTriangles(subTris[s], s);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            SaveMesh(mesh, $"Crowd_{Mathf.RoundToInt(cd)}", meshDir);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = shirts;
            keep.Add(new KeepOut { center = centre, radius = 16f });

            // Tape on two posts in front of the crowd.
            Vector3 a = Edge(path, Wrap(path, cd - 11.5f), side, front), b = Edge(path, Wrap(path, cd + 11.5f), side, front);
            if (Ground(a, out a) && Ground(b, out b))
            {
                var tape = new GameObject("Tape"); tape.transform.SetParent(parent, false);
                foreach (var p in new[] { a, b })
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.name = "TapePost"; post.transform.SetParent(tape.transform, false);
                    post.transform.position = p + Vector3.up * 0.5f; post.transform.localScale = new Vector3(0.08f, 1f, 0.08f);
                    post.GetComponent<Renderer>().sharedMaterial = wood; Object.DestroyImmediate(post.GetComponent<Collider>());
                }
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                line.name = "TapeLine"; line.transform.SetParent(tape.transform, false);
                Vector3 mid = (a + b) * 0.5f + Vector3.up * 0.85f;
                line.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(b - a, Vector3.up));
                line.transform.localScale = new Vector3(0.02f, 0.06f, Vector3.Distance(a, b));
                line.GetComponent<Renderer>().sharedMaterial = red; Object.DestroyImmediate(line.GetComponent<Collider>());
            }
        }

        /// <summary>
        /// Removes terrain trees whose canopy would overhang the road (art of rally's road masks clear trees within
        /// about 4 m of the edge), so the high rally camera isn't looking through foliage. Returns trees removed.
        /// </summary>
        public static int ClearTerrainTreesNearRoad(TrackPath path, float edgeMargin)
        {
            path.Build();
            const float cell = 16f;
            var grid = new Dictionary<(int, int), List<Vector3>>();
            foreach (var q in path.points)
            {
                var key = (Mathf.FloorToInt(q.x / cell), Mathf.FloorToInt(q.z / cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Vector3>();
                list.Add(q);
            }
            int removed = 0;
            var done = new HashSet<TerrainData>();
            foreach (var t in Object.FindObjectsByType<Terrain>())
            {
                var data = t.terrainData;
                if (data == null || !done.Add(data)) continue;
                var radius = new float[data.treePrototypes.Length];
                for (int i = 0; i < radius.Length; i++)
                {
                    var prefab = data.treePrototypes[i].prefab;
                    if (!prefab) continue;
                    var tmp = Object.Instantiate(prefab);
                    var rs = tmp.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); radius[i] = Mathf.Max(b.extents.x, b.extents.z); }
                    Object.DestroyImmediate(tmp);
                }
                Vector3 size = data.size, origin = t.transform.position;
                var keep = new List<TreeInstance>(data.treeInstanceCount);
                foreach (var tree in data.treeInstances)
                {
                    Vector3 w = origin + Vector3.Scale(tree.position, size);
                    float reach = path.halfWidth + edgeMargin + radius[tree.prototypeIndex] * tree.widthScale * 0.8f;
                    if (NearAny(grid, cell, w, reach)) { removed++; continue; }
                    keep.Add(tree);
                }
                if (keep.Count != data.treeInstanceCount)
                {
                    data.SetTreeInstances(keep.ToArray(), false);
                    EditorUtility.SetDirty(data);
                }
            }
            return removed;
        }

        private static bool NearAny(Dictionary<(int, int), List<Vector3>> grid, float cell, Vector3 p, float reach)
        {
            int r = Mathf.CeilToInt(reach / cell);
            int cx = Mathf.FloorToInt(p.x / cell), cz = Mathf.FloorToInt(p.z / cell);
            float r2 = reach * reach;
            for (int i = -r; i <= r; i++)
                for (int j = -r; j <= r; j++)
                    if (grid.TryGetValue((cx + i, cz + j), out var list))
                        foreach (var q in list)
                        {
                            float dx = q.x - p.x, dz = q.z - p.z;
                            if (dx * dx + dz * dz < r2) return true;
                        }
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private static float Wrap(TrackPath p, float d) => p.loop ? Mathf.Repeat(d, p.Length) : Mathf.Clamp(d, 0f, p.Length);

        private static Vector3 SideDir(TrackPath p, float d, int side) => Vector3.Cross(Vector3.up, p.DirectionAt(d)).normalized * side;

        private static Vector3 Edge(TrackPath p, float d, int side, float offset) => p.PointAt(d) + SideDir(p, d, side) * offset;

        /// <summary>Signed turn rate in rad/m (positive = turning left).</summary>
        private static float Curvature(TrackPath p, float d)
        {
            Vector3 a = p.DirectionAt(Wrap(p, d - 8f)), b = p.DirectionAt(Wrap(p, d + 8f));
            return -Vector3.SignedAngle(a, b, Vector3.up) * Mathf.Deg2Rad / 16f;
        }

        /// <summary>True when the spot is within this distance of any part of the road (e.g. another leg of the track).</summary>
        private static bool TooCloseToRoad(TrackPath p, Vector3 g, float min)
        {
            for (int i = 0; i < p.points.Count; i++)
            {
                Vector3 q = p.points[i];
                float dx = q.x - g.x, dz = q.z - g.z;
                if (dx * dx + dz * dz < min * min) return true;
            }
            return false;
        }

        private static bool Ground(Vector3 p, out Vector3 g)
        {
            g = p;
            if (!Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var hit, 120f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > 35f) return false;
            g = hit.point;
            return true;
        }

        private static GameObject Prop(string name, Transform parent, Mesh mesh, Material[] mats, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name) { layer = PropLayer };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        private static void Body(GameObject go, float mass)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.6f;
            rb.sleepThreshold = 0.2f;
        }

        private static void AddBox(List<Vector3> v, List<int> t, Vector3 c, Quaternion r, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] q =
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            int[][] faces = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 0, 1, 5, 4 } };
            foreach (var f in faces)
            {
                int b = v.Count;
                foreach (int k in f) v.Add(c + r * q[k]);
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
        }

        /// <summary>Hexagonal marker pole, 1.3 m: white post with a red top band (two submeshes).</summary>
        private static Mesh PoleMesh()
        {
            string path = SharedDir + "/MarkerPole.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m) return m;
            var v = new List<Vector3>(); var white = new List<int>(); var red = new List<int>();
            Prism(v, white, 6, 0.08f, 0f, 1.0f, true);
            Prism(v, red, 6, 0.085f, 1.0f, 1.3f, true);
            m = new Mesh { name = "MarkerPole" };
            m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(white, 0); m.SetTriangles(red, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return Save(m, path);
        }

        /// <summary>Round bale lying along local Z: 8-sided, 1.2 m across, 1.3 m long, centred on the origin.</summary>
        private static Mesh BaleMesh()
        {
            string path = SharedDir + "/HayBale.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m) return m;
            var v = new List<Vector3>(); var t = new List<int>();
            Prism(v, t, 8, 0.6f, -0.65f, 0.65f, true);
            var rot = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < v.Count; i++) v[i] = rot * v[i];
            m = new Mesh { name = "HayBale" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return Save(m, path);
        }

        /// <summary>Flat-shaded n-sided prism along Y from y0 to y1, with a top cap (and bottom when asked).</summary>
        private static void Prism(List<Vector3> v, List<int> t, int sides, float r, float y0, float y1, bool caps)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r), p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                int b = v.Count;
                v.Add(p0 + Vector3.up * y0); v.Add(p1 + Vector3.up * y0); v.Add(p1 + Vector3.up * y1); v.Add(p0 + Vector3.up * y1);
                t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                if (caps)
                {
                    b = v.Count;
                    v.Add(Vector3.up * y1); v.Add(p0 + Vector3.up * y1); v.Add(p1 + Vector3.up * y1);
                    t.AddRange(new[] { b, b + 2, b + 1 });
                    b = v.Count;
                    v.Add(Vector3.up * y0); v.Add(p0 + Vector3.up * y0); v.Add(p1 + Vector3.up * y0);
                    t.AddRange(new[] { b, b + 1, b + 2 });
                }
            }
        }

        private static Mesh Save(Mesh m, string path)
        {
            if (!AssetDatabase.IsValidFolder(SharedDir)) AssetDatabase.CreateFolder("Assets/PolyCar/Maps", "Shared");
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static void SaveMesh(Mesh m, string name, string dir)
        {
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'), System.IO.Path.GetFileName(dir));
            string p = $"{dir}/{name}.asset";
            AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(m, p);
        }

        private static Material Mat(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(SharedDir)) AssetDatabase.CreateFolder("Assets/PolyCar/Maps", "Shared");
            string path = $"{SharedDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.08f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
