#if GPIE_DEVELOPMENT
using System;
using DynamicDebugMenu;
using DynamicDebugMenu.Elements;
using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.Tutorial;
using Tools.Constants;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VContainer.Unity;

namespace Gameplay.Dev
{
    /// <summary>
    /// Dev/QA level-switch cheat for the isolated <c>ShapeFillBringup</c> scene (TASK-047): the reachable
    /// SayKit <b>DynamicDebugMenu</b> (DDM) UI, hosted directly inside the bringup DI scope. Replaces the
    /// hand-rolled IMGUI level-switch overlay (TASK-011, removed by this task) so there is a single cheat UI
    /// framework — DDM — in both launch contexts (prod <see cref="Tools.Debug"/> <c>GameDebugMenu</c>, bringup
    /// here). Editor/dev builds only (<c>GPIE_DEVELOPMENT</c>) — the whole file is guarded and never compiles
    /// into player builds.
    ///
    /// Why a scope-resident host and not the static <c>GameDebugMenu.Create</c>: the DDM *UI* elements
    /// (<see cref="DDMMain"/>/<see cref="DDMButton"/>/<see cref="DDMInputButton"/>/<see cref="DDMLabel"/>)
    /// depend only on <see cref="DebugItemsConfig"/> prefabs loaded from Resources — zero coupling to
    /// SayData/Data/GlobalService, which bringup deliberately does not raise (RFC R5). The static path is
    /// wired to the full prod data graph and is unliftable here; hosting DDM as a bringup entry point lets us
    /// inject <see cref="CampaignProgressionController"/> by ordinary DI — that constructor injection *is* the
    /// "meta↔gameplay-scope bridge", no statics involved.
    ///
    /// Triggers (either opens the menu): an always-visible on-screen <b>DEV</b> button built at runtime in
    /// <see cref="DevOverlayUi.CreateToggleButton"/> — the discoverable, keyboard-free entry point that works on
    /// device and in the Editor (TASK-062, the template's cut prefab button rebuilt in code since the Unity bridge
    /// has no prefab-save op; shared with the prod entry point since TASK-172) — and the backquote (`) key in
    /// <see cref="Tick"/>, an Editor convenience kept because
    /// bringup is usually played there. The button lives on its own DontDestroyOnLoad overlay canvas pinned to
    /// a screen corner; gameplay taps ride the raw PointerTapInputSource (not uGUI), an independent pipeline, so
    /// a detail under the button stays tappable. The switch semantics mirror the retired overlay: live
    /// <see cref="CampaignProgressionController.DevJumpToLevel"/> (0-based, no meta persist), prev/next, and a
    /// visible i/N + level-id label — bringup has no SayData.Meta, so persisting a jump is physically
    /// unavailable there (design-locked, RFC Q2).
    /// </summary>
    public sealed class BringupDebugMenuHost : IStartable, ITickable, IDisposable
    {
        private readonly ILevelSource _levelSource;
        private readonly GameModel _model;
        private readonly CampaignProgressionController _campaign;
        private readonly ICampaignStore _store;
        private readonly ITutorialProgressStore _tutorialProgress;
        private readonly TutorialCatalog _tutorialCatalog;

        private readonly RestartButtonGuard _restartGuard = new(); // hides the product RESTART canvas while the menu is open (TASK-117).

        private DebugItemsConfig _config;
        private DDMMain _menu;
        private GameObject _eventSystem;
        private GameObject _buttonCanvas;

        public BringupDebugMenuHost(
            ILevelSource levelSource, GameModel model, CampaignProgressionController campaign,
            ICampaignStore store, ITutorialProgressStore tutorialProgress, TutorialCatalog tutorialCatalog)
        {
            _levelSource = levelSource;
            _model = model;
            _campaign = campaign;
            _store = store;
            _tutorialProgress = tutorialProgress;
            _tutorialCatalog = tutorialCatalog;
        }

        private string CurrentLevelId => _model.CurrentLevel == null ? "<none>" : _model.CurrentLevel.LevelId;

