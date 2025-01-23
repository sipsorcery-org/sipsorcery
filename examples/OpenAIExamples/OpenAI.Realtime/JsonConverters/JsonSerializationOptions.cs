
using System.Text.Json.Serialization;
using System.Text.Json;

namespace demo;

public class JsonOptions
{
    public static readonly JsonSerializerOptions Default;

    static JsonOptions()
    {
        Default = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            WriteIndented = true,
            Converters =
                {
                    // Allow enum values or member attribute values,e.g. [EnumMember(Value = "xxx")] to be deserialised from strings.
                    new JsonStringEnumMemberConverter(),

                    // Allows "true" and "false" strings to be deserialised to booleans.
                    new BooleanAsStringConverter(),

                    // Newtonsoft allows numeric values to be deserialised from strings.
                    // This is not the default behaviour in System.Text.Json so use a custom converter.
                    //new NumericConverter<int>(),
                    //new NumericConverter<long>(),
                    //new NumericConverter<float>(),
                    //new NumericConverter<double>(),
                    //new NumericConverter<decimal>()
                },
            PropertyNamingPolicy = null // PacalCase by default.
        };
    }
}
