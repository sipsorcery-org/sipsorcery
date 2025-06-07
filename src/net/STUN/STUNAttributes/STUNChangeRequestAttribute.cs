//-----------------------------------------------------------------------------
// Filename: STUNChangeRequestAttribute.cs
//
// Description: Implements STUN change request attribute as defined in RFC5389.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Nov 2010	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class STUNChangeRequestAttribute : STUNAttribute
    {
        public const UInt16 CHANGEREQUEST_ATTRIBUTE_LENGTH = 4;

        public bool ChangeAddress = false;
        public bool ChangePort = false;

        public override UInt16 PaddedLength
        {
            get { return CHANGEREQUEST_ATTRIBUTE_LENGTH; }
        }

        private byte m_changeRequestByte;

        public STUNChangeRequestAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.ChangeRequest, attributeValue)
        {
            m_changeRequestByte = attributeValue[3];

            if (m_changeRequestByte == 0x02)
            {
                ChangePort = true;
            }
            else if (m_changeRequestByte == 0x04)
            {
                ChangeAddress = true;
            }
            else if (m_changeRequestByte == 0x06)
            {
                ChangePort = true;
                ChangeAddress = true;
            }
        }

        private protected override void ValueToString(ref ValueStringBuilder sb)
        {
            sb.Append("key byte=");
            sb.Append(m_changeRequestByte, "X");
            sb.Append(", change address=");
            sb.Append(ChangeAddress);
            sb.Append(", change port=");
            sb.Append(ChangePort);
        }
    }
}