        public void Start()
        {
            // The DDM "Console" button (DDMMain.MainMenuContent) calls SRDebug.Instance → NRE if SRDebug was
            // never initialized. bringup skips the LoadingGame bootstrap that normally inits it (LoadingGame.cs:98),
            // so guard-init it here. The IsInitialized gate is a cheap defensive/self-documenting guard, NOT a
            // correctness requirement: SRDebug.Init is in fact idempotent — SRServiceManager.RegisterAssembly
            // (early-returns if _assemblies already contains it) and RegisterService (early-returns on an identical
            // registered service) both short-circuit, so a redundant second init (prod boot → then enter bringup)
            // is a harmless no-op even without the gate.
            if (!SRDebug.IsInitialized)
                SRDebug.Init();

            // DDM UI prefabs live in a Resources ScriptableObject; loadable without SayData (LoadingGame.cs:95).
            // The menu is NOT shown on start — it is toggled by the button/key so it never occludes gameplay.
            _config = Resources.Load<DebugItemsConfig>(ConfigPath.DebugItems);

            // The always-visible on-screen entry point (TASK-062), shared with the prod path via DevOverlayUi
            // (TASK-172). Its own uGUI button needs an EventSystem to receive clicks, so raise one now instead of
            // lazily in ToggleMenu. EnsureEventSystem sets _eventSystem only when it creates one itself (null if a
            // pre-existing EventSystem was found), so Dispose destroys exactly the one we own.
            DevOverlayUi.EnsureEventSystem(out _eventSystem);
            _buttonCanvas = DevOverlayUi.CreateToggleButton(ToggleMenu);
        }

