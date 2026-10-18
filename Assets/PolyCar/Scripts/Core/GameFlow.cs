using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>What the next race is (chosen in the menu) and fading scene changes.</summary>
    public static class GameFlow
    {
        public const string MenuScene = "Menu";

        public static string MapId = "";
        public static string CarId = "";
        public static int Laps = -1;          // -1 = map default
        public static bool FreeRoam;

        public static MapSpec CurrentMap => MapCatalog.Instance ? MapCatalog.Instance.Get(string.IsNullOrEmpty(MapId) ? SaveData.Current.selectedMap : MapId) : null;
        public static CarSpec CurrentCar => CarCatalog.Instance ? CarCatalog.Instance.Get(string.IsNullOrEmpty(CarId) ? SaveData.Current.selectedCar : CarId) : null;

        public static int LapsFor(MapSpec map)
        {
            if (map == null || !map.IsCircuit) return 0;
            return Laps > 0 ? Laps : map.laps;
        }

        public static void StartRace(string mapId, string carId)
        {
            MapId = mapId; CarId = carId;
            var map = MapCatalog.Instance.Get(mapId);
            SaveData.Current.selectedMap = mapId;
            SaveData.Current.selectedCar = carId;
            SaveData.Current.Save();
            Load(map.sceneName);
        }

        public static void ToMenu() => Load(MenuScene);
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
            Application.runInBackground = false;
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
