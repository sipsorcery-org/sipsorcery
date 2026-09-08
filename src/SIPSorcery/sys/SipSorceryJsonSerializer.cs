#nullable enable
using System;
using System.Buffers;
using System.Text.Json;

namespace SIPSorcery.Sys;

internal static class SipSorceryJsonSerializer
{
    private static readonly JsonWriterOptions jsonWriterOptions = new()
    {
        Indented = false,
    };

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, SipSorceryJsonSerializerContext.Default.Options);

    public static void Serialize<T>(IBufferWriter<byte> writer, T value)
    {
        using var jsonWriter = new Utf8JsonWriter(writer, jsonWriterOptions);
        JsonSerializer.Serialize<T>(jsonWriter, value, SipSorceryJsonSerializerContext.Default.Options);
    }

    public static T? Deserialize<T>(ReadOnlySpan<char> json)
    {
        if (json.IsEmptyOrWhiteSpace())
        {
            return default;
        }

        // System.Text.Json rejects unescaped CR/LF inside JSON string values.
        // Single pass: only rent a buffer and backfill when the first offending
        // character is encountered inside a JSON string.
        var rented = default(char[]?);
        var pos = 0;
        var inString = false;
        var escaped = false;

        try
        {
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];

                if (escaped)
                {
                    if (rented is not null)
                    {
                        rented[pos++] = c;
                    }

                    escaped = false;
                    continue;
                }

                if (inString)
                {
                    if (c is '\r' or '\n' && rented is null)
                    {
                        // First offending char: rent and backfill everything before this point.
                        rented = ArrayPool<char>.Shared.Rent(json.Length * 2);
                        json[..i].CopyTo(rented);
                        pos = i;
                    }

                    if (rented is not null)
                    {
                        switch (c)
                        {
                            case '\\':
                                rented[pos++] = c;
                                escaped = true;
                                break;
                            case '"':
                                rented[pos++] = c;
                                inString = false;
                                break;
                            case '\r':
                                //rented[pos++] = '\\';
                                //rented[pos++] = 'r';
                                break;
                            case '\n':
                                rented[pos++] = '\\';
                                rented[pos++] = 'n';
                                break;
                            default:
                                rented[pos++] = c;
                                break;
                        }
                    }
                    else
                    {
                        switch (c)
                        {
                            case '\\': escaped = true; break;
                            case '"': inString = false; break;
                        }
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                    }

                    if (rented is not null)
                    {
                        rented[pos++] = c;
                    }
                }
            }

            var toParse = rented is not null
                ? rented.AsSpan(0, pos)
                : json;

            return JsonSerializer.Deserialize<T>(toParse, SipSorceryJsonSerializerContext.Default.Options);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }
}
