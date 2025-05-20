//-----------------------------------------------------------------------------
// Filename: STUNAddressAttribute.cs
//
// Description: Implements STUN address attribute as defined in RFC5389.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Nov 2010	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <remarks>
    /// There's no proper explanation of why this STUN attribute was obsoleted. My guess is to favour using the XOR Maoped Address attribute
    /// BUT that does not help when a STUN server provides this atype of address attribute. It will still need to be parsed and understood,
    /// Reverted this obsoletion on 13 Nov 2024 AC. 
    /// </remarks>
    //[Obsolete("Provided for backward compatibility with RFC3489 clients.")]
    public class STUNAddressAttribute : STUNAddressAttributeBase
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
            : this(STUNAttributeTypesEnum.MappedAddress, (ReadOnlySpan<byte>)attributeValue)
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
        public STUNAddressAttribute(ReadOnlySpan<byte> attributeValue)
            : this(STUNAttributeTypesEnum.MappedAddress, attributeValue)
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
            : this(attributeType, (ReadOnlySpan<byte>)attributeValue)
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
        public STUNAddressAttribute(STUNAttributeTypesEnum attributeType, ReadOnlySpan<byte> attributeValue)
            : base(attributeType, attributeValue)
        {
            Port = BinaryPrimitives.ReadUInt16BigEndian(attributeValue.Slice(2, 2));

            Address = attributeValue.Slice(4, 4).ToIPAddress();
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
        public STUNAddressAttribute(STUNAttributeTypesEnum attributeType, int port, IPAddress address)
            : base(attributeType, null)
        {
            Port = port;
            Address = address;

            base.AttributeType = attributeType;
            //base.Length = ADDRESS_ATTRIBUTE_LENGTH;
        }

        [Obsolete("Use ToByteBuffer(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            return base.WriteBytes(buffer.AsSpan(startIndex));
        }

        public override int WriteBytes(Span<byte> buffer)
        {
            if (BitConverter.IsLittleEndian)
            {
                var typeBytes = BitConverter.GetBytes(NetConvert.DoReverseEndian((ushort)base.AttributeType));
                typeBytes.CopyTo(buffer.Slice(0, 2));

                var lengthBytes = BitConverter.GetBytes(NetConvert.DoReverseEndian(ADDRESS_ATTRIBUTE_IPV4_LENGTH));
                lengthBytes.CopyTo(buffer.Slice(2, 2));
            }
            else
            {
                var typeBytes = BitConverter.GetBytes((ushort)base.AttributeType);
                typeBytes.CopyTo(buffer.Slice(0, 2));

                var lengthBytes = BitConverter.GetBytes(ADDRESS_ATTRIBUTE_IPV4_LENGTH);
                lengthBytes.CopyTo(buffer.Slice(2, 2));
            }

            buffer[5] = (byte)Family;

            if (BitConverter.IsLittleEndian)
            {
                var portBytes = BitConverter.GetBytes(NetConvert.DoReverseEndian(Convert.ToUInt16(Port)));
                portBytes.CopyTo(buffer.Slice(6, 2));
            }
            else
            {
                var portBytes = BitConverter.GetBytes(Convert.ToUInt16(Port));
                portBytes.CopyTo(buffer.Slice(6, 2));
            }

            var addressBytes = Address.GetAddressBytes();
            addressBytes.CopyTo(buffer.Slice(8, 4));

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_IPV4_LENGTH;
        }

        private protected override void ValueToString(ref ValueStringBuilder sb)
        {
            sb.Append("Address=");
            sb.Append(Address.ToString());
            sb.Append(", Port=");
            sb.Append(Port);
        }
    }
}
