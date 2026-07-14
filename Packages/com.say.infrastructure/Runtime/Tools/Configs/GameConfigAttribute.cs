using System;
using System.Reflection;

namespace SGS.Infrastructure.Tools.Configs
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class GameConfigAttribute : Attribute
    {
        public string ConfigName;
        public string ConfigDir = "Configs";
        public string Group = string.Empty;

        public string Path => ConfigDir + "/" + ConfigName + ".asset";
        public string AssetDbPath => System.IO.Path.Join("Assets" ,ConfigDir, ConfigName + ".asset");
        
        public static string GetAssetPathFromAttribute<T>()
        {
            var configAttribute = typeof(T).GetCustomAttribute<GameConfigAttribute>();
            return configAttribute == null ? string.Empty : configAttribute.AssetDbPath;
        }
        public static string GetPathFromAttribute<T>()
        {
            var configAttribute = typeof(T).GetCustomAttribute<GameConfigAttribute>();
            return configAttribute == null ? string.Empty : configAttribute.Path;
        }

    }
}