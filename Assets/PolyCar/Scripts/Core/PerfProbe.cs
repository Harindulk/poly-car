using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Benchmark mode (-benchmark): records frame times for 60 s of racing, writes a summary to
    /// persistentDataPath/benchmark.txt (and -benchmarkOut path if given), then quits. Each spike is logged with
    /// batches, SetPass calls, triangles, CPU/GPU frame time and awake rigidbodies, to tell what caused it.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        public float duration = 60f;

        private ProfilerRecorder _batches, _setPass, _tris;
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _batchSum, _trisSum;
        private long _trisMax;

        private void Start()
        {
            if (float.TryParse(GameFlow.Arg("-benchmarkSeconds"), out float secs) && secs > 2f) duration = secs;
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        }

        private void OnDestroy() { _batches.Dispose(); _setPass.Dispose(); _tris.Dispose(); }
        private readonly List<float> _frames = new List<float>(8000);
        private readonly int[] _spikesPer10s = new int[7];
        private readonly System.Text.StringBuilder _firstSpikes = new System.Text.StringBuilder();
        private int _spikeCount;
        private long _gcAtStart;
        private float _elapsed;
        private bool _done;

        private void Update()
        {
            if (_done) return;
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            if (_gcAtStart == 0) _gcAtStart = System.GC.CollectionCount(0);
            if (_elapsed > 1f)
            {
                _frames.Add(dt * 1000f); // skip the first second after GO
                _batchSum += _batches.LastValue; _trisSum += _tris.LastValue; _trisMax = System.Math.Max(_trisMax, _tris.LastValue);
            }
            FrameTimingManager.CaptureFrameTimings();
            if (dt > 0.0334f)
            {
                _spikesPer10s[Mathf.Clamp((int)(_elapsed / 10f), 0, 6)]++;
                if (_spikeCount++ < 25)
                {
                    string timing = FrameTimingManager.GetLatestTimings(1, _timing) > 0 ? $" cpu{_timing[0].cpuMainThreadFrameTime:0}/gpu{_timing[0].gpuFrameTime:0}" : "";
                    int awake = 0;
                    foreach (var rb in FindObjectsByType<Rigidbody>()) if (!rb.IsSleeping()) awake++;
                    _firstSpikes.Append($"{_elapsed:0.0}s:{dt * 1000f:0}[b{_batches.LastValue} sp{_setPass.LastValue} t{_tris.LastValue / 1000}k{timing} rb{awake}] ");
                }
            }
            if (_elapsed >= duration) Finish();
        }

        private void Finish()
        {
            _done = true;
            var sorted = new List<float>(_frames);
            sorted.Sort();
            float sum = 0f; foreach (var f in _frames) sum += f;
            float avg = sum / Mathf.Max(1, _frames.Count);
            float p99 = sorted.Count > 0 ? sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))] : 0f;
            float max = sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f;
            int over33 = 0; foreach (var f in _frames) if (f > 33.4f) over33++;
            var race = RaceManager.Current;
            int playing = 0;
            foreach (var a in FindObjectsByType<AudioSource>()) if (a.isPlaying) playing++;
            string text = $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} frames={_frames.Count} avgFps={1000f / avg:0.0} avgMs={avg:0.00} p99Ms={p99:0.00} maxMs={max:0.00} over33ms={over33} " +
                          $"res={Screen.width}x{Screen.height} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} gpu={SystemInfo.graphicsDeviceName} progress={(race && race.Player ? race.Player.TotalProgress : 0f):0}" +
                           $" audioPlaying={playing} respawns={(race && race.Player ? race.Player.Respawns : -1)}" +
                           $" avgBatches={_batchSum / Mathf.Max(1, _frames.Count):0} avgTris={_trisSum / Mathf.Max(1, _frames.Count) / 1000:0}k maxTris={_trisMax / 1000}k" + $"\nspikes/10s={string.Join(",", _spikesPer10s)} gen0GCs={System.GC.CollectionCount(0) - _gcAtStart} first={_firstSpikes}";
            if (race != null)
            {
                var sb = new System.Text.StringBuilder("\nracers:");
                foreach (var r in race.Racers)
                    sb.Append($" {r.displayName}[{r.Car.name.Replace("(Clone)", "")}] dist={r.TotalProgress:0} resp={r.Respawns}{(r.Finished ? " fin=" + r.FinishTime.ToString("0.0") : "")};");
                text += sb.ToString();
            }
            Debug.Log("Poly Car benchmark: " + text);
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "benchmark.txt"), text);
                string outPath = GameFlow.Arg("-benchmarkOut");
                if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, text);
            }
            catch (System.Exception e) { Debug.LogWarning(e.Message); }
            Application.Quit();
        }
    }
}
