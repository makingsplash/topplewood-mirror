using System;

using Newtonsoft.Json;
using SGS.Extensions;

namespace SGS.Infrastructure.Tools.Common
{
    public class DateTimeValueConverter : JsonConverter<DateTimeValue>
    {
        public override void WriteJson(JsonWriter writer, DateTimeValue value, JsonSerializer serializer)
        {
            writer.WriteValue(((DateTime)value).ToConfigString());
        }

        public override DateTimeValue ReadJson(JsonReader reader, Type objectType, DateTimeValue existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var value = (string)reader.Value;
            return new DateTimeValue(TimeSpanExtensions.FromDateTimeString(value));
        }
    }
}