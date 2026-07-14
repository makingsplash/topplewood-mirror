using Services.AssetPreload;
#if SAYKIT_PURCHASING
using SGS.Store.Purchases.Data;
#endif

namespace DataStorage
{
    public class Data
    {
        private readonly Configs _configs = new Configs();
        private readonly DebugData _debug = new();
        private readonly Settings _settings = new Settings();
        private readonly Meta _meta = new Meta();
        private readonly Scene _scene = new Scene();
        private readonly Messages _messages = new();
        private readonly App _app = new();
#if SAYKIT_PURCHASING
        private readonly Purchases _purchases = new();
#endif

        private static Data _instance;

        public static Configs Configs => _instance._configs;
        public static DebugData Debug => _instance._debug;
        public static Settings Settings => _instance._settings;
        
        public static Meta Meta => _instance._meta;
        public static Scene Scene => _instance._scene;
        public static Messages Messages => _instance._messages;

        public static App App => _instance._app;
#if SAYKIT_PURCHASING
        public static Purchases Purchases => _instance._purchases;
#endif

        private Data()
        {
            
        }

        public static void Create()
        {
            _instance ??= new Data();
        }

        public static bool IsReady()
        {
            return _instance != null;
        }
    }
}
