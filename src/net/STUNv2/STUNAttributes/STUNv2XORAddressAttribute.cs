//-----------------------------------------------------------------------------
// Filename: STUNv2XORAddressAttribute.cs
//
// Description: Implements STUN XOR mapped address attribute as defined in RFC5389.
//
// Author(s):
// Aaron Clauson
//
// History:
// 15 Oct 2014	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This attribue is the same as the mapped address attribute except the address details are XOR'ed with the STUN magic cookie. 
    /// THe reason for this is to stop NAT application layer gateways from doing string replacements of private IP adresses and ports.
    /// </summary>
    public class STUNv2XORAddressAttribute : STUNv2Attribute
    {
        public const UInt16 ADDRESS_ATTRIBUTE_LENGTH = 8;

        public int Family = 1;      // Ipv4 = 1, IPv6 = 2.
        public int Port;
        public IPAddress Address;

        public override UInt16 PaddedLength
        {
            get { return ADDRESS_ATTRIBUTE_LENGTH; }
        }

        public STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum attributeType, byte[] attributeValue)
            : base(attributeType, attributeValue)
        {
            if (BitConverter.IsLittleEndian)
            {
                Port = Utility.ReverseEndian(BitConverter.ToUInt16(attributeValue, 2)) ^ (UInt16)(STUNv2Header.MAGIC_COOKIE >> 16);
                UInt32 address = Utility.ReverseEndian(BitConverter.ToUInt32(attributeValue, 4)) ^ STUNv2Header.MAGIC_COOKIE;
                Address = new IPAddress(Utility.ReverseEndian(address));
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2) ^ (UInt16)(STUNv2Header.MAGIC_COOKIE >> 16);
                UInt32 address = BitConverter.ToUInt32(attributeValue, 4) ^ STUNv2Header.MAGIC_COOKIE;
                Address = new IPAddress(address);
            }
        }

        public STUNv2XORAddressAttribute(STUNv2AttributeTypesEnum attributeType, int port, IPAddress address)
            : base(attributeType, null)
        {
            Port = port;
            Address = address;
        }

        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian((UInt16)base.AttributeType)), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(ADDRESS_ATTRIBUTE_LENGTH)), 0, buffer, startIndex + 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)base.AttributeType), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(ADDRESS_ATTRIBUTE_LENGTH), 0, buffer, startIndex + 2, 2);
            }

            buffer[startIndex + 4] = 0x00;
            buffer[startIndex + 5] = (byte)Family;

            if (BitConverter.IsLittleEndian)
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (STUNv2Header.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = Utility.ReverseEndian(BitConverter.ToUInt32(Address.GetAddressBytes(), 0)) ^ STUNv2Header.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(xorPort)), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(xorAddress)), 0, buffer, startIndex + 8, 4);
            }
            else
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (STUNv2Header.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = BitConverter.ToUInt32(Address.GetAddressBytes(), 0) ^ STUNv2Header.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(xorPort), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(xorAddress), 0, buffer, startIndex + 8, 4);
            }

            return STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_LENGTH;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUNv2 XOR_MAPPED_ADDRESS Attribute: " + base.AttributeType + ", address=" + Address.ToString() + ", port=" + Port + ".";

            return attrDescrStr;
        }
    }
}
