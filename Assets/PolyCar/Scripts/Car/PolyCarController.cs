using System;
using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Rally car on raycast wheels, built on the same ideas as art of rally's handling (our own code):
    /// spring + two-stage damper suspension with anti-roll bars; a Magic-Formula-style tyre whose side grip
    /// stays almost flat past the peak (so slides can be held); slip that lags the contact patch over a
    /// relaxation length; a friction circle from normalised slip (throttle steers the rear); a rear-biased
    /// four-wheel drive with a viscous coupling; rear-only handbrake; automatic gearbox; and the arcade
    /// helpers on top: yaw torque straight from the steering input, a counter-slide stability torque, a brake
    /// helper, air levelling and a landing stick.
    /// Physics runs in FixedUpdate (tyres in 5 sub-steps) with an interpolated Rigidbody.
    /// </summary>
    /// <summary>What a wheel is rolling on (from the ground collider's physics material name).</summary>
    public enum SurfaceKind { Tarmac, Gravel, Snow, Verge, Grass, Sand, DeepSnow }

    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-20)]
    public class PolyCarController : MonoBehaviour
    {
        [Serializable]
        public class Wheel
        {
            public Transform pivot;   // centre of the wheel, identity rotation relative to the car
            public bool front;
            public bool left;
            [NonSerialized] public Vector3 restLocal;
            [NonSerialized] public bool grounded;
            [NonSerialized] public RaycastHit hit;
            [NonSerialized] public float compression;      // share of the travel in use (0 = hanging, 1 = bump stop)
            [NonSerialized] public float prevCompression;
            [NonSerialized] public float spin;             // visual roll angle, degrees
            [NonSerialized] public bool onRoad;
            [NonSerialized] public float omega;            // wheel speed, rad/s
            [NonSerialized] public float slipRatio;        // lagged longitudinal slip ratio
            [NonSerialized] public float tanSlip;          // lagged tan(slip angle); + = sliding to the car's right
            [NonSerialized] public float load;             // N
            [NonSerialized] public float grip = 1f, rolling = 0.015f;
            [NonSerialized] public SurfaceKind surface;
            [NonSerialized] public float longSlip, latSlip; // slip / peak slip (1 = top of the curve)
            [NonSerialized] public float slipSpeed;        // m/s of rubber sliding over the ground
            [NonSerialized] public float driveShare;
            [NonSerialized] public Vector3 fwd, side;      // contact frame this step
            [NonSerialized] public float vx, vy;           // contact patch velocity in that frame
            [NonSerialized] public float fx, fy;           // tyre forces this step
        }

        [Header("Rig")]
        public Wheel[] wheels = new Wheel[4];
        public Transform bodyVisual;
        public float wheelRadius = 0.36f;
        public float suspensionTravel = 0.32f;
        public Vector3 centerOfMass = new Vector3(0f, 0.35f, 0.05f);

        [Header("Handling (overwritten by the CarSpec)")]
        public float topSpeedKmh = 175f;
        public float acceleration = 10f;
        public float grip = 7f;
        public float driftGrip = 1.6f;
        public float turnRate = 95f;

        [Header("Suspension")]
        [Tooltip("Share of the travel taken by the car's weight at rest.")]
        public float staticSag = 0.45f;
        [Tooltip("Fractions of critical damping below the 0.3 m/s knee.")]
        public float bumpDamping = 0.35f, reboundDamping = 0.55f;
        [Tooltip("Anti-roll bar stiffness as a multiple of the spring rate.")]
        public float antiRollFront = 0.55f, antiRollRear = 0.5f;
        [Tooltip("Tyre forces act this far up the wheel from the contact patch (less roll, less weight transfer).")]
        public float forceLift = 0.35f;
        public float inertiaScale = 1.3f;

        [Header("Tyres")]
        public float peakSlipAngle = 10f;   // degrees
        public float peakSlipRatio = 0.12f;
        [Tooltip("Magic Formula C. 1.3 keeps about 90% of the side grip far past the peak.")]
        public float lateralShape = 1.3f;
        public float longShape = 1.5f;
        public float relaxLong = 0.35f, relaxLat = 0.5f;   // m
        [Tooltip("Slip lag never uses a speed below this (keeps the tyres calm near standstill).")]
        public float lowSpeedDamping = 8f;
        public float loadSensitivity = 0.08f;

        [Header("Drivetrain (gearing and torque come from the CarSpec)")]
        public float redlineRpm = 7200f;
        public float idleRpm = 900f;
        public float launchRpm = 4200f;
        public float engineInertia = 0.2f;
        public float wheelInertia = 1.4f;
        [Tooltip("Front axle coupling relative to the rear (rear-biased four-wheel drive).")]
        public float frontDriveWeight = 0.8f;
        [Tooltip("Viscous coupling between wheel speeds, N·m per rad/s.")]
        public float diffLock = 85f;
        public float shiftTime = 0.16f;
        public int gearCount = 5;
        public float brakeTorque = 2300f;       // per front wheel at full pedal; rears get 70 %
        public float handbrakeTorque = 1300f;   // per rear wheel

        [Header("Steering")]
        public float highSpeedSteerAngle = 15f;
        public float steerTime = 0.12f, veloSteerTime = 0.025f;
        public float releaseTime = 0.07f, veloReleaseTime = 0.02f;
        [Tooltip("Keyboard: the speed-scaled return is this much slower (art of rally uses 4), so taps add up and a slide holds after the key is released.")]
        public float keyboardReleaseScale = 2.5f;
        public float counterSteerBoost = 1.5f;
        [Tooltip("Keyboard: steering angle a held key reaches at 30 m/s and above (counter-steer can still go further).")]
        public float keyboardSteerAngle = 14f;

        [Header("Assists")]
        [Tooltip("Yaw acceleration (rad/s²) from the raw steering input at 45 m/s.")]
        public float yawAssist = 2.4f;
        [Tooltip("Yaw acceleration (rad/s²) that pulls the car out of very big slides.")]
        public float stabilityAssist = 2.2f;
        [Tooltip("Extra drag per second of speed while braking.")]
        public float brakeAssist = 0.4f;
        public float downforce = 0.0008f;
        public float airLevelRate = 30f;

        [Header("Boost")]
        public float boostAccel = 8f;
        public float boostTopSpeed = 1.2f;
        public float boostDrain = 0.33f;
        public float boostFillDrift = 0.2f;
        public float boostFillAir = 0.12f;

        /// <summary>Set every frame by the driver (player input or bot).</summary>
        [NonSerialized] public DriveInput Input;
        /// <summary>Held at the line during the countdown and after the finish.</summary>
        [NonSerialized] public bool InputLocked;
        /// <summary>Engine power multiplier (autopilot difficulty).</summary>
        [NonSerialized] public float SpeedMultiplier = 1f;
        /// <summary>Bots already produce smooth steering; skip the human smoothing.</summary>
        [NonSerialized] public bool FastSteer;

        public Rigidbody Body { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float LateralSpeed { get; private set; }
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
        public float SteerSmoothed => _steer;
        public float SteerAngle { get; private set; }
        public int GroundedWheels { get; private set; }
        public bool Grounded => GroundedWheels > 0;
        public bool OnRoad { get; private set; } = true;
        public bool IsDrifting { get; private set; }
        /// <summary>Body slip angle in degrees; + = moving to the car's right (nose pointing left of travel).</summary>
        public float DriftAngle { get; private set; }
        /// <summary>Average grip of the grounded wheels' surfaces (1 = tarmac).</summary>
        public float SurfaceGrip { get; private set; } = 1f;
        /// <summary>Fastest tyre sliding speed, m/s (skids, smoke, squeal).</summary>
        public float SlipSpeed { get; private set; }
        public float Boost { get; set; } = 0.35f;
        public bool Boosting { get; private set; }
        public float AirTime { get; private set; }
        public int Gear { get; private set; } = 1;   // 0 = reverse
        public float Rpm01 { get; private set; }
        public float Throttle01 { get; private set; }
        public bool Braking { get; private set; }
        public float TopSpeed => topSpeedKmh / 3.6f;

        public event Action<float> Impact;       // impulse magnitude / mass (m/s)
        public event Action<int> GearChanged;
        public event Action<float> Landed;        // airtime seconds

        private const int SubSteps = 5;
        private const float Efficiency = 0.9f;
        private const float RpmToRad = Mathf.PI / 30f;

        private float _steer;
        private float _springRate, _bump, _rebound;
        private float[] _ratios = new float[0];   // overall ratio per gear (index 1..gearCount)
        private float _peakTorque, _drag, _mu = 1.15f, _rearGrip = 1f, _yawStrength = 1f, _steerMax = 30f;
        private float _rearStatic = 0.5f;
        private int _gear = 1;                   // -1 reverse
        private int _nextGear = 1;
        private float _shiftTimer;
        private float _engineW;
        private bool _clutchIn;
        private float _rpmSmoothed;
        private float _keyboardTimer;
        private float _yawInput;
        private float _levelRamp;
        private float _landingWindow;
        private float _lastImpactTime;
        private Vector3 _lean;
        private Vector3 _prevVelocity;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private Collider[] _ownColliders;

        public void ApplySpec(CarSpec spec)
        {
            if (spec == null) return;
            topSpeedKmh = spec.topSpeedKmh;
            acceleration = spec.acceleration;
            grip = spec.grip;
            driftGrip = spec.driftGrip;
            turnRate = spec.turnRate;
            if (Body) Body.mass = spec.mass;
            Configure();
        }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.linearDamping = 0f;
            Body.angularDamping = 0.15f;
            Body.maxAngularVelocity = 8f;
            Body.centerOfMass = centerOfMass;
            _ownColliders = GetComponentsInChildren<Collider>();
            foreach (var w in wheels) if (w != null && w.pivot) w.restLocal = w.pivot.localPosition;
            Configure();
        }

        /// <summary>Turns the CarSpec numbers into springs, gearing, engine torque, drag and tyre grip.</summary>
        private void Configure()
        {
            if (!Body) return;
            float m = Body.mass, g = -Physics.gravity.y;
            // Inertia of the body box (the wheels are rays), scaled up a little for calmer rotation.
            var box = GetComponent<BoxCollider>();
            if (box)
            {
                Vector3 sz = box.size;
                Body.inertiaTensor = new Vector3(sz.y * sz.y + sz.z * sz.z, sz.x * sz.x + sz.z * sz.z, sz.x * sz.x + sz.y * sz.y) * (m / 12f * inertiaScale);
                Body.inertiaTensorRotation = Quaternion.identity;
            }
            _springRate = m * g / 4f / Mathf.Max(0.05f, staticSag * suspensionTravel);
            float critical = 2f * Mathf.Sqrt(_springRate * m / 4f);
            _bump = critical * bumpDamping;
            _rebound = critical * reboundDamping;

            // Gearing: first gear tops out at 30 % of top speed, top gear just past the boosted top speed.
            float vTop = Mathf.Max(10f, topSpeedKmh / 3.6f);
            float wRed = redlineRpm * RpmToRad;
            float gFirst = wRed * wheelRadius / (vTop * 0.3f);
            float gTop = wRed * wheelRadius / (vTop * boostTopSpeed * 1.02f);
            int n = Mathf.Max(2, gearCount);
            _ratios = new float[n + 1];
            for (int i = 1; i <= n; i++) _ratios[i] = gFirst * Mathf.Pow(gTop / gFirst, (i - 1f) / (n - 1f));

            // Engine: launch pull = the spec's acceleration; drag sized so the car tops out at its top speed.
            _peakTorque = m * acceleration * wheelRadius / (gFirst * Efficiency * TorqueCurve(launchRpm / redlineRpm));
            float xTop = vTop / wheelRadius * gTop / wRed;
            float fTop = _peakTorque * TorqueCurve(xTop) * gTop * Efficiency / wheelRadius;
            _drag = Mathf.Max(0.2f, (fTop - 0.015f * m * g) / (vTop * vTop));

            // Grip and character.
            _mu = Mathf.Lerp(1.05f, 1.3f, Mathf.InverseLerp(6.2f, 9.2f, grip));
            float loose = Mathf.InverseLerp(3.1f, 1.9f, driftGrip);   // 1 = the most tail-happy car
            _rearGrip = Mathf.Lerp(1.02f, 0.96f, loose);
            _yawStrength = Mathf.Lerp(0.9f, 1.15f, loose) * turnRate / 95f;
            _steerMax = Mathf.Lerp(26f, 34f, Mathf.InverseLerp(80f, 105f, turnRate));

            float fw = Mathf.Max(0f, frontDriveWeight), sum = 2f * fw + 2f;
            float zf = 0f, zr = 0f; int nf = 0, nr = 0;
            foreach (var w in wheels)
            {
                if (w == null) continue;
                w.driveShare = (w.front ? fw : 1f) / sum;
                if (w.front) { zf += w.restLocal.z; nf++; } else { zr += w.restLocal.z; nr++; }
            }
            if (nf > 0 && nr > 0)
            {
                zf /= nf; zr /= nr;
                _rearStatic = Mathf.Clamp((zf - centerOfMass.z) / Mathf.Max(0.1f, zf - zr), 0.3f, 0.7f);
            }
        }

        /// <summary>Engine torque shape over rpm / redline, 1 at the peak (about 55 % of redline).</summary>
        private static float TorqueCurve(float x)
        {
            x = Mathf.Clamp01(x);
            return (0.72f + 0.8f * x - 0.75f * x * x) / 0.9333f;
        }

        /// <summary>Magic Formula shape normalised so the peak (value 1) sits at s = 1.</summary>
        private static float Curve(float s, float c)
        {
            float b = Mathf.Tan(Mathf.PI / (2f * c));
            return Mathf.Sin(c * Mathf.Atan(b * s));
        }

        /// <summary>Puts the car down at a pose with no motion (respawn, grid).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            _steer = 0f; AirTime = 0f; _levelRamp = 0f; _landingWindow = 0f;
            ForwardSpeed = 0f; LateralSpeed = 0f; IsDrifting = false; Boosting = false; DriftAngle = 0f; SlipSpeed = 0f;
            _gear = _nextGear = 1; Gear = 1; _shiftTimer = 0f; _engineW = idleRpm * RpmToRad;
            _prevVelocity = Vector3.zero;
            foreach (var w in wheels)
            {
                w.compression = w.prevCompression = staticSag;
                w.omega = w.slipRatio = w.tanSlip = 0f;
                w.slipSpeed = w.longSlip = w.latSlip = 0f;
            }
            Physics.SyncTransforms();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var input = InputLocked ? new DriveInput { Brake = 1f, Handbrake = true } : Input;

            Vector3 v = Body.linearVelocity;
            ForwardSpeed = Vector3.Dot(v, transform.forward);
            LateralSpeed = Vector3.Dot(v, transform.right);
            float speed = v.magnitude;

            // Automatic reverse: brake at a standstill selects reverse, throttle selects first.
            float throttle = Mathf.Clamp01(input.Throttle), brake = Mathf.Clamp01(input.Brake);
            if (!InputLocked && _shiftTimer <= 0f)
            {
                if (throttle > 0.05f && _gear < 0 && ForwardSpeed > -0.5f) SetGear(1, false);
                else if (brake > 0.05f && throttle <= 0.05f && _gear > 0 && ForwardSpeed < 0.5f) SetGear(-1, false);
            }
            if (_gear < 0) { float t = throttle; throttle = brake; brake = t; }
            Throttle01 = throttle;
            Braking = brake > 0.05f && Mathf.Abs(ForwardSpeed) > 1f;

            Steering(input, dt);
            Suspension(dt);
            Tyres(input, throttle, brake, dt);
            Assists(input, throttle, brake, dt);
            Gearbox(throttle, dt);
            UpdateBoost(input, dt);

            Vector3 accel = (Body.linearVelocity - _prevVelocity) / dt;
            _prevVelocity = Body.linearVelocity;
            Vector3 localAccel = transform.InverseTransformDirection(accel);
            _lean = Vector3.Lerp(_lean, new Vector3(Mathf.Clamp(-localAccel.z * 0.12f, -2f, 2f), 0f, Mathf.Clamp(localAccel.x * 0.18f, -2.5f, 2.5f)), 1f - Mathf.Exp(-8f * dt));

            DriftAngle = speed > 3f && GroundedWheels > 0 ? Mathf.Atan2(LateralSpeed, Mathf.Abs(ForwardSpeed)) * Mathf.Rad2Deg : 0f;
            IsDrifting = speed > 9f && Mathf.Abs(DriftAngle) > 10f && GroundedWheels >= 2;
        }

        private void Steering(DriveInput input, float dt)
        {
            float raw = Mathf.Clamp(input.Steer, -1f, 1f);
            // Keyboard gives exactly 0 or ±1; any value in between means an analogue stick or wheel.
            float a = Mathf.Abs(raw);
            if (a > 0.001f && a < 0.999f) _keyboardTimer = 0f; else _keyboardTimer += dt;
            bool keyboard = _keyboardTimer > 1f;
            // Yaw help follows the raw input (instant, as in art of rally); on/off keys get a short ramp so a tap isn't a jolt.
            _yawInput = keyboard && !FastSteer ? Mathf.MoveTowards(_yawInput, raw, dt / 0.08f) : raw;

            if (FastSteer) _steer = raw;
            else
            {
                float vel = Mathf.Abs(ForwardSpeed);
                float build = 1f / (steerTime + veloSteerTime * vel);
                float release = 1f / (releaseTime + veloReleaseTime * vel * (keyboard ? keyboardReleaseScale : 1f));
                if (raw < _steer)
                {
                    float rate = _steer > 0f ? release : build;
                    if (raw < 0f && _steer > 0f) rate *= 1f + Mathf.Abs(Mathf.Abs(_steer) - a) * counterSteerBoost;
                    _steer = Mathf.Max(raw, _steer - rate * dt);
                }
                else if (raw > _steer)
                {
                    float rate = _steer < 0f ? release : build;
                    if (raw > 0f && _steer < 0f) rate *= 1f + Mathf.Abs(Mathf.Abs(_steer) - a) * counterSteerBoost;
                    _steer = Mathf.Min(raw, _steer + rate * dt);
                }
            }

            // Lock narrows with speed, but counter-steer may follow the slide so the fronts can point down the road.
            float limit = Mathf.Lerp(_steerMax, highSpeedSteerAngle, Mathf.Abs(ForwardSpeed) / 45f);
            // A held key means "turn", not "full lock": at speed, keyboard steering tops out near the tyres' best angle.
            if (keyboard && !FastSteer) limit = Mathf.Min(limit, Mathf.Lerp(_steerMax, keyboardSteerAngle, Mathf.Abs(ForwardSpeed) / 30f));
            if (Mathf.Abs(DriftAngle) > 3f && Mathf.Sign(_steer) == Mathf.Sign(DriftAngle))
                limit = Mathf.Max(limit, Mathf.Min(Mathf.Abs(DriftAngle) + 6f, 40f));
            SteerAngle = _steer * limit;
        }

        private void Suspension(float dt)
        {
            Vector3 up = transform.up;
            int grounded = 0, onRoad = 0;
            foreach (var w in wheels)
            {
                Vector3 top = transform.TransformPoint(w.restLocal + Vector3.up * (suspensionTravel * 0.5f));
                float maxDist = suspensionTravel + wheelRadius;
                w.prevCompression = w.compression;
                w.grounded = CastWheel(top, -up, maxDist, out w.hit);
                if (!w.grounded)
                {
                    w.compression = Mathf.MoveTowards(w.compression, 0f, dt * 3f);
                    w.load = 0f;
                    continue;
                }
                grounded++;
                float comp = maxDist - w.hit.distance;
                w.compression = comp / suspensionTravel;
                float compVel = (w.compression - w.prevCompression) * suspensionTravel / dt;
                float dv = Mathf.Abs(compVel);
                float damp = (dv <= 0.3f ? dv : 0.3f + (dv - 0.3f) * 0.6f) * Mathf.Sign(compVel) * (compVel > 0f ? _bump : _rebound);
                float bumpStop = comp > suspensionTravel ? (comp - suspensionTravel) * _springRate * 12f : 0f;
                w.load = Mathf.Max(0f, _springRate * Mathf.Min(comp, suspensionTravel) + damp + bumpStop);
                Surface(w);
                if (w.onRoad) onRoad++;
            }

            // Anti-roll bars move load from the extended to the compressed side of each axle.
            for (int axle = 0; axle < 2; axle++)
            {
                Wheel l = null, r = null;
                foreach (var w in wheels) if (w.front == (axle == 0)) { if (w.left) l = w; else r = w; }
                if (l == null || r == null || !l.grounded || !r.grounded) continue;
                float f = (l.compression - r.compression) * suspensionTravel * _springRate * (axle == 0 ? antiRollFront : antiRollRear);
                f = Mathf.Clamp(f, -r.load, l.load);
                l.load = Mathf.Max(0f, l.load + f);
                r.load = Mathf.Max(0f, r.load - f);
            }

            foreach (var w in wheels)
                if (w.grounded) Body.AddForceAtPosition(w.hit.normal * w.load, w.hit.point);

            GroundedWheels = grounded;
            OnRoad = grounded == 0 || onRoad * 2 >= grounded;
            float gripSum = 0f;
            foreach (var w in wheels) if (w.grounded) gripSum += w.grip;
            SurfaceGrip = grounded > 0 ? gripSum / grounded : SurfaceGrip;
        }

        /// <summary>
        /// Surface grip and rolling drag, relative to tarmac, from art of rally's surface table (tarmac 1.08,
        /// gravel 0.97, snow 0.96, snow off-road 0.78, grass 0.75). Loose surfaces mostly differ by rolling drag,
        /// dust and sound rather than by huge grip gaps. The ground's physics material names the surface.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<Collider, SurfaceKind> SurfaceOf = new System.Collections.Generic.Dictionary<Collider, SurfaceKind>();

        private static void Surface(Wheel w)
        {
            var c = w.hit.collider;
            // Names allocate, so each collider's surface is worked out once.
            if (!SurfaceOf.TryGetValue(c, out var k))
            {
                var pm = c.sharedMaterial;
                string n = pm ? pm.name : "";
                if (n.StartsWith("Surface_") && Enum.TryParse(n.Substring(8), out SurfaceKind parsed)) k = parsed;
                else if (c is TerrainCollider || c.name.StartsWith("Terrain")) k = SurfaceKind.Grass;
                else if (c.name.StartsWith("Verge")) k = SurfaceKind.Verge;
                else k = SurfaceKind.Tarmac;
                SurfaceOf[c] = k;
            }
            w.surface = k;
            w.onRoad = k == SurfaceKind.Tarmac || k == SurfaceKind.Gravel || k == SurfaceKind.Snow || k == SurfaceKind.Verge;
            switch (k)
            {
                case SurfaceKind.Gravel: w.grip = 0.9f; w.rolling = 0.035f; break;
                case SurfaceKind.Snow: w.grip = 0.86f; w.rolling = 0.04f; break;
                case SurfaceKind.Verge: w.grip = 0.86f; w.rolling = 0.045f; break;
                case SurfaceKind.Grass: w.grip = 0.72f; w.rolling = 0.06f; break;
                case SurfaceKind.Sand: w.grip = 0.74f; w.rolling = 0.07f; break;
                case SurfaceKind.DeepSnow: w.grip = 0.72f; w.rolling = 0.065f; break;
                default: w.grip = 1f; w.rolling = 0.015f; break;
            }
        }

        private bool CastWheel(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, _hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.rigidbody == Body || IsOwn(h.collider)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
            }
            return found;
        }

        private bool IsOwn(Collider c)
        {
            foreach (var o in _ownColliders) if (o == c) return true;
            return false;
        }

        private float OverallRatio => _gear > 0 ? _ratios[Mathf.Min(_gear, _ratios.Length - 1)] : _gear < 0 ? -_ratios[1] : 0f;

        private void Tyres(DriveInput input, float throttle, float brake, float dt)
        {
            float g = -Physics.gravity.y;
            float staticLoad = Body.mass * g / 4f;
            float G = OverallRatio;
            bool handbrake = input.Handbrake;
            bool clutchIn = _shiftTimer <= 0f && _gear != 0 && !handbrake;
            _clutchIn = clutchIn;

            // Engine speed follows the driven wheels; at launch the clutch slips and holds it at the launch rpm.
            float wheelW = 0f;
            foreach (var w in wheels) wheelW += w.omega * w.driveShare;
            float floorW = (idleRpm + (launchRpm - idleRpm) * throttle) * RpmToRad;
            float redW = redlineRpm * RpmToRad;
            if (clutchIn) _engineW = Mathf.Max(Mathf.Abs(wheelW * G), floorW);
            else _engineW = Mathf.Lerp(_engineW, Mathf.Lerp(idleRpm * RpmToRad, redW, throttle), 1f - Mathf.Exp(-6f * dt));
            float x = _engineW / redW;
            float engineT = x >= 1f ? 0f : throttle * _peakTorque * TorqueCurve(x) * SpeedMultiplier;
            float driveT = clutchIn ? engineT * G * Efficiency : 0f;
            float engineBrakeT = clutchIn && throttle < 0.05f ? 0.18f * _peakTorque * x * Mathf.Abs(G) * Efficiency : 0f;
            float engineI = clutchIn ? engineInertia * G * G : 0f;

            // Rear wheels carry less load under braking or lift-off: let them step out more easily (small slip angles only).
            float totalLoad = 0f, rearLoad = 0f;
            foreach (var w in wheels) { totalLoad += w.load; if (!w.front) rearLoad += w.load; }
            float factor = totalLoad > 1f ? Mathf.Clamp(rearLoad / totalLoad / _rearStatic, 0.25f, 1f) : 1f;

            Vector3 carUp = transform.up;
            foreach (var w in wheels)
            {
                w.fx = w.fy = 0f;
                Vector3 n = w.grounded ? w.hit.normal : carUp;
                Vector3 heading = w.front ? Quaternion.AngleAxis(SteerAngle, carUp) * transform.forward : transform.forward;
                w.fwd = Vector3.ProjectOnPlane(heading, n).normalized;
                w.side = Vector3.Cross(n, w.fwd);
                if (w.grounded)
                {
                    Vector3 vc = Body.GetPointVelocity(w.hit.point);
                    w.vx = Vector3.Dot(vc, w.fwd);
                    w.vy = Vector3.Dot(vc, w.side);
                }
                else { w.vx = w.vy = 0f; w.slipRatio = w.tanSlip = 0f; }
            }

            float h = dt / SubSteps;
            float peakAlpha = peakSlipAngle * Mathf.Deg2Rad;
            for (int s = 0; s < SubSteps; s++)
            {
                float avgW = 0f;
                foreach (var w in wheels) avgW += w.omega * 0.25f;
                foreach (var w in wheels)
                {
                    float inertia = wheelInertia + engineI * w.driveShare;
                    float torque = driveT * w.driveShare + (clutchIn ? diffLock * (avgW - w.omega) : 0f);
                    float fx = 0f, fy = 0f;
                    if (w.grounded && w.load > 0f)
                    {
                        float damp = Mathf.Max(Mathf.Abs(w.vx), lowSpeedDamping);
                        float lag = Mathf.Max(1f, Mathf.Abs(w.vx) * 3.6f * 0.02f);
                        w.slipRatio = Mathf.Clamp(w.slipRatio + (w.omega * wheelRadius - w.vx - damp * w.slipRatio) / (relaxLong * lag) * h, -1.5f, 1.5f);
                        w.tanSlip += (w.vy - damp * w.tanSlip) / (relaxLat * lag) * h;
                        float alpha = Mathf.Atan(w.tanSlip);
                        if (!w.front && factor < 1f && Mathf.Abs(alpha) < peakAlpha * 0.5f)
                            alpha /= (Mathf.Abs(alpha) * (1f - factor) + peakAlpha * 0.5f * factor) / (peakAlpha * 0.5f);

                        float sx = w.slipRatio / peakSlipRatio, sy = alpha / peakAlpha;
                        float rho = Mathf.Max(Mathf.Sqrt(sx * sx + sy * sy), 1e-4f);
                        float mu = _mu * w.grip * Mathf.Clamp(1f - loadSensitivity * (w.load / staticLoad - 1f), 0.75f, 1.15f);
                        fx = sx / rho * Curve(rho, longShape) * mu * 0.97f * w.load;
                        fy = -sy / rho * Curve(rho, lateralShape) * mu * w.load * (w.front ? 1f : _rearGrip);
                        w.longSlip = sx; w.latSlip = sy;
                        torque -= fx * wheelRadius;
                    }
                    w.omega += torque / inertia * h;

                    // Friction torques (brakes, handbrake, rolling drag, engine braking) slow the wheel but never reverse it.
                    float bt = brake * brakeTorque * (w.front ? 1f : 0.7f);
                    if (bt > 0f && Mathf.Abs(w.longSlip) > 1.15f && Mathf.Abs(w.vx) > 3f) bt *= 0.35f;   // ABS
                    if (handbrake && !w.front) bt += handbrakeTorque;
                    bt += w.rolling * w.load * wheelRadius + engineBrakeT * w.driveShare;
                    float dw = bt * h / inertia;
                    w.omega = Mathf.Abs(w.omega) > dw ? w.omega - Mathf.Sign(w.omega) * dw : 0f;

                    w.fx += fx / SubSteps;
                    w.fy += fy / SubSteps;
                }
            }

            float slipMax = 0f;
            foreach (var w in wheels)
            {
                if (!w.grounded) { w.slipSpeed = 0f; w.longSlip = w.latSlip = 0f; continue; }
                Vector3 at = w.hit.point + w.hit.normal * (wheelRadius * forceLift);
                Body.AddForceAtPosition(w.fwd * w.fx + w.side * w.fy, at);
                float ls = w.omega * wheelRadius - w.vx;
                w.slipSpeed = Mathf.Sqrt(ls * ls + w.vy * w.vy);
                slipMax = Mathf.Max(slipMax, w.slipSpeed);
            }
            SlipSpeed = slipMax;

            // Aero drag on the whole car.
            Vector3 vel = Body.linearVelocity;
            Body.AddForce(-vel * vel.magnitude * (_drag / Mathf.Max(0.3f, SpeedMultiplier)));
        }

        private void Assists(DriveInput input, float throttle, float brake, float dt)
        {
            Vector3 up = transform.up;
            Vector3 v = Body.linearVelocity;
            float vF = ForwardSpeed;
            bool allDown = GroundedWheels == 4;

            if (allDown && Mathf.Abs(vF) > 35f / 3.6f && !InputLocked)
            {
                // Yaw from the raw steering input: rotation answers at once even though the wheels turn slowly.
                // Steering further into a slide that is already rotating the car gets less help (no over-rotation).
                float steer = _yawInput;
                float into = Mathf.Sign(steer) == -Mathf.Sign(DriftAngle) ? Mathf.Clamp01(1f - (Mathf.Abs(DriftAngle) - 10f) / 20f) : 1f;
                float yaw = steer * into * Mathf.Clamp01(Mathf.Abs(vF) / 45f) * yawAssist * _yawStrength * Mathf.Sign(vF);
                Body.AddTorque(up * yaw, ForceMode.Acceleration);
            }

            if (GroundedWheels >= 2 && vF > 7f)
            {
                // Pull the nose back toward the direction of travel, harder the bigger the slide.
                float slip = 0f;
                foreach (var w in wheels) if (w.grounded && Mathf.Abs(w.latSlip) > Mathf.Abs(slip)) slip = w.latSlip;
                if (Mathf.Abs(slip) > 0.05f)
                    Body.AddTorque(up * (Mathf.Sign(slip) * Mathf.Clamp01(Mathf.Abs(slip) / 6f) * stabilityAssist), ForceMode.Acceleration);
            }

            if (allDown && brake > 0.1f && _gear > 0 && vF > 3f)
            {
                Vector3 flat = Vector3.ProjectOnPlane(v, up);
                Body.AddForce(-flat * (brakeAssist * brake), ForceMode.Acceleration);
            }

            if (GroundedWheels > 0) Body.AddForce(-up * (downforce * v.sqrMagnitude), ForceMode.Acceleration);

            // Parked or held: stop the creep (static friction stand-in).
            if (GroundedWheels >= 3 && v.magnitude < 1.2f && throttle < 0.05f && (brake > 0.05f || input.Handbrake))
            {
                Vector3 n = Vector3.zero;
                foreach (var w in wheels) if (w.grounded) n += w.hit.normal;
                n.Normalize();
                Body.AddForce(-Vector3.ProjectOnPlane(Physics.gravity, n) - Vector3.ProjectOnPlane(v, n) * 6f, ForceMode.Acceleration);
            }

            // Air: level out toward the heading when well clear of the ground, then stick the landing.
            if (GroundedWheels == 0)
            {
                AirTime += dt;
                bool clear = !Physics.Raycast(Body.position, Vector3.down, 0.5f + wheelRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (clear)
                {
                    _levelRamp = Mathf.Lerp(_levelRamp, 1f, dt * 3f);
                    Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                    if (flatFwd.sqrMagnitude > 1e-4f)
                    {
                        float tilt = Vector3.Angle(transform.up, Vector3.up);
                        Body.MoveRotation(Quaternion.RotateTowards(Body.rotation, Quaternion.LookRotation(flatFwd, Vector3.up),
                            Mathf.Min(airLevelRate, tilt * 1.5f) * dt * _levelRamp));
                    }
                }
                else _levelRamp = 0f;
            }
            else
            {
                _levelRamp = 0f;
                if (AirTime > 0.3f)
                {
                    Landed?.Invoke(AirTime);
                    _landingWindow = 0.15f;
                }
                AirTime = 0f;
            }
            if (_landingWindow > 0f)
            {
                _landingWindow -= dt;
                if (allDown)
                {
                    _landingWindow = 0f;
                    if (Body.angularVelocity.magnitude < 0.52f)
                    {
                        Body.angularVelocity *= 0.5f;
                        float upV = Vector3.Dot(Body.linearVelocity, Vector3.up);
                        if (upV > 0f) Body.linearVelocity -= Vector3.up * (upV * 0.9f);
                    }
                }
            }
        }

        private void Gearbox(float throttle, float dt)
        {
            float rpm = _engineW / RpmToRad;
            if (_shiftTimer > 0f)
            {
                _shiftTimer -= dt;
                if (_shiftTimer <= shiftTime * 0.5f && _gear != _nextGear) _gear = _nextGear;
            }
            else if (_gear > 0 && GroundedWheels >= 2 && _clutchIn)
            {
                float spin = 0f;
                foreach (var w in wheels) spin = Mathf.Max(spin, Mathf.Abs(w.longSlip));
                if (_gear < _ratios.Length - 1 && rpm > redlineRpm * 0.93f && spin < 1.6f) SetGear(_gear + 1, true);
                else if (_gear > 1 && rpm < redlineRpm * 0.55f) SetGear(_gear - 1, true);
            }

            int shown = _gear < 0 ? 0 : _gear;
            if (shown != Gear)
            {
                if (shown > Gear && shown > 1) GearChanged?.Invoke(shown);
                Gear = shown;
            }
            float target = Mathf.Clamp01(_engineW / (redlineRpm * RpmToRad));
            _rpmSmoothed = Mathf.Lerp(_rpmSmoothed, target, 1f - Mathf.Exp(-14f * dt));
            Rpm01 = _rpmSmoothed;
        }

        private void SetGear(int gear, bool timed)
        {
            _nextGear = gear;
            if (timed) _shiftTimer = shiftTime;
            else _gear = gear;
        }

        private void UpdateBoost(DriveInput input, float dt)
        {
            if (IsDrifting) Boost += boostFillDrift * Mathf.Clamp01((Mathf.Abs(DriftAngle) - 8f) / 25f) * dt;
            if (!Grounded && AirTime > 0.3f) Boost += boostFillAir * dt;
            Boosting = !InputLocked && input.Boost && Boost > 0.01f && ForwardSpeed > -1f;
            if (Boosting)
            {
                Boost -= boostDrain * dt;
                if (GroundedWheels >= 2)
                {
                    float room = Mathf.Clamp01((TopSpeed * boostTopSpeed - ForwardSpeed) / (TopSpeed * 0.1f));
                    Body.AddForce(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * (boostAccel * room), ForceMode.Acceleration);
                }
            }
            Boost = Mathf.Clamp01(Boost);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            foreach (var w in wheels)
            {
                if (w.pivot == null) continue;
                float drop = suspensionTravel * (0.5f - Mathf.Min(w.compression, 1f));
                w.pivot.localPosition = w.restLocal + Vector3.down * drop;
                w.spin = Mathf.Repeat(w.spin + w.omega * Mathf.Rad2Deg * dt, 360f);
                w.pivot.localRotation = Quaternion.Euler(w.spin, w.front ? SteerAngle : 0f, 0f);
            }
            if (bodyVisual) bodyVisual.localRotation = Quaternion.Euler(_lean.x, 0f, _lean.z);
        }

        private void OnCollisionEnter(Collision c)
        {
            float strength = c.impulse.magnitude / Mathf.Max(1f, Body.mass);
            if (strength > 1.2f && Time.time - _lastImpactTime > 0.15f)
            {
                _lastImpactTime = Time.time;
                Impact?.Invoke(strength);
            }
        }

        /// <summary>True when lying on its side or roof (for auto-respawn).</summary>
        public bool IsUpsideDown => Vector3.Dot(transform.up, Vector3.up) < 0.25f;
    }
}
