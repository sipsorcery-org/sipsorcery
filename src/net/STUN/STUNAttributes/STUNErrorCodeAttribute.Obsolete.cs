using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class STUNErrorCodeAttribute
    {
        [Obsolete("Use ToByteBuffer(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            return WriteBytes(buffer.AsSpan(startIndex));
        }
    }
}
