using System;
using System.Collections.Generic;
using SGS.Services.Interfaces;

namespace SGS.Services.Containers
{
    public interface IServiceContainer
    {
        void DeInit();
        bool IsReady();
        
        TService Register<TService>(IService service) where TService : IService;
        TService Get<TService>() where TService : class, IService;
    }
    
    public abstract class ServiceContainer<T> where T:ServiceContainer<T>
    {
        protected static ServiceContainer<T> _instance;

        private readonly Dictionary<Type, IService> _services = new();
        
        
        public virtual void DeInit()
        {
            foreach (IService service in _services.Values)
            {
                service.DeInit();
            }
            
            _services.Clear();
            _instance = null;
        }

        public static bool IsReady()
        {
            return _instance != null;
        }
        
        public static void Register(IService service)
        {
            Type type = service.GetType();
            _instance._services[type] = service;
            
            service.Init();
        }
        
        public static TService Register<TService>(TService service) where TService: IService
        {
            Type type = typeof(TService);
            _instance._services[type] = service;

            service.Init();
            return service;
        }

        public static TService Get<TService>() where TService : class, IService
        {
            if (_instance == null)
            {
                return null;
            }

            _instance._services.TryGetValue(typeof(TService), out IService service);
            return service as TService;
        }
    }
}