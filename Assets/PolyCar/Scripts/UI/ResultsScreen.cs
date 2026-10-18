using UnityEngine;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>End-of-race card: place, time, medal, record, drift score, standings, unlocks.</summary>
    public class ResultsScreen : MonoBehaviour
    {
        public static bool Visible { get; private set; }

        private RaceManager _race;
        private UIDocument _doc;
        private VisualElement _overlay;

        public static ResultsScreen Create(RaceManager race)
        {
            var doc = UiKit.CreateDocument("[Results]", 30, race.transform);
            var rs = doc.gameObject.AddComponent<ResultsScreen>();
            rs._race = race;
            rs._doc = doc;
            UiNavigator.Attach(doc);
            race.PlayerFinished += rs.Show;
            Visible = false;
            return rs;
        }

        private void OnDestroy() => Visible = false;

        private void Show(RaceResult r)
        {
            UiKit.EnsureEventSystem();
            Visible = true;
            GameAudio.DuckMusic(true);
            _overlay = UiKit.Box("overlay", "overlay-results");
            var panel = UiKit.Box("panel", "panel-results");

            bool multi = r.racers > 1;
            string head = multi ? (r.position == 1 ? "YOU WIN!" : UiKit.Ordinal(r.position) + " PLACE") : "FINISHED";
            var title = UiKit.Label(head, "panel-title", "results-title");
            if (multi && r.position == 1) title.AddToClassList("results-win");
            panel.Add(title);
            panel.Add(UiKit.Label((_race.Map != null ? _race.Map.displayName.ToUpper() : "") + "  -  " + _race.PlayerSpec.displayName.ToUpper(), "panel-subtitle"));

            var cols = UiKit.Box("results-cols");
            var left = UiKit.Box("results-left");
            left.Add(UiKit.Label("TIME", "results-key"));
            left.Add(UiKit.Label(UiKit.FormatTime(r.time), "results-time"));
            if (r.newRecord) left.Add(UiKit.Label(r.previousBest > 0f ? "NEW RECORD  (" + UiKit.FormatTime(r.time - r.previousBest, true) + ")" : "NEW RECORD", "results-record"));
            else if (r.previousBest > 0f) left.Add(UiKit.Label("BEST  " + UiKit.FormatTime(r.previousBest), "results-key"));

            var medal = UiKit.Box("medal", UiKit.MedalClass(r.medal));
            medal.Add(UiKit.Label(r.medal == Medal.None ? "NO MEDAL" : UiKit.MedalName(r.medal), "medal-text"));
            left.Add(medal);
            if (_race.Map != null)
            {
                float scale = _race.Path.loop && _race.Map.laps > 0 ? _race.Laps / (float)_race.Map.laps : 1f;
                left.Add(UiKit.Label($"GOLD {UiKit.FormatTime(_race.Map.goldTime * scale)}   SILVER {UiKit.FormatTime(_race.Map.silverTime * scale)}   BRONZE {UiKit.FormatTime(_race.Map.bronzeTime * scale)}", "results-par"));
            }
            if (r.bestLap > 0f) left.Add(UiKit.Label("BEST LAP  " + UiKit.FormatTime(r.bestLap), "results-key"));
            left.Add(UiKit.Label("DRIFT SCORE  " + r.driftScore.ToString("N0"), "results-key"));
            if (r.unlockedSomething) left.Add(UiKit.Label("UNLOCKED:  " + r.unlockedName.ToUpper(), "results-unlock"));
            cols.Add(left);

            var cup = GameFlow.Cup;
            if (multi)
            {
                var right = UiKit.Box("results-right");
                right.Add(UiKit.Label(cup != null ? $"RACE {cup.index + 1} OF {cup.maps.Length}" : "STANDINGS", "results-key"));
                int i = 1;
                foreach (var s in r.standings)
                {
                    var row = UiKit.Box("results-row");
                    if (s.player) row.AddToClassList("results-row-player");
                    row.Add(UiKit.Label(i + ".", "results-row-pos"));
                    row.Add(UiKit.Label(s.name.ToUpper(), "results-row-name"));
                    row.Add(UiKit.Label(s.finished ? UiKit.FormatTime(s.time) : "racing", "results-row-time"));
                    right.Add(row);
                    i++;
                }
                if (cup != null)
                {
                    right.Add(UiKit.Label(cup.IsLastRace ? "FINAL CUP STANDINGS" : "CUP POINTS", "results-key", "results-cup-head"));
                    int k = 1;
                    foreach (var kv in cup.Standings())
                    {
                        var row = UiKit.Box("results-row");
                        if (kv.Key == "You") row.AddToClassList("results-row-player");
                        row.Add(UiKit.Label(k + ".", "results-row-pos"));
                        row.Add(UiKit.Label(kv.Key.ToUpper(), "results-row-name"));
                        row.Add(UiKit.Label(kv.Value + " PTS", "results-row-time"));
                        right.Add(row);
                        k++;
                    }
                }
                cols.Add(right);
            }
            if (cup != null && cup.IsLastRace)
            {
                bool champ = cup.Standings()[0].Key == "You";
                title.text = champ ? "CHAMPION!" : "CUP COMPLETE";
                title.EnableInClassList("results-win", champ);
            }
            panel.Add(cols);

            var buttons = UiKit.Box("panel-buttons-row");
            Button again;
            if (cup != null)
            {
                if (!cup.IsLastRace)
                {
                    var nextMap = MapCatalog.Instance.Get(cup.maps[cup.index + 1]);
                    again = UiKit.Button("NEXT RACE: " + nextMap.displayName.ToUpper(), GameFlow.NextCupRace);
                    buttons.Add(again);
                    buttons.Add(UiKit.Button("QUIT CUP", GameFlow.ToMenu, "pc-button-dim"));
                }
                else
                {
                    again = UiKit.Button("MAIN MENU", GameFlow.ToMenu);
                    buttons.Add(again);
                }
            }
            else
            {
                again = UiKit.Button("RACE AGAIN", GameFlow.Restart);
                buttons.Add(again);
                var next = NextMap();
                if (next != null) buttons.Add(UiKit.Button("NEXT: " + next.displayName.ToUpper(), () => GameFlow.StartRace(next.id, _race.PlayerSpec.id)));
                buttons.Add(UiKit.Button("MAIN MENU", GameFlow.ToMenu, "pc-button-dim"));
            }
            panel.Add(buttons);

            _overlay.Add(panel);
            _doc.rootVisualElement.Add(_overlay);
            _overlay.AddToClassList("fade-in");
            again.schedule.Execute(() => again.Focus()).StartingIn(50);
        }

        private MapSpec NextMap()
        {
            var cat = MapCatalog.Instance;
            if (cat == null || _race.Map == null || cat.maps.Length < 2) return null;
            int i = cat.IndexOf(_race.Map.id);
            return cat.maps[(i + 1) % cat.maps.Length];
        }
    }
}
