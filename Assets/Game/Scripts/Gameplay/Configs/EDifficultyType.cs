using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Gameplay.Configs
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EDifficultyType
    {
        Normal = 0,
        Hard = 1,
        SuperHard = 2
    }

    public static class EDifficultyTypeExtensions
    {
        public static UnityEngine.Color GetColor(this EDifficultyType difficulty)
        {
            return difficulty switch
            {
                EDifficultyType.Normal => UnityEngine.Color.white,
                EDifficultyType.Hard => UnityEngine.Color.yellow,
                EDifficultyType.SuperHard => UnityEngine.Color.red,
                _ => UnityEngine.Color.white
            };
        }
    }
}
