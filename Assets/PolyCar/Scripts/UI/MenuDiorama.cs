using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PolyCar
{
    /// <summary>
    /// The main menu's backdrop, in the manner of art of rally's menu: a small floating diorama of the selected
    /// track's world with your car parked on its road, seen through a long lens with tilt-shift depth of field.
    /// Each menu screen has its own camera shot and the camera glides between them; picking a track swaps the
    /// diorama, sky colour and light to that track's theme.
    /// </summary>
    public class MenuDiorama : MonoBehaviour
    {
        [Serializable]
        public class Variant
        {
            public string mapId;
            public GameObject root;
            public Transform carAnchor;
            public Color background = new Color(0.8f, 0.85f, 0.9f);
            public Color sunColor = Color.white;
            public Vector3 sunEuler = new Vector3(40f, -40f, 0f);
            public float sunIntensity = 1.3f;
            public Color ambientSky = Color.gray, ambientEquator = Color.gray, ambientGround = Color.gray;
        }

        public enum Shot { Title, Tracks, Setup, Garage, Info }

        public Variant[] variants = new Variant[0];
        public Camera cam;
        public Light sun;
        public Volume volume;

        private struct Pose
        {
            public float yaw, pitch, distance, fov, screenShift, orbitSpeed;
            public bool onCar;
            public Pose(float yaw, float pitch, float distance, float fov, float screenShift, float orbitSpeed, bool onCar)
            { this.yaw = yaw; this.pitch = pitch; this.distance = distance; this.fov = fov; this.screenShift = screenShift; this.orbitSpeed = orbitSpeed; this.onCar = onCar; }
        }

        // Long lenses throughout: the diorama reads as a miniature (art of rally's car chooser zooms to a 13° lens).
        // Yaw is relative to the parked car; ~180 looks back at its nose down the road, which trees never block.
        // Diorama shots orbit slowly; car shots sway a few degrees instead.
        private static readonly Pose TitlePose = new Pose(-35f, 35f, 110f, 24f, 5f, 2.5f, false);
        private static readonly Pose TracksPose = new Pose(-20f, 52f, 118f, 22f, 0f, 1.5f, false);
        private static readonly Pose SetupPose = new Pose(205f, 19f, 62f, 15f, 6f, 0f, true);
        private static readonly Pose GaragePose = new Pose(150f, 14f, 44f, 13f, 3.4f, 0f, true);
        private static readonly Pose InfoPose = new Pose(20f, 38f, 112f, 22f, 0f, 2f, false);

        private Variant _active;
        private Pose _target = TitlePose;
        private float _yaw, _pitch, _distance, _fov, _shift;
        private Vector3 _pivot;
        private float _orbit;
        private bool _snapped;
        private DepthOfField _dof;
        private UniversalRenderPipelineAsset _urp;
        private float _shadowDistance;

        public Variant Active => _active;

        private void Awake()
        {
            if (!cam) cam = Camera.main;
            if (volume && volume.profile) volume.profile.TryGet(out _dof);
            // The long lens sits ~100 m back: keep sun shadows that far while the menu is up.
            _urp = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (_urp) { _shadowDistance = _urp.shadowDistance; _urp.shadowDistance = Mathf.Max(_shadowDistance, 220f); }
        }

        private void OnDestroy()
        {
            if (_urp) _urp.shadowDistance = _shadowDistance;
        }

        public void ShowMap(string mapId)
        {
            Variant v = null;
            foreach (var x in variants) if (x.mapId == mapId) v = x;
            v ??= variants.Length > 0 ? variants[0] : null;
            if (v == null || v == _active) return;
            foreach (var x in variants) if (x.root) x.root.SetActive(x == v);
            _active = v;
            if (cam) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = v.background; }
            RenderSettings.fogColor = v.background;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = v.ambientSky;
            RenderSettings.ambientEquatorColor = v.ambientEquator;
            RenderSettings.ambientGroundColor = v.ambientGround;
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(v.sunEuler);
                sun.color = v.sunColor;
                sun.intensity = v.sunIntensity;
            }
        }

        public void SetShot(Shot shot)
        {
            _target = shot switch
            {
                Shot.Tracks => TracksPose,
                Shot.Setup => SetupPose,
                Shot.Garage => GaragePose,
                Shot.Info => InfoPose,
                _ => TitlePose,
            };
            _orbit = 0f;
        }

        /// <summary>Where the selected car parks on the active diorama.</summary>
        public Transform CarAnchor => _active != null ? _active.carAnchor : transform;

        private void LateUpdate()
        {
            if (!cam || _active == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _orbit = _target.onCar ? 7f * Mathf.Sin(Time.unscaledTime * 0.22f) : _orbit + _target.orbitSpeed * dt;
            Vector3 carPos = CarAnchor.position + Vector3.up * 0.7f;
            Vector3 pivot = _target.onCar ? carPos : (_active.root ? _active.root.transform.position + Vector3.up * 1f : carPos);

            float wantYaw = _target.yaw + _orbit + CarAnchor.eulerAngles.y;
            if (!_snapped)
            {
                _snapped = true;
                _yaw = wantYaw; _pitch = _target.pitch; _distance = _target.distance; _fov = _target.fov; _shift = _target.screenShift; _pivot = pivot;
            }
            // Ease toward the shot (about a second to settle), like a camera dolly between marks.
            float k = 1f - Mathf.Exp(-2.6f * dt);
            _yaw = Mathf.LerpAngle(_yaw, wantYaw, k);
            _pitch = Mathf.Lerp(_pitch, _target.pitch, k);
            _distance = Mathf.Lerp(_distance, _target.distance, k);
            _fov = Mathf.Lerp(_fov, _target.fov, k);
            _shift = Mathf.Lerp(_shift, _target.screenShift, k);
            _pivot = Vector3.Lerp(_pivot, pivot, k);

            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            // Screen shift slides the subject sideways in frame so it sits beside the menu panels.
            Vector3 aim = _pivot - rot * Vector3.right * _shift;
            cam.transform.SetPositionAndRotation(aim - rot * Vector3.forward * _distance, rot);
            cam.fieldOfView = _fov;

            if (_dof != null)
            {
                _dof.focusDistance.Override(Vector3.Distance(cam.transform.position, carPos));
                _dof.focalLength.Override(Mathf.Lerp(240f, 300f, Mathf.InverseLerp(24f, 13f, _fov)));
            }
        }
    }
}
