using SGS.Infrastructure.Sequence;

namespace Gameplay.Sequence
{
    public interface ISequenceFactory
    {
        TElement CreateElement<TElement>(params object[] args) where TElement : ILevelSequenceElement;
        TGameSequence CreateGameSequence<TGameSequence>(params object[] args) where TGameSequence : GameSequence;
    }
}
