using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public abstract class STUNAddressAttributeBase : STUNAttribute
    {
        /// <summary>
        /// Obsolete.
        /// <br/> Please use <see cref="ADDRESS_ATTRIBUTE_IPV4_LENGTH"/> or <see cref="ADDRESS_ATTRIBUTE_IPV6_LENGTH"/> instead.
        /// <br/> <br/>
        /// </summary>
        [Obsolete("Default attribute length for IPv4 only.")]
        public const UInt16 ADDRESS_ATTRIBUTE_LENGTH = 8;

        public const UInt16 ADDRESS_ATTRIBUTE_IPV4_LENGTH = 8;
        public const UInt16 ADDRESS_ATTRIBUTE_IPV6_LENGTH = 20;

        protected UInt16 AddressAttributeLength = ADDRESS_ATTRIBUTE_IPV4_LENGTH;
        protected byte[] TransactionId;

        /// <summary>
        /// Defaults to IPv4 (0x01 // 1)
        /// </summary>
        public int Family = 1;      // Ipv4 = 1, IPv6 = 2.
        public int Port;
        public IPAddress Address;

        public override UInt16 PaddedLength
        {
            get => AddressAttributeLength;
        }

        [Obsolete("Use STUNAddressAttributeBase(STUNAttributeTypesEnum, ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNAddressAttributeBase(STUNAttributeTypesEnum attributeType, byte[] value)
            : this(attributeType, (ReadOnlySpan<byte>)value)
        {
        }

        public STUNAddressAttributeBase(STUNAttributeTypesEnum attributeType, ReadOnlySpan<byte> value)
            : base(attributeType, value)
        {
        }

        private protected override void ValueToString(ref ValueStringBuilder sb)
        {
            sb.Append("Address=");
            sb.Append(Address.ToString());
            sb.Append(", Port=");
            sb.Append(Port);
            sb.Append(", Family=");
            sb.Append(Family switch { 1 => "IPV4", 2 => "IPV6", _ => "Invalid", });
        }
    }
}
