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
using System.Text;

namespace SIPSorcery.Net
{
    public class STUNConnectionIdAttribute : STUNAttribute
    {
        public readonly uint ConnectionId;

        public STUNConnectionIdAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.ConnectionId, attributeValue)
        {
            ConnectionId = BinaryPrimitives.ReadUInt32BigEndian(attributeValue);
        }

        private static byte[] GetBytes(uint value)
        {
            var buf = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, value);
            return buf;
        }

        public STUNConnectionIdAttribute(uint connectionId)
            : base(STUNAttributeTypesEnum.ConnectionId, GetBytes(connectionId))
        {
            ConnectionId = connectionId;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUN CONNECTION_ID Attribute: value=" + ConnectionId + ".";

            return attrDescrStr;
        }
    }
}
