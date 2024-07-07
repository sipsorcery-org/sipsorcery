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
using System.Linq;
using System.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This attribute is the same as the mapped address attribute except the address details are XOR'ed with the STUN magic cookie. 
    /// THe reason for this is to stop NAT application layer gateways from doing string replacements of private IP addresses and ports.
    /// </summary>
    public class STUNXORAddressAttribute : STUNAddressAttributeBase
    {
        /// <summary>
        /// Obsolete.
        /// <br/> For IPv6 support, please parse using
        /// <br/> <see cref="STUNXORAddressAttribute(STUNAttributeTypesEnum, byte[], byte[])"/>
        /// <br/> <br/>
        /// Parses an XOR-d (encoded) IPv4 Address attribute.
        /// </summary>
        [Obsolete("Provided for backward compatibility with RFC3489 clients.")]
        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, byte[] attributeValue)
            : this(attributeType, attributeValue, null)
        {
        }

        /// <summary>
        /// Parses an XOR-d (encoded) Address attribute with IPv4/IPv6 support.
        /// </summary>
        /// <param name="attributeType">of <see cref="STUNAttributeTypesEnum.XORMappedAddress"/>
        /// or <see cref="STUNAttributeTypesEnum.XORPeerAddress"/>
        /// or <see cref="STUNAttributeTypesEnum.XORRelayedAddress"/></param>
        /// <param name="attributeValue">the raw bytes</param>
        /// <param name="transactionId">the <see cref="STUNHeader.TransactionId"/></param>
        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, byte[] attributeValue, byte[] transactionId)
            : base(attributeType, attributeValue)
        {
            Family = attributeValue[1];
            AddressAttributeLength = Family == 1 ? ADDRESS_ATTRIBUTE_IPV4_LENGTH : ADDRESS_ATTRIBUTE_IPV6_LENGTH;
            TransactionId = transactionId;

            byte[] address;

            if (BitConverter.IsLittleEndian)
            {
                Port = NetConvert.DoReverseEndian(BitConverter.ToUInt16(attributeValue, 2)) ^ (UInt16)(STUNHeader.MAGIC_COOKIE >> 16);
                address = BitConverter.GetBytes(NetConvert.DoReverseEndian(BitConverter.ToUInt32(attributeValue, 4)) ^ STUNHeader.MAGIC_COOKIE).Reverse().ToArray();
            }
            else
            {
                Port = BitConverter.ToUInt16(attributeValue, 2) ^ (UInt16)(STUNHeader.MAGIC_COOKIE >> 16);
                address = BitConverter.GetBytes(BitConverter.ToUInt32(attributeValue, 4)  ^ STUNHeader.MAGIC_COOKIE);
            }

            if (Family == STUNAttributeConstants.IPv6AddressFamily[0] && TransactionId != null)
            {
                address = address.Concat(BitConverter.GetBytes(BitConverter.ToUInt32(attributeValue, 08) ^ BitConverter.ToUInt32(TransactionId, 0)))
                                 .Concat(BitConverter.GetBytes(BitConverter.ToUInt32(attributeValue, 12) ^ BitConverter.ToUInt32(TransactionId, 4)))
                                 .Concat(BitConverter.GetBytes(BitConverter.ToUInt32(attributeValue, 16) ^ BitConverter.ToUInt32(TransactionId, 8)))
                                 .ToArray();
            }

            Address = new IPAddress(address);
        }

        /// <summary>
        /// Obsolete.
        /// <br/> For IPv6 support, please create using <see cref="STUNXORAddressAttribute(STUNAttributeTypesEnum, int, IPAddress, byte[])"/>
        /// <br/> <br/>
        /// Creates an XOR-d (encoded) IPv4 Address attribute.
        /// </summary>
        [Obsolete("Provided for backward compatibility with RFC3489 clients.")]
        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, int port, IPAddress address)
            : this(attributeType, port, address, null)
        {
        }

        /// <summary>
        /// Creates an XOR-d (encoded) Address attribute with IPv4/IPv6 support.
        /// </summary>
        /// <param name="attributeType">of <see cref="STUNAttributeTypesEnum.XORMappedAddress"/>
        /// or <see cref="STUNAttributeTypesEnum.XORPeerAddress"/>
        /// or <see cref="STUNAttributeTypesEnum.XORRelayedAddress"/></param>
        /// <param name="port">Allocated Port</param>
        /// <param name="address">Allocated IPAddress</param>
        /// <param name="transactionId">the <see cref="STUNHeader.TransactionId"/></param>
        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, int port, IPAddress address, byte[] transactionId)
            : base(attributeType, null)
        {
            Port = port;
            Address = address;
            Family = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 1 : 2;
            AddressAttributeLength = Family == 1 ? ADDRESS_ATTRIBUTE_IPV4_LENGTH : ADDRESS_ATTRIBUTE_IPV6_LENGTH;
            TransactionId = transactionId;
        }

        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((UInt16)base.AttributeType)), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(AddressAttributeLength)), 0, buffer, startIndex + 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)base.AttributeType), 0, buffer, startIndex, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(AddressAttributeLength), 0, buffer, startIndex + 2, 2);
            }

            buffer[startIndex + 4] = 0x00;
            buffer[startIndex + 5] = (byte)Family;

            var address = Address.GetAddressBytes();

            if (BitConverter.IsLittleEndian)
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (UInt16)(STUNHeader.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = NetConvert.DoReverseEndian(BitConverter.ToUInt32(address, 0)) ^ STUNHeader.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(xorPort)), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(xorAddress)), 0, buffer, startIndex + 8, 4);
            }
            else
            {
                UInt16 xorPort = Convert.ToUInt16(Convert.ToUInt16(Port) ^ (UInt16)(STUNHeader.MAGIC_COOKIE >> 16));
                UInt32 xorAddress = BitConverter.ToUInt32(address, 0) ^ STUNHeader.MAGIC_COOKIE;
                Buffer.BlockCopy(BitConverter.GetBytes(xorPort), 0, buffer, startIndex + 6, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(xorAddress), 0, buffer, startIndex + 8, 4);
            }

            if (Family == STUNAttributeConstants.IPv6AddressFamily[0] && TransactionId != null)
            {
                Buffer.BlockCopy(
                            BitConverter.GetBytes(BitConverter.ToUInt32(address, 04) ^ BitConverter.ToUInt32(TransactionId, 0))
                    .Concat(BitConverter.GetBytes(BitConverter.ToUInt32(address, 08) ^ BitConverter.ToUInt32(TransactionId, 4)))
                    .Concat(BitConverter.GetBytes(BitConverter.ToUInt32(address, 12) ^ BitConverter.ToUInt32(TransactionId, 8)))
                    .ToArray(),
                0, buffer, startIndex + 12, 12);
            }

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + PaddedLength;
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
