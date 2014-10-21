//-----------------------------------------------------------------------------
// Filename: STUNv2Header.cs
//
// Description: Implements STUN header as defined in RFC5389.

//   All STUN messages MUST start with a 20-byte header followed by zero
//   or more Attributes.  The STUN header contains a STUN message type,
//   magic cookie, transaction ID, and message length.

//       0                   1                   2                   3
//       0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//      |0 0|     STUN Message Type     |         Message Length        |
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//      |                         Magic Cookie                          |
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//      |                                                               |
//      |                     Transaction ID (96 bits)                  |
//      |                                                               |
//      +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

//                  Figure 2: Format of STUN Message Header

//   The most significant 2 bits of every STUN message MUST be zeroes.
//   This can be used to differentiate STUN packets from other protocols
//   when STUN is multiplexed with other protocols on the same port.
//
// .....
// 
//   The message type field is decomposed further into the following
//   structure:

//                        0                 1
//                        2  3  4 5 6 7 8 9 0 1 2 3 4 5

//                       +--+--+-+-+-+-+-+-+-+-+-+-+-+-+
//                       |M |M |M|M|M|C|M|M|M|C|M|M|M|M|
//                       |11|10|9|8|7|1|6|5|4|0|3|2|1|0|
//                       +--+--+-+-+-+-+-+-+-+-+-+-+-+-+

//                Figure 3: Format of STUN Message Type Field

//   Here the bits in the message type field are shown as most significant
//   (M11) through least significant (M0).  M11 through M0 represent a 12-
//   bit encoding of the method.  C1 and C0 represent a 2-bit encoding of
//   the class.  A class of 0b00 is a request, a class of 0b01 is an
//   indication, a class of 0b10 is a success response, and a class of
//   0b11 is an error response.  This specification defines a single
//   method, Binding.  The method and class are orthogonal, so that for
//   each method, a request, success response, error response, and
//   indication are possible for that method.  Extensions defining new
//   methods MUST indicate which classes are permitted for that method.

//   For example, a Binding request has class=0b00 (request) and
//   method=0b000000000001 (Binding) and is encoded into the first 16 bits
//   as 0x0001.  A Binding response has class=0b10 (success response) and
//   method=0b000000000001, and is encoded into the first 16 bits as
//   0x0101.
//
// History:
// 26 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
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
using System.Net;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public enum STUNv2MessageTypesEnum : ushort
    {
        BindingRequest = 0x0001,
        BindingSuccessResponse = 0x0101,
        BindingErrorResponse = 0x0111
    }

    /// <summary>
    /// Could not work out how the class and message type encoding work despite re-reading the paragraph in the RFC a dozen times!
    /// </summary>
    public enum STUNv2ClassTypesEnum : ushort
    {
        Request = 0x0b00,
        Indication = 0x0b01,
        SuccesResponse = 0x0b10,
        ErrorResponse = 0x0b11,
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
        public const byte STUN_INITIAL_BYTE_MASK = 0xc0; // Mask to check that the first two bits of the packet are 00.
        public const int STUN_HEADER_LENGTH = 20;
        public const UInt32 MAGIC_COOKIE = 0x2112A442;
        public const int TRANSACTION_ID_LENGTH = 12;

        private static ILog logger = STUNAppState.logger;

        public STUNv2MessageTypesEnum MessageType = STUNv2MessageTypesEnum.BindingRequest;
        //public STUNv2ClassTypesEnum ClassType = STUNv2ClassTypesEnum.Request;
        public UInt16 MessageLength;
        public byte[] TransactionId = new byte[TRANSACTION_ID_LENGTH];

        public STUNv2Header()
        { }

        public STUNv2Header(STUNv2MessageTypesEnum messageType)
        {
            MessageType = messageType;
            //ClassType = classType;
            TransactionId = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0,TRANSACTION_ID_LENGTH));
        }

        public static STUNv2Header ParseSTUNHeader(byte[] buffer)
        {
            if ((buffer[0] & STUN_INITIAL_BYTE_MASK) != 0)
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
                Buffer.BlockCopy(buffer, 8, stunHeader.TransactionId, 0, TRANSACTION_ID_LENGTH);

                return stunHeader;
            }

            return null;
        }

        public string GetTransactionId()
        {
            return BitConverter.ToString(TransactionId);
        }
	}
}
