#if GPIE_DEVELOPMENT
using System;
using DynamicDebugMenu;
using DynamicDebugMenu.Elements;
using Gameplay.Adapters;
using Gameplay.Core;
using Gameplay.Tutorial;
using Tools.Constants;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
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
    /// <see cref="CreateToggleButton"/> — the discoverable, keyboard-free entry point that works on device and
    /// in the Editor (TASK-062, the template's cut prefab button rebuilt in code since the Unity bridge has no
    /// prefab-save op) — and the backquote (`) key in <see cref="Tick"/>, an Editor convenience kept because
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
        private readonly TutorialDefinition _tutorial;

        private DebugItemsConfig _config;
        private DDMMain _menu;
        private GameObject _eventSystem;
        private GameObject _buttonCanvas;
        private GameObject _restartButton;    // product RESTART canvas GO we hide while the menu is open; restored on close.
        private bool _restartButtonWasActive; // its activeSelf before we hid it — close RESTORES this, never force-enables.

        public BringupDebugMenuHost(
            ILevelSource levelSource, GameModel model, CampaignProgressionController campaign,
            ICampaignStore store, ITutorialProgressStore tutorialProgress, TutorialDefinition tutorial)
        {
            _levelSource = levelSource;
            _model = model;
            _campaign = campaign;
            _store = store;
            _tutorialProgress = tutorialProgress;
            _tutorial = tutorial;
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

            // The always-visible on-screen entry point (TASK-062). Its own uGUI button needs an EventSystem to
            // receive clicks, so raise one now instead of lazily in ToggleMenu.
            EnsureEventSystem();
            CreateToggleButton();
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

            EnsureEventSystem();

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

            HideRestartButtonWhileMenuOpen();

            _menu = go.GetComponent<DDMMain>();
            _menu.Init(_config, BuildContent, _ =>
            {
                _menu = null;
                RestoreRestartButton();
            });
        }

        // Hide the product RESTART canvas for as long as the menu is open — the non-ordering lever described in the
        // TASK-117 comment in ToggleMenu. We SetActive(false) the whole GameObject rather than toggling Canvas.enabled:
        // a disabled Canvas still leaves its graphics registered for raycasting in uGUI, so only deactivating the GO
        // reliably removes RESTART from BOTH render and hit-testing. The button is found by its GameObject name
        // ("[PlayScreen] RestartButton", set in RestartButtonPresenter.CreateButton) — a fragile, string-coupled seam,
        // flagged here on purpose; DI injection was ruled out because exposing the canvas would mean editing the
        // product presenter, which TASK-117 forbids. If the button was never built (menu opened outside the
        // play-screen — e.g. a bringup scene with no RESTART presenter) GameObject.Find returns null and we silently
        // no-op: the dev menu must open in any scene.
        private void HideRestartButtonWhileMenuOpen()
        {
            _restartButton = GameObject.Find("[PlayScreen] RestartButton");
            if (_restartButton == null)
                return;

            _restartButtonWasActive = _restartButton.activeSelf;
            _restartButton.SetActive(false);
        }

        // Restore the RESTART canvas to the EXACT state it had before we hid it — not an unconditional enable, so a
        // button that was already inactive for another reason (a future IsBusy/level-transition path) stays inactive
        // (owner requirement: restore, don't force). Runs from the menu's close callback, which fires on every close
        // path — X tap and DEV-toggle re-tap both destroy the menu GO, and OnDestroy invokes the callback, including
        // teardown via Dispose. Cleared to null so a later open re-reads the live state.
        private void RestoreRestartButton()
        {
            if (_restartButton == null)
                return;

            _restartButton.SetActive(_restartButtonWasActive);
            _restartButton = null;
        }

        // bringup's ShapeFillBringup scene ships no EventSystem (gameplay taps use the raw new-Input-System
        // PointerTapInputSource, not uGUI), so DDM's uGUI buttons would receive no clicks. Raise one on demand
        // with the module for the project's active input handling (Input System package → InputSystemUIInputModule;
        // ProjectSettings activeInputHandler:1). DontDestroyOnLoad so a level reload does not drop it.
        private void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            _eventSystem = new GameObject("[Dev] EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(_eventSystem);
        }

        // The visible, keyboard-free entry point (TASK-062). The template shipped a draggable prefab button
        // (com.say.services.common DebugMenuButtonUi) instantiated by DebugService from UiPrefabs.asset, but that
        // path never runs in ShapeFillBringup (it skips LoadingGame/DebugService) and the prefab asset itself was
        // cut from the repo. Rebuild the affordance in code — no prefab asset, so nothing for the Unity bridge's
        // missing prefab-save op to author. A small corner button on its own overlay canvas keeps the play field
        // clear; gameplay taps ride the raw PointerTapInputSource (not uGUI), so anything under the button is still
        // reachable even without moving it.
        private void CreateToggleButton()
        {
            _buttonCanvas = new GameObject("[Dev] DebugMenuButton",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_buttonCanvas);

            var canvas = _buttonCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // above any gameplay/UI so it is never occluded.

            var scaler = _buttonCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            var buttonGo = new GameObject("Button", typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(_buttonCanvas.transform, false);

            var image = buttonGo.GetComponent<Image>();
            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f); // top-left corner, away from the play field's centre.
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(28f, -28f);

            buttonGo.GetComponent<Button>().onClick.AddListener(ToggleMenu);

            // Деревянная кнопка от владельца: надпись «DEV ≡» в спрайте запечена, отдельной подписи нет.
            var sprite = Gameplay.View.WoodArt.ButtonDev;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.color = Color.white;
                // Высота ОБЩАЯ для DEV и RESTART и лежит в настройках: владелец просил
                // уменьшить их в полтора раза (116 → 77) и держать одинаковыми.
                var height = Gameplay.View.WoodTuning.Current.UiButtonHeight;
                rect.sizeDelta = new Vector2(height * sprite.rect.width / sprite.rect.height, height);
                return;
            }

            image.color = new Color(0.12f, 0.12f, 0.12f, 0.7f);
            rect.sizeDelta = new Vector2(170f, 96f);

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(buttonGo.transform, false);

            var label = labelGo.GetComponent<Text>();
            label.text = "DEV ≡";
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 40;
            // Built-in font of Unity 6 (renamed from Arial.ttf pre-2022). If unavailable the glyphs just do not
            // draw — the button image and its click still work, so this is a cosmetic best-effort, not a gate.
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        // Content generator handed the live DDMContext by DDMMain — mirrors the retired overlay's UX.
        private void BuildContent(DDMContext context)
        {
            // Live i/N + current level id; DDMLabel.CreateDynamic re-reads this every frame in its own Update.
            DDMLabel.CreateDynamic(context,
                () => $"Level {_campaign.DevCampaignIndex + 1}/{_levelSource.Count}: {CurrentLevelId}");

            DDMButton.Create(context, "◀ Prev", () => Jump(_campaign.DevCampaignIndex - 1));
            DDMButton.Create(context, "Next ▶", () => Jump(_campaign.DevCampaignIndex + 1));

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
                // M-2: durable-reset the campaign pointer to the first level. WriteLevelIndex(0) is now durable even
                // when the stored value already equals the default (PrefsCampaignStore.ForceSave, TASK-109 §3.2).
                _store.WriteLevelIndex(0);

                // M-1: reset FTUE via the existing dev seam — "forget the tutorial so it replays"
                // (ITutorialProgressStore.cs:17-18), durable. tutorialId is the one canon tour id
                // (TutorialDefinition.Id = "first_tutorial", GameplayCoreInstaller.cs:105 — the same id
                // TutorialRunner.ShouldPlay reads). Resets the bringup Tutorial.Completed contour.
                _tutorialProgress.Reset(_tutorial.Id);

                // Reload level 0 in-place → GameModel.LevelLoaded → TutorialRunner.OnLevelLoaded → ShouldPlay
                // (now IsCompleted==false, ReadLevelIndex()==0) → FTUE replays. No relaunch needed.
                _campaign.DevJumpToLevel(0);
            });
        }

        // DevJumpToLevel clamps out-of-range to the level bounds; an edge prev/next simply reloads the current
        // level (a harmless dev restart). The dynamic label picks up the new index on its next Update.
        private void Jump(int index) => _campaign.DevJumpToLevel(index);

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
