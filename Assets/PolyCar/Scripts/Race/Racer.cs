using System;
using System.Collections.Generic;
using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// One car in the race: tracks distance along the TrackPath, laps, checkpoints and splits, keeps a
    /// rolling "last safe spot" for respawns, and respawns automatically when the car is lost
    /// (fallen off the world, on its roof, or far off the road).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class Racer : MonoBehaviour
    {
        public string displayName = "Player";
        public bool isPlayer;

        public PolyCarController Car { get; private set; }
        public RaceManager Race { get; private set; }
        public TrackPath Path => Race ? Race.Path : null;

        /// <summary>Distance along the path on the current lap.</summary>
        public float Progress { get; private set; }
        public float Lateral { get; private set; }
        /// <summary>0 = on lap one; -1 = still behind the start line on the grid.</summary>
        public int Lap { get; private set; }
        public int NextCheckpoint { get; private set; }
        public bool Finished { get; private set; }
        public float FinishTime { get; private set; }
        public int Position { get; set; } = 1;
        public bool WrongWay { get; private set; }
        /// <summary>Seconds without real progress along the track (stuck in a ditch, on a bank, against a tree).</summary>
        public float StallTime { get; private set; }
        public float BestLap { get; private set; }
        public float CurrentLapStart { get; private set; }
        public readonly List<float> LapTimes = new List<float>();
        public readonly List<float> Splits = new List<float>();
        public float DistanceDriven { get; private set; }
        public int Respawns { get; private set; }

        public event Action<int, float> CheckpointPassed;  // index, race time
        public event Action<int, float> LapCompleted;      // lap index finished, lap time
        public event Action Respawned;
        public event Action FinishedRace;

        /// <summary>Total race distance covered (laps x length + progress) for positions.</summary>
        public float TotalProgress
        {
            get
            {
                if (Path == null) return 0f;
                if (!Path.loop) return Progress;
                return Lap * Path.Length + Progress;
            }
        }

        private float _safeDist;
        private float _lostTimer;
        private float _wrongTimer;
        private float _upsideTimer;
        private float _respawnCooldown;
        private Vector3 _lastPos;
        private bool _started;
        private float _stallMark;
        private float _steepTimer;
        private int _highestLap;

        public void Init(RaceManager race, float startDist)
        {
            Race = race;
            Car = GetComponent<PolyCarController>();
            float len = Path.Length;
            Progress = Path.Wrap(startDist);
            _safeDist = Progress;
            NextCheckpoint = 0;
            if (Path.loop && Progress > len * 0.5f)
            {
                // Grid slot behind the line: crossing it starts lap one.
                Lap = -1;
                _highestLap = -1;
                NextCheckpoint = race.Checkpoints.Length;
            }
            _lastPos = transform.position;
        }

        public void BeginRace(float raceTime)
        {
            _started = true;
            _stallMark = TotalProgress;
            CurrentLapStart = raceTime;
        }

        private void FixedUpdate()
        {
            if (Race == null || Path == null) return;
            float len = Path.Length;
            float d = Path.Project(transform.position, Progress, 60f, out float lateral);
            Lateral = lateral;
            float raceTime = Race.RaceTime;

            Vector3 pos = transform.position;
            DistanceDriven += Vector3.Distance(pos, _lastPos);
            _lastPos = pos;

            if (Path.loop)
            {
                if (d < Progress - len * 0.5f) OnCrossLine(raceTime, true);
                else if (d > Progress + len * 0.5f) OnCrossLine(raceTime, false);
            }
            Progress = d;

            // Checkpoints (progress based, so cutting corners off-road still counts but skipping can't).
            var cps = Race.Checkpoints;
            while (!Finished && Lap >= 0 && NextCheckpoint < cps.Length && Progress >= cps[NextCheckpoint] && Progress - cps[NextCheckpoint] < 80f)
            {
                Splits.Add(raceTime);
                CheckpointPassed?.Invoke(NextCheckpoint, raceTime);
                NextCheckpoint++;
            }

            if (!Path.loop && !Finished && _started && Progress >= len - 3f) Finish(raceTime);

            UpdateSafety(Time.fixedDeltaTime);
        }

        private void OnCrossLine(float raceTime, bool forward)
        {
            if (forward)
            {
                if (NextCheckpoint >= Race.Checkpoints.Length)
                {
                    if (Lap >= 0 && Lap >= _highestLap)
                    {
                        float lapTime = raceTime - CurrentLapStart;
                        LapTimes.Add(lapTime);
                        if (BestLap <= 0f || lapTime < BestLap) BestLap = lapTime;
                        LapCompleted?.Invoke(Lap, lapTime);
                    }
                    Lap++;
                    if (Lap > _highestLap) { _highestLap = Lap; CurrentLapStart = raceTime; }
                    NextCheckpoint = 0;
                    if (_started && Lap >= Race.Laps && !Finished) Finish(raceTime);
                }
                // Crossing without the checkpoints (e.g. reversed over the line) doesn't count.
            }
            else
            {
                // Backed over the line: undo so laps can't be farmed by wiggling across it.
                Lap--;
                NextCheckpoint = Race.Checkpoints.Length;
            }
        }

        private void Finish(float raceTime)
        {
            Finished = true;
            FinishTime = raceTime;
            if (!Path.loop) { BestLap = raceTime; }
            FinishedRace?.Invoke();
        }

        private void UpdateSafety(float dt)
        {
            if (Car == null) return;
            _respawnCooldown -= dt;
            float half = Path.HalfWidthAt(Progress);
            bool upright = !Car.IsUpsideDown;
            Vector3 dir = Path.DirectionAt(Progress);
            float along = Vector3.Dot(Car.Body.linearVelocity, dir);

            if (Car.GroundedWheels >= 3 && upright && Mathf.Abs(Lateral) < half - 0.5f && along > -1f)
                _safeDist = Progress;

            // Wrong way.
            _wrongTimer = along < -4f && Car.Grounded ? _wrongTimer + dt : 0f;
            WrongWay = _wrongTimer > 1.2f;

            // Stall: no 8 m of progress within the window. Bots reset after 5 s, the player after 12 s
            // (the HUD prompts R before that).
            if (_started && !Finished)
            {
                float gained = TotalProgress - _stallMark;
                if (gained > 8f || gained < -200f) { _stallMark = TotalProgress; StallTime = 0f; }
                else StallTime += dt;
            }

            if (Finished || _respawnCooldown > 0f) return;
            Vector3 centre = Path.PointAt(Progress);
            float pitch = Vector3.Angle(transform.up, Vector3.up);
            _steepTimer = pitch > 42f && Car.Body.linearVelocity.magnitude < 6f ? _steepTimer + dt : 0f;
            if (StallTime > (isPlayer ? 12f : 5f) || _steepTimer > 2.5f) { Respawn(); return; }
            bool fell = transform.position.y < centre.y - 30f;
            bool farOff = Mathf.Abs(Lateral) > half + 30f;
            _upsideTimer = Car.IsUpsideDown && Car.Body.linearVelocity.magnitude < 4f ? _upsideTimer + dt : 0f;
            _lostTimer = farOff ? _lostTimer + dt : 0f;
            if (fell || _upsideTimer > 2.2f || _lostTimer > 2.5f) Respawn();
        }

        /// <summary>Back on the road at the last safe spot, facing down the track.</summary>
        public void Respawn()
        {
            if (Path == null || Car == null) return;
            _respawnCooldown = 1.2f;
            Respawns++;
            _upsideTimer = 0f; _lostTimer = 0f; _wrongTimer = 0f; _steepTimer = 0f;
            StallTime = 0f;
            float d = Path.Wrap(_safeDist - 4f);
            if (!Path.loop) d = Mathf.Max(d, 2f);
            Vector3 p = Path.PointAt(d);
            Quaternion rot = Path.RotationAt(d);
            // Drop onto whatever is under the centreline.
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                p = hit.point;
            float carry = Mathf.Clamp(Car.ForwardSpeed, 0f, 12f);
            Car.Teleport(p + Vector3.up * 0.9f, rot);
            Car.Body.linearVelocity = rot * Vector3.forward * carry;
            Progress = d;
            _stallMark = TotalProgress;
            _lastPos = Car.transform.position;
            var fx = GetComponent<CarFx>();
            if (fx) fx.ResetTrails();
            Respawned?.Invoke();
        }

        /// <summary>For the UI: share of the race done, 0..1.</summary>
        public float Completion
        {
            get
            {
                if (Path == null) return 0f;
                if (!Path.loop) return Mathf.Clamp01(Progress / Path.Length);
                return Mathf.Clamp01((Mathf.Max(0, Lap) * Path.Length + (Lap >= 0 ? Progress : 0f)) / (Race.Laps * Path.Length));
            }
        }
    }
}
