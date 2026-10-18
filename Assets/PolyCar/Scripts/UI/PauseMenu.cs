using UnityEngine;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>ESC / Start pause overlay: resume, restart, quick options, quit to menu.</summary>
    public class PauseMenu : MonoBehaviour
    {
        private RaceManager _race;
        private UIDocument _doc;
        private VisualElement _overlay, _main, _options;
        private Button _resume;

        public static PauseMenu Create(RaceManager race)
        {
            var doc = UiKit.CreateDocument("[Pause]", 20, race.transform);
            var pm = doc.gameObject.AddComponent<PauseMenu>();
            pm._race = race;
            pm._doc = doc;
            UiNavigator.Attach(doc);
            pm.Build();
            return pm;
        }

        private void Build()
        {
            UiKit.EnsureEventSystem();
            _overlay = UiKit.Box("overlay");
            var panel = UiKit.Box("panel", "panel-narrow");
            panel.Add(UiKit.Label("PAUSED", "panel-title"));
            panel.Add(UiKit.Label(_race.Map != null ? _race.Map.displayName.ToUpper() : "", "panel-subtitle"));

            _main = UiKit.Box("panel-buttons");
            _resume = UiKit.Button("RESUME", () => _race.SetPaused(false));
            _main.Add(_resume);
            _main.Add(UiKit.Button("RESTART", GameFlow.Restart));
            _main.Add(UiKit.Button("RESPAWN", () => { _race.SetPaused(false); _race.Player.Respawn(); }));
            _main.Add(UiKit.Button("OPTIONS", () => ShowOptions(true)));
            _main.Add(UiKit.Button("QUIT TO MENU", GameFlow.ToMenu, "pc-button-dim"));
            panel.Add(_main);

            _options = UiKit.Box("panel-buttons");
            var s = GameSettings.Current;
            _options.Add(UiKit.Slider("MASTER", 0f, 1f, s.masterVolume, v => { s.masterVolume = v; s.Apply(false); }));
            _options.Add(UiKit.Slider("MUSIC", 0f, 1f, s.musicVolume, v => { s.musicVolume = v; s.Apply(false); }));
            _options.Add(UiKit.Slider("EFFECTS", 0f, 1f, s.sfxVolume, v => { s.sfxVolume = v; s.Apply(false); }));
            _options.Add(UiKit.Stepper("CAMERA SHAKE", () => s.cameraShake ? "ON" : "OFF", _ => { s.cameraShake = !s.cameraShake; }, out _));
            _options.Add(UiKit.Stepper("SHOW FPS", () => s.showFps ? "ON" : "OFF", _ => { s.showFps = !s.showFps; }, out _));
            _options.Add(UiKit.Stepper("UNITS", () => s.useMph ? "MPH" : "KM/H", _ => { s.useMph = !s.useMph; }, out _));
            _options.Add(UiKit.Button("BACK", () => { s.Save(); ShowOptions(false); }));
            panel.Add(_options);

            panel.Add(UiKit.Label("ESC  resume", "panel-footer"));
            _overlay.Add(panel);
            _doc.rootVisualElement.Add(_overlay);
            _overlay.style.display = DisplayStyle.None;
            ShowOptions(false);
            _race.PauseChanged += OnPause;
        }

        private void ShowOptions(bool on)
        {
            _main.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            _options.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            var first = (on ? _options : _main).Q<Button>();
            first?.schedule.Execute(() => first.Focus()).StartingIn(10);
        }

        private void OnPause(bool paused)
        {
            _overlay.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;
            if (paused) { ShowOptions(false); }
            else GameSettings.Current.Save();
        }
    }
}
