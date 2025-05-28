using System;
using System.Runtime.InteropServices;

namespace SIPSorcery.Sys
{
    internal ref partial struct ValueStringBuilder
    {
        private static readonly char[] hexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        public void Append(byte[]? bytes, char? separator = null)
        {
            if (bytes is { Length: > 0 })
            {
                Append(bytes.AsSpan(), separator);
            }
        }

        public void Append(byte[]? bytes, int length, char? separator = null)
        {
            if (bytes is null)
            {
                return;
            }

            Append(bytes.AsSpan(0, length), separator);
        }

        public void Append(ReadOnlySpan<byte> bytes, char? separator = null)
        {
            if (bytes.IsEmpty)
            {
                return;
            }

            if (separator is { } s)
            {
                for (int i = 0; i < bytes.Length;)
                {
                    var b = bytes[i];
                    Append(hexmap[(int)b >> 4]);
                    Append(hexmap[(int)b & 0b1111]);
                    if (++i < bytes.Length)
                    {
                        Append(s);
                    }
                }
            }
            else
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    var b = bytes[i];
                    Append(hexmap[(int)b >> 4]);
                    Append(hexmap[(int)b & 0b1111]);
                }
            }
        }
    }
}
