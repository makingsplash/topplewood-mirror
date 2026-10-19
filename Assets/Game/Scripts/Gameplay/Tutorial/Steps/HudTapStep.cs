using Game.Services.LogService;
using R3;
using UnityEngine;

namespace Gameplay.Tutorial
{
    /// <summary>
    /// "Tap this HUD element" beat (TASK-635 plan §3.4) — the class, not the Hint button: the target comes from the
    /// hosting scope's <see cref="IHudTourTargets"/> by id, so a future booster button or the settings gear is one
    /// adapter plus one recipe line, no change here.
    /// <list type="bullet">
    /// <item>The field admits NOTHING for the whole beat (<c>AllowOnly(_ =&gt; false)</c>, not <c>Block()</c>: a block raises
    /// no <see cref="ITutorialInputGate.Rejected"/>, so a tap on the field would not answer with the miss pulse).</item>
    /// <item>Waits for <see cref="IHudTourTarget.Ready"/> (the Hint button builds and unlocks on its own schedule), then
    /// holds the target's focus exemption (full alpha, tappable under the tour's HUD dim) and shows the HUD beat.</item>
    /// <item>Completes on the target's ACCEPTED activation (<see cref="IHudTourTarget.Activated"/>).</item>
    /// <item>Target not hosted (bringup) → a warning and an immediate completion. Ready but
    /// <see cref="IHudTourTarget.CanActivate"/> false → a loud error and an immediate completion: with the field shut and
    /// RESTART dimmed by the tour's focus, waiting there would be a softlock. CanActivate is the element's lasting
    /// state (charges, eligibility), never the transient busy of a level transition.</item>
    /// </list>
    /// Everything armed lives in a <see cref="SerialDisposable"/> returned from <see cref="Observable.Create{T}"/>, so a
    /// mid-tour StopSequence (dispose without Exit) drops the gate and the focus hold — the <see cref="TapPieceStep"/> idiom.
    /// </summary>
    public sealed class HudTapStep : ITutorialStep
    {
        private readonly string _targetId;

        public HudTapStep(string targetId) => _targetId = targetId;

        public string TargetId => _targetId;

        public string Id => "tap_button";

        public Observable<Unit> Enter(TutorialContext context)
        {
            return Observable.Create<Unit>(observer =>
            {
                void Complete()
                {
                    observer.OnNext(Unit.Default);
                    observer.OnCompleted();
                }

                if (!context.HudTargets.TryGet(_targetId, out var target))
                {
                    Debug.unityLogger.Log(LogType.Warning,
                        $"[{LogCategory.Tutorial}] HUD target '{_targetId}' not hosted — step skipped");
                    Complete();
                    return Disposable.Empty;
                }

                var gate = context.InputGate.AllowOnly(_ => false);
                var miss = context.InputGate.Rejected.Subscribe(_ => context.View.PulseMiss());
                var current = new SerialDisposable();

                void Arm()
                {
                    if (!target.CanActivate.CurrentValue)
                    {
                        Debug.unityLogger.Log(LogType.Error,
                            $"[{LogCategory.Tutorial}] HUD target '{_targetId}' cannot be activated — step skipped (softlock guard)");
                        Complete();
                        return;
                    }
                    var focus = target.HoldFocus();
                    context.View.ShowHudTap(new HudTapCue(target));
                    var activated = target.Activated.Take(1).Subscribe(_ => Complete());
                    current.Disposable = Disposable.Combine(activated, focus);
                }

                if (target.Ready.CurrentValue)
                {
                    Arm();
                }
                else
                {
                    // CurrentValue is false, so the replayed value is filtered out — Arm never runs inside Subscribe.
                    current.Disposable = target.Ready.Where(r => r).Take(1).Subscribe(_ => Arm());
                }
                return Disposable.Combine(current, miss, gate);
            });
        }

        public void Exit(TutorialContext context) => context.View.HideTap();
    }
}
