using UnityEngine;

namespace PolyCar
{
    /// <summary>Feeds keyboard / gamepad input to the player's car.</summary>
    [DefaultExecutionOrder(-30)]
    public class PlayerDriver : MonoBehaviour
    {
        private PolyCarController _car;
        private void Awake() => _car = GetComponent<PolyCarController>();
        private void Update() => _car.Input = PolyInput.ReadDrive();
    }

    /// <summary>
    /// AI driver: pure-pursuit steering toward a point ahead on the TrackPath (plus its own lane offset),
    /// speed planned from the curvature ahead, handbrake for hairpins, boost on straights, and a
    /// reverse-out / respawn when stuck. Skill scales cornering speed and reaction.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class BotDriver : MonoBehaviour
    {
        [Range(0.5f, 1.2f)] public float skill = 0.9f;
        public float lateralGrip = 15f; // m/s² the bot is willing to corner at (scaled by skill)

        private PolyCarController _car;
        private Racer _racer;
        private float _lane;
        private float _laneSeed;
        private float _stuckTimer;
        private float _reverseTimer;
        private float _reverseSteer;
        public float DebugDesired { get; private set; }
        public float DebugAngle { get; private set; }

        private void Awake()
        {
            _car = GetComponent<PolyCarController>();
            _racer = GetComponent<Racer>();
            _laneSeed = Random.value * 100f;
            _car.AutoDrift = false;
            _car.FastSteer = true;
        }

        private void Update()
        {
            var path = _racer ? _racer.Path : null;
            if (path == null || _car.InputLocked) { _car.Input = default; return; }

            float speed = _car.ForwardSpeed;
            float d = _racer.Progress;
            float half = path.HalfWidthAt(d);

            // Wander gently across the road, and step aside when a car is right ahead.
            float wander = (Mathf.PerlinNoise(_laneSeed, Time.time * 0.07f) - 0.5f) * 0.7f;
            float avoid = AvoidTraffic(path, d);
            _lane = Mathf.MoveTowards(_lane, Mathf.Clamp(wander + avoid, -0.45f, 0.45f), Time.deltaTime * 0.6f);

            // Shorter look-ahead when off the road so it heads straight back instead of cutting through scenery.
            bool offRoad = Mathf.Abs(_racer.Lateral) > half + 1f;
            float look = offRoad ? 7f : Mathf.Clamp(Mathf.Abs(speed) * 0.5f, 8f, 26f);
            float td = d + look;
            Vector3 dir = path.DirectionAt(td);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 target = path.PointAt(td) + right * (_lane * half);
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;

            // Speed plan: precomputed corner + braking profile along the track.
            float aLat = lateralGrip * skill;
            float corner = path.SpeedLimitAt(d + Mathf.Max(0f, speed) * 0.25f, aLat, 13f * skill);
            float desired = Mathf.Min(_car.TopSpeed * _car.SpeedMultiplier, corner);
            float maxCurv = path.CurvatureAhead(d, 40f);

            // Big heading error (recovering, hairpin): slow right down so the car can come round.
            float err = Mathf.Abs(angle);
            if (err > 18f) desired = Mathf.Min(desired, Mathf.Lerp(_car.TopSpeed, 11f, Mathf.Clamp01((err - 18f) / 50f)));
            DebugDesired = desired; DebugAngle = angle;

            // PD steering: heading error minus a yaw-rate damping term stops the left-right weave.
            float yawRate = Vector3.Dot(_car.Body.angularVelocity, Vector3.up) * Mathf.Rad2Deg;
            float steerCmd = (angle - yawRate * 0.18f) / 24f;
            var input = new DriveInput { Steer = Mathf.Clamp(steerCmd, -1f, 1f) };
            if (speed > desired + 1.5f)
            {
                input.Brake = Mathf.Clamp01((speed - desired) / 7f);
                input.Throttle = 0f;
            }
            else input.Throttle = speed < desired - 3f ? 1f : 0.6f;
            input.Boost = _car.Boost > 0.4f && maxCurv < 0.004f && speed > 15f && Mathf.Abs(angle) < 8f && desired > speed + 5f;

            // Unstick: reverse out with opposite lock, respawn if that fails.
            if (_reverseTimer > 0f)
            {
                _reverseTimer -= Time.deltaTime;
                input = new DriveInput { Brake = 1f, Steer = _reverseSteer };
            }
            else if (Mathf.Abs(speed) < 1.5f && !_racer.Finished)
            {
                _stuckTimer += Time.deltaTime;
                if (_stuckTimer > 2.5f)
                {
                    _reverseTimer = 1.3f;
                    _reverseSteer = -Mathf.Sign(angle);
                    if (_stuckTimer > 7f) { _racer.Respawn(); _stuckTimer = 0f; _reverseTimer = 0f; }
                }
            }
            else _stuckTimer = Mathf.Max(0f, _stuckTimer - Time.deltaTime * 2f);

            if (_racer.Finished) input = new DriveInput { Throttle = 0.25f, Steer = input.Steer };
            _car.Input = input;
        }

        private float AvoidTraffic(TrackPath path, float d)
        {
            if (_racer.Race == null) return 0f;
            foreach (var other in _racer.Race.Racers)
            {
                if (other == _racer || other == null) continue;
                float gap = other.Progress - d;
                if (path.loop) gap = Mathf.Repeat(gap + path.Length * 0.5f, path.Length) - path.Length * 0.5f;
                if (gap > 2f && gap < 14f)
                {
                    float half = path.HalfWidthAt(d);
                    float myLat = _racer.Lateral / Mathf.Max(1f, half), theirLat = other.Lateral / Mathf.Max(1f, half);
                    if (Mathf.Abs(myLat - theirLat) < 0.35f) return theirLat > 0f ? -0.45f : 0.45f;
                }
            }
            return 0f;
        }
    }

    /// <summary>
    /// Drift scoring for the player: points build with slip angle x speed and a combo multiplier that grows
    /// while you chain drifts; they bank after a short pause, and a hard crash loses the unbanked points.
    /// </summary>
    public class DriftScorer : MonoBehaviour
    {
        public float BankDelay = 1.6f;
        public float Pending { get; private set; }
        public int Multiplier { get; private set; } = 1;
        public long Total { get; private set; }
        public float DriftTime { get; private set; }
        public event System.Action<long> Banked;
        public event System.Action Lost;

        private PolyCarController _car;
        private float _sinceDrift = 99f;

        private void Awake()
        {
            _car = GetComponent<PolyCarController>();
            _car.Impact += s => { if (s > 6f && Pending > 0f) { Pending = 0f; Multiplier = 1; DriftTime = 0f; Lost?.Invoke(); } };
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (_car.IsDrifting && !_car.InputLocked)
            {
                _sinceDrift = 0f;
                DriftTime += dt;
                Multiplier = Mathf.Clamp(1 + (int)(DriftTime / 2.5f), 1, 5);
                float angle = Mathf.Clamp(Mathf.Abs(_car.DriftAngle), 0f, 60f);
                Pending += angle * Mathf.Abs(_car.ForwardSpeed) * dt * 0.6f;
            }
            else
            {
                _sinceDrift += dt;
                if (_sinceDrift > BankDelay && Pending > 0f) Bank();
                if (_sinceDrift > BankDelay) { DriftTime = 0f; Multiplier = 1; }
            }
        }

        public void Bank()
        {
            if (Pending <= 0f) return;
            long pts = (long)(Pending * Multiplier);
            Total += pts;
            Pending = 0f;
            Banked?.Invoke(pts);
        }
    }
}
