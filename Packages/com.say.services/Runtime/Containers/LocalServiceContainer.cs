
namespace SGS.Services.Containers
{
    public class LocalServiceContainer : ServiceContainer<LocalServiceContainer>
    {
        public static LocalServiceContainer Create()
        {
            if (_instance != null)
            {
                _instance.DeInit();
            }
            
            var localService = new LocalServiceContainer();
            _instance = localService;
            
            return localService;
        }
        
        public static void Release()
        {
            if (_instance != null)
            {
                _instance.DeInit();
                _instance = null;
            }
        }
    }
}