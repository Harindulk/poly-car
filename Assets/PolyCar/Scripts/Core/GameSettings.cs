using System;
using UnityEngine;

namespace PolyCar
{
    public enum Difficulty { Easy = 0, Normal = 1, Hard = 2 }
    public enum CameraMode { Chase = 0, Near = 1, Hood = 2 }

    /// <summary>Player options, persisted as JSON in PlayerPrefs and applied at boot and on change.</summary>
    [Serializable]
    public class GameSettings
    {
        private const string Key = "polycar.settings.v1";

        public int quality = -1;          // -1 = keep project default
        public int resolutionWidth;       // 0 = native
        public int resolutionHeight;
        public int windowMode = 0;        // 0 fullscreen window, 1 exclusive, 2 windowed
        public bool vSync = true;
        public int fpsLimit = 0;          // 0 = unlimited (vsync governs)
        public float renderScale = 1f;
        public float masterVolume = 0.9f;
        public float musicVolume = 0.55f;
        public float sfxVolume = 0.9f;
        public bool useMph;
        public bool showFps;
        public int cameraMode;
        public int opponents = 3;
        public int difficulty = 1;
        public bool ghost = true;
        public bool cameraShake = true;
        public bool motionBlur = true;

        public static event Action Changed;

        private static GameSettings _current;
        public static GameSettings Current => _current ??= Load();

        public static GameSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var s = JsonUtility.FromJson<GameSettings>(json);
                    if (s != null) return s;
                }
            }
            catch (Exception e) { Debug.LogWarning("Poly Car: settings unreadable, using defaults. " + e.Message); }
            return new GameSettings();
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        /// <summary>Applies everything that is engine-wide (display, quality, frame pacing, audio).</summary>
        public void Apply(bool display = true)
        {
            if (quality >= 0 && quality < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != quality)
                QualitySettings.SetQualityLevel(quality, true);

            QualitySettings.vSyncCount = vSync ? 1 : 0;
            Application.targetFrameRate = fpsLimit > 0 ? fpsLimit : (vSync ? -1 : 300);

            // Render scale lives on the pipeline asset; never dirty the project asset from the Editor.
            if (!Application.isEditor && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.renderScale = Mathf.Clamp(renderScale, 0.5f, 1f);

            AudioListener.volume = Mathf.Clamp01(masterVolume);

            // Explicit -screen-width/-screen-height launch arguments win (benchmarks, streaming setups).
            if (display && !Application.isEditor && !GameFlow.HasArg("-screen-width"))
            {
                var mode = windowMode == 1 ? FullScreenMode.ExclusiveFullScreen : windowMode == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                int w = resolutionWidth > 0 ? resolutionWidth : Screen.currentResolution.width;
                int h = resolutionHeight > 0 ? resolutionHeight : Screen.currentResolution.height;
                if (mode == FullScreenMode.Windowed && resolutionWidth <= 0)
                {
                    w = Mathf.RoundToInt(Screen.currentResolution.width * 0.75f);
                    h = Mathf.RoundToInt(Screen.currentResolution.height * 0.75f);
                }
                if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode)
                    Screen.SetResolution(w, h, mode);
            }
            Changed?.Invoke();
        }

        public static string FormatSpeed(float kmh) => Current.useMph ? Mathf.RoundToInt(kmh * 0.621371f).ToString() : Mathf.RoundToInt(kmh).ToString();
        public static string SpeedUnit => Current.useMph ? "MPH" : "KM/H";
    }
}
