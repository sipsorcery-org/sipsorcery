using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <remarks>
    /// There's no proper explanation of why this STUN attribute was obsoleted. My guess is to favour using the XOR Maoped Address attribute
    /// BUT that does not help when a STUN server provides this atype of address attribute. It will still need to be parsed and understood,
    /// Reverted this obsoletion on 13 Nov 2024 AC. 
    /// </remarks>
    //[Obsolete("Provided for backward compatibility with RFC3489 clients.")]
    partial class STUNAddressAttribute
    {
        /// <summary>
        /// Parses an IPv4 Address attribute.
        /// </summary>
        /// <remarks>
        /// There's no proper explanation of why this STUN attribute was obsoleted. My guess is to favour using the XOR Maoped Address attribute
        /// BUT that does not help when a STUN server provides this atype of address attribute. It will still need to be parsed and understood,
        /// Reverted this obsoletion on 13 Nov 2024 AC. 
        /// </remarks>
        //[Obsolete("Provided for backward compatibility with RFC3489 clients.")]
        [Obsolete("Use STUNAddressAttribute(STUNAttributeTypesEnum, ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNAddressAttribute(byte[] attributeValue)
            : this(STUNAttributeTypesEnum.MappedAddress, (ReadOnlyMemory<byte>)attributeValue)
        {
        }

        /// <summary>
        /// Parses an IPv4 Address attribute.
        /// </summary>
        /// <remarks>
        /// There's no proper explanation of why this STUN attribute was obsoleted. My guess is to favour using the XOR Maoped Address attribute
        /// BUT that does not help when a STUN server provides this atype of address attribute. It will still need to be parsed and understood,
        /// Reverted this obsoletion on 13 Nov 2024 AC. 
        /// </remarks>
        //[Obsolete("Provided for backward compatibility with RFC3489 clients.")]
        [Obsolete("Use STUNAddressAttribute(STUNAttributeTypesEnum, ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNAddressAttribute(STUNAttributeTypesEnum attributeType, byte[] attributeValue)
            : this(attributeType, (ReadOnlyMemory<byte>)attributeValue)
        {
        }

        [Obsolete("Use ToByteBuffer(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            return base.WriteBytes(buffer.AsSpan(startIndex));
        }
    }
}
