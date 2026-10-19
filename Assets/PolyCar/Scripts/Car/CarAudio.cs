using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Layered engine (idle/low/mid/high loops crossfaded by RPM), tyre squeal, wind, gear shifts,
    /// backfires and impacts. The player's car is 2D; rivals are 3D so you hear them around you.
    /// </summary>
    public class CarAudio : MonoBehaviour
    {
        public PolyCarController car;
        public bool isPlayer = true;
        [Range(0.5f, 1.5f)] public float enginePitch = 1f;

        private AudioSource[] _engine;
        private AudioSource _squeal, _wind, _oneShot;
        private AudioClip _gear, _backfire, _thump;
        private static readonly float[] Centers = { 0.05f, 0.35f, 0.65f, 0.95f };
        private float _volume = 1f;
        private bool _paused;

        private void Start()
        {
            if (!car) car = GetComponent<PolyCarController>();
            string[] names = { "Engine_Idle", "Engine_Low", "Engine_Mid", "Engine_High" };
            _engine = new AudioSource[names.Length];
            for (int i = 0; i < names.Length; i++) _engine[i] = MakeLoop(Resources.Load<AudioClip>("Audio/Engine/" + names[i]));
            _squeal = MakeLoop(Resources.Load<AudioClip>("Audio/Car/Tyre_Squeal_Loop"));
            _wind = isPlayer ? MakeLoop(Resources.Load<AudioClip>("Audio/Fx/WindLoop")) : null;
            _oneShot = gameObject.AddComponent<AudioSource>();
            Setup(_oneShot);
            _gear = Resources.Load<AudioClip>("Audio/Engine/Gear_Change");
            _backfire = Resources.Load<AudioClip>("Audio/Engine/Backfire");
            _thump = Resources.Load<AudioClip>("Audio/Fx/Land_Thump");
            car.GearChanged += OnGear;
            car.Impact += OnImpact;
            car.Landed += t => PlayOne(_thump, Mathf.Clamp01(t * 0.6f) * 0.8f, 1f);
            GameSettings.Changed += ReadVolume;
            ReadVolume();
        }

        private void OnDestroy() => GameSettings.Changed -= ReadVolume;

        private void ReadVolume() => _volume = GameSettings.Current.sfxVolume * (isPlayer ? 1f : 0.8f);

        private AudioSource MakeLoop(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            Setup(s);
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            // Random start offset so rivals don't phase with the player.
            if (clip) s.time = Random.Range(0f, clip.length * 0.9f);
            s.Play();
            return s;
        }

        private void Setup(AudioSource s)
        {
            s.playOnAwake = false;
            s.spatialBlend = isPlayer ? 0f : 1f;
            s.dopplerLevel = isPlayer ? 0f : 0.3f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 6f;
            s.maxDistance = 70f;
            s.priority = isPlayer ? 32 : 160;
        }

        private void Update()
        {
            bool paused = Time.timeScale < 0.01f;
            if (paused != _paused)
            {
                _paused = paused;
                foreach (var s in GetComponents<AudioSource>()) { if (paused) s.Pause(); else s.UnPause(); }
            }
            if (paused || car == null) return;

            float rpm = car.Rpm01;
            float load = Mathf.Lerp(0.55f, 1f, car.Throttle01);
            for (int i = 0; i < _engine.Length; i++)
            {
                // Triangular crossfade between neighbouring layers.
                float w = Mathf.Clamp01(1f - Mathf.Abs(rpm - Centers[i]) / 0.32f);
                if (i == 0) w = Mathf.Max(w, rpm < Centers[0] ? 1f : 0f);
                if (i == _engine.Length - 1) w = Mathf.Max(w, rpm > Centers[i] ? 1f : 0f);
                _engine[i].volume = w * load * 0.55f * _volume;
                _engine[i].pitch = enginePitch * (0.8f + (rpm - Centers[i]) * 0.9f + 0.2f);
            }

            float slip = car.Grounded ? Mathf.Clamp01((car.SlipSpeed - 3f) / 9f) : 0f;
            _squeal.volume = Mathf.Lerp(_squeal.volume, slip * 0.45f * _volume, 1f - Mathf.Exp(-12f * Time.deltaTime));
            _squeal.pitch = 0.9f + slip * 0.2f;
            if (_wind)
            {
                float sp = Mathf.Clamp01(car.SpeedKmh / 200f);
                _wind.volume = sp * sp * 0.35f * _volume;
                _wind.pitch = 0.8f + sp * 0.5f;
            }
        }

        private void OnGear(int gear)
        {
            PlayOne(_gear, 0.35f, Random.Range(0.95f, 1.08f));
            if (gear >= 3 && Random.value < 0.35f) PlayOne(_backfire, 0.4f, Random.Range(0.9f, 1.1f));
        }

        private void OnImpact(float strength) => PlayOne(_thump, Mathf.Clamp01(strength / 8f), Random.Range(0.85f, 1.05f));

        private void PlayOne(AudioClip clip, float vol, float pitch)
        {
            if (clip == null || _oneShot == null) return;
            _oneShot.pitch = pitch;
            _oneShot.PlayOneShot(clip, vol * _volume);
        }
    }
}
