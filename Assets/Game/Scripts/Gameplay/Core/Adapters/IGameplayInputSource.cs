using R3;

namespace Gameplay.Core
{
    /// <summary>
    /// Domain-facing input intents (overview §Адаптерная граница). Unity impl bridges IInputService;
    /// headless impl pushes a synthetic Subject&lt;InputIntent&gt;. Domain references R3 core only.
    /// </summary>
    public interface IGameplayInputSource
    {
        Observable<InputIntent> Intents { get; }
    }
}
