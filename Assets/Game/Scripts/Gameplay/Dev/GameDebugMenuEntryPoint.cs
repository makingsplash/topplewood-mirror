#if GPIE_DEVELOPMENT
using System;
using Gameplay.Adapters;
using Gameplay.Core;
using SGS.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer.Unity;

namespace Gameplay.Dev
{
    /// <summary>
    /// Visible, keyboard-free entry into the cheats from the PRODUCTION Loading path (TASK-172). The owner directive
    /// (2026-08-21) is that the game boots only through <c>Loading.unity</c>, yet in prod the SayKit DDM menu opens
    /// only by the undiscoverable shake gesture (Escape is legacy-input-only and the project runs the new Input
    /// System, so the Editor had no keyboard trigger at all). This entry point adds the missing affordance without a
    /// second menu: a corner <b>DEV</b> button + backquote (`) flip <c>SayData.Debug.DebugMenuShown</c> — the SAME
    /// observable seam shake flips — so <c>DebugService.ShowDebugMenu</c> instantiates the existing prod
    /// <c>GameDebugMenu</c> (Go to Level, Reset, resources, language). Nothing about the menu content or the jump
    /// persist path (TASK-113: "Go to Level" → <c>SayData.Meta.CurrentLevel</c> → <c>GameDebugMenuRestartBridge</c>)
    /// is touched — the deficit was only the entry, so that is all this adds.
    ///
    /// Registered ONLY by the prod <c>GameplayLifetimeScope</c> under <c>GPIE_DEVELOPMENT</c> (next to
    /// <c>GameDebugMenuRestartBridge</c>), never the shared <c>GameplayCoreInstaller</c>: there it would raise a
    /// second menu in bringup and require the prod-only <c>DebugService</c>/<c>SayData</c> graph that bringup does not
    /// hold. The headless sim news the domain past the scopes, so this entry point is never created there and the
    /// poseHash/headless invariant is unaffected. The corner button + EventSystem bootstrap and the RESTART-hide are
    /// shared with the bringup host via <see cref="DevOverlayUi"/>/<see cref="RestartButtonGuard"/>.
    ///
    /// Two things beyond "just open the menu":
    ///  1. An always-visible level indicator ("L i/N: levelId"; in the pool "L number · pool k/M: levelId", TASK-528)
    ///     sits under the button, so a developer looking at the
    ///     new art sees the current level without opening anything.
    ///  2. While the menu is open the RESTART canvas is hidden (<see cref="RestartButtonGuard"/>) — otherwise the
    ///     z-order collision of TASK-117 reproduces on the prod path (RESTART at short.MaxValue-1 swallows the menu's
    ///     close "X"). The hook is on <c>DebugMenuShown.OnChanged</c>, so it also fixes the collision for shake-opens.
    /// </summary>
    public sealed class GameDebugMenuEntryPoint : IStartable, ITickable, IDisposable
    {
        private readonly CampaignProgressionController _campaign;
        private readonly ILevelSource _levelSource;
        private readonly GameModel _model;
        private readonly RestartButtonGuard _restartGuard = new();

        private GameObject _eventSystem;
        private GameObject _buttonCanvas;
        private Text _indicator;

        // Cached indicator inputs so Tick rebuilds the label string only when it actually changes (no per-frame alloc).
        private int _lastNumber = int.MinValue;
        private int _lastCount = int.MinValue;
        private string _lastLevelId;

        public GameDebugMenuEntryPoint(CampaignProgressionController campaign, ILevelSource levelSource, GameModel model)
        {
            _campaign = campaign;
            _levelSource = levelSource;
            _model = model;
        }

        private string CurrentLevelId => _model.CurrentLevel == null ? "<none>" : _model.CurrentLevel.LevelId;

        public void Start()
        {
            DevOverlayUi.EnsureEventSystem(out _eventSystem);
            _buttonCanvas = DevOverlayUi.CreateToggleButton(OpenMenu);
            _indicator = CreateIndicator(_buttonCanvas);

            // DebugService (app-lifetime) outlives this scope, so the -= in Dispose is a hard requirement — a leaked
            // delegate would fire into a destroyed guard/GO after the scope tears down (same discipline as TASK-113).
            SayData.Debug.DebugMenuShown.OnChanged += OnMenuVisibilityChanged;
        }

        public void Tick()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.backquoteKey.wasPressedThisFrame)
                OpenMenu();

            UpdateIndicator();
        }

        // Flip the observable to true; if the menu is already open this is a no-op (Observable fires only on a value
        // change), so a re-tap/re-press cannot spawn a second menu. Closing is owned by the menu's own "X".
        private void OpenMenu() => SayData.Debug.DebugMenuShown.Value = true;

        // Fires for BOTH close paths (X tap and any programmatic flip): DebugService sets DebugMenuShown=false in
        // HandleOnDebugMenuClosed, so one subscription covers open→hide and close→restore.
        private void OnMenuVisibilityChanged(bool shown)
        {
            if (shown)
                _restartGuard.Hide();
            else
                _restartGuard.Restore();
        }

        private void UpdateIndicator()
        {
            if (_indicator == null)
                return;

            // The shown number keeps growing past the line-up (TASK-528), so it is the cache key next to the id.
            var number = _campaign.DevProgress.LevelNumber;
            var count = _levelSource.Count;
            var levelId = CurrentLevelId;
            if (number == _lastNumber && count == _lastCount && levelId == _lastLevelId)
                return;

            _lastNumber = number;
            _lastCount = count;
            _lastLevelId = levelId;
            var position = _campaign.DevPosition;
            _indicator.text = position.Track == CampaignTrack.Pool
                ? $"L {number} · pool {position.PoolPlace + 1}/{_campaign.DevPoolCount}: {levelId}"
                : $"L {position.LineupIndex + 1}/{count}: {levelId}";
        }

        // Always-visible overlay label under the DEV button (its own canvas). raycastTarget off so it never steals a
        // tap; an Outline keeps it legible over arbitrary art. Same sources as bringup's in-menu label
        // (DevCampaignIndex is the authoritative live in-memory index, ILevelSource.Count, GameModel.CurrentLevel).
        private static Text CreateIndicator(GameObject buttonCanvas)
        {
            var labelGo = new GameObject("[Dev] LevelIndicator", typeof(Text), typeof(Outline));
            labelGo.transform.SetParent(buttonCanvas.transform, false);

            var text = labelGo.GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.fontSize = 34;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f); // top-left, directly below the DEV button.
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -120f);
            rect.sizeDelta = new Vector2(700f, 56f);

            return text;
        }

        public void Dispose()
        {
            SayData.Debug.DebugMenuShown.OnChanged -= OnMenuVisibilityChanged;

            // Safety net: if the scope tears down while the menu is open, put RESTART back to the state we found it in
            // (no-op if we never hid it).
            _restartGuard.Restore();

            // Our overlay canvas is DontDestroyOnLoad, so tear it down explicitly (the indicator is its child and dies
            // with it). The EventSystem is destroyed ONLY when we created it ourselves (EnsureEventSystem left it null
            // if a pre-existing one was found — never destroy someone else's).
            if (_buttonCanvas != null)
                UnityEngine.Object.Destroy(_buttonCanvas);
            _buttonCanvas = null;

            if (_eventSystem != null)
                UnityEngine.Object.Destroy(_eventSystem);
            _eventSystem = null;
        }
    }
}
#endif
