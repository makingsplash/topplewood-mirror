using System;
using JetBrains.Annotations;
using SGS.Infrastructure.Sequence;
using VContainer;

namespace Gameplay.Sequence
{
    [UsedImplicitly]
    public sealed class SequenceFactory : ISequenceFactory
    {
        private readonly IObjectResolver _resolver;

        public SequenceFactory(IObjectResolver resolver) => 
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        public TElement CreateElement<TElement>(params object[] args) where TElement : ILevelSequenceElement => 
            _resolver.Instantiate<TElement>(Lifetime.Scoped, args);

        public TGameSequence CreateGameSequence<TGameSequence>(params object[] args) where TGameSequence : GameSequence => 
            _resolver.Instantiate<TGameSequence>(Lifetime.Scoped, args);
    }
}
