using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Smooth chase camera: follows the car's heading (blending toward the velocity in a drift so you see
    /// the angle), widens the FOV with speed and boost, pulls in when terrain is in the way, and shakes on
    /// impacts. Three modes: Chase, Near, Hood (C to cycle, Q to look back).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ChaseCamera : MonoBehaviour
    {
        public PolyCarController target;
        public float baseFov = 62f;
        public float speedFov = 14f;
        public float boostFov = 8f;

        private static readonly Vector3[] Offsets = { new Vector3(0f, 2.45f, -6.6f), new Vector3(0f, 1.85f, -4.9f), new Vector3(0f, 1.22f, 0.25f) };
        private Camera _cam;
        private Vector3 _vel;
        private Vector3 _heading;
        private float _shake;
        private float _fov;
        private CameraMode _mode;
        private bool _snap = true;

        public CameraMode Mode => _mode;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _mode = (CameraMode)Mathf.Clamp(GameSettings.Current.cameraMode, 0, 2);
            _fov = baseFov;
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
            _mode = (CameraMode)(((int)_mode + 1) % 3);
            GameSettings.Current.cameraMode = (int)_mode;
            GameSettings.Current.Save();
            Snap();
        }

        private void LateUpdate()
        {
            if (!target) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var t = target.transform;
            Vector3 carPos = t.position;
            Vector3 v = target.Body ? target.Body.linearVelocity : Vector3.zero;

            // Heading: car forward, leaning toward travel direction at speed (shows drift angle).
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            Vector3 vFlat = Vector3.ProjectOnPlane(v, Vector3.up);
            if (vFlat.magnitude > 6f && Vector3.Dot(vFlat, fwd) > 0f)
                fwd = Vector3.Slerp(fwd, vFlat.normalized, 0.35f);
            bool lookBack = PolyInput.LookBackHeld();
            if (lookBack) fwd = -fwd;

            if (_snap || _heading == Vector3.zero) _heading = fwd;
            float headingLerp = _mode == CameraMode.Hood ? 25f : 6.5f;
            _heading = Vector3.Slerp(_heading, fwd, 1f - Mathf.Exp(-headingLerp * dt)).normalized;
            if (lookBack) _heading = fwd;
            var rot = Quaternion.LookRotation(_heading, Vector3.up);

            Vector3 offset = Offsets[(int)_mode];
            float speed01 = Mathf.Clamp01(target.SpeedKmh / 200f);
            if (_mode != CameraMode.Hood) offset.z -= speed01 * 1.1f;

            Vector3 desired;
            Quaternion look;
            if (_mode == CameraMode.Hood)
            {
                desired = t.TransformPoint(offset);
                look = Quaternion.LookRotation(lookBack ? -t.forward : t.forward, t.up);
                transform.SetPositionAndRotation(desired, look);
            }
            else
            {
                Vector3 pivot = carPos + Vector3.up * 1.1f;
                desired = carPos + rot * offset;
                // Keep the camera out of hills and walls.
                Vector3 toCam = desired - pivot;
                if (Physics.SphereCast(pivot, 0.35f, toCam.normalized, out var hit, toCam.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    && hit.rigidbody != target.Body)
                    desired = pivot + toCam.normalized * Mathf.Max(1.2f, hit.distance - 0.1f);

                if (_snap) { transform.position = desired; _vel = Vector3.zero; }
                else transform.position = Vector3.SmoothDamp(transform.position, desired, ref _vel, 0.075f, Mathf.Infinity, dt);

                Vector3 lookAt = carPos + Vector3.up * 1.05f + _heading * (2.5f + speed01 * 3f);
                look = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
                transform.rotation = _snap ? look : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-14f * dt));
            }

            if (_shake > 0.001f)
            {
                float s = _shake * _shake * 0.35f;
                transform.position += new Vector3(Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f, 0f) * s;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 2.2f);
            }

            float fovTarget = baseFov + speed01 * speedFov + (target.Boosting ? boostFov : 0f);
            if (_mode == CameraMode.Hood) fovTarget += 6f;
            _fov = _snap ? fovTarget : Mathf.Lerp(_fov, fovTarget, 1f - Mathf.Exp(-4f * dt));
            if (_cam) _cam.fieldOfView = _fov;
            _snap = false;
        }
    }
}
