using System;

using Newtonsoft.Json;
using SGS.Extensions;

namespace SGS.Infrastructure.Tools.Common
{
    public class TimeOfDayValueConverter : JsonConverter<TimeOfDayValue>
    {
        public override void WriteJson(JsonWriter writer, TimeOfDayValue value, JsonSerializer serializer)
        {
            writer.WriteValue(((TimeSpan)value).ToTimeOfDayString());
        }

        public override TimeOfDayValue ReadJson(JsonReader reader, Type objectType, TimeOfDayValue existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;
            return new TimeOfDayValue(TimeSpanExtensions.FromTimeOfDayString(value));
        }
    }
}