using System;
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Net;
using CommunityToolkit.HighPerformance.Buffers;
using Xunit;

namespace SIPSorcery.Sys.UnitTests;

[Trait("Category", "unit")]
public class CharBufferWriterUnitTest
{
    [Fact]
    public void WriteTextValuesProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var boolean = random.Next(2) == 1;
        var character = (char)random.Next('a', 'z' + 1);
        var text = Guid.NewGuid().ToString("N");
        var repeatCharacter = (char)random.Next('A', 'Z' + 1);
        var repeatCount = random.Next(1, 16);
        var spanText = Guid.NewGuid().ToString("N");

        var returnedWriter = writer
            .Write(boolean)
            .WriteLine()
            .Write(character)
            .WriteLine()
            .Write(text)
            .WriteLine()
            .Write((string)null)
            .WriteLine()
            .Write(repeatCharacter, repeatCount)
            .WriteLine()
            .Write(spanText.AsSpan());

        var expected = string.Join(Environment.NewLine, new[]
        {
            boolean ? "true" : "false",
            character.ToString(),
            text,
            string.Empty,
            new string(repeatCharacter, repeatCount),
            spanText,
        });
        Assert.Same(writer, returnedWriter);
        Assert.Equal(expected, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteSpanReservesWritableOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = Guid.NewGuid().ToString("N");

        value.AsSpan().CopyTo(writer.WriteSpan(value.Length));

        Assert.Equal(value, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteNumericValuesUsesRequestedFormats()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var provider = CultureInfo.InvariantCulture;
        var intValue = random.Next();
        var uintValue = (uint)random.Next();
        var ushortValue = (ushort)random.Next(ushort.MinValue, ushort.MaxValue + 1);
        ushort? nullableUshortValue = (ushort)random.Next(ushort.MinValue, ushort.MaxValue + 1);
        var longValue = ((long)random.Next() << 32) | (uint)random.Next();
        var ulongValue = (ulong)(((long)random.Next() << 32) | (uint)random.Next());
        var floatValue = (float)(random.NextDouble() * random.Next());
        var doubleValue = random.NextDouble() * random.Next();
        var decimalValue = (decimal)random.NextDouble() * random.Next();

        var returnedWriter = writer
            .Write(intValue, "X", provider)
            .WriteLine()
            .Write(uintValue, "X", provider)
            .WriteLine()
            .Write(ushortValue, "X", provider)
            .WriteLine()
            .Write(nullableUshortValue, "X", provider)
            .WriteLine()
            .Write((ushort?)null, "X", provider)
            .WriteLine()
            .Write(longValue, "X", provider)
            .WriteLine()
            .Write(ulongValue, "X", provider)
            .WriteLine()
            .Write(floatValue, "R", provider)
            .WriteLine()
            .Write(doubleValue, "R", provider)
            .WriteLine()
            .Write(decimalValue, "F2", provider);

        var expected = string.Join(Environment.NewLine, new[]
        {
            intValue.ToString("X", provider),
            uintValue.ToString("X", provider),
            ushortValue.ToString("X", provider),
            nullableUshortValue.Value.ToString("X", provider),
            string.Empty,
            longValue.ToString("X", provider),
            ulongValue.ToString("X", provider),
            floatValue.ToString("R", provider),
            doubleValue.ToString("R", provider),
            decimalValue.ToString("F2", provider),
        });

        Assert.Same(writer, returnedWriter);
        Assert.Equal(expected, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteBytesProducesExpectedHexOutput()
    {
        var random = new Random();
        var bytes = new byte[random.Next(2, 32)];
        random.NextBytes(bytes);
        var separator = (char)random.Next('g', 'z' + 1);
        using var writer = new ArrayPoolBufferWriter<char>();

        var returnedWriter = writer
            .Write(bytes, separator)
            .WriteLine()
            .Write(bytes.AsSpan(), lowercase: true);

        var uppercase = string.Join(separator.ToString(), bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
        var lowercase = string.Concat(bytes.Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        Assert.Same(writer, returnedWriter);
        Assert.Equal(uppercase + Environment.NewLine + lowercase, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteBooleanProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = new Random().Next(2) == 1;

        Assert.Same(writer, writer.Write(value));

        Assert.Equal(value ? "true" : "false", writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteCharacterProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomCharacter();

        Assert.Same(writer, writer.Write(value));

        Assert.Equal(value.ToString(), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteStringProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = Guid.NewGuid().ToString("N");

        Assert.Same(writer, writer.Write(value));

        Assert.Equal(value, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteLineProducesEnvironmentNewLine()
    {
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.WriteLine());

        Assert.Equal(Environment.NewLine, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteNullStringProducesNoOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write((string)null));

        Assert.Empty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteRepeatedCharacterProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomCharacter(random);
        var count = random.Next(1, 64);

        Assert.Same(writer, writer.Write(value, count));

        Assert.Equal(new string(value, count), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteCharacterSpanProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = Guid.NewGuid().ToString("N");

        Assert.Same(writer, writer.Write(value.AsSpan()));

        Assert.Equal(value, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteSpanReservesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = Guid.NewGuid().ToString("N");

        value.AsSpan().CopyTo(writer.WriteSpan(value.Length));

        Assert.Equal(value, writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteInt32ProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomInt32();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "X", provider));

        Assert.Equal(value.ToString("X", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteUInt32ProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomUInt32();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "X", provider));

        Assert.Equal(value.ToString("X", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteUInt16ProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = (ushort)random.Next(ushort.MinValue, ushort.MaxValue + 1);
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "X", provider));

        Assert.Equal(value.ToString("X", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteNullableUInt16ValueProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        ushort? value = (ushort)random.Next(ushort.MinValue, ushort.MaxValue + 1);
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "X", provider));

        Assert.Equal(value.Value.ToString("X", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteNullableUInt16NullProducesNoOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write((ushort?)null, "X", CultureInfo.InvariantCulture));

        Assert.Empty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteInt64ProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomInt64();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "X", provider));

        Assert.Equal(value.ToString("X", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteUInt64ProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomUInt64();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "X", provider));

        Assert.Equal(value.ToString("X", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteSingleProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = (float)((random.NextDouble() - 0.5) * random.Next());
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "R", provider));

        Assert.Equal(value.ToString("R", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteDoubleProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = (random.NextDouble() - 0.5) * random.Next();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "R", provider));

        Assert.Equal(value.ToString("R", provider), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteDecimalProducesExpectedOutput()
    {
        var random = new Random();
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = (decimal)(random.NextDouble() - 0.5) * random.Next();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.Write(value, "F2", provider));

        Assert.Equal(value.ToString("F2", provider), writer.WrittenSpan.ToString());
    }

#if NET6_0_OR_GREATER
    [Fact]
    public void WriteSpanFormattableProducesExpectedOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();
        var value = GetRandomInt64();
        var provider = CultureInfo.InvariantCulture;

        Assert.Same(writer, writer.WriteSpanFormattable(value, "N0", provider));

        Assert.Equal(value.ToString("N0", provider), writer.WrittenSpan.ToString());
    }
#endif

    [Fact]
    public void WriteByteArrayProducesExpectedOutput()
    {
        var bytes = GetRandomBytes();
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write(bytes));

        Assert.Equal(GetHexString(bytes, null, false), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteByteArrayWithSeparatorProducesExpectedOutput()
    {
        var bytes = GetRandomBytes();
        using var writer = new ArrayPoolBufferWriter<char>();
        var separator = GetRandomCharacter();

        Assert.Same(writer, writer.Write(bytes, separator));

        Assert.Equal(GetHexString(bytes, separator, false), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteNullByteArrayProducesNoOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write((byte[])null));

        Assert.Empty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteEmptyByteArrayProducesNoOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write(Array.Empty<byte>()));

        Assert.Empty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteByteSpanProducesExpectedOutput()
    {
        var bytes = GetRandomBytes();
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write(bytes.AsSpan()));

        Assert.Equal(GetHexString(bytes, null, false), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteByteSpanWithSeparatorProducesExpectedOutput()
    {
        var bytes = GetRandomBytes();
        using var writer = new ArrayPoolBufferWriter<char>();
        var separator = GetRandomCharacter();

        Assert.Same(writer, writer.Write(bytes.AsSpan(), separator));

        Assert.Equal(GetHexString(bytes, separator, false), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteLowercaseByteSpanProducesExpectedOutput()
    {
        var bytes = GetRandomBytes();
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write(bytes.AsSpan(), lowercase: true));

        Assert.Equal(GetHexString(bytes, null, true), writer.WrittenSpan.ToString());
    }

    [Fact]
    public void WriteEmptyByteSpanProducesNoOutput()
    {
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write(ReadOnlySpan<byte>.Empty));

        Assert.Empty(writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WriteIPAddressProducesExpectedOutput()
    {
        var random = new Random();
        var addressBytes = new byte[4];
        random.NextBytes(addressBytes);
        var address = new IPAddress(addressBytes);
        using var writer = new ArrayPoolBufferWriter<char>();

        Assert.Same(writer, writer.Write(address));

        Assert.Equal(address.ToString(), writer.WrittenSpan.ToString());
    }

    private static byte[] GetRandomBytes(int? length = null)
    {
        var random = new Random();
        var bytes = new byte[length ?? random.Next(1, 64)];
        random.NextBytes(bytes);
        return bytes;
    }

    private static char GetRandomCharacter(Random random = null)
    {
        random ??= new Random();
        return (char)random.Next('!', '~' + 1);
    }

    private static string GetHexString(byte[] bytes, char? separator, bool lowercase)
    {
        var format = lowercase ? "x2" : "X2";
        return string.Join(separator?.ToString() ?? string.Empty, bytes.Select(value => value.ToString(format, CultureInfo.InvariantCulture)));
    }

    private static long GetRandomInt64()
    {
        var bytes = GetRandomBytes(sizeof(long));
        return BitConverter.ToInt64(bytes, 0);
    }

    private static int GetRandomInt32()
    {
        var bytes = GetRandomBytes(sizeof(int));
        return BitConverter.ToInt32(bytes, 0);
    }

    private static uint GetRandomUInt32()
    {
        var bytes = GetRandomBytes(sizeof(uint));
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static ulong GetRandomUInt64()
    {
        var bytes = GetRandomBytes(sizeof(ulong));
        return BitConverter.ToUInt64(bytes, 0);
    }
}
