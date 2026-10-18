using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PolyCar
{
    public enum RaceState { Intro, Countdown, Racing, Finished }

    /// <summary>Everything the results screen shows about the player's finished race.</summary>
    public class RaceResult
    {
        public int position;
        public int racers;
        public float time;
        public float bestLap;
        public Medal medal;
        public bool newRecord;
        public float previousBest;
        public long driftScore;
        public bool unlockedSomething;
        public string unlockedName;
        public List<(string name, float time, bool player, bool finished)> standings = new List<(string, float, bool, bool)>();
    }

    /// <summary>
    /// Runs a race in a level scene: spawns the player, AI rivals and the best-run ghost on the grid,
    /// plays the intro and countdown, tracks positions, and stores records, ghosts and unlocks at the
    /// finish. The level only needs a TrackPath; HUD, pause and results are built at runtime.
    /// </summary>
    public class RaceManager : MonoBehaviour
    {
        public TrackPath Path;
        [Tooltip("Used when the scene isn't in the map catalog.")]
        public string fallbackMapId = "green_forest";
        public float checkpointSpacing = 90f;
        [Tooltip("Sprint tracks: distance from the start of the path to the front of the grid.")]
        public float gridStartDistance = 14f;

        public static RaceManager Current { get; private set; }

        /// <summary>Test/benchmark mode: the AI drives the player's car (dev pref in the Editor, -autopilot in builds).</summary>
        public static bool Autopilot => Application.isEditor
            ? PlayerPrefs.GetInt("polycar.dev.autopilot", 0) == 1
            : Array.IndexOf(Environment.GetCommandLineArgs(), "-autopilot") >= 0;

        public RaceState State { get; private set; } = RaceState.Intro;
        public float RaceTime { get; private set; }
        public int Laps { get; private set; }
        public float[] Checkpoints { get; private set; } = Array.Empty<float>();
        public readonly List<Racer> Racers = new List<Racer>();
        public Racer Player { get; private set; }
        public PolyCarController PlayerCar { get; private set; }
        public DriftScorer Drift { get; private set; }
        public MapSpec Map { get; private set; }
        public CarSpec PlayerSpec { get; private set; }
        public TrackRecord PreviousRecord { get; private set; }
        public ChaseCamera Chase { get; private set; }
        public RaceResult Result { get; private set; }
        public bool Paused { get; private set; }
        public int CountdownValue { get; private set; } = -1;
        public bool HasGhost => _ghost != null;

        public event Action<RaceState> StateChanged;
        public event Action<int> CountdownTick;           // 3,2,1,0(GO)
        public event Action<RaceResult> PlayerFinished;
        public event Action<bool> PauseChanged;
        public event Action<float, float> SplitDelta;     // delta vs best, race time
        public event Action<int, float, bool> LapDone;    // lap index, lap time, best lap

        private GhostRecorder _recorder;
        private GhostData _ghost;
        private Camera _cam;
        private int _finishCounter;
        private static readonly string[] BotNames = { "Kenji", "Maya", "Rico", "Lena", "Viktor", "Aiko", "Sol", "Nova", "Dex", "Juno" };

        private void Awake()
        {
            Current = this;
            if (!Path) Path = FindAnyObjectByType<TrackPath>();
            Path.Build();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            Time.timeScale = 1f;
        }

        private void Start()
        {
            string scene = SceneManager.GetActiveScene().name;
            var catalog = MapCatalog.Instance;
            Map = catalog ? (catalog.ByScene(scene) ?? catalog.Get(fallbackMapId)) : null;
            Laps = Path.loop ? Mathf.Max(1, GameFlow.LapsFor(Map) > 0 ? GameFlow.LapsFor(Map) : (Map != null && Map.laps > 0 ? Map.laps : 3)) : 1;
            string mapId = Map != null ? Map.id : fallbackMapId;
            PreviousRecord = SaveData.Current.Get(mapId, RecordLaps);

            BuildCheckpoints();
            SetupCamera();
            SpawnField();
            if (GameSettings.Current.ghost) SpawnGhost(mapId);

            RaceHud.Create(this);
            PauseMenu.Create(this);
            ResultsScreen.Create(this);
            GameAudio.PlayRaceMusic(mapId);
            StartCoroutine(Sequence());
        }

        /// <summary>Sprints are stored with laps = 0, circuits with the lap count raced.</summary>
        public int RecordLaps => Path.loop ? Laps : 0;

        private void BuildCheckpoints()
        {
            float len = Path.Length;
            var cps = new List<float>();
            float spacing = Mathf.Max(40f, checkpointSpacing);
            for (float d = spacing; d < len - spacing * 0.5f; d += spacing) cps.Add(d);
            Checkpoints = cps.ToArray();
        }

        private void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            // Old 2022 camera scripts would fight the chase camera.
            foreach (var mb in _cam.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name is "CameraController" or "CameraFollow") mb.enabled = false;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = Mathf.Max(_cam.farClipPlane, 1500f);
            Chase = _cam.GetComponent<ChaseCamera>() ?? _cam.gameObject.AddComponent<ChaseCamera>();
            var camData = _cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (camData) camData.renderPostProcessing = true;

            // Optional speed blur (Settings > Motion Blur), as a runtime-only global volume.
            if (GameSettings.Current.motionBlur)
            {
                var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                var mb = profile.Add<UnityEngine.Rendering.Universal.MotionBlur>(true);
                mb.intensity.Override(0.18f);
                mb.quality.Override(UnityEngine.Rendering.Universal.MotionBlurQuality.Low);
                mb.clamp.Override(0.05f);
                var vol = new GameObject("[MotionBlur]").AddComponent<UnityEngine.Rendering.Volume>();
                vol.isGlobal = true;
                vol.priority = 10f;
                vol.sharedProfile = profile;
            }
        }

        private void SpawnField()
        {
            var catalog = CarCatalog.Instance;
            PlayerSpec = GameFlow.CurrentCar ?? catalog.cars[0];
            int opponents = Mathf.Clamp(GameSettings.Current.opponents, 0, 7);

            // Rivals use the other cars, shuffled.
            var pool = new List<CarSpec>();
            foreach (var c in catalog.cars) if (c != PlayerSpec && c.prefab) pool.Add(c);
            for (int i = pool.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

            var diff = (Difficulty)GameSettings.Current.difficulty;
            float skill = diff == Difficulty.Easy ? 0.72f : diff == Difficulty.Hard ? 0.98f : 0.86f;
            float speedMul = diff == Difficulty.Easy ? 0.86f : diff == Difficulty.Hard ? 1.01f : 0.94f;

            int slots = opponents + 1;
            for (int slot = 0; slot < slots; slot++)
            {
                bool isPlayer = slot == slots - 1; // player starts at the back and chases
                CarSpec spec = isPlayer ? PlayerSpec : (pool.Count > 0 ? pool[slot % pool.Count] : PlayerSpec);
                GridPose(slot, slots, out Vector3 pos, out Quaternion rot, out float dist);
                var racer = SpawnCar(spec, pos, rot, dist, isPlayer);
                racer.displayName = isPlayer ? "You" : BotNames[slot % BotNames.Length];
                if (!isPlayer)
                {
                    var bot = racer.gameObject.AddComponent<BotDriver>();
                    bot.skill = skill * UnityEngine.Random.Range(0.96f, 1.04f);
                    racer.Car.SpeedMultiplier = speedMul * UnityEngine.Random.Range(0.97f, 1.02f);
                }
            }
        }

        private void GridPose(int slot, int slots, out Vector3 pos, out Quaternion rot, out float dist)
        {
            int row = slot / 2;
            int col = slot % 2;
            int rows = (slots + 1) / 2;
            // Circuits: grid just behind the line. Sprints: back row at gridStartDistance.
            dist = Path.loop ? Path.Length - 7f - row * 9f : gridStartDistance + (rows - 1 - row) * 9f;
            dist = Path.Wrap(dist);
            Vector3 centre = Path.PointAt(dist);
            Vector3 dir = Path.DirectionAt(dist);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            float half = Path.HalfWidthAt(dist);
            float lateral = (col == 0 ? -1f : 1f) * Mathf.Min(2.6f, half * 0.4f);
            pos = centre + right * lateral;
            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;
            rot = Path.RotationAt(dist);
        }

        private Racer SpawnCar(CarSpec spec, Vector3 pos, Quaternion rot, float dist, bool isPlayer)
        {
            var go = Instantiate(spec.prefab, pos + Vector3.up * 0.7f, rot);
            go.name = (isPlayer ? "Player_" : "Rival_") + spec.id;
            var car = go.GetComponent<PolyCarController>();
            car.ApplySpec(spec);
            car.InputLocked = true;
            var racer = go.AddComponent<Racer>();
            racer.isPlayer = isPlayer;
            racer.Init(this, dist);
            var audio = go.GetComponent<CarAudio>();
            if (audio) audio.isPlayer = isPlayer;
            if (isPlayer)
            {
                go.tag = "Player";
                if (Autopilot) { var ap = go.AddComponent<BotDriver>(); ap.skill = 0.92f; }
                else go.AddComponent<PlayerDriver>();
                Drift = go.AddComponent<DriftScorer>();
                _recorder = go.AddComponent<GhostRecorder>();
                Player = racer;
                PlayerCar = car;
                Chase.SetTarget(car);
                racer.CheckpointPassed += OnPlayerCheckpoint;
                racer.LapCompleted += (lap, t) => LapDone?.Invoke(lap, t, Mathf.Approximately(t, racer.BestLap));
                racer.FinishedRace += OnPlayerFinish;
                racer.Respawned += () => Chase.Snap();
            }
            else
            {
                racer.FinishedRace += () => racer.FinishOrder = ++_finishCounter;
            }
            Racers.Add(racer);
            return racer;
        }

        private void SpawnGhost(string mapId)
        {
            _ghost = GhostStore.Load(mapId, RecordLaps);
            if (_ghost == null || _ghost.positions.Count < 10) { _ghost = null; return; }
            var spec = CarCatalog.Instance.Get(_ghost.carId) ?? PlayerSpec;
            var body = spec.prefab ? spec.prefab.GetComponent<PolyCarController>()?.bodyVisual : null;
            if (body == null) return;
            var go = new GameObject("Ghost");
            var copy = Instantiate(body.gameObject, go.transform);
            copy.transform.localPosition = body.localPosition;
            // Wheels are siblings of the body in the car rig; copy them too so the ghost reads as a car.
            foreach (var w in spec.prefab.GetComponent<PolyCarController>().wheels)
            {
                if (w.pivot == null) continue;
                var wc = Instantiate(w.pivot.gameObject, go.transform);
                wc.transform.localPosition = w.pivot.localPosition;
            }
            var mat = Resources.Load<Material>("GhostMaterial");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is TrailRenderer || r is ParticleSystemRenderer) { r.enabled = false; continue; }
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            var player = go.AddComponent<GhostPlayer>();
            player.Data = _ghost;
            player.Race = this;
        }

        private IEnumerator Sequence()
        {
            // Intro: swoop down from above the grid to the chase camera.
            SetState(RaceState.Intro);
            Chase.enabled = false;
            var carT = PlayerCar.transform;
            Vector3 startPos = carT.position + carT.rotation * new Vector3(14f, 12f, 22f);
            float t = 0f;
            const float introTime = 2.4f;
            while (t < introTime)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / introTime);
                Vector3 endPos = carT.position + carT.rotation * new Vector3(0f, 2.45f, -6.6f);
                Vector3 orbit = Quaternion.AngleAxis(Mathf.Lerp(-70f, 0f, k), Vector3.up) * (startPos - carT.position);
                _cam.transform.position = Vector3.Lerp(carT.position + orbit, endPos, k);
                _cam.transform.LookAt(carT.position + Vector3.up * 1f);
                if (PolyInput.AnyKeyPressed() && t > 0.3f) break;
                yield return null;
            }
            Chase.enabled = true;
            Chase.Snap();

            SetState(RaceState.Countdown);
            for (int i = 3; i >= 1; i--)
            {
                CountdownValue = i;
                CountdownTick?.Invoke(i);
                GameAudio.CountBeep(false);
                yield return new WaitForSeconds(0.85f);
            }
            CountdownValue = 0;
            CountdownTick?.Invoke(0);
            GameAudio.CountBeep(true);
            RaceTime = 0f;
            foreach (var r in Racers) { r.Car.InputLocked = false; r.BeginRace(0f); }
            _recorder.Begin(PlayerSpec.id);
            SetState(RaceState.Racing);
            yield return new WaitForSeconds(0.8f);
            CountdownValue = -1;
        }

        private void SetState(RaceState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        private void Update()
        {
            if (PolyInput.PausePressed() && State != RaceState.Intro && (Result == null || !ResultsScreen.Visible)) SetPaused(!Paused);
            if (Paused) return;

            if (State == RaceState.Racing || State == RaceState.Finished) RaceTime += Time.deltaTime;
            if (State == RaceState.Racing)
            {
                if (PolyInput.RespawnPressed()) Player.Respawn();
                if (PolyInput.RestartPressed()) GameFlow.Restart();
            }
            if (PolyInput.CameraPressed() && Chase) Chase.CycleMode();
            UpdatePositions();
            RubberBand();
        }

        public void SetPaused(bool paused)
        {
            if (Paused == paused) return;
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            GameAudio.DuckMusic(paused);
            PauseChanged?.Invoke(paused);
        }

        private void UpdatePositions()
        {
            Racers.Sort((a, b) =>
            {
                if (a.Finished && b.Finished) return a.FinishTime.CompareTo(b.FinishTime);
                if (a.Finished) return -1;
                if (b.Finished) return 1;
                return b.TotalProgress.CompareTo(a.TotalProgress);
            });
            for (int i = 0; i < Racers.Count; i++) Racers[i].Position = i + 1;
        }

        private void RubberBand()
        {
            if (Player == null || State != RaceState.Racing) return;
            var diff = (Difficulty)GameSettings.Current.difficulty;
            float baseMul = diff == Difficulty.Easy ? 0.86f : diff == Difficulty.Hard ? 1.01f : 0.94f;
            foreach (var r in Racers)
            {
                if (r == Player || r.Finished) continue;
                float gap = r.TotalProgress - Player.TotalProgress; // + ahead of the player
                float k = Mathf.Clamp(-gap / 150f, -1f, 1f);
                r.Car.SpeedMultiplier = baseMul * (1f + k * 0.06f);
            }
        }

        private void OnPlayerCheckpoint(int index, float time)
        {
            if (PreviousRecord != null && PreviousRecord.splits != null && index < PreviousRecord.splits.Length && PreviousRecord.splits[index] > 0f)
                SplitDelta?.Invoke(time - PreviousRecord.splits[index], time);
        }

        private void OnPlayerFinish()
        {
            Player.FinishOrder = ++_finishCounter;
            _recorder.Recording = false;
            Drift.Bank();
            SetState(RaceState.Finished);

            // Let the AI bring the player's car home.
            var driver = PlayerCar.GetComponent<PlayerDriver>();
            if (driver) driver.enabled = false;
            var bot = PlayerCar.gameObject.AddComponent<BotDriver>();
            bot.skill = 0.7f;

            var save = SaveData.Current;
            string mapId = Map != null ? Map.id : fallbackMapId;
            int starsBefore = save.Stars;
            var res = new RaceResult
            {
                position = Player.Position,
                racers = Racers.Count,
                time = Player.FinishTime,
                bestLap = Path.loop ? Player.BestLap : 0f,
                medal = Medals.For(Map, Player.FinishTime, Laps),
                previousBest = PreviousRecord != null ? PreviousRecord.bestTime : 0f,
                driftScore = Drift.Total,
            };
            res.newRecord = save.Submit(mapId, RecordLaps, res.time, res.bestLap, res.medal, Player.Splits.ToArray(), PlayerSpec.id);
            save.racesFinished++;
            if (res.position == 1 && Racers.Count > 1) save.racesWon++;
            save.totalDriftScore += res.driftScore;
            save.totalDistanceKm += Player.DistanceDriven / 1000f;
            save.Save();
            if (res.newRecord && _recorder.Data != null) GhostStore.Save(mapId, RecordLaps, _recorder.Data);

            int starsAfter = save.Stars;
            foreach (var c in CarCatalog.Instance.cars)
            {
                if (c.unlockMedals > starsBefore && c.unlockMedals <= starsAfter)
                {
                    res.unlockedSomething = true;
                    res.unlockedName = res.unlockedName == null ? c.displayName : res.unlockedName + ", " + c.displayName;
                }
            }
            Result = res;
            StartCoroutine(ShowResults());
        }

        private IEnumerator ShowResults()
        {
            yield return new WaitForSeconds(2.2f);
            // Standings: finished racers by time, the rest by distance (estimated).
            UpdatePositions();
            Result.standings.Clear();
            foreach (var r in Racers)
                Result.standings.Add((r.displayName, r.Finished ? r.FinishTime : -1f, r == Player, r.Finished));
            Result.position = Player.Position;
            PlayerFinished?.Invoke(Result);
            if (Result.newRecord || Result.medal != Medal.None) GameAudio.Jingle();
        }
    }
}
