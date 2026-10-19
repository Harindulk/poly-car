using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>
    /// Main menu (UI Toolkit, built in code) over a 3D showroom: Play (track, laps, rivals, difficulty),
    /// Garage (car select with stats and unlocks), Settings, Records, Credits, Quit.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Tooltip("Where the selected car is shown.")]
        public Transform turntable;
        public float spinSpeed = 18f;

        private UIDocument _doc;
        private VisualElement _root, _screenHost;
        private readonly Stack<System.Action> _back = new Stack<System.Action>();
        private GameObject _showCar;
        private string _shownCarId;
        private int _carIndex;
        private int _mapIndex;
        private Label _starsLabel;
        private static bool _argsConsumed;

        private CarCatalog Cars => CarCatalog.Instance;
        private MapCatalog Maps => MapCatalog.Instance;
        private SaveData Save => SaveData.Current;

        private void Start()
        {
            Time.timeScale = 1f;
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UiKit.EnsureEventSystem();
            _doc = UiKit.CreateDocument("[Menu]", 5, transform);
            UiNavigator.Attach(_doc);
            _root = _doc.rootVisualElement;
            _carIndex = Cars.IndexOf(Save.selectedCar);
            if (!Save.IsUnlocked(Cars.cars[_carIndex])) _carIndex = 0;
            _mapIndex = Maps.IndexOf(Save.selectedMap);
            // Command line: -map <id> [-car <id>] jumps straight into a race (benchmarks, testing).
            string argMap = GameFlow.Arg("-map");
            if (!string.IsNullOrEmpty(argMap) && !_argsConsumed && Maps.Get(argMap) != null && Maps.Get(argMap).id == argMap)
            {
                _argsConsumed = true;
                GameFlow.StartRace(argMap, GameFlow.Arg("-car") ?? Save.selectedCar);
                return;
            }
            GameAudio.PlayMenuMusic();
            ShowCar(Cars.cars[_carIndex]);
            ShowTitle();
        }

        private void Update()
        {
            if (turntable) turntable.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            if (PolyInput.BackPressed() && _back.Count > 0)
            {
                GameAudio.Click();
                _back.Pop()?.Invoke();
            }
        }

        // ---------- Screens ----------

        private VisualElement NewScreen(string cls, System.Action back)
        {
            _root.Clear();
            UiKit.AddStyles(_root);
            _back.Clear();
            if (back != null) _back.Push(back);
            var screen = UiKit.Box("menu", cls);
            _root.Add(screen);
            screen.AddToClassList("fade-in");
            return screen;
        }

        private static void FocusFirst(VisualElement container)
        {
            var b = container.Q<Button>();
            b?.schedule.Execute(() => b.Focus()).StartingIn(30);
        }

        private void ShowTitle()
        {
            var s = NewScreen("menu-title-screen", null);
            var left = UiKit.Box("menu-left");
            var logo = UiKit.Box("logo");
            logo.Add(UiKit.Label("POLY", "logo-a"));
            logo.Add(UiKit.Label("CAR", "logo-b"));
            left.Add(logo);
            left.Add(UiKit.Label("LOW-POLY DRIFT RACING", "logo-tag"));

            var buttons = UiKit.Box("menu-buttons");
            buttons.Add(UiKit.Button("RACE", ShowPlay, "pc-button-big"));
            buttons.Add(UiKit.Button("CHAMPIONSHIP", ShowCupSetup));
            buttons.Add(UiKit.Button("GARAGE", ShowGarage));
            buttons.Add(UiKit.Button("RECORDS", ShowRecords));
            buttons.Add(UiKit.Button("SETTINGS", () => ShowSettings(ShowTitle)));
            buttons.Add(UiKit.Button("CREDITS", ShowCredits));
            buttons.Add(UiKit.Button("QUIT", Quit, "pc-button-dim"));
            left.Add(buttons);
            s.Add(left);

            var right = UiKit.Box("menu-right");
            var car = Cars.Get(Save.selectedCar);
            var card = UiKit.Box("info-card");
            card.Add(UiKit.Label("CURRENT CAR", "info-key"));
            card.Add(UiKit.Label(car.displayName.ToUpper(), "info-value"));
            _starsLabel = UiKit.Label(Save.Stars + " / " + Maps.maps.Length * 3 + "  MEDAL STARS", "info-key");
            card.Add(_starsLabel);
            card.Add(UiKit.Label(Save.racesFinished + " RACES   " + Save.racesWon + " WINS   " + Save.totalDriftScore.ToString("N0") + " DRIFT", "info-small"));
            right.Add(card);
            s.Add(right);

            s.Add(UiKit.Label("v" + Application.version + "  -  WASD / ARROWS / GAMEPAD", "menu-footer"));
            FocusFirst(buttons);
        }

        private void ShowPlay()
        {
            var s = NewScreen("menu-play", ShowTitle);
            s.Add(Header("CHOOSE TRACK", ShowTitle));
            var row = UiKit.Box("map-row");
            Button firstCard = null;
            for (int i = 0; i < Maps.maps.Length; i++)
            {
                int idx = i;
                var m = Maps.maps[i];
                var card = new Button(() => { GameAudio.Confirm(); _mapIndex = idx; ShowRaceSetup(); });
                card.AddToClassList("map-card");
                card.RegisterCallback<MouseEnterEvent>(_ => card.Focus());
                card.RegisterCallback<FocusInEvent>(_ => GameAudio.Hover());
                var thumb = UiKit.Box("map-thumb");
                if (m.thumbnail) thumb.style.backgroundImage = m.thumbnail;
                thumb.style.borderBottomColor = m.accent;
                card.Add(thumb);
                card.Add(UiKit.Label(m.displayName.ToUpper(), "map-name"));
                card.Add(UiKit.Label((m.IsCircuit ? "CIRCUIT  -  " + m.laps + " LAPS" : "SPRINT") + (string.IsNullOrEmpty(m.lengthLabel) ? "" : "  -  " + m.lengthLabel), "map-meta"));
                card.Add(UiKit.Label(m.blurb, "map-blurb"));
                var rec = Save.Get(m.id, m.IsCircuit ? m.laps : 0);
                var medal = Save.BestMedal(m.id);
                var foot = UiKit.Box("map-foot");
                foot.Add(UiKit.Label(rec != null && rec.bestTime > 0f ? "BEST " + UiKit.FormatTime(rec.bestTime) : "NOT RACED", "map-best"));
                var badge = UiKit.Box("medal-small", UiKit.MedalClass(medal));
                badge.Add(UiKit.Label(medal == Medal.None ? "-" : UiKit.MedalName(medal), "medal-text-small"));
                foot.Add(badge);
                card.Add(foot);
                row.Add(card);
                if (i == _mapIndex) firstCard = card;
            }
            s.Add(row);
            s.Add(UiKit.Label("ENTER select   ESC back", "menu-footer"));
            (firstCard ?? row.Q<Button>())?.schedule.Execute(() => (firstCard ?? row.Q<Button>()).Focus()).StartingIn(30);
        }

        private void ShowRaceSetup()
        {
            var map = Maps.maps[_mapIndex];
            var st = GameSettings.Current;
            if (GameFlow.MapId != map.id) { GameFlow.MapId = map.id; GameFlow.Laps = map.laps; }
            if (GameFlow.Laps <= 0) GameFlow.Laps = map.laps;
            var s = NewScreen("menu-setup", ShowPlay);
            s.Add(Header(map.displayName.ToUpper(), ShowPlay));
            var panel = UiKit.Box("panel", "panel-setup");
            var car = Cars.cars[_carIndex];
            panel.Add(UiKit.Label("CAR:  " + car.displayName.ToUpper(), "panel-subtitle"));

            if (map.IsCircuit)
                panel.Add(UiKit.Stepper("LAPS", () => GameFlow.Laps.ToString(), d => GameFlow.Laps = Mathf.Clamp(GameFlow.Laps + d, 1, 10), out _));
            panel.Add(UiKit.Stepper("RIVALS", () => st.opponents == 0 ? "NONE (TIME TRIAL)" : st.opponents.ToString(), d => st.opponents = Mathf.Clamp(st.opponents + d, 0, 7), out _));
            panel.Add(UiKit.Stepper("DIFFICULTY", () => ((Difficulty)st.difficulty).ToString().ToUpper(), d => st.difficulty = (st.difficulty + d + 3) % 3, out _));
            panel.Add(UiKit.Stepper("GHOST", () => st.ghost ? "ON" : "OFF", _ => st.ghost = !st.ghost, out _));

            var par = UiKit.Box("par-row");
            float scale = map.IsCircuit && map.laps > 0 ? GameFlow.Laps / (float)map.laps : 1f;
            par.Add(UiKit.Label($"GOLD {UiKit.FormatTime(map.goldTime)}", "par-gold"));
            par.Add(UiKit.Label($"SILVER {UiKit.FormatTime(map.silverTime)}", "par-silver"));
            par.Add(UiKit.Label($"BRONZE {UiKit.FormatTime(map.bronzeTime)}", "par-bronze"));
            panel.Add(UiKit.Label(map.IsCircuit ? "MEDAL TIMES FOR " + map.laps + " LAPS (SCALED BY LAP COUNT)" : "MEDAL TIMES", "info-small"));
            panel.Add(par);

            var buttons = UiKit.Box("panel-buttons-row");
            var go = UiKit.Button("START RACE", () =>
            {
                st.Save();
                GameAudio.Confirm();
                GameFlow.StartRace(map.id, Cars.cars[_carIndex].id);
            }, "pc-button-big", "pc-button-go");
            buttons.Add(go);
            buttons.Add(UiKit.Button("CHANGE CAR", () => ShowGarage(ShowRaceSetup)));
            panel.Add(buttons);
            s.Add(panel);
            s.Add(UiKit.Label("LEFT / RIGHT change   ENTER start   ESC back", "menu-footer"));
            go.schedule.Execute(() => go.Focus()).StartingIn(30);
        }

        private void ShowCupSetup()
        {
            var st = GameSettings.Current;
            var s = NewScreen("menu-setup", ShowTitle);
            s.Add(Header("CHAMPIONSHIP", ShowTitle));
            var panel = UiKit.Box("panel", "panel-setup");
            var car = Cars.cars[_carIndex];
            panel.Add(UiKit.Label("CAR:  " + car.displayName.ToUpper(), "panel-subtitle"));
            panel.Add(UiKit.Label($"A rally over all {Maps.maps.Length} tracks against five rivals on the clock. Your times add up stage by stage; the lowest total wins the championship.", "garage-blurb"));
            var trackList = new System.Text.StringBuilder();
            foreach (var m in Maps.maps) trackList.Append(m.displayName.ToUpper()).Append(m.IsCircuit ? $" ({m.laps} LAPS)" : " (SPRINT)").Append("   ");
            panel.Add(UiKit.Label(trackList.ToString(), "info-small"));
            panel.Add(UiKit.Stepper("DIFFICULTY", () => ((Difficulty)st.difficulty).ToString().ToUpper(), d => st.difficulty = (st.difficulty + d + 3) % 3, out _));
            panel.Add(UiKit.Label($"CUPS WON {Save.cupsWon}   -   CUPS PLAYED {Save.cupsPlayed}", "info-small"));
            var buttons = UiKit.Box("panel-buttons-row");
            var go = UiKit.Button("START CUP", () =>
            {
                st.Save();
                GameAudio.Confirm();
                GameFlow.StartCup(Cars.cars[_carIndex].id);
            }, "pc-button-big", "pc-button-go");
            buttons.Add(go);
            buttons.Add(UiKit.Button("CHANGE CAR", () => ShowGarage(ShowCupSetup)));
            panel.Add(buttons);
            s.Add(panel);
            s.Add(UiKit.Label("ENTER start   ESC back", "menu-footer"));
            go.schedule.Execute(() => go.Focus()).StartingIn(30);
        }

        private void ShowGarage() => ShowGarage(ShowTitle);

        private void ShowGarage(System.Action back)
        {
            // Leaving without pressing SELECT goes back to the car you actually own/selected.
            System.Action leave = () =>
            {
                _carIndex = Cars.IndexOf(Save.selectedCar);
                ShowCar(Cars.cars[_carIndex]);
                back();
            };
            var s = NewScreen("menu-garage", leave);
            s.Add(Header("GARAGE", leave));
            var panel = UiKit.Box("garage-panel");
            var car = Cars.cars[_carIndex];
            bool unlocked = Save.IsUnlocked(car);

            var nav = UiKit.Box("garage-nav");
            var prev = UiKit.Button("<", () => StepCar(-1, back), "pc-step-big");
            var next = UiKit.Button(">", () => StepCar(1, back), "pc-step-big");
            var nameBox = UiKit.Box("garage-name-box");
            nameBox.Add(UiKit.Label(car.displayName.ToUpper(), "garage-name"));
            nameBox.Add(UiKit.Label((_carIndex + 1) + " / " + Cars.cars.Length, "garage-count"));
            nav.Add(prev); nav.Add(nameBox); nav.Add(next);
            panel.Add(nav);
            panel.Add(UiKit.Label(car.blurb, "garage-blurb"));
            panel.Add(UiKit.Bar("TOP SPEED", car.SpeedRating));
            panel.Add(UiKit.Bar("ACCELERATION", car.AccelRating));
            panel.Add(UiKit.Bar("HANDLING", car.HandlingRating));
            panel.Add(UiKit.Bar("DRIFT", car.DriftRating, "pc-stat-fill-drift"));
            panel.Add(UiKit.Label(Mathf.RoundToInt(car.topSpeedKmh) + " KM/H  -  " + Mathf.RoundToInt(car.mass) + " KG", "info-small"));

            Button select;
            if (unlocked)
            {
                bool current = Save.selectedCar == car.id;
                select = UiKit.Button(current ? "SELECTED" : "SELECT", () =>
                {
                    Save.selectedCar = car.id;
                    Save.Save();
                    GameAudio.Confirm();
                    back();
                }, "pc-button-big");
            }
            else
            {
                panel.Add(UiKit.Label("LOCKED  -  EARN " + car.unlockMedals + " MEDAL STARS  (YOU HAVE " + Save.Stars + ")", "garage-locked"));
                select = UiKit.Button("LOCKED", () => { }, "pc-button-dim");
                select.SetEnabled(false);
            }
            panel.Add(select);
            s.Add(panel);
            s.Add(UiKit.Label("LEFT / RIGHT browse   ENTER select   ESC back", "menu-footer"));

            // Arrow keys / d-pad browse cars from anywhere on this screen.
            s.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction == NavigationMoveEvent.Direction.Left) { StepCar(-1, back); e.StopPropagation(); }
                else if (e.direction == NavigationMoveEvent.Direction.Right) { StepCar(1, back); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            var f = unlocked ? select : next;
            f.schedule.Execute(() => f.Focus()).StartingIn(30);
            ShowCar(car);
        }

        private void StepCar(int d, System.Action back)
        {
            _carIndex = (_carIndex + d + Cars.cars.Length) % Cars.cars.Length;
            GameAudio.Click();
            ShowGarage(back);
        }

        private void ShowRecords()
        {
            var s = NewScreen("menu-records", ShowTitle);
            s.Add(Header("RECORDS", ShowTitle));
            var panel = UiKit.Box("panel", "panel-wide");
            foreach (var m in Maps.maps)
            {
                var row = UiKit.Box("record-row");
                row.Add(UiKit.Label(m.displayName.ToUpper(), "record-map"));
                var rec = Save.Get(m.id, m.IsCircuit ? m.laps : 0);
                row.Add(UiKit.Label(rec != null && rec.bestTime > 0f ? UiKit.FormatTime(rec.bestTime) : "--", "record-time"));
                row.Add(UiKit.Label(rec != null && rec.bestLap > 0f && m.IsCircuit ? "LAP " + UiKit.FormatTime(rec.bestLap) : "", "record-lap"));
                var medal = Save.BestMedal(m.id);
                var badge = UiKit.Box("medal-small", UiKit.MedalClass(medal));
                badge.Add(UiKit.Label(medal == Medal.None ? "-" : UiKit.MedalName(medal), "medal-text-small"));
                row.Add(badge);
                row.Add(UiKit.Label(rec != null && !string.IsNullOrEmpty(rec.carId) ? Cars.Get(rec.carId).displayName.ToUpper() : "", "record-car"));
                panel.Add(row);
            }
            panel.Add(UiKit.Label($"TOTAL DRIFT {Save.totalDriftScore:N0}   -   RACES {Save.racesFinished}   -   WINS {Save.racesWon}   -   {Save.totalDistanceKm:0.0} KM DRIVEN", "info-small"));
            var back = UiKit.Button("BACK", ShowTitle);
            panel.Add(back);
            s.Add(panel);
            FocusFirst(panel);
        }

        private void ShowSettings(System.Action back)
        {
            var s = NewScreen("menu-settings", back);
            s.Add(Header("SETTINGS", back));
            var st = GameSettings.Current;
            var panel = UiKit.Box("panel", "panel-wide", "settings-panel");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("settings-scroll");

            scroll.Add(UiKit.Label("DISPLAY", "settings-section"));
            var resolutions = Screen.resolutions;
            var resList = new List<Vector2Int>();
            foreach (var r in resolutions) { var v = new Vector2Int(r.width, r.height); if (!resList.Contains(v)) resList.Add(v); }
            if (resList.Count == 0) resList.Add(new Vector2Int(Screen.width, Screen.height));
            int ri = Mathf.Max(0, resList.IndexOf(new Vector2Int(st.resolutionWidth > 0 ? st.resolutionWidth : Screen.currentResolution.width, st.resolutionHeight > 0 ? st.resolutionHeight : Screen.currentResolution.height)));
            scroll.Add(UiKit.Stepper("RESOLUTION", () => resList[ri].x + " x " + resList[ri].y, d => { ri = Mathf.Clamp(ri + d, 0, resList.Count - 1); st.resolutionWidth = resList[ri].x; st.resolutionHeight = resList[ri].y; st.Apply(); }, out _));
            string[] modes = { "BORDERLESS", "FULLSCREEN", "WINDOWED" };
            scroll.Add(UiKit.Stepper("WINDOW MODE", () => modes[st.windowMode], d => { st.windowMode = (st.windowMode + d + 3) % 3; st.Apply(); }, out _));
            // VSync and an FPS cap are exclusive (Unity ignores the cap while VSync is on).
            int[] fps = { 0, 30, 60, 120, 144, 165, 240 };
            int fi = Mathf.Max(0, System.Array.IndexOf(fps, st.fpsLimit));
            System.Action refreshVsync = null, refreshFps = null;
            scroll.Add(UiKit.Stepper("VSYNC", () => st.vSync ? "ON" : "OFF", _ =>
            {
                st.vSync = !st.vSync;
                if (st.vSync) { st.fpsLimit = 0; fi = 0; }
                st.Apply(false);
                refreshFps?.Invoke();
            }, out refreshVsync));
            scroll.Add(UiKit.Stepper("FPS LIMIT", () => fps[fi] == 0 ? (st.vSync ? "VSYNC" : "UNLIMITED") : fps[fi].ToString(), d =>
            {
                fi = Mathf.Clamp(fi + d, 0, fps.Length - 1);
                st.fpsLimit = fps[fi];
                if (st.fpsLimit > 0) st.vSync = false;
                st.Apply(false);
                refreshVsync?.Invoke();
            }, out refreshFps));
            var qNames = QualitySettings.names;
            scroll.Add(UiKit.Stepper("GRAPHICS", () => qNames[Mathf.Clamp(st.quality < 0 ? QualitySettings.GetQualityLevel() : st.quality, 0, qNames.Length - 1)].ToUpper(), d =>
            {
                int q = st.quality < 0 ? QualitySettings.GetQualityLevel() : st.quality;
                st.quality = Mathf.Clamp(q + d, 0, qNames.Length - 1);
                st.Apply(false);
            }, out _));
            scroll.Add(UiKit.Stepper("RENDER SCALE", () => Mathf.RoundToInt(st.renderScale * 100f) + "%", d => { st.renderScale = Mathf.Clamp(st.renderScale + d * 0.1f, 0.5f, 1f); st.Apply(false); }, out _));
            scroll.Add(UiKit.Stepper("MOTION BLUR", () => st.motionBlur ? "ON" : "OFF", _ => { st.motionBlur = !st.motionBlur; st.Apply(false); }, out _));

            scroll.Add(UiKit.Label("AUDIO", "settings-section"));
            scroll.Add(UiKit.Slider("MASTER", 0f, 1f, st.masterVolume, v => { st.masterVolume = v; st.Apply(false); }));
            scroll.Add(UiKit.Slider("MUSIC", 0f, 1f, st.musicVolume, v => { st.musicVolume = v; st.Apply(false); }));
            scroll.Add(UiKit.Slider("EFFECTS", 0f, 1f, st.sfxVolume, v => { st.sfxVolume = v; st.Apply(false); }));

            scroll.Add(UiKit.Label("GAMEPLAY", "settings-section"));
            scroll.Add(UiKit.Stepper("UNITS", () => st.useMph ? "MPH" : "KM/H", _ => st.useMph = !st.useMph, out _));
            string[] cams = { "RALLY", "RALLY FAR", "CHASE", "HOOD" };
            scroll.Add(UiKit.Stepper("CAMERA", () => cams[Mathf.Clamp(st.cameraMode, 0, 3)], d => st.cameraMode = (st.cameraMode + d + 4) % 4, out _));
            scroll.Add(UiKit.Stepper("CAMERA SHAKE", () => st.cameraShake ? "ON" : "OFF", _ => st.cameraShake = !st.cameraShake, out _));
            scroll.Add(UiKit.Stepper("SHOW FPS", () => st.showFps ? "ON" : "OFF", _ => st.showFps = !st.showFps, out _));

            scroll.Add(UiKit.Label("CONTROLS", "settings-section"));
            string[,] keys =
            {
                { "ACCELERATE / BRAKE", "W S  /  UP DOWN  /  RT LT" },
                { "STEER", "A D  /  LEFT RIGHT  /  LEFT STICK" },
                { "HANDBRAKE (DRIFT)", "SPACE  /  A  /  RB" },
                { "BOOST", "SHIFT  /  X  /  LB" },
                { "RESPAWN", "R  /  Y" },
                { "CAMERA / LOOK BACK", "C  Q  /  BACK  R3" },
                { "PAUSE", "ESC  /  START" },
                { "QUICK RESTART", "T" },
            };
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                var row = UiKit.Box("pc-row");
                row.Add(UiKit.Label(keys[i, 0], "pc-row-label"));
                row.Add(UiKit.Label(keys[i, 1], "pc-row-keys"));
                scroll.Add(row);
            }
            scroll.Add(UiKit.Label("DATA", "settings-section"));
            Button reset = null;
            int confirm = 0;
            reset = UiKit.Button("RESET PROGRESS", () =>
            {
                if (++confirm < 2) { reset.text = "PRESS AGAIN TO ERASE RECORDS"; return; }
                SaveData.ResetAll();
                reset.text = "PROGRESS RESET";
            }, "pc-button-dim");
            scroll.Add(reset);

            panel.Add(scroll);
            var done = UiKit.Button("BACK", () => { st.Save(); back(); });
            panel.Add(done);
            s.Add(panel);
            _back.Clear();
            _back.Push(() => { st.Save(); back(); });
            FocusFirst(scroll);
        }

        private void ShowCredits()
        {
            var s = NewScreen("menu-credits", ShowTitle);
            s.Add(Header("CREDITS", ShowTitle));
            var panel = UiKit.Box("panel", "panel-wide");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("settings-scroll");
            var text = Resources.Load<TextAsset>("Credits");
            scroll.Add(UiKit.Label(text ? text.text : "Poly Car by Harindu Fonseka", "credits-text"));
            panel.Add(scroll);
            var links = UiKit.Box("panel-buttons-row");
            links.Add(UiKit.Button("ITCH.IO", () => Application.OpenURL("https://harindulk.itch.io/")));
            links.Add(UiKit.Button("GITHUB", () => Application.OpenURL("https://github.com/Harindulk/poly-car")));
            links.Add(UiKit.Button("BACK", ShowTitle));
            panel.Add(links);
            s.Add(panel);
            FocusFirst(links);
        }

        private VisualElement Header(string title, System.Action back)
        {
            var h = UiKit.Box("menu-header");
            h.Add(UiKit.Button("<  BACK", back, "pc-button-back"));
            h.Add(UiKit.Label(title, "menu-header-title"));
            return h;
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Showroom ----------

        private void ShowCar(CarSpec spec)
        {
            if (spec == null || spec.prefab == null || _shownCarId == spec.id || turntable == null) return;
            if (_showCar) Destroy(_showCar);
            _shownCarId = spec.id;
            _showCar = Instantiate(spec.prefab, turntable.position, turntable.rotation, turntable);
            // Display only: no physics, sound or driving in the showroom.
            foreach (var mb in _showCar.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            foreach (var a in _showCar.GetComponentsInChildren<AudioSource>()) a.enabled = false;
            var rb = _showCar.GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
            foreach (var c in _showCar.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var t in _showCar.GetComponentsInChildren<TrailRenderer>()) t.enabled = false;
            bool locked = !Save.IsUnlocked(spec);
            if (locked)
            {
                var mat = Resources.Load<Material>("LockedMaterial");
                if (mat)
                    foreach (var r in _showCar.GetComponentsInChildren<Renderer>())
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        r.sharedMaterials = mats;
                    }
            }
        }
    }
}
