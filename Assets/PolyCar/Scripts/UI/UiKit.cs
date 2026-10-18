using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>Shared UI Toolkit helpers: the panel settings, the stylesheet and small widget builders.</summary>
    public static class UiKit
    {
        private static PanelSettings _panel;
        private static StyleSheet _styles;

        public static PanelSettings Panel => _panel ? _panel : (_panel = Resources.Load<PanelSettings>("PolyCarPanel"));
        public static StyleSheet Styles => _styles ? _styles : (_styles = Resources.Load<StyleSheet>("PolyCarStyles"));

        public static void AddStyles(VisualElement root)
        {
            if (Styles != null && !root.styleSheets.Contains(Styles)) root.styleSheets.Add(Styles);
            root.AddToClassList("pc-root");
        }

        /// <summary>A UIDocument on its own GameObject with the game stylesheet, ready for code-built UI.</summary>
        public static UIDocument CreateDocument(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Panel;
            doc.sortingOrder = sortingOrder;
            AddStyles(doc.rootVisualElement);
            return doc;
        }

        /// <summary>One EventSystem driven by the Input System, so mouse, keyboard and gamepad all reach the UI.</summary>
        public static void EnsureEventSystem()
        {
            var es = UnityEngine.EventSystems.EventSystem.current ?? UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                es = go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            }
            foreach (var legacy in es.GetComponents<UnityEngine.EventSystems.StandaloneInputModule>()) UnityEngine.Object.Destroy(legacy);
            if (es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        public static Label Label(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        public static VisualElement Box(params string[] classes)
        {
            var v = new VisualElement();
            foreach (var c in classes) v.AddToClassList(c);
            return v;
        }

        public static Button Button(string text, Action onClick, params string[] classes)
        {
            var b = new Button(() =>
            {
                GameAudio.Click();
                onClick?.Invoke();
            }) { text = text };
            b.AddToClassList("pc-button");
            foreach (var c in classes) b.AddToClassList(c);
            b.focusable = true;
            b.RegisterCallback<FocusInEvent>(_ => GameAudio.Hover());
            b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledSelf) b.Focus(); });
            return b;
        }

        /// <summary>A "◀ value ▶" selector used by the settings and race-setup screens.</summary>
        public static VisualElement Stepper(string label, Func<string> value, Action<int> step, out Action refresh)
        {
            var row = Box("pc-row");
            row.Add(Label(label, "pc-row-label"));
            var val = Label("", "pc-row-value");
            var left = Button("<", () => { step(-1); }, "pc-step");
            var right = Button(">", () => { step(1); }, "pc-step");
            row.Add(left); row.Add(val); row.Add(right);
            refresh = () => val.text = value();
            var r = refresh;
            left.clicked += () => r();
            right.clicked += () => r();
            // Left/right on a focused arrow changes the value as well.
            row.RegisterCallback<NavigationMoveEvent>(e =>
            {
                if (e.direction == NavigationMoveEvent.Direction.Left) { step(-1); r(); GameAudio.Click(); e.StopPropagation(); }
                else if (e.direction == NavigationMoveEvent.Direction.Right) { step(1); r(); GameAudio.Click(); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            refresh();
            return row;
        }

        public static VisualElement Slider(string label, float min, float max, float value, Action<float> changed)
        {
            var row = Box("pc-row");
            row.Add(Label(label, "pc-row-label"));
            var s = new UnityEngine.UIElements.Slider(min, max) { value = value };
            s.AddToClassList("pc-slider");
            var val = Label(Mathf.RoundToInt(value * 100f) + "%", "pc-row-value-small");
            s.RegisterValueChangedCallback(e => { val.text = Mathf.RoundToInt(e.newValue * 100f) + "%"; changed(e.newValue); });
            row.Add(s); row.Add(val);
            return row;
        }

        public static VisualElement Bar(string label, float value01, string cls = "")
        {
            var row = Box("pc-stat");
            row.Add(Label(label, "pc-stat-label"));
            var track = Box("pc-stat-track");
            var fill = Box("pc-stat-fill");
            if (!string.IsNullOrEmpty(cls)) fill.AddToClassList(cls);
            fill.style.width = Length.Percent(Mathf.Clamp01(value01) * 100f);
            track.Add(fill);
            row.Add(track);
            return row;
        }

        public static string FormatTime(float seconds, bool showPlus = false)
        {
            if (seconds <= 0f && !showPlus) return "--:--.--";
            string sign = "";
            if (showPlus) { sign = seconds < 0f ? "-" : "+"; seconds = Mathf.Abs(seconds); }
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60f;
            return m > 0 || !showPlus ? $"{sign}{m}:{s:00.00}" : $"{sign}{s:0.00}";
        }

        public static string MedalName(Medal m) => m switch { Medal.Gold => "GOLD", Medal.Silver => "SILVER", Medal.Bronze => "BRONZE", _ => "" };
        public static string MedalClass(Medal m) => m switch { Medal.Gold => "medal-gold", Medal.Silver => "medal-silver", Medal.Bronze => "medal-bronze", _ => "medal-none" };

        public static string Ordinal(int n) => n switch { 1 => "1ST", 2 => "2ND", 3 => "3RD", _ => n + "TH" };
    }
}
