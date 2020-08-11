//-----------------------------------------------------------------------------
// Filename: STUNHeader.cs
//
// Description: Implements STUN header as defined in RFC5389
// https://tools.ietf.org/html/rfc5389.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Nov 2010	Aaron Clauson	Created, Hobart, Australia.
//
// Notes:
//
//   All STUN messages MUST start with a 20-byte header followed by zero
//   or more Attributes.  The STUN header contains a STUN message type,
//   magic cookie, transaction ID, and message length.
//
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
//
//                  Figure 2: Format of STUN Message Header
//
//   The most significant 2 bits of every STUN message MUST be zeroes.
//   This can be used to differentiate STUN packets from other protocols
//   when STUN is multiplexed with other protocols on the same port.
//
// .....
// 
//   The message type field is decomposed further into the following
//   structure:
//
//                        0                 1
//                        2  3  4 5 6 7 8 9 0 1 2 3 4 5
//
//                       +--+--+-+-+-+-+-+-+-+-+-+-+-+-+
//                       |M |M |M|M|M|C|M|M|M|C|M|M|M|M|
//                       |11|10|9|8|7|1|6|5|4|0|3|2|1|0|
//                       +--+--+-+-+-+-+-+-+-+-+-+-+-+-+
//
//                Figure 3: Format of STUN Message Type Field
//
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
//
//   For example, a Binding request has class=0b00 (request) and
//   method=0b000000000001 (Binding) and is encoded into the first 16 bits
//   as 0x0001.  A Binding response has class=0b10 (success response) and
//   method=0b000000000001, and is encoded into the first 16 bits as
//   0x0101.
//
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum STUNMessageTypesEnum : ushort
    {
        BindingRequest = 0x0001,
        BindingSuccessResponse = 0x0101,
        BindingErrorResponse = 0x0111,

        // New methods defined in TURN (RFC5766).
        Allocate = 0x0003,
        Refresh = 0x0004,
        Send = 0x0006,
        Data = 0x0007,
        CreatePermission = 0x0008,
        ChannelBind = 0x0009,

        SendIndication = 0x0016,
        DataIndication = 0x0017,

        AllocateSuccessResponse = 0x0103,
        RefreshSuccessResponse = 0x0104,
        CreatePermissionSuccessResponse = 0x0108,
        ChannelBindSuccessResponse = 0x0109,
        AllocateErrorResponse = 0x0113,
        RefreshErrorResponse = 0x0114,
        CreatePermissionErrorResponse = 0x0118,
        ChannelBindErrorResponse = 0x0119,
    }

    /// <summary>
    /// The class is interpreted from the message type. It does not get explicitly
    /// set in the STUN header.
    /// </summary>
    public enum STUNClassTypesEnum
    {
        Request = 0,
        Indication = 1,
        SuccessResponse = 2,
        ErrorResponse = 3,
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
        public const byte STUN_INITIAL_BYTE_MASK = 0xc0; // Mask to check that the first two bits of the packet are 00.
        public const ushort STUN_MESSAGE_CLASS_MASK = 0x0110;
        public const int STUN_HEADER_LENGTH = 20;
        public const UInt32 MAGIC_COOKIE = 0x2112A442;
        public const int TRANSACTION_ID_LENGTH = 12;

        public STUNMessageTypesEnum MessageType = STUNMessageTypesEnum.BindingRequest;
        public STUNClassTypesEnum MessageClass
        {
            get
            {
                int @class = ((ushort)MessageType >> 8 & 0x01) * 2 | ((ushort)MessageType >> 4 & 0x01);
                return (STUNClassTypesEnum)@class;
            }
        }

        public UInt16 MessageLength;
        public byte[] TransactionId = new byte[TRANSACTION_ID_LENGTH];

        public STUNHeader()
        { }

        public STUNHeader(STUNMessageTypesEnum messageType)
        {
            MessageType = messageType;
            TransactionId = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0, TRANSACTION_ID_LENGTH));
        }

        public static STUNHeader ParseSTUNHeader(byte[] buffer)
        {
            if ((buffer[0] & STUN_INITIAL_BYTE_MASK) != 0)
            {
                throw new ApplicationException("The STUN header did not begin with 0x00.");
            }

            if (buffer != null && buffer.Length > 0 && buffer.Length >= STUN_HEADER_LENGTH)
            {
                STUNHeader stunHeader = new STUNHeader();

                UInt16 stunTypeValue = BitConverter.ToUInt16(buffer, 0);
                UInt16 stunMessageLength = BitConverter.ToUInt16(buffer, 2);

                if (BitConverter.IsLittleEndian)
                {
                    stunTypeValue = NetConvert.DoReverseEndian(stunTypeValue);
                    stunMessageLength = NetConvert.DoReverseEndian(stunMessageLength);
                }

                stunHeader.MessageType = STUNMessageTypes.GetSTUNMessageTypeForId(stunTypeValue);
                stunHeader.MessageLength = stunMessageLength;
                Buffer.BlockCopy(buffer, 8, stunHeader.TransactionId, 0, TRANSACTION_ID_LENGTH);

                return stunHeader;
            }

            return null;
        }
    }
}
