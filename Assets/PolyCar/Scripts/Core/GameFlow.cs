using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>
    /// A championship in progress, run like a rally: every track in order against the same virtual rivals,
    /// and the lowest total time over all of them wins.
    /// </summary>
    public class CupState
    {
        public string[] maps;
        public int index;
        public string playerCar;
        public int seed;
        public VirtualRival[] rivals = new VirtualRival[0];
        public readonly Dictionary<string, float> totals = new Dictionary<string, float>();

        public bool IsLastRace => index >= maps.Length - 1;
        public string CurrentMap => maps[Mathf.Clamp(index, 0, maps.Length - 1)];

        /// <summary>Same rival times for a stage however often it's restarted.</summary>
        public System.Random StageRandom() => new System.Random(seed + index * 7919);

        public List<KeyValuePair<string, float>> Standings()
        {
            var list = new List<KeyValuePair<string, float>>(totals);
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            return list;
        }
    }

    /// <summary>What the next race is (chosen in the menu) and fading scene changes.</summary>
    public static class GameFlow
    {
        public static CupState Cup;
        public const int CupRivals = 5;

        public static void StartCup(string carId)
        {
            int seed = Random.Range(1, int.MaxValue / 2);
            Cup = new CupState
            {
                maps = System.Array.ConvertAll(MapCatalog.Instance.maps, m => m.id),
                index = 0,
                playerCar = carId,
                seed = seed,
                rivals = RivalTimes.Field(CupRivals, carId, new System.Random(seed)),
            };
            Cup.totals["You"] = 0f;
            foreach (var r in Cup.rivals) Cup.totals[r.name] = 0f;
            Laps = -1;
            StartRace(Cup.CurrentMap, carId);
        }

        public static void NextCupRace()
        {
            if (Cup == null || Cup.IsLastRace) { ToMenu(); return; }
            Cup.index++;
            StartRace(Cup.CurrentMap, Cup.playerCar);
        }

        public const string MenuScene = "Menu";

        public static string MapId = "";
        public static string CarId = "";
        public static int Laps = -1;          // -1 = map default
        public static bool FreeRoam;

        public static MapSpec CurrentMap => MapCatalog.Instance ? MapCatalog.Instance.Get(string.IsNullOrEmpty(MapId) ? SaveData.Current.selectedMap : MapId) : null;
        public static CarSpec CurrentCar => CarCatalog.Instance ? CarCatalog.Instance.Get(string.IsNullOrEmpty(CarId) ? SaveData.Current.selectedCar : CarId) : null;

        /// <summary>Value after a command-line flag (e.g. -map sakura_ring), or null.</summary>
        public static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        public static bool HasArg(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        public static int LapsFor(MapSpec map)
        {
            if (map == null || !map.IsCircuit) return 0;
            return Laps > 0 ? Laps : map.laps;
        }

        public static void StartRace(string mapId, string carId)
        {
            // A lap count chosen for one track never carries over to another.
            if (mapId != MapId) Laps = -1;
            MapId = mapId; CarId = carId;
            var map = MapCatalog.Instance.Get(mapId);
            SaveData.Current.selectedMap = mapId;
            SaveData.Current.selectedCar = carId;
            SaveData.Current.Save();
            Load(map.sceneName);
        }

        public static void ToMenu() { Cup = null; Load(MenuScene); }
        public static void Restart() => Load(SceneManager.GetActiveScene().name);

        public static void Load(string scene)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneLoader.Instance.LoadScene(scene);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            GameSettings.Current.Apply();
            // Benchmarks and autopilot runs keep going when the window isn't focused.
            Application.runInBackground = HasArg("-benchmark") || HasArg("-autopilot");
            if (HasArg("-mute")) AudioListener.volume = 0f; // silent test runs; audio still plays internally
        }
    }

    /// <summary>Persistent fade overlay and async scene loading with a short tip.</summary>
    public class SceneLoader : MonoBehaviour
    {
        private static SceneLoader _instance;
        private UIDocument _doc;
        private VisualElement _overlay;
        private Label _tip, _title;
        private VisualElement _art;
        private bool _busy;

        private static readonly string[] Tips =
        {
            "Hold SPACE (handbrake) through corners to drift - drifting fills your boost.",
            "Press SHIFT to fire your boost on the straights.",
            "Stuck or upside down? Press R to jump back to the last checkpoint.",
            "Press C to switch camera, Q to look behind.",
            "Win medals to unlock new cars in the garage.",
            "Beat your ghost - it replays your best run on every track.",
            "A gamepad works too: triggers drive, A handbrakes, X boosts.",
        };

        public static SceneLoader Instance
        {
            get
            {
                if (_instance) return _instance;
                var go = new GameObject("[SceneLoader]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SceneLoader>();
                return _instance;
            }
        }

        private void Awake()
        {
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = UiKit.Panel;
            _doc.sortingOrder = 1000;
            var root = _doc.rootVisualElement;
            UiKit.AddStyles(root);
            _overlay = new VisualElement();
            _overlay.AddToClassList("loader");
            _overlay.pickingMode = PickingMode.Ignore;
            _art = new VisualElement();
            _art.AddToClassList("loader-art");
            _art.pickingMode = PickingMode.Ignore;
            var shade = new VisualElement();
            shade.AddToClassList("loader-shade");
            _title = new Label("LOADING");
            _title.AddToClassList("loader-title");
            _tip = new Label();
            _tip.AddToClassList("loader-tip");
            _overlay.Add(_art);
            _overlay.Add(shade);
            _overlay.Add(_title);
            _overlay.Add(_tip);
            root.Add(_overlay);
            root.pickingMode = PickingMode.Ignore;
            // Draw every character once in both UI fonts so their glyph atlases are filled at boot, not mid-race
            // (adding glyphs the first time a split message shows new text caused frame spikes).
            const string glyphs = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-:.,/%!?()'&_<>[]#*=x ";
            foreach (var cls in new[] { "loader-tip", "hud-time" })
            {
                var warm = new Label(glyphs);
                warm.AddToClassList(cls);
                warm.style.position = Position.Absolute;
                warm.style.left = 0; warm.style.top = 0;
                warm.style.opacity = 0.01f;
                warm.pickingMode = PickingMode.Ignore;
                root.Add(warm);
                warm.schedule.Execute(() => warm.RemoveFromHierarchy()).StartingIn(3000);
            }
            _overlay.style.opacity = 0f;
            _overlay.style.display = DisplayStyle.None;
        }

        public void LoadScene(string scene)
        {
            if (_busy) return;
            StartCoroutine(LoadRoutine(scene));
        }

        private IEnumerator LoadRoutine(string scene)
        {
            _busy = true;
            _tip.text = Tips[Random.Range(0, Tips.Length)];
            var map = MapCatalog.Instance ? MapCatalog.Instance.ByScene(scene) : null;
            _art.style.backgroundImage = map != null && map.thumbnail ? new StyleBackground(map.thumbnail) : new StyleBackground(StyleKeyword.None);
            _title.text = map != null ? map.displayName.ToUpper() : "LOADING";
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.pickingMode = PickingMode.Position;
            yield return Fade(0f, 1f, 0.25f);
            var op = SceneManager.LoadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;
            // Let the new scene settle: a race renders its warm-up frames under this overlay first.
            float until = Time.realtimeSinceStartup + 5f;
            yield return null;
            yield return null;
            while (RaceManager.Current != null && !RaceManager.Current.WarmedUp && Time.realtimeSinceStartup < until) yield return null;
            yield return Fade(1f, 0f, 0.35f);
            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.display = DisplayStyle.None;
            _busy = false;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                _overlay.style.opacity = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            _overlay.style.opacity = to;
        }
    }
}
