using System;

using Newtonsoft.Json;
using SGS.Extensions;

namespace SGS.Infrastructure.Tools.Common
{
    public class TimespanValueConverter : JsonConverter<TimeSpanValue>
    {
        public override void WriteJson(JsonWriter writer, TimeSpanValue value, JsonSerializer serializer)
        {
            writer.WriteValue(((TimeSpan)value).ToConfigString());
        }

        public override TimeSpanValue ReadJson(JsonReader reader, Type objectType, TimeSpanValue existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;
            return new TimeSpanValue(TimeSpanExtensions.FromConfigString(value));
        }
    }
}