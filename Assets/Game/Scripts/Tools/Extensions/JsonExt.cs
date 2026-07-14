using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Game.Services.LogService;

namespace Game
{
    public static class JsonExt
    {

        public static T DeserializeJson<T>(string json, params JsonConverter[] jsonConverters)
        {
            try
            {
                var result = JsonConvert.DeserializeObject<T>(json, jsonConverters);
                return result;
            }
            catch (JsonSerializationException ex)
            {
                var errorString = $"Can't deserialize Json file from config: {ex}";
                Debug.unityLogger.Log(LogType.Error, errorString);
                
                return default;
            }
        }
        
        
        public static string SerializeObject(object obj)
        {
            try
            {
                var result = JsonConvert.SerializeObject(obj);
                return result;
            }
            catch (JsonSerializationException ex)
            {
                Debug.unityLogger.Log(LogType.Error, $"Can't serialize object: {ex}");
                return default;
            }
        }
        
        
        public static bool IsValidJson(string strInput)
        {
            if (string.IsNullOrWhiteSpace(strInput)) { return false;}
            strInput = strInput.Trim();
            
            if ((strInput.StartsWith("{") && strInput.EndsWith("}")) || //For object
                (strInput.StartsWith("[") && strInput.EndsWith("]"))) //For array
            {
                try
                {
                    JToken.Parse(strInput);
                    return true;
                }
                catch (JsonReaderException jex)
                {
                    //Exception in parsing json
                    Debug.unityLogger.Log(LogType.Error, $"Json parsing error: {jex.Message}");
                    return false;
                }
                catch (Exception ex) //some other exception
                {
                    Debug.unityLogger.Log(LogType.Error, $"Invalid json: {ex}");
                    return false;
                }
            }
            
            return false;
        }
    }
}