        public void Tick()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.backquoteKey.wasPressedThisFrame)
                ToggleMenu();
        }

        private void ToggleMenu()
        {
            if (_menu != null)
            {
                // CloseMenu destroys the menu GO; its OnDestroy fires our close callback, which nulls _menu.
                _menu.CloseMenu();
                return;
            }

            // Defensive re-ensure (the button in Start already raised one); keep _eventSystem pointing at the GO we
            // own — only overwrite it when EnsureEventSystem actually created a new one, never clobber it with null.
            DevOverlayUi.EnsureEventSystem(out var ensured);
            if (ensured != null)
                _eventSystem = ensured;

            // MainPrefab carries its own screen-space Canvas + GraphicRaycaster + CanvasScaler (verified by the
            // owner), so it renders and receives clicks without an external UI canvas — the only missing piece
            // is a scene EventSystem, raised by EnsureEventSystem above.
            var go = UnityEngine.Object.Instantiate(_config.MainPrefab);

            // TASK-117 — why the menu's close "X" could not be tapped, and the lever that fixes it.
            //
            // MECHANISM (measured, not guessed). Menu and RESTART are both ScreenSpaceOverlay canvases, so a tap in
            // their overlap is decided by Canvas.sortingOrder — higher wins the GraphicRaycaster hit-sort. The
            // always-on RESTART button (RestartButtonPresenter, TASK-116) rides its own overlay canvas at
            // short.MaxValue-1 = 32766; the DDM MainPrefab canvas ships at sortingOrder 2 (DDM Main.prefab: single
            // root Canvas, m_OverrideSorting:0). 32766 ≫ 2, so in the shared top-right corner RESTART swallowed the
            // tap meant for the menu's "X" — the menu could not be closed by X.
            //
            // WHY NOT JUST RE-ORDER (measured). Canvas.sortingOrder is a signed 16-bit field that WRAPS, not clamps
            // (play-mode probe: set 32768 → readback -32768; set 40000 → -25536). The ceiling is short.MaxValue
            // (32767); anything above sinks to the BOTTOM of the stack. So above a fixed RESTART=32766 there is
            // exactly ONE slot (32767) — you cannot stack two strictly-ordered canvases there. And DDM's page root is
            // a FULL-SCREEN raycast scrim (DDM Page.prefab: anchors 0,0–1,1, RaycastTarget on), so a menu sharing an
            // order with the DEV toggle (also short.MaxValue) is an undefined raycast winner in the toggle's corner —
            // the emergency exit would stop being deterministic. Ordering alone cannot give both (X > RESTART) and
            // (toggle > menu-scrim).
            //
            // LEVER (owner decision, TASK-117): remove the third participant instead of re-ordering. While the menu
            // is open we HIDE the RESTART canvas (HideRestartButtonWhileMenuOpen, below), so X has no competitor in
            // the top-right AND the menu no longer needs the top slot — it sits at short.MaxValue-1, STRICTLY below
            // the DEV toggle (short.MaxValue), which makes the toggle emergency-exit win its corner deterministically.
            // Resulting order:
            //   menu open:   DEV toggle (32767) > menu scrim (32766) > [RESTART hidden]
            //   menu closed: DEV toggle (32767) > RESTART (32766) > banner (31000) > gameplay
            // The whole host is #if GPIE_DEVELOPMENT, so player builds and the product play-screen layout are
            // untouched regardless.
            go.GetComponent<Canvas>().sortingOrder = short.MaxValue - 1;

            // While the menu is open, hide the product RESTART canvas so X has no competitor in the top-right — the
            // non-ordering lever from the TASK-117 comment above, now shared with the prod path (RestartButtonGuard,
            // TASK-172). Runs from the close callback on every close path (X tap and DEV-toggle re-tap both destroy
            // the menu GO, and OnDestroy invokes the callback).
            _restartGuard.Hide();

            _menu = go.GetComponent<DDMMain>();
            _menu.Init(_config, BuildContent, _ =>
            {
                _menu = null;
                _restartGuard.Restore();
            });
        }

        // Content generator handed the live DDMContext by DDMMain — mirrors the retired overlay's UX.
        private void BuildContent(DDMContext context)
        {
            // Live number + track position + current level id; DDMLabel.CreateDynamic re-reads this every frame.
            DDMLabel.CreateDynamic(context, PositionLabel);

            // ◀/▶ move along the current track (TASK-528): clamped on the line-up, around the circle in the pool.
            // Non-persisting, like every bringup jump (RFC Q2).
            DDMButton.Create(context, "◀ Prev", () => _campaign.DevStep(-1));
            DDMButton.Create(context, "Next ▶", () => _campaign.DevStep(1));
            DDMButton.Create(context, "Jump to pool", () => _campaign.DevJumpToPool(0, persist: false));

            DDMInputButton.Create(context, "Go to Level (1-based): ", "Go", InputField.ContentType.IntegerNumber,
                () => (_campaign.DevCampaignIndex + 1).ToString(),
                stringValue =>
                {
                    if (int.TryParse(stringValue, out var oneBased))
                        Jump(oneBased - 1);
                });

            // Reset progress reachable through existing seams (owner condition, TASK-047 / fix TASK-109): persist
            // the 0-based pointer to 0 AND forget the tutorial, then jump there live. No new seam method — both
            // domain contracts (ICampaignStore, ITutorialProgressStore) are untouched.
            DDMButton.Create(context, "Reset progress", () =>
            {
                // M-2: durable-reset the campaign to a fresh install — number, "line-up cleared" and pool place
                // (TASK-528). Durable even when the stored values already equal the defaults
                // (PrefsCampaignStore.ForceSave, TASK-109 §3.2).
                _store.Write(CampaignProgress.Fresh);

                // M-1: reset FTUE via the existing dev seam — "forget the tutorial so it replays"
                // (ITutorialProgressStore.cs:17-18), durable. The reset jumps to level 0, so it forgets the tour
                // registered for level 0's id in the TutorialCatalog (sailboat → "first_tutorial", TASK-377) — the
                // same id TutorialRunner.ShouldPlay reads there. A level 0 with no tour → nothing to reset.
                var firstTour = _tutorialCatalog.Resolve(_levelSource.Load(0).LevelId);
                if (firstTour != null)
                    _tutorialProgress.Reset(firstTour.Id);

                // Reload level 0 in-place → GameModel.LevelLoaded → TutorialRunner.OnLevelLoaded → ShouldPlay
                // (now IsCompleted==false, level 0 loaded) → FTUE replays. No relaunch needed.
                _campaign.DevJumpToLevel(0);
            });
        }

        // DevJumpToLevel clamps out-of-range to the line-up bounds (a first-pass jump). The dynamic label picks up the
        // new position on its next Update.
        private void Jump(int index) => _campaign.DevJumpToLevel(index);

        // "Level N · lineup i/Count: id" or "Level N · pool k/M: id" (TASK-528) — the shown number and the content
        // position diverge after the line-up, so both are visible.
        private string PositionLabel()
        {
            var position = _campaign.DevPosition;
            var number = _campaign.DevProgress.LevelNumber;
            return position.Track == CampaignTrack.Pool
                ? $"Level {number} · pool {position.PoolPlace + 1}/{_campaign.DevPoolCount}: {CurrentLevelId}"
                : $"Level {number} · lineup {position.LineupIndex + 1}/{_levelSource.Count}: {CurrentLevelId}";
        }

        public void Dispose()
        {
            if (_menu != null)
                UnityEngine.Object.Destroy(_menu.gameObject);
            _menu = null;

            // Destroy ONLY the EventSystem we created ourselves: it is DontDestroyOnLoad, so without this it
            // leaks for the whole play session. _eventSystem is non-null exactly when EnsureEventSystem created
            // one; if it found a pre-existing EventSystem instead, _eventSystem stayed null and we must not touch
            // someone else's object.
            if (_eventSystem != null)
                UnityEngine.Object.Destroy(_eventSystem);
            _eventSystem = null;

            // The on-screen button canvas is DontDestroyOnLoad too, so tear it down explicitly like the EventSystem.
            if (_buttonCanvas != null)
                UnityEngine.Object.Destroy(_buttonCanvas);
            _buttonCanvas = null;
        }
    }
}
#endif
