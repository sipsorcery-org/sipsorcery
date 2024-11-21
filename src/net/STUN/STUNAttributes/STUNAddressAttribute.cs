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
using System.Net;
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
        public STUNAddressAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.MappedAddress, attributeValue)
        {
            if (BitConverter.IsLittleEndian)
            {
                Port = NetConvert.DoReverseEndian(BitConverter.ToUInt16(attributeValue, 2));
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2);
            }

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
            if (BitConverter.IsLittleEndian)
            {
                Port = NetConvert.DoReverseEndian(BitConverter.ToUInt16(attributeValue, 2));
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2);
            }

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
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((UInt16)base.AttributeType)), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ADDRESS_ATTRIBUTE_IPV4_LENGTH)), 0, buffer, startIndex + 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)base.AttributeType), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(ADDRESS_ATTRIBUTE_IPV4_LENGTH), 0, buffer, startIndex + 2, 2);
            }

            buffer[startIndex + 5] = (byte)Family;

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Convert.ToUInt16(Port))), 0, buffer, startIndex + 6, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToUInt16(Port)), 0, buffer, startIndex + 6, 2);
            }
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
