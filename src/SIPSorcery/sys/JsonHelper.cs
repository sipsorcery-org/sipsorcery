//-----------------------------------------------------------------------------
// Filename: JsonHelper.cs
//
// Description: Minimal JSON writing and reading helpers for the small number of
// flat objects the library exchanges over WebRTC signalling, specifically
// RTCIceCandidateInit and RTCSessionDescriptionInit.
//
// These replace the previously bundled TinyJson serialiser. A general purpose
// serialiser is not needed: both types are flat collections of strings, a single
// integer and a single enum. Avoiding one keeps the library free of any JSON
// dependency and, because nothing here uses reflection, keeps the signalling
// helpers working under trimming and Native AOT.
//
// The wire format is fixed rather than configurable. It is the shape produced by
// a browser calling JSON.stringify on RTCIceCandidate.toJSON() or
// RTCSessionDescription.toJSON(), which is what these types have to interoperate
// with.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 09 Sep 2026	Aaron Clauson	Created, replaces TinyJson.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using Polyfills;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// The kind of JSON value read for an object member.
    /// </summary>
    internal enum JsonValueKind
    {
        String,
        Number,
        Boolean,
        Null,
        Object,
        Array
    }

    /// <summary>
    /// Writes a flat JSON object. Callers omit members with a null value, matching the
    /// behaviour of the TinyJson serialiser this replaced and of a browser serialising
    /// a dictionary with absent optional members.
    /// </summary>
    internal struct JsonObjectWriter
    {
        private readonly StringBuilder _builder;
        private bool _isFirst;

        public JsonObjectWriter(StringBuilder builder)
        {
            _builder = builder;
            _isFirst = true;
            _builder.Append('{');
        }

        /// <summary>
        /// Writes a string member.
        /// </summary>
        public void WriteString(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
        {
            AppendName(name);
            AppendEscaped(_builder, value);
        }

        /// <summary>
        /// Writes an integer member.
        /// </summary>
        public void WriteNumber(ReadOnlySpan<char> name, ushort value)
        {
            AppendName(name);
            _builder.Append(value);
        }

        /// <summary>
        /// Completes the object.
        /// </summary>
        public void End()
        {
            _builder.Append('}');
        }

        private void AppendName(ReadOnlySpan<char> name)
        {
            if (_isFirst)
            {
                _isFirst = false;
            }
            else
            {
                _builder.Append(',');
            }

            _builder.Append('"').Append(name).Append("\":");
        }

        /// <summary>
        /// Appends a quoted and escaped JSON string. Control characters, the quote and the
        /// backslash are escaped; everything else, including non-ASCII, is emitted as is,
        /// except for an unpaired surrogate which is escaped because it cannot be transcoded
        /// to UTF-8.
        /// </summary>
        internal static void AppendEscaped(StringBuilder builder, ReadOnlySpan<char> value)
        {
            builder.Append('"');

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];

                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ' || IsUnpairedSurrogate(value, i))
                        {
                            builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            builder.Append('"');
        }

        private static bool IsUnpairedSurrogate(ReadOnlySpan<char> value, int i)
        {
            var c = value[i];

            if (char.IsHighSurrogate(c))
            {
                return i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]);
            }

            if (char.IsLowSurrogate(c))
            {
                return i == 0 || !char.IsHighSurrogate(value[i - 1]);
            }

            return false;
        }
    }

    /// <summary>
    /// Reads the members of a flat JSON object one at a time.
    /// </summary>
    /// <remarks>
    /// Deliberately lenient in the ways that matter for interoperating with other WebRTC
    /// stacks: member order is free, unknown members are skipped (including nested objects
    /// and arrays), and unescaped control characters inside strings are accepted even though
    /// RFC 8259 requires them to be escaped.
    /// </remarks>
    internal ref struct JsonObjectParser
    {
        private readonly ReadOnlySpan<char> _json;
        private int _position;
        private bool _readAny;
        private bool _completed;
        private bool _failed;

        private JsonObjectParser(ReadOnlySpan<char> json, int position)
        {
            _json = json;
            _position = position;
            _readAny = false;
            _completed = false;
            _failed = false;
        }

        /// <summary>
        /// True if the JSON was malformed at any point during reading.
        /// </summary>
        public bool Failed => _failed;

        /// <summary>
        /// Matches a member name without regard to case, which is how the TinyJson serialiser
        /// this replaced matched them. Peers using PascalCase property names rely on it.
        /// </summary>
        public static bool IsMember(ReadOnlySpan<char> name, ReadOnlySpan<char> member) =>
            name.Equals(member, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Attempts to position a parser at the start of a JSON object.
        /// </summary>
        /// <returns>False if the input is not a JSON object, for example a bare null or a
        /// non-JSON string.</returns>
        public static bool TryCreate(ReadOnlySpan<char> json, out JsonObjectParser parser)
        {
            parser = default;

            var position = SkipWhitespace(json, 0);

            if (position >= json.Length || json[position] != '{')
            {
                return false;
            }

            parser = new JsonObjectParser(json, position + 1);
            return true;
        }

        /// <summary>
        /// Reads the next member of the object.
        /// </summary>
        /// <returns>False when the end of the object is reached or the JSON is malformed.
        /// Check <see cref="Failed"/> to tell the two apart.</returns>
        public bool TryReadMember(out ReadOnlySpan<char> name, out JsonValueKind kind, out ReadOnlySpan<char> value)
        {
            name = default;
            kind = JsonValueKind.Null;
            value = default;

            if (_failed || _completed)
            {
                return false;
            }

            var i = SkipWhitespace(_json, _position);

            if (i >= _json.Length)
            {
                return Fail();
            }

            if (_json[i] == '}')
            {
                _completed = true;
                _position = i + 1;
                return false;
            }

            if (_readAny)
            {
                if (_json[i] != ',')
                {
                    return Fail();
                }

                i = SkipWhitespace(_json, i + 1);
            }

            if (i >= _json.Length || _json[i] != '"')
            {
                return Fail();
            }

            if (!TryReadString(_json, ref i, out name))
            {
                return Fail();
            }

            i = SkipWhitespace(_json, i);

            if (i >= _json.Length || _json[i] != ':')
            {
                return Fail();
            }

            i = SkipWhitespace(_json, i + 1);

            if (!TryReadValue(_json, ref i, out kind, out value))
            {
                return Fail();
            }

            _position = i;
            _readAny = true;
            return true;
        }

        private bool Fail()
        {
            _failed = true;
            return false;
        }

        private static int SkipWhitespace(ReadOnlySpan<char> json, int i)
        {
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t' || json[i] == '\r' || json[i] == '\n'))
            {
                i++;
            }

            return i;
        }

        private static bool TryReadValue(ReadOnlySpan<char> json, scoped ref int i, out JsonValueKind kind, out ReadOnlySpan<char> value)
        {
            kind = JsonValueKind.Null;
            value = default;

            if (i >= json.Length)
            {
                return false;
            }

            switch (json[i])
            {
                case '"':
                    kind = JsonValueKind.String;
                    return TryReadString(json, ref i, out value);

                case '{':
                    kind = JsonValueKind.Object;
                    return TrySkipNested(json, ref i, '{', '}');

                case '[':
                    kind = JsonValueKind.Array;
                    return TrySkipNested(json, ref i, '[', ']');

                case 't':
                    kind = JsonValueKind.Boolean;
                    value = "true";
                    return TryReadLiteral(json, ref i, "true");

                case 'f':
                    kind = JsonValueKind.Boolean;
                    value = "false";
                    return TryReadLiteral(json, ref i, "false");

                case 'n':
                    kind = JsonValueKind.Null;
                    return TryReadLiteral(json, ref i, "null");

                default:
                    kind = JsonValueKind.Number;
                    return TryReadNumber(json, ref i, out value);
            }
        }

        private static bool TryReadLiteral(ReadOnlySpan<char> json, ref int i, ReadOnlySpan<char> literal)
        {
            if (i + literal.Length > json.Length || !json.Slice(i, literal.Length).SequenceEqual(literal))
            {
                return false;
            }

            i += literal.Length;
            return true;
        }

        /// <summary>
        /// Reads a number token following the RFC 8259 grammar,
        /// -? (0 | [1-9][0-9]*) (.[0-9]+)? ([eE][+-]?[0-9]+)?
        /// Tokens such as "+1", "01", "1." or "1+2" are rejected rather than being passed on
        /// for the caller to misinterpret.
        /// </summary>
        private static bool TryReadNumber(ReadOnlySpan<char> json, scoped ref int i, out ReadOnlySpan<char> value)
        {
            value = default;
            var start = i;

            if (i < json.Length && json[i] == '-')
            {
                i++;
            }

            if (i >= json.Length || !IsDigit(json[i]))
            {
                return false;
            }

            i = json[i] == '0' ? i + 1 : SkipDigits(json, i);

            if (i < json.Length && json[i] == '.')
            {
                i++;

                if (i >= json.Length || !IsDigit(json[i]))
                {
                    return false;
                }

                i = SkipDigits(json, i);
            }

            if (i < json.Length && (json[i] == 'e' || json[i] == 'E'))
            {
                i++;

                if (i < json.Length && (json[i] == '+' || json[i] == '-'))
                {
                    i++;
                }

                if (i >= json.Length || !IsDigit(json[i]))
                {
                    return false;
                }

                i = SkipDigits(json, i);
            }

            value = json.Slice(start, i - start);
            return true;
        }

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        private static int SkipDigits(ReadOnlySpan<char> json, int i)
        {
            while (i < json.Length && IsDigit(json[i]))
            {
                i++;
            }

            return i;
        }

        /// <summary>
        /// Skips over a nested object or array, respecting strings and escapes so that a
        /// brace inside a string value does not unbalance the scan.
        /// </summary>
        private static bool TrySkipNested(ReadOnlySpan<char> json, ref int i, char open, char close)
        {
            var depth = 0;

            while (i < json.Length)
            {
                var c = json[i];

                if (c == '"')
                {
                    if (!TrySkipString(json, ref i))
                    {
                        return false;
                    }

                    continue;
                }

                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;

                    if (depth == 0)
                    {
                        i++;
                        return true;
                    }
                }

                i++;
            }

            return false;
        }

        private static bool TrySkipString(ReadOnlySpan<char> json, ref int i)
        {
            if (i >= json.Length || json[i] != '"')
            {
                return false;
            }

            i++;

            while (i < json.Length)
            {
                var c = json[i];

                if (c == '"')
                {
                    i++;
                    return true;
                }

                if (c == '\\')
                {
                    i++;

                    if (i >= json.Length)
                    {
                        return false;
                    }

                    if (json[i] == 'u')
                    {
                        if (i + 4 >= json.Length ||
                            !ushort.TryParse(json.Slice(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
                        {
                            return false;
                        }

                        i += 4;
                    }
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// Reads a quoted string starting at the opening quote and leaves the index just past
        /// the closing quote.
        /// </summary>
        private static bool TryReadString(ReadOnlySpan<char> json, scoped ref int i, out ReadOnlySpan<char> value)
        {
            value = default;

            if (i >= json.Length || json[i] != '"')
            {
                return false;
            }

            i++;

            var start = i;
            StringBuilder builder = null;

            while (i < json.Length)
            {
                var c = json[i];

                if (c == '"')
                {
                    if (builder == null)
                    {
                        value = json.Slice(start, i - start);
                    }
                    else
                    {
                        builder.Append(json.Slice(start, i - start));
                        value = builder.ToString().AsSpan();
                    }

                    i++;
                    return true;
                }

                if (c != '\\')
                {
                    i++;
                    continue;
                }

                if (builder == null)
                {
                    builder = new StringBuilder(json.Length - start);
                }

                builder.Append(json.Slice(start, i - start));
                i++;

                if (i >= json.Length)
                {
                    return false;
                }

                var escaped = json[i];

                switch (escaped)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (i + 4 >= json.Length ||
                            !ushort.TryParse(json.Slice(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
                        {
                            return false;
                        }

                        builder.Append((char)codePoint);
                        i += 4;
                        break;
                    default:
                        // Not a recognised escape. Emit it as it appeared rather than failing
                        // the whole parse.
                        builder.Append('\\').Append(escaped);
                        break;
                }

                i++;
                start = i;
            }

            return false;
        }
    }
}
