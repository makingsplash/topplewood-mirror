#if GPIE_DEVELOPMENT
using System;
using Gameplay.Adapters;
using Gameplay.PlayScreen;
using Services;
using SGS.Services.DebugLog;
using VContainer.Unity;

namespace Gameplay.Dev
{
    /// <summary>
    /// Delivers the prod <c>GameDebugMenu</c> "Reset Progress" / "Go to Level" restart request to the in-scope
    /// campaign owner (TASK-113). The prod menu is a static app-layer host (<c>LoadingGame</c> wires
    /// <c>GlobalService.Debug.OnDebugMenuShown = GameDebugMenu.Create</c>); its restart path only flips
    /// <c>DDMMain._isNeedRestart</c>, which surfaces as <c>DebugService.OnDebugMenuClose(bool isRestart)</c> — a
    /// multicast that, until this bridge, had ZERO subscribers, so every reset/jump wrote
    /// <c>Data.Meta.CurrentLevel</c> to disk but never reloaded the level (the game kept spinning the stale
    /// in-memory index; TASK-157 made it worse — the first win would re-persist the stale pointer).
    ///
    /// The subscriber must live where the index owner (<see cref="CampaignProgressionController"/>) and
    /// <see cref="PlayScreenInteraction"/> live — the gameplay child scope — while <c>OnDebugMenuClose</c> is an
    /// app-lifetime <c>DebugService</c> seam, so this entry point bridges the two: subscribe in <see cref="Start"/>,
    /// unsubscribe in <see cref="Dispose"/>. <c>DebugService</c> outlives this scope, so the <c>-=</c> is a hard
    /// requirement (a leaked delegate would fire into a dead controller and NRE on the next close); re-entering
    /// gameplay raises a fresh bridge that subscribes its own, and the old one is already gone — never a
    /// double-fire, never a leak.
    ///
    /// "Restart" here is NOT an Application relaunch: it is resync-from-store + reset-in-place reload
    /// (<see cref="CampaignProgressionController.ResyncFromStore"/>), the same thing bringup's
    /// <c>BringupDebugMenuHost</c> does after writing the store. Registered ONLY by the prod
    /// <c>GameplayLifetimeScope</c>, never the shared <c>GameplayCoreInstaller</c> (bringup gates its own menu
    /// through a local callback, not <c>GlobalService.Debug</c>, and would not have <see cref="PlayScreenInteraction"/>).
    /// Editor/dev builds only — the whole file is guarded and never compiles into player builds (and
    /// <c>GlobalService.Debug</c> itself only exists under <c>GPIE_DEVELOPMENT</c>).
    /// </summary>
    public sealed class GameDebugMenuRestartBridge : IStartable, IDisposable
    {
        private readonly CampaignProgressionController _campaign;
        private readonly PlayScreenInteraction _interaction;

        public GameDebugMenuRestartBridge(
            CampaignProgressionController campaign, PlayScreenInteraction interaction)
        {
            _campaign = campaign;
            _interaction = interaction;
        }

        // += , NOT = : OnDebugMenuClose is a multicast Action. Prod assigns the sibling OnDebugMenuShown with =,
        // but the close seam must accumulate — clobbering it would silently drop any future subscriber. No other
        // subscriber exists today, so += is safe now and correct later; Dispose's -= restores the balance.
        public void Start()
        {
            GlobalService.Debug.OnDebugMenuClose += HandleClose;
            // Second seam (TASK-174): the ◀/▶ arrows page levels WITHOUT closing the menu, so they cannot ride the
            // close event above — they emit through the project-owned GameDebugMenuLevelStep signal instead.
            GameDebugMenuLevelStep.Requested += HandleStep;
        }

        public void Dispose()
        {
            GlobalService.Debug.OnDebugMenuClose -= HandleClose;
            // -= is mandatory: the static event outlives this scope; a leaked delegate would fire into a dead
            // controller (same contract as OnDebugMenuClose above).
            GameDebugMenuLevelStep.Requested -= HandleStep;
        }

        private void HandleClose(bool isRestart)
        {
            // Menu closed without Reset/GoTo (isRestart == false): the resource edits (Gold/Hearts) write their
            // own live prefs immediately and need no level reload, so do nothing.
            if (!isRestart)
                return;

            // Skip while the win/lose panel is pending/up — the SAME IsBusy contract that disables the RESTART
            // button (RestartButtonPresenter) so a manual reload cannot race the pending tap transition
            // (GatedResolutionFlow drives IsBusy true until the transition actually runs). We touch neither the
            // index nor the reload while busy, so the applyTransition captured in the panel callback stays valid
            // and fires normally on the tap — nothing is orphaned, IsBusy never sticks. Log the skip so the no-op
            // is not silent (dev-UX: close the panel with its own tap, then Reset again).
            if (_interaction.IsBusy.Value)
            {
                GlobalService.DebugLog.Log(
                    "[TASK-113] debug reset skipped: resolution panel open", DebugLogType.ApplicationFlow);
                return;
            }

            _campaign.ResyncFromStore();
        }

        // ◀/▶ level step (TASK-174). Same IsBusy gate as HandleClose: while the win/lose panel is pending/up we
        // touch neither the index nor the reload, so the panel's captured applyTransition stays valid and fires on
        // the tap. Log the skip so the no-op is not silent (dev-UX: close the panel first, then step). delta is
        // clamped to the line-up bounds in DevStepPersisted (edge = no-op, no wrap).
        private void HandleStep(int delta)
        {
            if (_interaction.IsBusy.Value)
            {
                GlobalService.DebugLog.Log(
                    "[TASK-174] level step skipped: resolution panel open", DebugLogType.ApplicationFlow);
                return;
            }

            _campaign.DevStepPersisted(delta);
        }
    }
}
#endif
