using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace MiroTools
{
    public static class ErrorHandler
    {
        public class ErrorResponse
        {
            [JsonProperty("message")] public string Message;
            [JsonProperty("code")] public string Code;
            [JsonProperty("context")] public ErrorContext Context;
        }

        public class ErrorContext { [JsonProperty("fields")] public List<ErrorField> Fields; }
        public class ErrorField { [JsonProperty("field")] public string Field; [JsonProperty("message")] public string Message; }

        public static string GetFriendlyError(UnityWebRequest request)
        {
            if (request.downloadHandler == null || string.IsNullOrEmpty(request.downloadHandler.text))
                return $"Network Error: {request.error}";

            try
            {
                var errorData = JsonConvert.DeserializeObject<ErrorResponse>(request.downloadHandler.text);
                if (errorData == null) return request.error;

                return errorData.Code switch
                {
                    "2.0703" => ParseValidationFields(errorData),
                    "401" => "Invalid API Token",
                    "403" => "Access Denied (Check Token Scopes)",
                    "404" => "Resource Not Found (Check IDs or Endpoints)",
                    "429" => "Too many requests",
                    _ => $"{errorData.Message} (Code: {errorData.Code})"
                };
            }
            catch
            {
                return $"Status {request.responseCode}: {request.error}";
            }
        }

        private static string ParseValidationFields(ErrorResponse error)
        {
            if (error.Context?.Fields == null) return error.Message;
            var details = error.Context.Fields.Select(f => $"{f.Field}: {f.Message}");
            return $"Validation Error: {string.Join(", ", details)}";
        }
    }
}
