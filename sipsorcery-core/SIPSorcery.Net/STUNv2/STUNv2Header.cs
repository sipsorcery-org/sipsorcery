//-----------------------------------------------------------------------------
// Filename: STUNv2Header.cs
//
// Description: Implements STUN header as defined in RFC5389.
//
// History:
// 26 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using System.Net;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public enum STUNv2MessageTypesEnum : ushort
    {
        BindingRequest = 0x001,
    }

    public class STUNv2MessageTypes
    {
        public static STUNv2MessageTypesEnum GetSTUNMessageTypeForId(int stunMessageTypeId)
        {
            return (STUNv2MessageTypesEnum)Enum.Parse(typeof(STUNv2MessageTypesEnum), stunMessageTypeId.ToString(), true);
        }
    }

    public class STUNv2Header
	{
        public const byte STUN_INITIAL_BYTE = 0x00;
        public const int STUN_HEADER_LENGTH = 20;
        public const int MAGIC_COOKIE = 0x2112A442;
        public const int TRANSACTION_ID_LENGTH = 12;

        private static ILog logger = STUNAppState.logger;

        public STUNv2MessageTypesEnum MessageType = STUNv2MessageTypesEnum.BindingRequest;
        public UInt16 MessageLength;
        public byte[] TransactionId = new byte[TRANSACTION_ID_LENGTH];

        public STUNv2Header()
        { }

        public STUNv2Header(STUNv2MessageTypesEnum messageType)
        {
            MessageType = messageType;
            TransactionId = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0,TRANSACTION_ID_LENGTH));
        }

        public static STUNv2Header ParseSTUNHeader(byte[] buffer)
        {
            if (buffer[0] != STUN_INITIAL_BYTE)
            {
                throw new ApplicationException("The STUNv2 header did not begin with 0x00.");
            }

            if (buffer != null && buffer.Length > 0 && buffer.Length >= STUN_HEADER_LENGTH)
            {
                STUNv2Header stunHeader = new STUNv2Header();

                UInt16 stunTypeValue = BitConverter.ToUInt16(buffer, 0);
                UInt16 stunMessageLength = BitConverter.ToUInt16(buffer, 2);

                if (BitConverter.IsLittleEndian)
                {
                    stunTypeValue = Utility.ReverseEndian(stunTypeValue);
                    stunMessageLength = Utility.ReverseEndian(stunMessageLength);
                }

                stunHeader.MessageType = STUNv2MessageTypes.GetSTUNMessageTypeForId(stunTypeValue);
                stunHeader.MessageLength = stunMessageLength;
                Buffer.BlockCopy(buffer, 4, stunHeader.TransactionId, 0, TRANSACTION_ID_LENGTH);

                return stunHeader;
            }

            return null;
        }

        public string GetTransactionId()
        {
            string transId = null;
            foreach (byte transByte in TransactionId)
            {
                string byteStr = transByte.ToString("X");

                if (byteStr.Length < 2)
                {
                    byteStr = "0" + byteStr;
                }

                transId += byteStr;
            }

            return transId;
        }
	}
}
