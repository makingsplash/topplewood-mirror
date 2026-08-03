namespace Gameplay.Core
{
    /// <summary>
    /// Optional per-fixed-step diagnostic hook (TASK-070). <see cref="GameController"/> calls
    /// <see cref="OnStep"/> exactly once per consumed fixed step, AFTER simulation.Step + mechanic.Tick —
    /// i.e. when the domain model pose mirror (<c>PieceInstance.Position/Rotation/State</c>) is current for
    /// that step. Prod and the headless sim leave it unset: null ⇒ the call is skipped ⇒ zero cost and zero
    /// effect on the pose sequence or poseHash. A dev build may attach a READ-ONLY logger to capture the
    /// same per-step numbers on editor and device and pin where cross-device physics diverges (ADR-0001
    /// M0.2, the deferred cross-device gap). Contract: an observer MUST NOT mutate model/simulation state —
    /// doing so would perturb the very trajectory it is meant to measure.
    /// </summary>
    public interface IStepObserver
    {
        void OnStep();
    }
}
