using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class STUNAddressAttributeBase
    {
        [Obsolete("Use STUNAddressAttributeBase(STUNAttributeTypesEnum, ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNAddressAttributeBase(STUNAttributeTypesEnum attributeType, byte[] value)
            : this(attributeType, (ReadOnlyMemory<byte>)value)
        {
        }
    }
}
