//-----------------------------------------------------------------------------
// Filename: STUNXORAddressAttribute.cs
//
// Description: Implements STUN XOR mapped address attribute as defined in RFC5389.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Oct 2014	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This attribute is the same as the mapped address attribute except the address details are XOR'ed with the STUN magic cookie. 
    /// THe reason for this is to stop NAT application layer gateways from doing string replacements of private IP addresses and ports.
    /// </summary>
    public class STUNXORAddressAttribute : STUNAttribute
    {
        public const UInt16 ADDRESS_ATTRIBUTE_LENGTH = 8;

        public int Family = 1;      // Ipv4 = 1, IPv6 = 2.
        public int Port;
        public IPAddress Address;

        public override UInt16 PaddedLength
        {
            get { return ADDRESS_ATTRIBUTE_LENGTH; }
        }

        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, byte[] attributeValue)
            : base(attributeType, attributeValue)
        {
            if (BitConverter.IsLittleEndian)
            {
                Port = NetConvert.DoReverseEndian(BitConverter.ToUInt16(attributeValue, 2)) ^ (UInt16)(STUNHeader.MAGIC_COOKIE >> 16);
                UInt32 address = NetConvert.DoReverseEndian(BitConverter.ToUInt32(attributeValue, 4)) ^ STUNHeader.MAGIC_COOKIE;
                Address = new IPAddress(NetConvert.DoReverseEndian(address));
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2) ^ (UInt16)(STUNHeader.MAGIC_COOKIE >> 16);
                UInt32 address = BitConverter.ToUInt32(attributeValue, 4) ^ STUNHeader.MAGIC_COOKIE;
                Address = new IPAddress(address);
            }
        }

        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, int port, IPAddress address)
            : base(attributeType, null)
        {
            Port = port;
            Address = address;
        }

        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((UInt16)base.AttributeType)), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(ADDRESS_ATTRIBUTE_LENGTH)), 0, buffer, startIndex + 2, 2);
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
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (STUNHeader.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = NetConvert.DoReverseEndian(BitConverter.ToUInt32(Address.GetAddressBytes(), 0)) ^ STUNHeader.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(xorPort)), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(xorAddress)), 0, buffer, startIndex + 8, 4);
            }
            else
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (STUNHeader.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = BitConverter.ToUInt32(Address.GetAddressBytes(), 0) ^ STUNHeader.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(xorPort), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(xorAddress), 0, buffer, startIndex + 8, 4);
            }

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_LENGTH;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUN XOR_MAPPED_ADDRESS Attribute: " + base.AttributeType + ", address=" + Address.ToString() + ", port=" + Port + ".";

            return attrDescrStr;
        }

        public IPEndPoint GetIPEndPoint()
        {
            if (Address != null)
            {
                return new IPEndPoint(Address, Port);
            }
            else
            {
                return null;
            }
        }
    }
}
