using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PolyCar
{
    /// <summary>A recorded run: car poses sampled at a fixed rate.</summary>
    public class GhostData
    {
        public const float Interval = 0.05f;
        public string carId = "";
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Quaternion> rotations = new List<Quaternion>();

        public float Duration => positions.Count * Interval;

        public void Sample(float t, out Vector3 pos, out Quaternion rot)
        {
            int n = positions.Count;
            if (n == 0) { pos = Vector3.zero; rot = Quaternion.identity; return; }
            float f = Mathf.Clamp(t / Interval, 0f, n - 1);
            int i = Mathf.Min((int)f, n - 2 < 0 ? 0 : n - 2);
            float k = n > 1 ? f - i : 0f;
            int j = Mathf.Min(i + 1, n - 1);
            pos = Vector3.LerpUnclamped(positions[i], positions[j], k);
            rot = Quaternion.Slerp(rotations[i], rotations[j], k);
        }

        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(1); // version
            w.Write(carId ?? "");
            w.Write(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                var p = positions[i]; var r = rotations[i];
                w.Write(p.x); w.Write(p.y); w.Write(p.z);
                w.Write(r.x); w.Write(r.y); w.Write(r.z); w.Write(r.w);
            }
            return ms.ToArray();
        }

        public static GhostData FromBytes(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms);
            int version = r.ReadInt32();
            if (version != 1) return null;
            var g = new GhostData { carId = r.ReadString() };
            int n = r.ReadInt32();
            if (n < 0 || n > 200000) return null;
            for (int i = 0; i < n; i++)
            {
                g.positions.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                g.rotations.Add(new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            }
            return g;
        }
    }

    /// <summary>Best-run ghosts on disk, one per track and lap count.</summary>
    public static class GhostStore
    {
        private static string Dir => Path.Combine(Application.persistentDataPath, "ghosts");
        private static string FileFor(string mapId, int laps) => Path.Combine(Dir, $"{mapId}_{laps}.ghost");

        public static GhostData Load(string mapId, int laps)
        {
            try
            {
                string f = FileFor(mapId, laps);
                return File.Exists(f) ? GhostData.FromBytes(File.ReadAllBytes(f)) : null;
            }
            catch (Exception e) { Debug.LogWarning("Poly Car: ghost unreadable. " + e.Message); return null; }
        }

        public static void Save(string mapId, int laps, GhostData data)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(FileFor(mapId, laps), data.ToBytes());
            }
            catch (Exception e) { Debug.LogWarning("Poly Car: could not save ghost. " + e.Message); }
        }

        public static void DeleteAll()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch (Exception) { }
        }
    }

    /// <summary>Records the player's run at a fixed rate.</summary>
    public class GhostRecorder : MonoBehaviour
    {
        public GhostData Data { get; private set; }
        public bool Recording { get; set; }
        private float _next;
        private float _t;

        public void Begin(string carId)
        {
            Data = new GhostData { carId = carId };
            _t = 0f; _next = 0f;
            Recording = true;
        }

        private void FixedUpdate()
        {
            if (!Recording) return;
            _t += Time.fixedDeltaTime;
            while (_t >= _next)
            {
                Data.positions.Add(transform.position);
                Data.rotations.Add(transform.rotation);
                _next += GhostData.Interval;
                if (Data.positions.Count > 72000) { Recording = false; break; } // one hour cap
            }
        }
    }

    /// <summary>Plays a ghost back on a see-through copy of the car body.</summary>
    public class GhostPlayer : MonoBehaviour
    {
        public GhostData Data;
        public float Time0 = -1f;
        public RaceManager Race;
        private Renderer[] _renderers;

        private void Start() => _renderers = GetComponentsInChildren<Renderer>();

        private void LateUpdate()
        {
            if (Data == null || Race == null) return;
            float t = Race.RaceTime;
            bool visible = Race.State == RaceState.Racing || Race.State == RaceState.Finished;
            if (t > Data.Duration) visible = false;
            if (_renderers != null) foreach (var r in _renderers) r.enabled = visible;
            Data.Sample(Mathf.Max(0f, t), out var p, out var q);
            transform.SetPositionAndRotation(p, q);
        }
    }
}
