#if !SAY_INFRASTURCTURE

using System;
using Newtonsoft.Json;

namespace Tools.Purchases.Data.Common
{
    public class TimespanValueConverter : JsonConverter<TimeSpanValue>
    {
        public override void WriteJson(JsonWriter writer, TimeSpanValue value, JsonSerializer serializer)
        {
            writer.WriteValue(Extensions.TimeSpanExtensions.ToConfigString(((TimeSpan)value)));
        }

        public override TimeSpanValue ReadJson(JsonReader reader, Type objectType, TimeSpanValue existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;
            return new TimeSpanValue(TimeSpanExtensions.FromConfigString(value));
        }
    }
}
#endif