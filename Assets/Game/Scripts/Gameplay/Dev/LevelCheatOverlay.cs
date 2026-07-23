#if GPIE_DEVELOPMENT
using UnityEngine;

namespace Gameplay.Dev
{
    /// <summary>
    /// Dev/QA level-switch overlay view (TASK-011): IMGUI on-screen controls — ◀ prev / ▶ next / a direct
    /// per-level jump list — plus a label showing the current level id, and '[' / ']' hotkeys. Pure view: it
    /// holds no gameplay state and defers every switch to <see cref="LevelCheatController"/>, applied in
    /// <see cref="Update"/> (never inside OnGUI) so the level is not reloaded mid-layout. Editor/dev builds only
    /// (GPIE_DEVELOPMENT).
    /// </summary>
    public sealed class LevelCheatOverlay : MonoBehaviour
    {
        // Sentinel for "no queued jump". A prev at index 0 resolves to -1 and correctly no-ops (there is no
        // level before the first), so the collision with the sentinel is harmless.
        private const int NoPending = -1;

        private LevelCheatController _controller;
        private bool _expanded;
        private int _pendingIndex = NoPending;

        public void Bind(LevelCheatController controller) => _controller = controller;

        private void Update()
        {
            if (_controller == null || _pendingIndex == NoPending)
                return;

            _controller.JumpTo(_pendingIndex);
            _pendingIndex = NoPending;
        }

        private void OnGUI()
        {
            if (_controller == null)
                return;

            ReadHotkeys();

            using (new GUILayout.AreaScope(new Rect(10f, 10f, 240f, Screen.height - 20f)))
            using (new GUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label($"DEV · Levels ({_controller.CurrentIndex + 1}/{_controller.LevelCount})");
                GUILayout.Label($"Now: {_controller.CurrentLevelId}");

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("◀ Prev"))
                        _pendingIndex = _controller.CurrentIndex - 1;
                    if (GUILayout.Button("Next ▶"))
                        _pendingIndex = _controller.CurrentIndex + 1;
                }

                _expanded = GUILayout.Toggle(_expanded, _expanded ? "Hide list" : "Jump to…", GUI.skin.button);
                if (_expanded)
                {
                    for (var i = 0; i < _controller.LevelCount; i++)
                    {
                        var marker = i == _controller.CurrentIndex ? "▶ " : "    ";
                        if (GUILayout.Button($"{marker}{i + 1}. {_controller.LevelIdAt(i)}"))
                            _pendingIndex = i;
                    }
                }

                GUILayout.Label("Hotkeys: [ prev · ] next");
            }
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
        }
    }
}
#endif
