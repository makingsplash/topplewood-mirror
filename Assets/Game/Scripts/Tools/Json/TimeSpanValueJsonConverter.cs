using System;
using Newtonsoft.Json;
using SGS.Extensions;
using SGS.Infrastructure.Tools.Common;
using SGS.Infrastructure.Tools.Extensions;

namespace Tools.Json
{
    public class TimeSpanValueJsonConverter : JsonConverter<TimeSpanValue>
    {
        public override void WriteJson(JsonWriter writer, TimeSpanValue value, JsonSerializer serializer)
        {
            TimeSpan timeSpan = value;
            writer.WriteValue(timeSpan.ToConfigString());
        }

        public override TimeSpanValue ReadJson(JsonReader reader, Type objectType, TimeSpanValue existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            string value = (string)reader.Value;
            return new TimeSpanValue(TimeSpanExtensions.FromConfigString(value));
        }
    }
}
