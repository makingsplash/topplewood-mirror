using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace MiroTools
{
    public static partial class ApiClient
    {
        public enum TextAlign
        {
            Left,
            Center,
            Right,
            Justify
        }

        [Serializable]
        public class TextStyle
        {
            [JsonProperty("color")] public string Color;
            [JsonProperty("textAlign")] public string TextAlign;
        }

        [Serializable]
        public class TextData
        {
            [JsonProperty("content")] public string Content;
        }

        [Serializable]
        public class CreateTextRequest 
        { 
            [JsonProperty("data")] public TextData Data; 
            [JsonProperty("style")] public TextStyle Style;
            [JsonProperty("position")] public PositionData Position; 
            [JsonProperty("geometry")] public GeometryData Geometry;
            [JsonProperty("parent")] public ParentData Parent;

            [Serializable]
            public class ParentData { [JsonProperty("id")] public string Id; }
        }
        
        
        public static UniTask<bool> CreateTextItem(
            string token,
            string boardId,
            string content,
            float x = 0,
            float y = 0,
            float width = 300,
            string parentId = null,
            Color? color = null,
            float? opacity = null,
            TextAlign? textAlign = null)
        {
            string textAlignValue = textAlign.HasValue ? ToTextAlignValue(textAlign.Value) : null;
            return CreateTextItem(token, boardId, content, x, y, width, parentId, color, opacity, textAlignValue);
        }

        private static async UniTask<bool> CreateTextItem(
            string token,
            string boardId,
            string content,
            float x = 0,
            float y = 0,
            float width = 300,
            string parentId = null,
            Color? color = null,
            float? opacity = null,
            string textAlign = null)
        {
            string colorValue = BuildTextColor(color, opacity);
            string textAlignValue = NormalizeTextAlign(textAlign);
            TextStyle style = null;
            if (!string.IsNullOrEmpty(colorValue) || !string.IsNullOrEmpty(textAlignValue))
            {
                style = new TextStyle { Color = colorValue, TextAlign = textAlignValue };
            }

            var requestData = new CreateTextRequest 
            { 
                Data = new TextData { Content = content }, 
                Style = style,
                Position = new PositionData { X = x, Y = y },
                Geometry = new GeometryData { Width = width },
                Parent = !string.IsNullOrEmpty(parentId) ? new CreateTextRequest.ParentData { Id = parentId } : null
            };
            string json = JsonConvert.SerializeObject(requestData, JsonSettings);
            var (success, _) = await SendRequest(token, $"{BaseUrl}/boards/{boardId}/texts", "POST", json);
            return success;
        }

        private static string BuildTextColor(Color? color, float? opacity)
        {
            if (!color.HasValue && !opacity.HasValue)
            {
                return null;
            }

            Color resolved = color ?? Color.black;
            float alpha = resolved.a;
            if (opacity.HasValue)
            {
                alpha = Mathf.Clamp01(opacity.Value);
            }

            if (alpha < 1f - Mathf.Epsilon)
            {
                int r = Mathf.Clamp(Mathf.RoundToInt(resolved.r * 255f), 0, 255);
                int g = Mathf.Clamp(Mathf.RoundToInt(resolved.g * 255f), 0, 255);
                int b = Mathf.Clamp(Mathf.RoundToInt(resolved.b * 255f), 0, 255);
                return $"rgba({r}, {g}, {b}, {alpha:0.###})";
            }

            return $"#{ColorUtility.ToHtmlStringRGB(resolved)}";
        }

        private static string ToTextAlignValue(TextAlign textAlign)
        {
            switch (textAlign)
            {
                case TextAlign.Left:
                    return "left";
                case TextAlign.Center:
                    return "center";
                case TextAlign.Right:
                    return "right";
                case TextAlign.Justify:
                    return "justify";
                default:
                    throw new ArgumentOutOfRangeException(nameof(textAlign), textAlign, "Unsupported textAlign value.");
            }
        }

        private static string NormalizeTextAlign(string textAlign)
        {
            if (string.IsNullOrWhiteSpace(textAlign))
            {
                return null;
            }

            string normalized = textAlign.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "left":
                case "center":
                case "right":
                case "justify":
                    return normalized;
                default:
                    throw new ArgumentException("textAlign must be one of: left, center, right, justify.", nameof(textAlign));
            }
        }
    }
}
