//-----------------------------------------------------------------------------
// Filename: STUNv2ChangeRequestAttribute.cs
//
// Description: Implements STUN change request attribute as defined in RFC5389.
//
// History:
// 26 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using log4net;

namespace SIPSorcery.Net
{
    public class STUNv2ChangeRequestAttribute : STUNv2Attribute
    {
        public const UInt16 CHANGEREQUEST_ATTRIBUTE_LENGTH = 4;
        
        public bool ChangeAddress = false;
        public bool ChangePort = false;

        public override UInt16 PaddedLength
        {
            get { return CHANGEREQUEST_ATTRIBUTE_LENGTH; }
        }

        private byte m_changeRequestByte;

        public STUNv2ChangeRequestAttribute(byte[] attributeValue)
            : base(STUNv2AttributeTypesEnum.ChangeRequest, attributeValue)
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

        public override string ToString()
        {
            string attrDescrStr = "STUNv2 Attribute: " + STUNAttributeTypesEnum.ChangeRequest.ToString() + ", key byte=" + m_changeRequestByte.ToString("X") + ", change address=" + ChangeAddress + ", change port=" + ChangePort + ".";

            return attrDescrStr;
        }
    }
}
