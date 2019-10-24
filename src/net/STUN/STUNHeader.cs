//-----------------------------------------------------------------------------
// Filename: STUNHeader.cs
//
// Description: Implements STUN header as defined in RFC3489.
//
// Author(s):
// Aaron Clauson
//
// History:
// 27 Dec 2006	Aaron Clauson	Created  (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
// 04 Jan 2007  Aaron Clauson   Added Send Keep Alive request message type.
//
// Notes:
//
// 11.1  Message Header
//
// All STUN messages consist of a 20 byte header:
//
//  0                   1                   2                   3
//  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |      STUN Message Type        |         Message Length        |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//                          Transaction ID
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//                                                                 |
// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// The Message Types can take on the following values:
//
//    0x0001  :  Binding Request
//    0x0101  :  Binding Response
//    0x0111  :  Binding Error Response
//    0x0112  :  Send Keep Alive Request.   (Extension)
//    0x0002  :  Shared Secret Request
//    0x0102  :  Shared Secret Response
//    0x0112  :  Shared Secret Error Response
//
// The message length is the count, in bytes, of the size of the
// message, not including the 20 byte header.
//
// The transaction ID is a 128 bit identifier.  It also serves as salt
// to randomize the request and the response.  All responses carry the
// same identifier as the request they correspond to.
//
// Adding custom request to allow server agents to request NAT Keep Alives be sent to clients.
//
// Message Type:
//    0x0112 : Send Keep Alive Request.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    public enum STUNMessageTypesEnum
    {
        Unknown = 0,
        BindingRequest = 1,
        SharedSecretRequest = 2,
        BindingResponse = 257,
        SendKeepAlive = 258,
        SharedSecretResponse = 258,
        BindingErrorResponse = 272,
        SharedSecretErrorResponse = 273,
    }

    public class STUNMessageTypes
    {
        public static STUNMessageTypesEnum GetSTUNMessageTypeForId(int stunMessageTypeId)
        {
            return (STUNMessageTypesEnum)Enum.Parse(typeof(STUNMessageTypesEnum), stunMessageTypeId.ToString(), true);
        }
    }

    public class STUNHeader
	{
        public const int STUN_HEADER_LENGTH = 20;
        public const int TRANSACTION_ID_LENGTH = 16;

        private static ILogger logger = Log.Logger;

        public STUNMessageTypesEnum MessageType = STUNMessageTypesEnum.Unknown;
        public UInt16 MessageLength;
        public byte[] TransactionId = new byte[TRANSACTION_ID_LENGTH];

        public STUNHeader()
        { }

        public STUNHeader(STUNMessageTypesEnum messageType)
        {
            MessageType = messageType;
            TransactionId = GenerateNewTransactionID();
        }

        public static STUNHeader ParseSTUNHeader(byte[] buffer)
        {
            if (buffer != null && buffer.Length > 0 && buffer.Length >= STUN_HEADER_LENGTH)
            {
                STUNHeader stunHeader = new STUNHeader();

                UInt16 stunTypeValue = BitConverter.ToUInt16(buffer, 0);
                UInt16 stunMessageLength = BitConverter.ToUInt16(buffer, 2);

                if (BitConverter.IsLittleEndian)
                {
                    stunTypeValue = Utility.ReverseEndian(stunTypeValue);
                    stunMessageLength = Utility.ReverseEndian(stunMessageLength);
                }

                stunHeader.MessageType = STUNMessageTypes.GetSTUNMessageTypeForId(stunTypeValue);
                stunHeader.MessageLength = stunMessageLength;
                //stunHeader.TransactionId = BitConverter.ToString(buffer, 4, TRANSACTION_ID_LENGTH);
                Buffer.BlockCopy(buffer, 4, stunHeader.TransactionId, 0, TRANSACTION_ID_LENGTH);

                return stunHeader;
            }

            return null;
        }

        public static byte[] GenerateNewTransactionID()
        {
            return Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0, TRANSACTION_ID_LENGTH));
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
