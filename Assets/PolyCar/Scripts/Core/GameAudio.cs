using UnityEngine;

namespace PolyCar
{
    /// <summary>Music player and 2D one-shots (UI, race jingles). Volumes follow GameSettings.</summary>
    public class GameAudio : MonoBehaviour
    {
        private static GameAudio _instance;
        private AudioSource _music;
        private AudioSource _sfx;
        private AudioClip[] _raceTracks;
        private AudioClip _menuTrack;
        private AudioClip _click, _hover, _confirm, _jingle, _beep, _go;
        private string _currentKey;
        private float _musicDuck = 1f;

        public static GameAudio Instance
        {
            get
            {
                if (_instance) return _instance;
                var go = new GameObject("[GameAudio]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<GameAudio>();
                return _instance;
            }
        }

        public static float Sfx => GameSettings.Current.sfxVolume;

        private void Awake()
        {
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _sfx.spatialBlend = 0f;
            _menuTrack = Resources.Load<AudioClip>("Audio/Music/Menu_OnceInParis");
            _raceTracks = new[]
            {
                Resources.Load<AudioClip>("Audio/Music/Drive_EnergeticFunkyGroove"),
                Resources.Load<AudioClip>("Audio/Music/Drive_Flashes"),
                Resources.Load<AudioClip>("Audio/Music/Drive_FashionableDeepHouse"),
                Resources.Load<AudioClip>("Audio/Music/Drive_Embrace"),
                Resources.Load<AudioClip>("Audio/Music/stargazer"),
            };
            _click = Resources.Load<AudioClip>("Audio/UI/Button Press");
            _hover = Resources.Load<AudioClip>("Audio/UI/Navigation Button Ding 01");
            _confirm = Resources.Load<AudioClip>("Audio/UI/Navigation Button Ding 04");
            _jingle = Resources.Load<AudioClip>("Audio/Fx/NewBest_Steel07");
            _beep = Resources.Load<AudioClip>("Audio/Fx/beep");
            _go = Resources.Load<AudioClip>("Audio/Fx/unlock");
            GameSettings.Changed += ApplyVolumes;
            ApplyVolumes();
        }

        private void OnDestroy() => GameSettings.Changed -= ApplyVolumes;

        private void ApplyVolumes()
        {
            if (_music) _music.volume = GameSettings.Current.musicVolume * 0.6f * _musicDuck;
            if (_sfx) _sfx.volume = GameSettings.Current.sfxVolume;
        }

        public static void PlayMenuMusic() => Instance.PlayTrack("menu", Instance._menuTrack);

        public static void PlayRaceMusic(string mapId)
        {
            var i = Instance;
            int idx = Mathf.Abs((mapId ?? "").GetHashCode()) % i._raceTracks.Length;
            i.PlayTrack("race" + idx, i._raceTracks[idx]);
        }

        public static void StopMusic() { Instance._music.Stop(); Instance._currentKey = null; }

        /// <summary>Lower the music under the pause menu / results.</summary>
        public static void DuckMusic(bool duck)
        {
            Instance._musicDuck = duck ? 0.45f : 1f;
            Instance.ApplyVolumes();
        }

        private void PlayTrack(string key, AudioClip clip)
        {
            if (clip == null || _currentKey == key) return;
            _currentKey = key;
            _music.clip = clip;
            _music.Play();
        }

        private void PlayOne(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null) return;
            _sfx.pitch = pitch;
            _sfx.PlayOneShot(clip, volume);
        }

        public static void Click() => Instance.PlayOne(Instance._click, 0.55f);
        public static void Hover() => Instance.PlayOne(Instance._hover, 0.18f, 1.2f);
        public static void Confirm() => Instance.PlayOne(Instance._confirm, 0.6f);
        public static void Jingle() => Instance.PlayOne(Instance._jingle, 0.8f);
        public static void CountBeep(bool go)
        {
            Instance.PlayOne(Instance._beep, go ? 0.75f : 0.6f, go ? 1.6f : 1f);
            if (go && Instance._go) Instance.PlayOne(Instance._go, 0.35f, 1.2f);
        }
    }
}
