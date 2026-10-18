using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PolyCar
{
    /// <summary>
    /// Keyboard / gamepad navigation for a code-built UI Toolkit document: arrows, WASD, d-pad and the
    /// left stick move focus spatially (with hold-to-repeat), Enter / Space / A presses the focused button.
    /// Left/Right go to the focused control first, so steppers and sliders change their value.
    /// </summary>
    public class UiNavigator : MonoBehaviour
    {
        public UIDocument document;
        private float _repeatAt;
        private Vector2Int _heldDir;

        public static UiNavigator Attach(UIDocument doc)
        {
            var nav = doc.GetComponent<UiNavigator>() ?? doc.gameObject.AddComponent<UiNavigator>();
            nav.document = doc;
            return nav;
        }

        private void Update()
        {
            if (document == null || document.rootVisualElement == null) return;
            var root = document.rootVisualElement;
            if (root.resolvedStyle.display == DisplayStyle.None || root.childCount == 0) return;
            var focus = root.panel?.focusController;
            if (focus == null) return;

            var focused = focus.focusedElement as VisualElement;
            if (focused == null || !IsUsable(focused) || !Visible(focused, root))
            {
                // Nothing usable focused (screen changed): wait for input, then focus the first control.
                if (ReadDirection() != Vector2Int.zero || SubmitPressed())
                {
                    var first = Candidates(root);
                    if (first.Count > 0) { first[0].Focus(); GameAudio.Hover(); }
                    _repeatAt = Time.unscaledTime + 0.35f;
                }
                return;
            }

            if (SubmitPressed())
            {
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = focused; focused.SendEvent(e); }
                return;
            }

            Vector2Int dir = ReadDirection();
            if (dir == Vector2Int.zero) { _heldDir = Vector2Int.zero; return; }
            if (dir == _heldDir && Time.unscaledTime < _repeatAt) return;
            _repeatAt = Time.unscaledTime + (dir == _heldDir ? 0.12f : 0.38f);
            _heldDir = dir;

            var navDir = dir.x < 0 ? NavigationMoveEvent.Direction.Left : dir.x > 0 ? NavigationMoveEvent.Direction.Right
                       : dir.y > 0 ? NavigationMoveEvent.Direction.Up : NavigationMoveEvent.Direction.Down;
            // Let the focused control (stepper row, slider, garage) consume left/right first.
            if (dir.x != 0)
            {
                bool consumed = false;
                using (var e = NavigationMoveEvent.GetPooled(navDir))
                {
                    e.target = focused;
                    focused.SendEvent(e);
                    consumed = e.isPropagationStopped;
                }
                if (consumed) return;
                if (focused is Slider slider)
                {
                    slider.value = Mathf.Clamp(slider.value + dir.x * (slider.highValue - slider.lowValue) * 0.05f, slider.lowValue, slider.highValue);
                    return;
                }
            }
            var next = FindNeighbour(root, focused, dir);
            if (next != null) { next.Focus(); EnsureVisible(next); }
        }

        private static bool SubmitPressed()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
        }

        private static Vector2Int ReadDirection()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            int x = 0, y = 0;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) y = 1;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) y = -1;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) x = -1;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) x = 1;
            }
            if (pad != null)
            {
                Vector2 s = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
                if (s.y > 0.5f) y = 1; else if (s.y < -0.5f) y = -1;
                if (s.x > 0.5f) x = 1; else if (s.x < -0.5f) x = -1;
            }
            if (x != 0 && y != 0) y = 0; // one axis at a time
            return new Vector2Int(x, y);
        }

        private static bool IsUsable(VisualElement e) => e.focusable && e.enabledInHierarchy && e.canGrabFocus;

        private static bool Visible(VisualElement e, VisualElement root)
        {
            for (var v = e; v != null && v != root; v = v.parent)
                if (v.resolvedStyle.display == DisplayStyle.None || v.resolvedStyle.visibility == Visibility.Hidden) return false;
            return e.worldBound.width > 1f;
        }

        private static List<VisualElement> Candidates(VisualElement root)
        {
            var list = new List<VisualElement>();
            root.Query<VisualElement>().Where(v => (v is Button || v is Slider) && IsUsable(v) && Visible(v, root)).ForEach(list.Add);
            return list;
        }

        private static VisualElement FindNeighbour(VisualElement root, VisualElement from, Vector2Int dir)
        {
            Vector2 c = from.worldBound.center;
            Vector2 d = new Vector2(dir.x, -dir.y); // screen space: y grows downward
            VisualElement best = null;
            float bestScore = float.MaxValue;
            foreach (var v in Candidates(root))
            {
                if (v == from) continue;
                Vector2 delta = v.worldBound.center - c;
                float along = Vector2.Dot(delta, d);
                if (along <= 4f) continue;
                float across = Mathf.Abs(Vector2.Dot(delta, new Vector2(-d.y, d.x)));
                float score = along + across * 2.5f;
                if (score < bestScore) { bestScore = score; best = v; }
            }
            return best;
        }

        private static void EnsureVisible(VisualElement e)
        {
            for (var p = e.parent; p != null; p = p.parent)
                if (p is ScrollView sv) { sv.ScrollTo(e); break; }
        }
    }
}
