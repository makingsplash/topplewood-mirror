
namespace SGS.Services.Containers
{
    public class GlobalServiceContainer : ServiceContainer<GlobalServiceContainer>
    {
        public static GlobalServiceContainer Create()
        {
            if (_instance != null)
            {
                return _instance as GlobalServiceContainer;
            }
            
            var globalService = new GlobalServiceContainer();
            _instance = globalService;
            return globalService;
        }
 
    }
}
