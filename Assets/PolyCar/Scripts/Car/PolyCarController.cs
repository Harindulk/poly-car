using System;
using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Arcade car on raycast suspension. The handling model comes from Drive Away: speed eases toward a
    /// target, the yaw rate follows steer x turning speed, and sideways velocity decays by grip (low grip =
    /// drift). Handbrake or holding full lock at speed drops into drift grip; drifting fills the boost meter.
    /// Physics runs in FixedUpdate with an interpolated Rigidbody, so motion stays smooth at any frame rate.
    /// </summary>
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
            [NonSerialized] public float compression;
            [NonSerialized] public float prevCompression;
            [NonSerialized] public float spin;
            [NonSerialized] public bool onRoad;
        }

        [Header("Rig")]
        public Wheel[] wheels = new Wheel[4];
        public Transform bodyVisual;
        public float wheelRadius = 0.36f;
        public float suspensionTravel = 0.32f;
        [Tooltip("Spring force at full compression, in multiples of one wheel's static load.")]
        public float springStrength = 2.3f;
        [Tooltip("Damper, per second (multiplied by a quarter of the mass).")]
        public float damper = 9f;
        public float antiRoll = 0.8f;
        public Vector3 centerOfMass = new Vector3(0f, 0.35f, 0.05f);

        [Header("Handling (overwritten by the CarSpec)")]
        public float topSpeedKmh = 175f;
        public float acceleration = 10f;
        public float grip = 7f;
        public float driftGrip = 1.6f;
        public float turnRate = 95f;

        [Header("Feel")]
        public float brakeDecel = 24f;
        public float reverseAccel = 7f;
        public float maxReverse = 11f;
        public float coastDecel = 2.2f;
        public float handbrakeDecel = 4f;
        public float steerBuildUp = 4.5f;
        public float steerRelease = 9f;
        public float yawResponse = 8f;
        public float driftYawResponse = 5f;
        public float driftYawBoost = 1.05f;
        public float driftMomentum = 0.45f;
        public float offRoadSpeed = 0.72f;
        public float offRoadGrip = 0.65f;
        public float downforce = 0.0016f;
        public float airGravity = 6f;
        public float airControl = 2.2f;
        public float uprightStrength = 7f;

        [Header("Boost")]
        public float boostAccel = 9f;
        public float boostTopSpeed = 1.22f;
        public float boostDrain = 0.33f;
        public float boostFillDrift = 0.2f;
        public float boostFillAir = 0.12f;

        /// <summary>Set every frame by the driver (player input or bot).</summary>
        [NonSerialized] public DriveInput Input;
        /// <summary>Held at the line during the countdown and after the finish.</summary>
        [NonSerialized] public bool InputLocked;
        /// <summary>AI difficulty / catch-up multiplier on top speed.</summary>
        [NonSerialized] public float SpeedMultiplier = 1f;
        /// <summary>Holding full lock at speed drops into drift grip (players); bots turn this off.</summary>
        [NonSerialized] public bool AutoDrift = true;
        /// <summary>Bots already produce smooth steering; skip the human smoothing so they don't lag and weave.</summary>
        [NonSerialized] public bool FastSteer;

        public Rigidbody Body { get; private set; }
        public float ForwardSpeed { get; private set; }
        public float LateralSpeed { get; private set; }
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
        public float SteerSmoothed => _steer;
        public int GroundedWheels { get; private set; }
        public bool Grounded => GroundedWheels > 0;
        public bool OnRoad { get; private set; } = true;
        public bool IsDrifting { get; private set; }
        public float DriftAngle { get; private set; }
        public float Boost { get; set; } = 0.35f;
        public bool Boosting { get; private set; }
        public float AirTime { get; private set; }
        public int Gear { get; private set; } = 1;
        public float Rpm01 { get; private set; }
        public float Throttle01 { get; private set; }
        public bool Braking { get; private set; }
        public float TopSpeed => topSpeedKmh / 3.6f;

        public event Action<float> Impact;       // impulse magnitude / mass (m/s)
        public event Action<int> GearChanged;
        public event Action<float> Landed;        // airtime seconds

        private float _steer;
        private float _gripNow;
        private bool _driftActive;
        private float _fullLockTime;
        private float _lastImpactTime;
        private Vector3 _lean;
        private Vector3 _prevVelocity;
        private float _rpmSmoothed;
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
        }

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Body.linearDamping = 0f;
            Body.angularDamping = 0.6f;
            Body.maxAngularVelocity = 8f;
            Body.centerOfMass = centerOfMass;
            _ownColliders = GetComponentsInChildren<Collider>();
            foreach (var w in wheels) if (w != null && w.pivot) w.restLocal = w.pivot.localPosition;
            _gripNow = grip;
        }

        /// <summary>Puts the car down at a pose with no motion (respawn, grid).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            _steer = 0f; _driftActive = false; _fullLockTime = 0f; AirTime = 0f;
            ForwardSpeed = 0f; LateralSpeed = 0f; IsDrifting = false; Boosting = false;
            _prevVelocity = Vector3.zero;
            foreach (var w in wheels) { w.compression = w.prevCompression = 0.45f; }
            Physics.SyncTransforms();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var input = InputLocked ? default : Input;

            // Steering wheel: builds up gently (slower at speed), recentres quickly, counter-steer is fast,
            // and while drifting the keyboard recentres slowly so a slide holds when the key is released.
            float target = Mathf.Clamp(input.Steer, -1f, 1f);
            float speedAbs0 = Mathf.Abs(ForwardSpeed);
            bool building = Mathf.Abs(target) > Mathf.Abs(_steer) && (Mathf.Sign(target) == Mathf.Sign(_steer) || Mathf.Abs(_steer) < 0.05f);
            bool counter = Mathf.Abs(target) > 0.1f && Mathf.Abs(LateralSpeed) > 2f && Mathf.Sign(target) == Mathf.Sign(LateralSpeed);
            float rate = building ? steerBuildUp / (1f + speedAbs0 * 0.03f) : steerRelease;
            if (counter) rate *= 1.6f;
            if (!building && Mathf.Abs(target) < 0.05f && _driftActive) rate *= 0.35f;
            if (FastSteer) rate = 12f;
            _steer = Mathf.MoveTowards(_steer, target, rate * dt);

            Suspension(dt);

            Vector3 v = Body.linearVelocity;
            if (GroundedWheels > 0) GroundDrive(input, dt, ref v);
            else AirDrive(input, dt);

            UpdateBoost(input, dt);
            UpdateGearbox(input, dt);

            Vector3 accel = (Body.linearVelocity - _prevVelocity) / dt;
            _prevVelocity = Body.linearVelocity;
            Vector3 localAccel = transform.InverseTransformDirection(accel);
            _lean = Vector3.Lerp(_lean, new Vector3(Mathf.Clamp(-localAccel.z * 0.25f, -3.5f, 3.5f), 0f, Mathf.Clamp(localAccel.x * 0.35f, -5f, 5f)), 1f - Mathf.Exp(-8f * dt));
        }

        private void Suspension(float dt)
        {
            float staticLoad = Body.mass * -Physics.gravity.y / 4f;
            Vector3 up = transform.up;
            int grounded = 0, onRoad = 0;
            foreach (var w in wheels)
            {
                Vector3 top = transform.TransformPoint(w.restLocal + Vector3.up * (suspensionTravel * 0.5f));
                float maxDist = suspensionTravel + wheelRadius;
                w.prevCompression = w.compression;
                w.grounded = CastWheel(top, -up, maxDist, out w.hit);
                if (w.grounded)
                {
                    grounded++;
                    w.compression = Mathf.Clamp((maxDist - w.hit.distance) / suspensionTravel, 0f, 1.3f);
                    w.onRoad = !(w.hit.collider is TerrainCollider);
                    if (w.onRoad) onRoad++;
                    float compressionSpeed = (w.compression - w.prevCompression) * suspensionTravel / dt;
                    float force = staticLoad * springStrength * w.compression + Body.mass * 0.25f * damper * compressionSpeed;
                    if (force > 0f) Body.AddForceAtPosition(up * force, top);
                }
                else
                {
                    w.compression = Mathf.MoveTowards(w.compression, 0f, dt * 3f);
                }
            }

            // Anti-roll: lift the compressed side of each axle.
            for (int axle = 0; axle < 2; axle++)
            {
                Wheel l = null, r = null;
                foreach (var w in wheels) if (w.front == (axle == 0)) { if (w.left) l = w; else r = w; }
                if (l == null || r == null || !l.grounded || !r.grounded) continue;
                float ar = (l.compression - r.compression) * antiRoll * staticLoad;
                Body.AddForceAtPosition(up * ar, l.pivot.position);
                Body.AddForceAtPosition(-up * ar, r.pivot.position);
            }

            GroundedWheels = grounded;
            OnRoad = grounded == 0 || onRoad * 2 >= grounded;
        }

        private bool CastWheel(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
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

        private void GroundDrive(DriveInput input, float dt, ref Vector3 v)
        {
            float groundFactor = GroundedWheels / 4f;
            Vector3 normal = Vector3.zero;
            foreach (var w in wheels) if (w.grounded) normal += w.hit.normal;
            Vector3 up = normal.sqrMagnitude > 1e-4f ? normal.normalized : transform.up;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, fwd);

            float vF = Vector3.Dot(v, fwd), vR = Vector3.Dot(v, right), vU = Vector3.Dot(v, up);
            float speedAbs = Mathf.Abs(vF);
            float top = TopSpeed * SpeedMultiplier * (OnRoad ? 1f : offRoadSpeed) * (Boosting ? boostTopSpeed : 1f);

            // --- Longitudinal: speed eases toward the target.
            float a = 0f;
            Braking = false;
            Throttle01 = input.Throttle;
            if (InputLocked)
            {
                a = -Mathf.Sign(vF) * Mathf.Min(speedAbs / dt, brakeDecel);
                Braking = true;
            }
            else
            {
                if (input.Throttle > 0.05f)
                {
                    if (vF < -1f) { a += input.Throttle * brakeDecel; Braking = true; }
                    else
                    {
                        float x = Mathf.Clamp01(vF / Mathf.Max(1f, top));
                        a += input.Throttle * acceleration * (1f - Mathf.Pow(x, 2.2f)) * (OnRoad ? 1f : 0.8f);
                    }
                }
                if (input.Brake > 0.05f)
                {
                    if (vF > 1f) { a -= input.Brake * brakeDecel; Braking = true; }
                    else if (vF > -maxReverse) a -= input.Brake * reverseAccel;
                }
                if (input.Throttle <= 0.05f && input.Brake <= 0.05f)
                    a -= Mathf.Sign(vF) * Mathf.Min(speedAbs / dt, coastDecel + 0.0006f * vF * vF);
                if (input.Handbrake)
                    a -= Mathf.Sign(vF) * Mathf.Min(speedAbs / dt, handbrakeDecel);
                if (Boosting && vF > -1f) a += boostAccel;
                if (vF > top) a -= (vF - top) * 1.5f;
            }
            vF += a * dt * groundFactor;

            // --- Drift state: handbrake, or holding full lock at speed for a moment.
            _fullLockTime = AutoDrift && Mathf.Abs(_steer) > 0.9f && speedAbs > 22f ? _fullLockTime + dt : 0f;
            if ((input.Handbrake && speedAbs > 9f) || _fullLockTime > 0.7f) _driftActive = true;
            if (_driftActive && ((!input.Handbrake && Mathf.Abs(vR) < 1.6f && _fullLockTime <= 0f) || speedAbs < 5f)) _driftActive = false;

            float gripTarget = (_driftActive ? driftGrip : grip) * (OnRoad ? 1f : offRoadGrip);
            _gripNow = Mathf.Lerp(_gripNow, gripTarget, 1f - Mathf.Exp(-(gripTarget > _gripNow ? 3.5f : 10f) * dt));

            // --- Lateral: sideways slip decays by grip; some of it carries into forward speed while drifting.
            float vRNew = vR * Mathf.Exp(-_gripNow * groundFactor * dt);
            if (_driftActive && vF > 0f) vF += (Mathf.Abs(vR) - Mathf.Abs(vRNew)) * driftMomentum;
            vR = vRNew;

            Body.linearVelocity = fwd * vF + right * vR + up * vU;

            // --- Yaw: rate follows steer x turning speed, scaled by speed.
            float authority = Mathf.Clamp01(speedAbs / 7f) * Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(speedAbs / Mathf.Max(1f, TopSpeed)));
            float yawTarget = _steer * turnRate * Mathf.Deg2Rad * authority * (vF >= -0.5f ? 1f : -1f);
            if (_driftActive) yawTarget *= driftYawBoost;
            Vector3 ang = Body.angularVelocity;
            float yawNow = Vector3.Dot(ang, up);
            float resp = _driftActive ? driftYawResponse : yawResponse;
            // Stability: past ~38 degrees of slip, steer the nose back toward the travel direction (anti-spin).
            float slipDeg = speedAbs > 4f ? Mathf.Atan2(vR, Mathf.Abs(vF)) * Mathf.Rad2Deg : 0f;
            if (Mathf.Abs(slipDeg) > 38f) yawTarget += Mathf.Sign(slipDeg) * (Mathf.Abs(slipDeg) - 38f) * 0.09f;
            float yawNew = Mathf.Lerp(yawNow, yawTarget, 1f - Mathf.Exp(-resp * groundFactor * dt));
            Body.angularVelocity = ang + up * (yawNew - yawNow);

            // Downforce keeps it planted at speed.
            Body.AddForce(-up * (downforce * vF * vF), ForceMode.Acceleration);

            ForwardSpeed = vF;
            LateralSpeed = vR;
            DriftAngle = speedAbs > 3f ? Mathf.Atan2(vR, Mathf.Abs(vF)) * Mathf.Rad2Deg : 0f;
            IsDrifting = speedAbs > 9f && Mathf.Abs(vR) > 2.6f && GroundedWheels >= 2;

            if (AirTime > 0.35f)
            {
                // Landing stick: kill most of the bounce and half the tumble so jumps land clean.
                Vector3 lv = Body.linearVelocity;
                float upV = Vector3.Dot(lv, up);
                if (upV > 0f) Body.linearVelocity = lv - up * (upV * 0.9f);
                Vector3 av = Body.angularVelocity;
                Vector3 yawPart = up * Vector3.Dot(av, up);
                Body.angularVelocity = yawPart + (av - yawPart) * 0.5f;
                Landed?.Invoke(AirTime);
            }
            AirTime = 0f;
        }

        private void AirDrive(DriveInput input, float dt)
        {
            AirTime += dt;
            IsDrifting = false;
            Braking = false;
            Body.AddForce(Vector3.down * airGravity, ForceMode.Acceleration);

            // Gentle yaw control and self-levelling so jumps land on the wheels.
            Vector3 ang = Body.angularVelocity;
            float yawNow = Vector3.Dot(ang, Vector3.up);
            float yawTarget = _steer * airControl;
            Body.angularVelocity = ang + Vector3.up * (Mathf.Lerp(yawNow, yawTarget, 1f - Mathf.Exp(-3f * dt)) - yawNow);
            Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
            Body.AddTorque(axis * uprightStrength - Vector3.ProjectOnPlane(Body.angularVelocity, Vector3.up) * 1.5f, ForceMode.Acceleration);

            Vector3 v = Body.linearVelocity;
            ForwardSpeed = Vector3.Dot(v, transform.forward);
            LateralSpeed = Vector3.Dot(v, transform.right);
        }

        private void UpdateBoost(DriveInput input, float dt)
        {
            if (IsDrifting) Boost += boostFillDrift * Mathf.Clamp01(Mathf.Abs(LateralSpeed) / 6f) * dt;
            if (!Grounded && AirTime > 0.3f) Boost += boostFillAir * dt;
            Boosting = !InputLocked && input.Boost && Boost > 0.01f && ForwardSpeed > -1f;
            if (Boosting) Boost -= boostDrain * dt;
            Boost = Mathf.Clamp01(Boost);
        }

        private void UpdateGearbox(DriveInput input, float dt)
        {
            // Fake six-speed box for the engine sound and HUD.
            float s = Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, TopSpeed * boostTopSpeed);
            float[] up = { 0f, 0.15f, 0.29f, 0.45f, 0.62f, 0.8f, 2f };
            int gear = 1;
            for (int g = 1; g <= 6; g++) if (s >= up[g - 1]) gear = g;
            if (ForwardSpeed < -0.5f) gear = 0;
            if (gear != Gear)
            {
                if (gear > Gear && gear > 1) GearChanged?.Invoke(gear);
                Gear = gear;
            }
            float lo = gear <= 1 ? 0f : up[gear - 1], hi = gear <= 0 ? 0.2f : up[gear];
            float inGear = gear == 0 ? Mathf.Clamp01(-ForwardSpeed / maxReverse) : Mathf.Clamp01((s - lo) / Mathf.Max(0.01f, hi - lo));
            float target = Mathf.Lerp(0.22f, 1f, inGear);
            if (!Grounded) target = Mathf.Lerp(target, input.Throttle > 0.1f ? 1f : 0.3f, 0.6f);
            if (IsDrifting && input.Throttle > 0.1f) target = Mathf.Max(target, 0.75f);
            if (input.Throttle < 0.05f && Grounded) target *= 0.85f;
            _rpmSmoothed = Mathf.Lerp(_rpmSmoothed, target, 1f - Mathf.Exp(-10f * dt));
            Rpm01 = _rpmSmoothed;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float speedNorm = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(1f, TopSpeed));
            float steerAngle = _steer * Mathf.Lerp(32f, 12f, speedNorm);
            foreach (var w in wheels)
            {
                if (w.pivot == null) continue;
                float drop = suspensionTravel * (0.5f - Mathf.Min(w.compression, 1f));
                w.pivot.localPosition = w.restLocal + Vector3.down * drop;
                float roll = (w.grounded || Mathf.Abs(ForwardSpeed) > 0.2f ? ForwardSpeed : 0f);
                if (Braking && !w.front && Input.Handbrake) roll = 0f;
                w.spin = Mathf.Repeat(w.spin + roll / Mathf.Max(0.1f, wheelRadius) * Mathf.Rad2Deg * dt, 360f);
                w.pivot.localRotation = Quaternion.Euler(w.spin, w.front ? steerAngle : 0f, 0f);
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
