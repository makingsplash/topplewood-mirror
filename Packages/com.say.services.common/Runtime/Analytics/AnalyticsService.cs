using SGS.Services.Interfaces;

namespace SGS.Services.Analytics
{
    public class AnalyticsService : IService
    {
        private InfrastructureAnalytics _infrastructure;
        private ResourceAnalytics _resource; 
        private TutorialAnalytics _tutorial;
        
        public InfrastructureAnalytics Infrastructure => _infrastructure;
        public ResourceAnalytics Resource => _resource;
        public TutorialAnalytics Tutorial => _tutorial;
        
        public virtual void Init()
        {
            _infrastructure = new InfrastructureAnalytics();
            _resource = new ResourceAnalytics();
            _tutorial = new TutorialAnalytics();

        }

        public void DeInit()
        {
        }
        
    }
}