
namespace SGS.Data
{
    public class SayData
    {
        private static SayData _instance;
        
        private readonly SayConfigs _configs = new ();
        private readonly DebugData _debug = new ();
        private readonly SaySettings _settings = new ();
        private readonly SayMeta _meta = new ();
        private readonly SayScene _scene = new ();

        public static SayConfigs Configs => _instance._configs;
        public static DebugData Debug => _instance._debug;
        public static SaySettings Settings => _instance._settings;
        public static SayMeta Meta=> _instance._meta;
        public static SayScene Scene=> _instance._scene;
        
        private SayData()
        {
        }
        
        public static void Create()
        {
            _instance = new SayData();
        }
    }
}