using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class STUNConnectionIdAttribute
    {
        [Obsolete("Use STUNConnectionIdAttribute(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNConnectionIdAttribute(byte[] attributeValue)
            : this((ReadOnlyMemory<byte>)attributeValue)
        {
        }
    }
}
