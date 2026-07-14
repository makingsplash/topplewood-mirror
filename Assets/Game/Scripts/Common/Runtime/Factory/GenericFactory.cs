using System;
using VContainer;

namespace Common
{
    public interface IFactory
    {
        TInstance Create<TInstance>(Lifetime lifetime = Lifetime.Scoped);
        TInstance Create<TInstance>(Lifetime lifetime = Lifetime.Scoped, params object[] args);
    }
    
    public class GenericFactory : IFactory
    {
        private readonly IObjectResolver _resolver;

        public GenericFactory(IObjectResolver resolver) => 
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        
        public TInstance Create<TInstance>(Lifetime lifetime = Lifetime.Scoped) => 
            _resolver.Instantiate<TInstance>(lifetime);

        public TInstance Create<TInstance>(Lifetime lifetime = Lifetime.Scoped, params object[] args) => 
            _resolver.Instantiate<TInstance>(lifetime, args);
    }
}
