using System.Text.Json;
using System.Text.Json.Serialization;
namespace CCStats.Core.Library;
public static class RecordTime {
    // Offset-free historical fields are UTC by contract; never infer the importing machine's zone.
    public static DateTime Utc(DateTime time)=>time.Kind==DateTimeKind.Local?time.ToUniversalTime():DateTime.SpecifyKind(time,DateTimeKind.Utc);
    public static JsonSerializerOptions Json {get;}=new(){Converters={new UtcConverter()}};
    private sealed class UtcConverter:JsonConverter<DateTime> {
        public override DateTime Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options) {var parsed=reader.GetDateTime();return parsed.Kind==DateTimeKind.Unspecified?Utc(parsed):reader.GetDateTimeOffset().UtcDateTime;}
        public override void Write(Utf8JsonWriter writer,DateTime value,JsonSerializerOptions options)=>writer.WriteStringValue(Utc(value));
    }
}
