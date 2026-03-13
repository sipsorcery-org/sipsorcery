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
using System.Net;

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
        public STUNAddressAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.MappedAddress, attributeValue)
        {
            Port = BinaryPrimitives.ReadUInt16BigEndian(attributeValue.AsSpan(2));

            Address = new IPAddress(new byte[] { attributeValue[4], attributeValue[5], attributeValue[6], attributeValue[7] });
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
        public STUNAddressAttribute(STUNAttributeTypesEnum attributeType, byte[] attributeValue)
            : base(attributeType, attributeValue)
        {
            Port = BinaryPrimitives.ReadUInt16BigEndian(attributeValue.AsSpan(2));

            Address = new IPAddress(new byte[] { attributeValue[4], attributeValue[5], attributeValue[6], attributeValue[7] });
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

        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(startIndex), (UInt16)base.AttributeType);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(startIndex + 2), ADDRESS_ATTRIBUTE_IPV4_LENGTH);

            buffer[startIndex + 5] = (byte)Family;

            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(startIndex + 6), Convert.ToUInt16(Port));
            Buffer.BlockCopy(Address.GetAddressBytes(), 0, buffer, startIndex + 8, 4);

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_IPV4_LENGTH;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUN Attribute: " + base.AttributeType + ", address=" + Address.ToString() + ", port=" + Port + ".";

            return attrDescrStr;
        }
    }
}
