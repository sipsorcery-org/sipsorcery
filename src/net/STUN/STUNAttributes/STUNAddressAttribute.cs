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
using System.Diagnostics;
using System.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <remarks>
/// There's no proper explanation of why this STUN attribute was obsoleted. My guess is to favour using the XOR Maoped Address attribute
/// BUT that does not help when a STUN server provides this atype of address attribute. It will still need to be parsed and understood,
/// Reverted this obsoletion on 13 Nov 2024 AC. 
/// </remarks>
//[Obsolete("Provided for backward compatibility with RFC3489 clients.")]
public partial class STUNAddressAttribute : STUNAddressAttributeBase
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
    public STUNAddressAttribute(ReadOnlyMemory<byte> attributeValue)
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
    public STUNAddressAttribute(STUNAttributeTypesEnum attributeType, ReadOnlyMemory<byte> attributeValue)
        : base(attributeType, attributeValue)
    {
        Port = BinaryPrimitives.ReadUInt16BigEndian(attributeValue.Span.Slice(2, 2));

        Address = IPAddress.Create(attributeValue.Span.Slice(4, 4));
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

        //base.AttributeType = attributeType;
        //base.Length = ADDRESS_ATTRIBUTE_LENGTH;
    }

    /// <inheritdoc/>
    public override int GetByteCount() => STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_IPV4_LENGTH;

    /// <inheritdoc/>
    public override int WriteBytes(Span<byte> buffer)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(0, 2), (ushort)base.AttributeType);

        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(2, 2), ADDRESS_ATTRIBUTE_IPV4_LENGTH);

        buffer[5] = (byte)Family;

        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(6, 2), (ushort)Port);

        Debug.Assert(Address is { });
        Address.GetAddressBytes().CopyTo(buffer.Slice(8, 4));

        return STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + ADDRESS_ATTRIBUTE_IPV4_LENGTH;
    }

    private protected override void ValueToString(ref ValueStringBuilder sb)
    {
        sb.Append("Address=");
        Debug.Assert(Address is { });
        sb.Append(Address);
        sb.Append(", Port=");
        sb.Append(Port);
    }
}
