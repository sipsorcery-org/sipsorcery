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
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <summary>
/// This attribute is the same as the mapped address attribute except the address details are XOR'ed with the STUN magic cookie. 
/// THe reason for this is to stop NAT application layer gateways from doing string replacements of private IP addresses and ports.
/// </summary>
public partial class STUNXORAddressAttribute : STUNAddressAttributeBase
{
    /// <summary>
    /// Parses an XOR-d (encoded) Address attribute with IPv4/IPv6 support.
    /// </summary>
    /// <param name="attributeType">of <see cref="STUNAttributeTypesEnum.XORMappedAddress"/>
    /// or <see cref="STUNAttributeTypesEnum.XORPeerAddress"/>
    /// or <see cref="STUNAttributeTypesEnum.XORRelayedAddress"/></param>
    /// <param name="attributeValue">the raw bytes</param>
    /// <param name="transactionId">the <see cref="STUNHeader.TransactionId"/></param>
    public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, ReadOnlyMemory<byte> attributeValue, ReadOnlySpan<byte> transactionId)
     : base(attributeType, attributeValue)
    {
        var attributeValueSpan = attributeValue.Span;
        Family = attributeValueSpan[1];
        AddressAttributeLength = Family == 1 ? ADDRESS_ATTRIBUTE_IPV4_LENGTH : ADDRESS_ATTRIBUTE_IPV6_LENGTH;
        TransactionId = transactionId.ToArray();

        var port = BinaryPrimitives.ReadUInt16BigEndian(attributeValueSpan.Slice(2));
        Port = (ushort)(port ^ (STUNHeader.MAGIC_COOKIE >> 16));

        if (Family == STUNAttributeConstants.IPv4AddressFamily[0])
        {
            var ipv4 = BinaryPrimitives.ReadUInt32BigEndian(attributeValueSpan.Slice(4)) ^ STUNHeader.MAGIC_COOKIE;
            Span<byte> addressBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(addressBytes, ipv4);
            Address = new IPAddress(MemoryMarshal.Read<uint>(addressBytes));
        }
        else if (Family == STUNAttributeConstants.IPv6AddressFamily[0] && TransactionId is { })
        {
            Span<byte> addressBytes = stackalloc byte[16];

            var part0 = BinaryPrimitives.ReadUInt32BigEndian(attributeValueSpan.Slice(4));
            var tid0 = BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(0));
            BinaryPrimitives.WriteUInt32BigEndian(addressBytes.Slice(0), part0 ^ tid0);

            var part1 = BinaryPrimitives.ReadUInt32BigEndian(attributeValueSpan.Slice(8));
            var tid1 = BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(addressBytes.Slice(4), part1 ^ tid1);

            var part2 = BinaryPrimitives.ReadUInt32BigEndian(attributeValueSpan.Slice(12));
            var tid2 = BinaryPrimitives.ReadUInt32BigEndian(TransactionId.AsSpan(8));
            BinaryPrimitives.WriteUInt32BigEndian(addressBytes.Slice(8), part2 ^ tid2);

            var lastPart = BinaryPrimitives.ReadUInt32BigEndian(attributeValueSpan.Slice(4 + 12));
            BinaryPrimitives.WriteUInt32LittleEndian(addressBytes.Slice(12), lastPart ^ STUNHeader.MAGIC_COOKIE);

            Address = IPAddress.Create(addressBytes);
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
    public STUNXORAddressAttribute(STUNAttributeTypesEnum attributeType, int port, IPAddress address, byte[]? transactionId)
        : base(attributeType, null)
    {
        Port = port;
        Address = address;
        Family = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 1 : 2;
        AddressAttributeLength = Family == 1 ? ADDRESS_ATTRIBUTE_IPV4_LENGTH : ADDRESS_ATTRIBUTE_IPV6_LENGTH;
        TransactionId = transactionId;
    }

    /// <inheritdoc/>
    public override int GetByteCount() => STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + AddressAttributeLength;

    /// <inheritdoc/>
    public override int WriteBytes(Span<byte> buffer)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(0, 2), (ushort)base.AttributeType);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2, 2), AddressAttributeLength);

        buffer[4] = 0x00;
        buffer[5] = (byte)Family;

        Debug.Assert(Address is { });
        var address = Address.GetAddressBytes();

        var xorPort = (ushort)(Port ^ (STUNHeader.MAGIC_COOKIE >> 16));
        var xorAddress = BinaryPrimitives.ReadUInt32BigEndian(address) ^ STUNHeader.MAGIC_COOKIE;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(6, 2), xorPort);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8, 4), xorAddress);

        if (Family == STUNAttributeConstants.IPv6AddressFamily[0] && TransactionId is { })
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

    public IPEndPoint? GetIPEndPoint()
    {
        if (Address is { })
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
