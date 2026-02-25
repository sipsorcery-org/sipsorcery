//-----------------------------------------------------------------------------
// Filename: STUNErrorCodeAttribute.cs
//
// Description: Implements STUN error attribute as defined in RFC5389.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Feb 2016	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public partial class STUNConnectionIdAttribute : STUNAttribute
{
    public readonly uint ConnectionId;

    public STUNConnectionIdAttribute(ReadOnlyMemory<byte> attributeValue)
        : base(STUNAttributeTypesEnum.ConnectionId, attributeValue)
    {
        ConnectionId = BinaryPrimitives.ReadUInt32BigEndian(attributeValue.Span);
    }

    public STUNConnectionIdAttribute(uint connectionId)
        : base(STUNAttributeTypesEnum.ConnectionId, connectionId)
    {
        ConnectionId = connectionId;
    }

    private protected override void ValueToString(ref ValueStringBuilder sb)
    {
        sb.Append("connection ID=");
        sb.Append(ConnectionId);
    }
}
