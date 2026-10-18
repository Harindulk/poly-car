using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>In-race HUD: position, laps, timers, split deltas, speed/gear/boost, drift combo, minimap.</summary>
    public class RaceHud : MonoBehaviour
    {
        private RaceManager _race;
        private UIDocument _doc;
        private VisualElement _root;
        private Label _pos, _posTotal, _lap, _lapTime, _time, _best, _delta, _speed, _unit, _gear, _center, _sub, _driftPts, _driftMult, _driftTotal, _fps, _hint;
        private VisualElement _boostFill, _boostBar, _drift, _standings, _mini, _miniPath;
        private readonly Dictionary<Racer, VisualElement> _dotOf = new Dictionary<Racer, VisualElement>();
        private readonly List<Label> _rows = new List<Label>();
        private float _deltaHide, _centerHide;
        private Bounds _bounds;
        private float _miniScale;
        private Vector2 _miniOffset;
        private float _fpsTimer; private int _fpsFrames;
        private const float MiniSize = 230f;

        public static RaceHud Create(RaceManager race)
        {
            var doc = UiKit.CreateDocument("[HUD]", 10, race.transform);
            var hud = doc.gameObject.AddComponent<RaceHud>();
            hud._race = race;
            hud._doc = doc;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            _root = UiKit.Box("hud");
            _root.pickingMode = PickingMode.Ignore;
            _doc.rootVisualElement.Add(_root);

            // Top left: position, lap, standings.
            var tl = UiKit.Box("hud-top-left");
            var posRow = UiKit.Box("hud-pos-row");
            _pos = UiKit.Label("1", "hud-pos");
            _posTotal = UiKit.Label("/4", "hud-pos-total");
            posRow.Add(_pos); posRow.Add(_posTotal);
            tl.Add(posRow);
            _lap = UiKit.Label("LAP 1/3", "hud-lap");
            _lapTime = UiKit.Label("", "hud-laptime");
            tl.Add(_lap); tl.Add(_lapTime);
            _standings = UiKit.Box("hud-standings");
            tl.Add(_standings);
            _root.Add(tl);

            // Top centre: race time, best, split delta.
            var tc = UiKit.Box("hud-top-center");
            _time = UiKit.Label("0:00.00", "hud-time");
            _best = UiKit.Label("", "hud-best");
            _delta = UiKit.Label("", "hud-delta");
            tc.Add(_time); tc.Add(_best); tc.Add(_delta);
            _root.Add(tc);

            // Top right: minimap.
            _mini = UiKit.Box("hud-minimap");
            _miniPath = new VisualElement();
            _miniPath.AddToClassList("hud-minimap-path");
            _miniPath.generateVisualContent += DrawMinimap;
            _mini.Add(_miniPath);
            _root.Add(_mini);

            // Bottom right: speed, gear, boost.
            var br = UiKit.Box("hud-speedo");
            var spRow = UiKit.Box("hud-speed-row");
            _speed = UiKit.Label("0", "hud-speed");
            var unitCol = UiKit.Box("hud-unit-col");
            _gear = UiKit.Label("1", "hud-gear");
            _unit = UiKit.Label(GameSettings.SpeedUnit, "hud-unit");
            unitCol.Add(_gear); unitCol.Add(_unit);
            spRow.Add(_speed); spRow.Add(unitCol);
            br.Add(spRow);
            _boostBar = UiKit.Box("hud-boost-track");
            _boostFill = UiKit.Box("hud-boost-fill");
            _boostBar.Add(_boostFill);
            br.Add(UiKit.Label("BOOST  [SHIFT]", "hud-boost-label"));
            br.Add(_boostBar);
            _root.Add(br);

            // Bottom left: drift combo.
            _drift = UiKit.Box("hud-drift");
            _driftPts = UiKit.Label("", "hud-drift-points");
            _driftMult = UiKit.Label("", "hud-drift-mult");
            _driftTotal = UiKit.Label("DRIFT 0", "hud-drift-total");
            var dRow = UiKit.Box("hud-drift-row");
            dRow.Add(_driftPts); dRow.Add(_driftMult);
            _drift.Add(dRow); _drift.Add(_driftTotal);
            _root.Add(_drift);

            // Centre messages.
            var centerBox = UiKit.Box("hud-center");
            _center = UiKit.Label("", "hud-center-msg");
            _sub = UiKit.Label("", "hud-center-sub");
            centerBox.Add(_center); centerBox.Add(_sub);
            _root.Add(centerBox);

            _hint = UiKit.Label("R  respawn     C  camera     Q  look back     ESC  pause", "hud-hint");
            _root.Add(_hint);
            _fps = UiKit.Label("", "hud-fps");
            _root.Add(_fps);

            // Minimap dots.
            foreach (var r in _race.Racers)
            {
                var dot = UiKit.Box("hud-mini-dot");
                if (r.isPlayer) dot.AddToClassList("hud-mini-dot-player");
                _dotOf[r] = dot;
            }
            // Player dot last so it draws on top.
            foreach (var kv in _dotOf) if (!kv.Key.isPlayer) _mini.Add(kv.Value);
            foreach (var kv in _dotOf) if (kv.Key.isPlayer) _mini.Add(kv.Value);
            if (_race.Racers.Count > 1)
                for (int i = 0; i < _race.Racers.Count; i++) { var l = UiKit.Label("", "hud-standing"); _standings.Add(l); _rows.Add(l); }

            PrepareMinimap();
            _race.CountdownTick += OnCountdown;
            _race.SplitDelta += OnSplit;
            _race.LapDone += OnLap;
            _race.StateChanged += s => { if (s == RaceState.Intro) Show(_race.Map != null ? _race.Map.displayName.ToUpper() : "", LapsLabel(), 2.4f); };
            _race.PauseChanged += p => _root.style.display = p ? DisplayStyle.None : DisplayStyle.Flex;
            _race.PlayerFinished += _ => _root.style.display = DisplayStyle.None;
            if (_race.Drift)
            {
                _race.Drift.Banked += pts => { if (pts > 50) ShowSmall("+" + pts.ToString("N0") + " DRIFT", "hud-delta-good"); };
                _race.Drift.Lost += () => ShowSmall("DRIFT LOST", "hud-delta-bad");
            }
            _race.Player.Respawned += () => { if (_race.State == RaceState.Racing) Show("", "RESPAWNED", 0.8f); };
            _race.Player.FinishedRace += () => Show(UiKit.Ordinal(_race.Player.Position), "FINISHED", 3f);
            var rec = _race.PreviousRecord;
            _best.text = rec != null && rec.bestTime > 0f ? "BEST  " + UiKit.FormatTime(rec.bestTime) : "NO RECORD YET";
            _lap.style.display = _race.Path.loop ? DisplayStyle.Flex : DisplayStyle.None;
            _lapTime.style.display = _race.Path.loop ? DisplayStyle.Flex : DisplayStyle.None;
            if (_race.Racers.Count <= 1) _pos.parent.style.display = DisplayStyle.None;
        }

        private string LapsLabel()
        {
            string what = _race.Path.loop ? _race.Laps + (_race.Laps == 1 ? " LAP" : " LAPS") : "SPRINT";
            string who = _race.Racers.Count > 1 ? "  -  " + (_race.Racers.Count - 1) + " RIVALS" : "  -  TIME TRIAL";
            return what + who + (_race.HasGhost ? "  -  GHOST ON" : "");
        }

        private void Show(string big, string small, float seconds)
        {
            _center.text = big;
            _sub.text = small;
            _centerHide = Time.unscaledTime + seconds;
            _center.RemoveFromClassList("hud-wrongway");
        }

        private void ShowSmall(string text, string cls)
        {
            _delta.text = text;
            _delta.RemoveFromClassList("hud-delta-good");
            _delta.RemoveFromClassList("hud-delta-bad");
            _delta.AddToClassList(cls);
            _deltaHide = Time.unscaledTime + 2f;
        }

        private void OnCountdown(int n)
        {
            if (n > 0) Show(n.ToString(), "", 1f);
            else Show("GO!", "", 0.9f);
            _center.RemoveFromClassList("pop");
            _center.AddToClassList("pop");
            _center.schedule.Execute(() => _center.RemoveFromClassList("pop")).StartingIn(60);
        }

        private void OnSplit(float delta, float time) => ShowSmall(UiKit.FormatTime(delta, true), delta <= 0f ? "hud-delta-good" : "hud-delta-bad");

        private void OnLap(int lap, float lapTime, bool best)
        {
            if (_race.Player.Finished) return;
            int remaining = _race.Laps - (lap + 1);
            Show(remaining == 1 ? "FINAL LAP" : "LAP " + (lap + 2), (best ? "BEST LAP  " : "LAP  ") + UiKit.FormatTime(lapTime), 1.8f);
        }

        private void Update()
        {
            if (_race == null || _race.Player == null) return;
            var p = _race.Player;
            var car = _race.PlayerCar;

            _pos.text = p.Position.ToString();
            _posTotal.text = "/" + _race.Racers.Count;
            int lapShown = Mathf.Clamp(p.Lap + 1, 1, _race.Laps);
            _lap.text = "LAP " + lapShown + "/" + _race.Laps;
            _lapTime.text = p.Lap >= 0 && _race.State == RaceState.Racing ? UiKit.FormatTime(_race.RaceTime - p.CurrentLapStart) + (p.BestLap > 0f ? "   BEST " + UiKit.FormatTime(p.BestLap) : "") : "";
            _time.text = UiKit.FormatTime(p.Finished ? p.FinishTime : _race.RaceTime);
            if (_race.State == RaceState.Intro || _race.State == RaceState.Countdown) _time.text = "0:00.00";

            _speed.text = GameSettings.FormatSpeed(car.SpeedKmh);
            _unit.text = GameSettings.SpeedUnit;
            _gear.text = car.Gear == 0 ? "R" : car.Gear.ToString();
            _boostFill.style.width = Length.Percent(car.Boost * 100f);
            _boostFill.EnableInClassList("hud-boost-active", car.Boosting);
            _boostFill.EnableInClassList("hud-boost-full", car.Boost > 0.98f);

            var d = _race.Drift;
            if (d != null)
            {
                bool active = d.Pending > 1f;
                _driftPts.text = active ? "+" + Mathf.RoundToInt(d.Pending).ToString("N0") : "";
                _driftMult.text = active && d.Multiplier > 1 ? "x" + d.Multiplier : "";
                _driftTotal.text = "DRIFT  " + d.Total.ToString("N0");
            }

            if (Time.unscaledTime > _deltaHide) _delta.text = "";
            if (p.WrongWay && _race.State == RaceState.Racing)
            {
                _center.text = "WRONG WAY";
                _sub.text = "turn around or press R";
                _center.AddToClassList("hud-wrongway");
                _centerHide = Time.unscaledTime + 0.2f;
            }
            else if (p.StallTime > 4f && _race.State == RaceState.Racing)
            {
                _center.text = "";
                _sub.text = "STUCK?  PRESS R TO RESPAWN";
                _centerHide = Time.unscaledTime + 0.2f;
            }
            else if (Time.unscaledTime > _centerHide) { _center.text = ""; _sub.text = ""; _center.RemoveFromClassList("hud-wrongway"); }

            _hint.style.opacity = Mathf.Clamp01(1f - (_race.RaceTime - 6f) / 2f);

            // Standings.
            for (int i = 0; i < _rows.Count && i < _race.Racers.Count; i++)
            {
                var r = _race.Racers[i];
                _rows[i].text = (i + 1) + "  " + r.displayName.ToUpper();
                _rows[i].EnableInClassList("hud-standing-player", r.isPlayer);
            }

            UpdateMinimapDots();

            if (GameSettings.Current.showFps)
            {
                _fpsFrames++;
                _fpsTimer += Time.unscaledDeltaTime;
                if (_fpsTimer > 0.5f) { _fps.text = Mathf.RoundToInt(_fpsFrames / _fpsTimer) + " FPS"; _fpsTimer = 0f; _fpsFrames = 0; }
            }
            else _fps.text = "";
        }

        private void PrepareMinimap()
        {
            _bounds = _race.Path.FlatBounds();
            float span = Mathf.Max(_bounds.size.x, _bounds.size.z, 1f);
            _miniScale = (MiniSize - 30f) / span;
            _miniOffset = new Vector2((MiniSize - _bounds.size.x * _miniScale) * 0.5f, (MiniSize - _bounds.size.z * _miniScale) * 0.5f);
        }

        private Vector2 ToMini(Vector3 w)
        {
            return new Vector2(_miniOffset.x + (w.x - _bounds.min.x) * _miniScale,
                               MiniSize - (_miniOffset.y + (w.z - _bounds.min.z) * _miniScale));
        }

        private void DrawMinimap(MeshGenerationContext ctx)
        {
            var path = _race != null ? _race.Path : null;
            if (path == null || path.Count < 2) return;
            var p2d = ctx.painter2D;
            p2d.lineJoin = LineJoin.Round;
            p2d.lineCap = LineCap.Round;
            for (int pass = 0; pass < 2; pass++)
            {
                p2d.strokeColor = pass == 0 ? new Color(0f, 0f, 0f, 0.55f) : new Color(1f, 1f, 1f, 0.92f);
                p2d.lineWidth = pass == 0 ? 9f : 4.5f;
                p2d.BeginPath();
                int step = Mathf.Max(1, path.Count / 400);
                p2d.MoveTo(ToMini(path.points[0]));
                for (int i = step; i < path.Count; i += step) p2d.LineTo(ToMini(path.points[i]));
                p2d.LineTo(ToMini(path.points[path.Count - 1]));
                if (path.loop) p2d.ClosePath();
                p2d.Stroke();
            }
            // Start / finish markers.
            p2d.fillColor = new Color(0.3f, 1f, 0.45f);
            p2d.BeginPath(); p2d.Arc(ToMini(path.points[0]), 5f, 0f, 360f); p2d.Fill();
            if (!path.loop)
            {
                p2d.fillColor = new Color(1f, 0.25f, 0.3f);
                p2d.BeginPath(); p2d.Arc(ToMini(path.points[path.Count - 1]), 5f, 0f, 360f); p2d.Fill();
            }
        }

        private void UpdateMinimapDots()
        {
            foreach (var kv in _dotOf)
            {
                var r = kv.Key;
                if (r == null) continue;
                Vector2 m = ToMini(r.transform.position);
                float size = r.isPlayer ? 14f : 10f;
                kv.Value.style.left = m.x - size * 0.5f;
                kv.Value.style.top = m.y - size * 0.5f;
            }
        }
    }
}
