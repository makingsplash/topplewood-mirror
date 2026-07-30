#if GPIE_DEVELOPMENT
using System;
using DynamicDebugMenu;
using DynamicDebugMenu.Elements;
using Gameplay.Adapters;
using Gameplay.Core;
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
    /// Trigger: the backquote (`) key toggles the menu (bringup is played in the Editor per
    /// docs/architecture/overview.md; a keyboard is always present). The switch semantics mirror the retired
    /// overlay: live <see cref="CampaignProgressionController.DevJumpToLevel"/> (0-based, no meta persist),
    /// prev/next, and a visible i/N + level-id label — bringup has no SayData.Meta, so persisting a jump is
    /// physically unavailable there (design-locked, RFC Q2).
    /// </summary>
    public sealed class BringupDebugMenuHost : IStartable, ITickable, IDisposable
    {
        private readonly ILevelSource _levelSource;
        private readonly GameModel _model;
        private readonly CampaignProgressionController _campaign;
        private readonly ICampaignStore _store;

        private DebugItemsConfig _config;
        private DDMMain _menu;
        private GameObject _eventSystem;

        public BringupDebugMenuHost(
            ILevelSource levelSource, GameModel model, CampaignProgressionController campaign, ICampaignStore store)
        {
            _levelSource = levelSource;
            _model = model;
            _campaign = campaign;
            _store = store;
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
            // The menu is NOT shown on start — it is toggled by the trigger key so it never occludes gameplay.
            _config = Resources.Load<DebugItemsConfig>(ConfigPath.DebugItems);
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
            _menu = go.GetComponent<DDMMain>();
            _menu.Init(_config, BuildContent, _ => _menu = null);
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

            // Reset progress reachable through the existing ICampaignStore seam (owner condition, TASK-047): persist
            // the 0-based pointer to 0, then jump there live. No new seam method — the domain contract is untouched.
            DDMButton.Create(context, "Reset progress", () =>
            {
                _store.WriteLevelIndex(0);
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
        }
    }
}
#endif
