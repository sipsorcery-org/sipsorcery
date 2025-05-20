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
using System.ComponentModel;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class STUNConnectionIdAttribute : STUNAttribute
    {
        public readonly uint ConnectionId;

        [Obsolete("Use STUNConnectionIdAttribute(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public STUNConnectionIdAttribute(byte[] attributeValue)
            : this((ReadOnlySpan<byte>)attributeValue)
        {
        }

        public STUNConnectionIdAttribute(ReadOnlySpan<byte> attributeValue)
            : base(STUNAttributeTypesEnum.ConnectionId, attributeValue)
        {
            ConnectionId = BinaryPrimitives.ReadUInt32BigEndian(attributeValue);
        }

        public STUNConnectionIdAttribute(uint connectionId)
            : base(STUNAttributeTypesEnum.ConnectionId, ReadOnlySpan<byte>.Empty)
        {
            ConnectionId = connectionId;

            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, connectionId);
            Value = bytes;
        }

        private protected override void ValueToString(ref ValueStringBuilder sb)
        {
            sb.Append("connection ID=");
            sb.Append(ConnectionId);
        }
    }
}
