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
using System.Buffers.Binary;
using System.ComponentModel;
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
        [Obsolete("Use STUNXORAddressAttribute(STUNAttributeTypesEnum, ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, byte[] attributeValue, byte[] transactionId)
            : this(attributeType, (ReadOnlySpan<byte>)attributeValue, (ReadOnlySpan<byte>)transactionId)
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
        public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, ReadOnlySpan<byte> attributeValue, ReadOnlySpan<byte> transactionId)
            : base(attributeType, attributeValue)
        {
            Family = attributeValue[1];
            AddressAttributeLength = Family == 1 ? ADDRESS_ATTRIBUTE_IPV4_LENGTH : ADDRESS_ATTRIBUTE_IPV6_LENGTH;
            TransactionId = transactionId.ToArray();

            var port = BinaryPrimitives.ReadUInt16BigEndian(attributeValue.Slice(2));
            Port = (ushort)(port ^ (STUNHeader.MAGIC_COOKIE >> 16));

            if (Family == STUNAttributeConstants.IPv4AddressFamily[0])
            {
                var ipv4 = BinaryPrimitives.ReadUInt32BigEndian(attributeValue.Slice(4)) ^ STUNHeader.MAGIC_COOKIE;
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                Span<byte> addressBytes = stackalloc byte[4];
#else
                var addressBytes = new byte[4];
#endif
                BinaryPrimitives.WriteUInt32BigEndian(addressBytes, ipv4);
                Address = new IPAddress(addressBytes);
            }
            else if (Family == STUNAttributeConstants.IPv6AddressFamily[0] && TransactionId != null)
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                Span<byte> addressBytes = stackalloc byte[16];
                var addressSpan = addressBytes;
#else
                var addressBytes = new byte[16];
                var addressSpan = addressBytes.AsSpan();
#endif

                var part0 = BinaryPrimitives.ReadUInt32BigEndian(attributeValue.Slice(4));
                var tid0 = BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(0));
                BinaryPrimitives.WriteUInt32BigEndian(addressSpan.Slice(0), part0 ^ tid0);

                var part1 = BinaryPrimitives.ReadUInt32BigEndian(attributeValue.Slice(8));
                var tid1 = BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(4));
                BinaryPrimitives.WriteUInt32BigEndian(addressSpan.Slice(4), part1 ^ tid1);

                var part2 = BinaryPrimitives.ReadUInt32BigEndian(attributeValue.Slice(12));
                var tid2 = BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(8));
                BinaryPrimitives.WriteUInt32BigEndian(addressSpan.Slice(8), part2 ^ tid2);

                var lastPart = BinaryPrimitives.ReadUInt32BigEndian(attributeValue.Slice(4 + 12));
                BinaryPrimitives.WriteUInt32BigEndian(addressSpan.Slice(12), lastPart ^ STUNHeader.MAGIC_COOKIE);

                Address = new IPAddress(addressBytes);
            }
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

        [Obsolete("Use ToByteBuffer(Span<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            return WriteBytes(buffer.AsSpan(startIndex));
        }

        public override int WriteBytes(Span<byte> buffer)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(0, 2), (ushort)base.AttributeType);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2, 2), AddressAttributeLength);

            buffer[4] = 0x00;
            buffer[5] = (byte)Family;

            var address = Address.GetAddressBytes();

            var xorPort = (ushort)(Port ^ (STUNHeader.MAGIC_COOKIE >> 16));
            var xorAddress = BinaryPrimitives.ReadUInt32BigEndian(address) ^ STUNHeader.MAGIC_COOKIE;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(6, 2), xorPort);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8, 4), xorAddress);

            if (Family == STUNAttributeConstants.IPv6AddressFamily[0] && TransactionId != null)
            {
                BinaryPrimitives.WriteUInt32BigEndian(
                    buffer.Slice(12, 4),
                    BinaryPrimitives.ReadUInt32BigEndian(address.AsSpan(4)) ^ BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(0))
                );

                BinaryPrimitives.WriteUInt32BigEndian(
                    buffer.Slice(16, 4),
                    BinaryPrimitives.ReadUInt32BigEndian(address.AsSpan(8)) ^ BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(4))
                );

                BinaryPrimitives.WriteUInt32BigEndian(
                    buffer.Slice(20, 4),
                    BinaryPrimitives.ReadUInt32BigEndian(address.AsSpan(12)) ^ BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(8))
                );
            }

            return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + PaddedLength;
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

        private protected override void ValueToString(ref ValueStringBuilder sb) => base.ValueToString(ref sb);
    }
}
