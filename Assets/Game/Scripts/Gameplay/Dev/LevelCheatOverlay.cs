#if GPIE_DEVELOPMENT
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Dev/QA level-switch overlay view (TASK-011): IMGUI on-screen controls — ◀ prev / ▶ next / a direct
    /// per-level jump list — plus a label showing the current level id, and '[' / ']' hotkeys. Pure view: it
    /// holds no gameplay state and defers every switch to <see cref="LevelCheatController"/>, applied in
    /// <see cref="Update"/> (never inside OnGUI) so the level is not reloaded mid-layout. Editor/dev builds only
    /// (GPIE_DEVELOPMENT). Fonts/buttons/panel scale with screen height so the overlay stays readable and
    /// tappable on high-DPI devices.
    /// Collapse/expand (TASK-015): the overlay starts <b>collapsed</b> into a small top-left widget
    /// ('DEV: &lt;levelId&gt; i/N ▸') that shows the level name/index without covering the play field; clicking it
    /// (or the '\' hotkey) expands the full panel, and clicking the header (or '\') collapses it again. The '['
    /// / ']' switch hotkeys stay live in both states.
    /// </summary>
    public sealed class LevelCheatOverlay : MonoBehaviour
    {
        // Sentinel for "no queued jump". A prev at index 0 resolves to -1 and correctly no-ops (there is no
        // level before the first), so the collision with the sentinel is harmless.
        private const int NoPending = -1;

        // The base pixel sizes below are authored against this screen height; on taller (high-DPI) screens the
        // whole overlay is scaled up by Screen.height / ReferenceHeight so it does not shrink into invisibility.
        private const float ReferenceHeight = 720f;

        private LevelCheatController _controller;

        // Whole-overlay collapse state (TASK-015). Default false → the overlay boots as the compact corner widget
        // and only unfolds the full panel on demand, so gameplay (target + detail pieces) is not occluded.
        private bool _overlayExpanded;

        // Inner jump-list toggle, shown only while the overlay is expanded (distinct from _overlayExpanded).
        private bool _jumpListExpanded;

        private int _pendingIndex = NoPending;

        // Deferred collapse/expand request from the '\' hotkey, applied in Update — mutating the GUI structure
        // mid-OnGUI (between the Layout and Repaint passes) would desync IMGUI's control groups.
        private bool _pendingToggle;

        private GUIStyle _widgetStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _nowStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _listButtonStyle;
        private GUIStyle _hintStyle;
        private float _builtScale;

        public void Bind(LevelCheatController controller) => _controller = controller;

        private void Update()
        {
            if (_controller == null)
                return;

            if (_pendingToggle)
            {
                _overlayExpanded = !_overlayExpanded;
                _pendingToggle = false;
            }

            if (_pendingIndex == NoPending)
                return;

            _controller.JumpTo(_pendingIndex);
            _pendingIndex = NoPending;
        }

        private void OnGUI()
        {
            if (_controller == null)
                return;

            var scale = Mathf.Max(1f, Screen.height / ReferenceHeight);
            EnsureStyles(scale);

            ReadHotkeys();

            var margin = 24f * scale;

            if (_overlayExpanded)
                DrawExpandedPanel(scale, margin);
            else
                DrawCollapsedWidget(margin);
        }

        // Compact top-left widget: one button that shows the current level id + i/N and expands on click. Sized to
        // its own content so it never spans the screen and leaves the play field clear.
        private void DrawCollapsedWidget(float margin)
        {
            var label = $"DEV: {_controller.CurrentLevelId}  {_controller.CurrentIndex + 1}/{_controller.LevelCount} ▸";
            var size = _widgetStyle.CalcSize(new GUIContent(label));

            using (new GUILayout.AreaScope(new Rect(margin, margin, size.x, size.y)))
            {
                if (GUILayout.Button(label, _widgetStyle))
                    _overlayExpanded = true;
            }
        }

        private void DrawExpandedPanel(float scale, float margin)
        {
            var width = 380f * scale;

            using (new GUILayout.AreaScope(new Rect(margin, margin, width, Screen.height - margin * 2f)))
            using (new GUILayout.VerticalScope(GUI.skin.box))
            {
                // Header doubles as the collapse control ('▾'): clicking it folds the overlay back to the widget.
                if (GUILayout.Button($"DEV · Levels  {_controller.CurrentIndex + 1}/{_controller.LevelCount}  ▾", _headerStyle))
                    _overlayExpanded = false;
                GUILayout.Label($"Now: {_controller.CurrentLevelId}", _nowStyle);

                GUILayout.Space(10f * scale);

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("◀ Prev", _buttonStyle))
                        _pendingIndex = _controller.CurrentIndex - 1;
                    if (GUILayout.Button("Next ▶", _buttonStyle))
                        _pendingIndex = _controller.CurrentIndex + 1;
                }

                _jumpListExpanded = GUILayout.Toggle(_jumpListExpanded, _jumpListExpanded ? "Hide list" : "Jump to…", _toggleStyle);
                if (_jumpListExpanded)
                {
                    for (var i = 0; i < _controller.LevelCount; i++)
                    {
                        var marker = i == _controller.CurrentIndex ? "▶ " : "     ";
                        if (GUILayout.Button($"{marker}{i + 1}. {_controller.LevelIdAt(i)}", _listButtonStyle))
                            _pendingIndex = i;
                    }
                }

                GUILayout.Space(8f * scale);
                GUILayout.Label("Hotkeys: [ prev · ] next · \\ collapse", _hintStyle);
            }
        }

        // Styles derive from GUI.skin (only valid inside OnGUI) and cache their font sizes for the current screen
        // scale; rebuilt only when the scale changes (e.g. a device orientation flip alters Screen.height).
        private void EnsureStyles(float scale)
        {
            if (_widgetStyle != null && Mathf.Approximately(_builtScale, scale))
                return;

            _builtScale = scale;

            _widgetStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(22f * scale),
                fontStyle = FontStyle.Bold,
                fixedHeight = 58f * scale,
            };
            _headerStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(22f * scale),
                fontStyle = FontStyle.Bold,
                fixedHeight = 58f * scale,
            };
            _nowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(32f * scale),
                fontStyle = FontStyle.Bold,
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(26f * scale),
                fixedHeight = 66f * scale,
            };
            _toggleStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(22f * scale),
                fixedHeight = 58f * scale,
            };
            _listButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(22f * scale),
                fixedHeight = 58f * scale,
                alignment = TextAnchor.MiddleLeft,
            };
            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(17f * scale),
            };
        }

        private void ReadHotkeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            if (e.keyCode == KeyCode.LeftBracket)
            {
                _pendingIndex = _controller.CurrentIndex - 1;
                e.Use();
            }
            else if (e.keyCode == KeyCode.RightBracket)
            {
                _pendingIndex = _controller.CurrentIndex + 1;
                e.Use();
            }
            else if (e.keyCode == KeyCode.Backslash)
            {
                _pendingToggle = true;
                e.Use();
            }
        }
    }
}
#endif
