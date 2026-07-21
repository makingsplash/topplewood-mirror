namespace Gameplay.Core
{
    /// <summary>Session state machine (overview §Стейт-машина). TASK-004 fills Resolving→Won|Lost.</summary>
    public enum GamePhase
    {
        Loading,
        Ready,
        Running,
        Resolving,
        Won,
        Lost,
    }

    /// <summary>Terminal outcome of a level.</summary>
    public enum GameOutcome
    {
        None,
        Won,
        Lost,
    }
}
