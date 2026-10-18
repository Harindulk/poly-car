using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>A championship in progress: every track in order against the same rivals, points per race.</summary>
    public class CupState
    {
        public static readonly int[] PointsTable = { 10, 7, 5, 3, 2, 1 };
        public string[] maps;
        public int index;
        public string playerCar;
        public string[] rivalCars;
        public string[] rivalNames;
        public readonly Dictionary<string, int> points = new Dictionary<string, int>();

        public bool IsLastRace => index >= maps.Length - 1;
        public string CurrentMap => maps[Mathf.Clamp(index, 0, maps.Length - 1)];

        public List<KeyValuePair<string, int>> Standings()
        {
            var list = new List<KeyValuePair<string, int>>(points);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }
    }

    /// <summary>What the next race is (chosen in the menu) and fading scene changes.</summary>
    public static class GameFlow
    {
        public static CupState Cup;
        private static readonly string[] RivalNames = { "Kenji", "Maya", "Rico", "Lena", "Viktor" };

        public static void StartCup(string carId)
        {
            var maps = MapCatalog.Instance.maps;
            var cars = new List<string>();
            foreach (var c in CarCatalog.Instance.cars) if (c.id != carId) cars.Add(c.id);
            for (int i = cars.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (cars[i], cars[j]) = (cars[j], cars[i]); }
            int rivals = Mathf.Min(3, cars.Count);
            Cup = new CupState
            {
                maps = System.Array.ConvertAll(maps, m => m.id),
                index = 0,
                playerCar = carId,
                rivalCars = cars.GetRange(0, rivals).ToArray(),
                rivalNames = new List<string>(RivalNames).GetRange(0, rivals).ToArray(),
            };
            Cup.points["You"] = 0;
            foreach (var n in Cup.rivalNames) Cup.points[n] = 0;
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
        private Label _tip;
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
            var title = new Label("LOADING");
            title.AddToClassList("loader-title");
            _tip = new Label();
            _tip.AddToClassList("loader-tip");
            _overlay.Add(title);
            _overlay.Add(_tip);
            root.Add(_overlay);
            root.pickingMode = PickingMode.Ignore;
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
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.pickingMode = PickingMode.Position;
            yield return Fade(0f, 1f, 0.25f);
            var op = SceneManager.LoadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;
            // Let the new scene settle (first frames do shader warm-up and spawning).
            yield return null;
            yield return null;
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
