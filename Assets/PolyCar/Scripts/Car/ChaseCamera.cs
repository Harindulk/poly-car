using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Race camera. Default "Rally" modes follow the art of rally recipe: a high, telephoto view that sits
    /// a fixed distance behind and a fixed height above the car, turns with the car's heading through a
    /// gentle lerp (fading out while the car spins, so spins don't whip the view), aims slightly above the
    /// car, widens the lens a little with speed and pulls in when terrain blocks the view.
    /// Also a low Chase camera and a Hood camera. C cycles, Q looks back.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ChaseCamera : MonoBehaviour
    {
        public PolyCarController target;

        private struct Preset
        {
            public float distance, height, aimAbove, fov, yawDamping;
            public Preset(float distance, float height, float aimAbove, float fov, float yawDamping)
            { this.distance = distance; this.height = height; this.aimAbove = aimAbove; this.fov = fov; this.yawDamping = yawDamping; }
        }

        // Rally: ~25 degrees down on the car, 42 degree lens. Rally Far: higher, more of the road ahead.
        private static readonly Preset Rally = new Preset(17f, 7.2f, 1.6f, 42f, 2.4f);
        private static readonly Preset RallyFar = new Preset(26f, 12.5f, 2.2f, 40f, 1.9f);
        private static readonly Preset Chase = new Preset(7.2f, 2.6f, 1.2f, 60f, 6f);

        private Camera _cam;
        private CameraMode _mode;
        private float _yaw;
        private float _height;
        private float _distance;
        private float _fov, _fovVel;
        private float _shake;
        private bool _snap = true;

        public CameraMode Mode => _mode;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _mode = (CameraMode)Mathf.Clamp(GameSettings.Current.cameraMode, 0, 3);
        }

        public void SetTarget(PolyCarController car)
        {
            target = car;
            if (car) car.Impact += s => { if (GameSettings.Current.cameraShake) _shake = Mathf.Min(1f, _shake + s * 0.06f); };
            Snap();
        }

        public void Snap() => _snap = true;

        public void CycleMode()
        {
            _mode = (CameraMode)(((int)_mode + 1) % 4);
            GameSettings.Current.cameraMode = (int)_mode;
            GameSettings.Current.Save();
            Snap();
        }

        private Preset Current => _mode == CameraMode.RallyFar ? RallyFar : _mode == CameraMode.Chase ? Chase : Rally;

        private void LateUpdate()
        {
            if (!target) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var t = target.transform;
            var rb = target.Body;
            float speed = rb ? rb.linearVelocity.magnitude : 0f;
            bool lookBack = PolyInput.LookBackHeld();

            if (_mode == CameraMode.Hood)
            {
                transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 1.22f, 0.25f)), Quaternion.LookRotation(lookBack ? -t.forward : t.forward, t.up));
                SetFov(68f + Mathf.Clamp01(target.SpeedKmh / 200f) * 12f, dt);
                _snap = false;
                return;
            }

            var p = Current;
            float wantedYaw = t.eulerAngles.y + (lookBack ? 180f : 0f);
            float spin = rb ? Mathf.Abs(rb.angularVelocity.y) : 0f;
            float follow = spin > 2.8f ? Mathf.Clamp01(3.5f - spin) : 1f;

            if (_snap || lookBack)
            {
                _yaw = wantedYaw;
                _height = t.position.y + p.height;
                _distance = p.distance;
            }
            else
            {
                _yaw = Mathf.LerpAngle(_yaw, wantedYaw, follow * p.yawDamping * dt);
                _height = Mathf.Lerp(_height, t.position.y + p.height, 2f * dt);
            }

            // Pull in when terrain or scenery blocks the line from the car to the camera.
            Vector3 carTop = t.position + Vector3.up * 1f;
            Vector3 back = Quaternion.Euler(0f, _yaw, 0f) * Vector3.back;
            Vector3 toCam = back * p.distance + Vector3.up * (p.height - 1f);
            bool blocked = Physics.SphereCast(carTop, 0.4f, toCam.normalized, out var hit, toCam.magnitude, ~0, QueryTriggerInteraction.Ignore)
                           && hit.rigidbody != rb;
            float wantedDistance = blocked ? p.distance * 0.45f : p.distance;
            if (!_snap) _distance = Mathf.Lerp(_distance, wantedDistance, (blocked ? 3f : 1f) * dt);

            Vector3 pos = t.position + back * _distance;
            float heightScale = _distance / p.distance;
            pos.y = Mathf.Lerp(t.position.y + 1.5f, _height, Mathf.Clamp01(heightScale));
            // Never below the ground under the camera.
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out var ground, 100f, ~0, QueryTriggerInteraction.Ignore) && ground.rigidbody == null)
                pos.y = Mathf.Max(pos.y, ground.point.y + 1.2f);

            // Aim a little above the car; at speed the aim drops slightly to show more road ahead.
            float speed01 = Mathf.InverseLerp(20f, 35f, speed);
            Vector3 aim = t.position + Vector3.up * Mathf.Lerp(p.aimAbove, p.aimAbove * 0.8f, speed01);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(aim - pos, Vector3.up));

            if (_shake > 0.001f)
            {
                float s = _shake * _shake * 0.4f;
                transform.position += new Vector3(Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f, 0f) * s;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.2f);
            }

            // Lens widens 10 % between 20 and 35 m/s (plus a small kick while boosting).
            SetFov(Mathf.Lerp(p.fov, p.fov * 1.1f, speed01) + (target.Boosting ? 3f : 0f), dt);
            _snap = false;
        }

        private void SetFov(float wanted, float dt)
        {
            if (_snap || _fov <= 0f) { _fov = wanted; _fovVel = 0f; }
            else _fov = Mathf.SmoothDamp(_fov, wanted, ref _fovVel, 0.75f, Mathf.Infinity, dt);
            if (_cam) _cam.fieldOfView = _fov;
        }
    }
}
