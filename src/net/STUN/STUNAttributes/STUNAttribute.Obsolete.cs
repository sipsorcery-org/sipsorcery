using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class STUNAttribute
    {
        [Obsolete("Use STUNAttribute(STUNAttributeTypesEnum, ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNAttribute(STUNAttributeTypesEnum attributeType, byte[] value)
            : this(attributeType, (ReadOnlyMemory<byte>)value)
        {
        }

        [Obsolete("Use ParseMessageAttributes(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static List<STUNAttribute> ParseMessageAttributes(byte[] buffer, int startIndex, int endIndex)
            => ParseMessageAttributes(buffer.AsSpan(startIndex, endIndex - startIndex), null);

        [Obsolete("Use ParseMessageAttributes(ReadOnlySpan<byte>, STUNHeader) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static List<STUNAttribute> ParseMessageAttributes(byte[] buffer, int startIndex, int endIndex, STUNHeader header)
            => ParseMessageAttributes(buffer.AsSpan(startIndex, endIndex - startIndex), header);

        [Obsolete("Use WriteBytes(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public virtual int ToByteBuffer(byte[] buffer, int startIndex)
        {
            return WriteBytes(buffer.AsSpan(startIndex));
        }
    }
}
