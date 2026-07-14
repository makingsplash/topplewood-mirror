using System;
using System.Collections.Generic;
using VContainer.Unity;

namespace VContainer
{
    public class ResolvedObject<TObject, TScope> : IDisposable 
        where TObject : class 
        where TScope : LifetimeScope
    {
        private static TScope _lifetimeScope;
        private TObject _object;

        public TObject Value
        {
            get
            {
                if (_object != null)
                    return _object;
                
                if (_lifetimeScope == null)
                    _lifetimeScope = (TScope)LifetimeScope.Find<TScope>();
                
                _object = _lifetimeScope?.Container?.Resolve<TObject>();

                return _object;
            }
        }

        public void Dispose()
        {
            _object = null;
        }
    }
}