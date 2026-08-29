using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SIPSorcery.Gemini.Realtime.Models;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

[Trait("Category", "unit")]
public class JsonStringEnumMemberConverterUnitTests
{
    private readonly ILogger logger;

    public JsonStringEnumMemberConverterUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    public enum SampleEnum
    {
        [EnumMember(Value = "wire-value-one")]
        MemberOne,

        PlainMember
    }

    private class Holder
    {
        [JsonPropertyName("required")]
        public SampleEnum Required { get; set; }

        [JsonPropertyName("optional")]
        public SampleEnum? Optional { get; set; }
    }

    private static Holder? Deserialise(string json) => JsonSerializer.Deserialize<Holder>(json, JsonOptions.Default);

    [Theory]
    [InlineData(@"{ ""required"": ""wire-value-one"" }", SampleEnum.MemberOne)]
    [InlineData(@"{ ""required"": ""WIRE-VALUE-ONE"" }", SampleEnum.MemberOne)]
    [InlineData(@"{ ""required"": ""PlainMember"" }", SampleEnum.PlainMember)]
    [InlineData(@"{ ""required"": ""plainmember"" }", SampleEnum.PlainMember)]
    [InlineData(@"{ ""required"": 1 }", SampleEnum.PlainMember)]
    public void Reads_Member_Values_Names_And_Numbers(string json, SampleEnum expected)
    {
        logger.LogDebug("--> {MethodName} {Json}", System.Reflection.MethodBase.GetCurrentMethod()?.Name, json);

        Assert.Equal(expected, Deserialise(json)!.Required);
    }

    [Fact]
    public void Reads_The_Same_Values_Into_An_Optional_Property()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        Assert.Equal(SampleEnum.MemberOne, Deserialise(@"{ ""optional"": ""wire-value-one"" }")!.Optional);
        Assert.Equal(SampleEnum.PlainMember, Deserialise(@"{ ""optional"": ""PlainMember"" }")!.Optional);
        Assert.Equal(SampleEnum.PlainMember, Deserialise(@"{ ""optional"": 1 }")!.Optional);
    }

    [Fact]
    public void An_Unknown_Value_Throws_For_A_Required_Property()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        Assert.Throws<JsonException>(() => Deserialise(@"{ ""required"": ""something-new"" }"));
    }

    [Fact]
    public void An_Unknown_Or_Absent_Value_Is_Null_For_An_Optional_Property()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        // Optional enums stay lenient on purpose: a value Google adds later shouldn't cost the
        // whole message.
        Assert.Null(Deserialise(@"{ ""optional"": ""something-new"" }")!.Optional);
        Assert.Null(Deserialise(@"{ ""optional"": """" }")!.Optional);
        Assert.Null(Deserialise(@"{ ""optional"": null }")!.Optional);
        Assert.Null(Deserialise(@"{ }")!.Optional);
    }

    [Fact]
    public void Writes_The_Member_Value_When_One_Is_Declared_And_The_Name_Otherwise()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var json = JsonSerializer.Serialize(
            new Holder { Required = SampleEnum.MemberOne, Optional = SampleEnum.PlainMember },
            JsonOptions.Default);

        logger.LogDebug(json);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("wire-value-one", doc.RootElement.GetProperty("required").GetString());
        Assert.Equal("PlainMember", doc.RootElement.GetProperty("optional").GetString());
    }

    [Fact]
    public void A_Null_Optional_Property_Is_Omitted()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var json = JsonSerializer.Serialize(new Holder { Required = SampleEnum.PlainMember }, JsonOptions.Default);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("optional", out _));
    }

    [Fact]
    public void ToEnumString_Returns_The_Member_Value_Or_The_Name()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        Assert.Equal("wire-value-one", SampleEnum.MemberOne.ToEnumString());
        Assert.Equal("PlainMember", SampleEnum.PlainMember.ToEnumString());
        Assert.Equal(
            "models/gemini-2.5-flash-native-audio-latest",
            GeminiLiveModelsEnum.Gemini25FlashNativeAudioLatest.ToEnumString());
    }
}
