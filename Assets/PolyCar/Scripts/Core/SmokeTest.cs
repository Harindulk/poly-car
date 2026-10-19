using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>
    /// End-to-end test of a player build (launch with -smoketest -autopilot): clicks the real menu buttons,
    /// races every track to the finish (pause, pause options, respawn, restart), runs a full championship,
    /// steps every safe setting up and back, visits records, credits and the garage, quits a race to the menu,
    /// and counts every error or exception logged. Writes smoketest.txt and quits.
    /// </summary>
    public class SmokeTest : MonoBehaviour
    {
        private readonly StringBuilder _log = new StringBuilder();
        private int _errors;
        private int _steps;
        private int _failures;
        private float _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (!GameFlow.HasArg("-smoketest")) return;
            var go = new GameObject("[SmokeTest]");
            DontDestroyOnLoad(go);
            go.AddComponent<SmokeTest>();
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors++;
                if (_errors <= 20) _log.AppendLine($"  !! {type}: {message.Split('\n')[0]}  @ {stack.Split('\n')[0]}");
            }
        }

        private float _speed = 3f;

        private void Update()
        {
            // Race at triple speed to keep the run short (physics still steps at the fixed rate);
            // -smokeSpeed 1 for slow machines (a software renderer can't keep physics steps per frame sane at 3x).
            var race = RaceManager.Current;
            if (race != null && !race.Paused && (race.State == RaceState.Racing || race.State == RaceState.Finished))
                Time.timeScale = _speed;
        }

        private void Step(bool ok, string what)
        {
            _steps++;
            if (!ok) _failures++;
            _log.AppendLine($"[{Time.realtimeSinceStartup - _started,6:0.0}s] {(ok ? "OK  " : "FAIL")} {what}");
        }

        private IEnumerator Start()
        {
            _started = Time.realtimeSinceStartup;
            if (float.TryParse(GameFlow.Arg("-smokeSpeed"), out float sp) && sp > 0f) _speed = sp;
            _log.AppendLine($"platform {Application.platform}, {SystemInfo.operatingSystem}, {SystemInfo.graphicsDeviceType} on {SystemInfo.graphicsDeviceName}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, {Screen.width}x{Screen.height}");
            yield return Wait(() => FindAnyObjectByType<MainMenu>() != null && FindButton("RACE") != null, 30f, "main menu shown");

            // Single races on every track via the real menu.
            string[] maps = { "GREEN FOREST", "SAKURA RING", "FROST PEAK", "DUST CANYON" };
            yield return Click("RACE");
            yield return Click(maps[0]);
            yield return Click("START RACE");
            for (int i = 0; i < maps.Length; i++)
            {
                yield return RunRace(maps[i], i == 0);
                if (i < maps.Length - 1) yield return Click("NEXT: " + maps[i + 1]);
            }
            yield return Click("MAIN MENU");
            yield return Wait(() => FindAnyObjectByType<MainMenu>() != null && FindButton("CHAMPIONSHIP") != null, 30f, "back at the main menu");

            // Garage round trip.
            yield return Click("GARAGE");
            yield return Wait(() => FindButton("SELECTED") != null || FindButton("SELECT") != null, 5f, "garage shown");
            yield return Click("<  BACK");

            // Settings: every safe option up and back (graphics preset switching included), then records and credits.
            yield return Click("SETTINGS");
            yield return Wait(() => RowValue("GRAPHICS") != null, 5f, "settings shown");
            foreach (var label in new[] { "VSYNC", "FPS LIMIT", "GRAPHICS", "RENDER SCALE", "MOTION BLUR", "UNITS", "CAMERA", "CAMERA SHAKE", "SHOW FPS" })
            {
                string before = RowValue(label);
                yield return StepRow(label, 1);
                string after = RowValue(label);
                if (after != before) yield return StepRow(label, -1);
                string back = RowValue(label);
                Step(back == before && !string.IsNullOrEmpty(before), $"setting {label}: {before} -> {after} -> {back}");
            }
            yield return Click("BACK");
            yield return Click("RECORDS");
            yield return Wait(() => FindLabelStarting("RECORDS") != null, 5f, "records shown");
            yield return Click("BACK");
            yield return Click("CREDITS");
            yield return Wait(() => FindLabelStarting("CREDITS") != null, 5f, "credits shown");
            yield return Click("BACK");

            // Full championship.
            yield return Click("CHAMPIONSHIP");
            yield return Click("START CUP");
            for (int i = 0; i < maps.Length; i++)
            {
                yield return RunRace("CUP " + (i + 1) + " " + maps[i], false);
                var cup = GameFlow.Cup;
                Step(cup != null && cup.index == i, $"cup state at race {i + 1}");
                if (i < maps.Length - 1) yield return Click("NEXT RACE: " + maps[i + 1]);
            }
            var title = FindLabelStarting("CHAMPION!") ?? FindLabelStarting("CUP COMPLETE");
            Step(title != null, "championship finale shown (" + (title != null ? title.text : "none") + ")");
            yield return Click("MAIN MENU");
            yield return Wait(() => FindAnyObjectByType<MainMenu>() != null && FindButton("RACE") != null, 30f, "menu after championship");
            Step(GameFlow.Cup == null, "championship cleared on return to menu");

            // Quit a race to the menu from the pause menu.
            yield return Click("RACE");
            yield return Click(maps[1]);
            yield return Click("START RACE");
            yield return Wait(() => RaceManager.Current != null && RaceManager.Current.State == RaceState.Racing, 40f, "race for quit test started");
            yield return new WaitForSecondsRealtime(1.5f);
            RaceManager.Current?.SetPaused(true);
            yield return Click("QUIT TO MENU");
            yield return Wait(() => FindAnyObjectByType<MainMenu>() != null && FindButton("RACE") != null, 30f, "quit to menu from pause");
            Step(Time.timeScale == 1f, "time scale restored after quitting");

            Finish();
        }

        private IEnumerator RunRace(string name, bool extraChecks)
        {
            yield return Wait(() => RaceManager.Current != null && RaceManager.Current.State == RaceState.Racing, 40f, name + ": race started");
            var race = RaceManager.Current;
            if (race == null) yield break;
            Step(race.Player != null && race.Racers.Count == 1, $"{name}: player on the line, {race.Rivals.Length} rivals on the clock, map {race.Map?.id}");
            if (extraChecks)
            {
                yield return new WaitForSecondsRealtime(2f);
                race.SetPaused(true);
                yield return null;
                Step(Time.timeScale == 0f && FindButton("RESUME") != null, $"{name}: pause menu");
                yield return Click("RESUME");
                yield return null;
                Step(!race.Paused, $"{name}: resumed");
                var before = race.Player.Respawns;
                race.Player.Respawn();
                Step(race.Player.Respawns == before + 1, $"{name}: respawn");

                // Pause options and back, then restart the race from the pause menu.
                race.SetPaused(true);
                yield return Click("OPTIONS");
                yield return Click("BACK");
                yield return Click("RESTART");
                yield return Wait(() => RaceManager.Current != null && RaceManager.Current != race && RaceManager.Current.State == RaceState.Racing, 40f, $"{name}: restarted from pause");
                race = RaceManager.Current;
                if (race == null) yield break;
            }
            if (name.StartsWith("CUP 1") && GameSettings.Current.ghost) Step(race.HasGhost, $"{name}: best-run ghost on track");
            yield return Wait(() => ResultsScreen.Visible, 420f * 3f / _speed, name + ": finished and results shown");
            var r = race.Result;
            if (r != null)
                Step(r.time > 10f, $"{name}: place {r.position}/{r.racers}, time {UiKit.FormatTime(r.time)}, medal {r.medal}, record {r.newRecord}, drift {r.driftScore}, respawns {race.Player.Respawns}");
        }

        private IEnumerator Wait(Func<bool> condition, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok;
                try { ok = condition(); } catch { ok = false; }
                if (ok) { Step(true, what); yield break; }
                yield return null;
            }
            Step(false, what + " (timed out)");
        }

        private IEnumerator Click(string text)
        {
            Button b = null;
            float end = Time.realtimeSinceStartup + 20f;
            while (b == null && Time.realtimeSinceStartup < end)
            {
                b = FindButton(text);
                if (b == null) yield return null;
            }
            if (b == null) { Step(false, $"click '{text}' (button not found)"); yield break; }
            yield return new WaitForSecondsRealtime(0.3f);
            b.Focus();
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
            Step(true, $"click '{text}'");
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>Current value text of a settings row (label, value, arrows), or null.</summary>
        private static string RowValue(string label)
        {
            foreach (var doc in FindObjectsByType<UIDocument>())
            {
                string found = null;
                doc.rootVisualElement?.Query<VisualElement>(className: "pc-row").ForEach(row =>
                {
                    if (found != null) return;
                    var l = row.Q<Label>(className: "pc-row-label");
                    if (l != null && l.text == label) found = row.Q<Label>(className: "pc-row-value")?.text;
                });
                if (found != null) return found;
            }
            return null;
        }

        private IEnumerator StepRow(string label, int dir)
        {
            Button arrow = null;
            foreach (var doc in FindObjectsByType<UIDocument>())
                doc.rootVisualElement?.Query<VisualElement>(className: "pc-row").ForEach(row =>
                {
                    var l = row.Q<Label>(className: "pc-row-label");
                    if (arrow == null && l != null && l.text == label)
                        row.Query<Button>().ForEach(b => { if (b.text == (dir > 0 ? ">" : "<")) arrow = b; });
                });
            if (arrow == null) { Step(false, $"setting {label}: arrow not found"); yield break; }
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = arrow; arrow.SendEvent(e); }
            yield return new WaitForSecondsRealtime(0.3f);
        }

        private static Button FindButton(string text)
        {
            foreach (var doc in FindObjectsByType<UIDocument>())
            {
                if (doc.rootVisualElement == null) continue;
                Button found = null;
                doc.rootVisualElement.Query<Button>().ForEach(b =>
                {
                    if (found != null || !b.enabledInHierarchy || b.worldBound.width < 1f) return;
                    if (b.text == text) { found = b; return; }
                    b.Query<Label>().ForEach(l => { if (found == null && l.text == text) found = b; });
                });
                if (found != null) return found;
            }
            return null;
        }

        private static Label FindLabelStarting(string text)
        {
            foreach (var doc in FindObjectsByType<UIDocument>())
            {
                Label found = null;
                doc.rootVisualElement?.Query<Label>().ForEach(l => { if (found == null && l.text != null && l.text.StartsWith(text)) found = l; });
                if (found != null) return found;
            }
            return null;
        }

        private void Finish()
        {
            string summary = $"SMOKE TEST {(_failures == 0 && _errors == 0 ? "PASSED" : "FAILED")}: {_steps} steps, {_failures} failed, {_errors} errors/exceptions logged, {Time.realtimeSinceStartup - _started:0}s\n";
            string text = summary + _log;
            Debug.Log(text);
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "smoketest.txt"), text);
                string outPath = GameFlow.Arg("-smokeOut");
                if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, text);
            }
            catch (Exception e) { Debug.LogWarning(e.Message); }
            Application.Quit();
        }
    }
}
