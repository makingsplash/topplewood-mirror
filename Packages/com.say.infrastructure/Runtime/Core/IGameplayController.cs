using System;

namespace SGS.Infrastructure.Core
{
    public enum LoseReason
    {
        Debug = 0,
        Restart = 1,
        Revive = 2,
        Fail = 3,
        BackToHome = 4,
        NoMoves = 5
    }
    
    public interface IGameplayController
    {
        event Action Win;
        event Action Revive;
        event Action DebugRestart;
        event Action<LoseReason> Lose;

        void Start();
    }
}