//-----------------------------------------------------------------------------
// Filename: STUNv2ErrorCodeAttribute.cs
//
// Description: Implements STUN error attribute as defined in RFC5389.
// 
// History:
// 04 Feb 2016	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2016 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
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
using System.Text;

namespace SIPSorcery.Net
{
    public class STUNv2ErrorCodeAttribute : STUNv2Attribute
    {
        public byte ErrorClass;             // The hundreds value of the error code must be between 3 and 6.
        public byte ErrorNumber;            // The units value of the eror code must be between 0 and 99.
        public string ReasonPhrase;

        public int ErrorCode
        {
            get
            {
                return ErrorClass * 100 + ErrorNumber;
            }
        }

        public STUNv2ErrorCodeAttribute(byte[] attributeValue)
            : base(STUNv2AttributeTypesEnum.ErrorCode, attributeValue)
        {
            ErrorClass = (byte)BitConverter.ToChar(attributeValue, 2);
            ErrorNumber = (byte)BitConverter.ToChar(attributeValue, 3);
            ReasonPhrase = Encoding.UTF8.GetString(attributeValue, 4, attributeValue.Length - 4);
        }

        public STUNv2ErrorCodeAttribute(int errorCode, string reasonPhrase)
            : base(STUNv2AttributeTypesEnum.ErrorCode, null)
        {
            ErrorClass = errorCode < 700 ? Convert.ToByte(ErrorCode / 100) : (byte)0x00;
            ErrorNumber = Convert.ToByte(errorCode % 100);
            ReasonPhrase = reasonPhrase;
        }

        public override int ToByteBuffer(byte[] buffer, int startIndex)
        {
            buffer[startIndex] = 0x00;
            buffer[startIndex + 1] = 0x00;
            buffer[startIndex + 2] = ErrorClass;
            buffer[startIndex + 3] = ErrorNumber;

            byte[] reasonPhraseBytes = Encoding.UTF8.GetBytes(ReasonPhrase);
            Buffer.BlockCopy(reasonPhraseBytes, 0, buffer, startIndex + 4, reasonPhraseBytes.Length);

            return STUNv2Attribute.STUNATTRIBUTE_HEADER_LENGTH + 4 + reasonPhraseBytes.Length;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUNv2 ERROR_CODE_ADDRESS Attribute: error code=" + ErrorCode + ", reason phrase=" + ReasonPhrase + ".";

            return attrDescrStr;
        }
    }
}
