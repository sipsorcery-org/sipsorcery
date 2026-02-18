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

using SIPSorcery.Sys;
using System;
using System.Text;

namespace SIPSorcery.Net
{
    public class STUNConnectionIdAttribute : STUNAttribute
    {
        public readonly uint ConnectionId;

        public STUNConnectionIdAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.ConnectionId, attributeValue)
        {
            if (BitConverter.IsLittleEndian)
            {
                ConnectionId = NetConvert.DoReverseEndian(BitConverter.ToUInt32(attributeValue, 0));
            }
            else
            {
                ConnectionId = BitConverter.ToUInt32(attributeValue, 0);
            }
        }

        public STUNConnectionIdAttribute(uint connectionId)
            : base(STUNAttributeTypesEnum.ConnectionId, 
                  BitConverter.IsLittleEndian?
                  BitConverter.GetBytes(NetConvert.DoReverseEndian(connectionId)) : 
                  BitConverter.GetBytes(connectionId))
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
