using System;

using Newtonsoft.Json;
using SGS.Extensions;

namespace SGS.Infrastructure.Tools.Configs
{
    public class TimespanConverter : JsonConverter<TimeSpan>
    {
        public override void WriteJson(JsonWriter writer, TimeSpan value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToConfigString());
        }

        public override TimeSpan ReadJson(JsonReader reader, Type objectType, TimeSpan existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;
            return TimeSpanExtensions.FromConfigString(value);
        }
    }

}