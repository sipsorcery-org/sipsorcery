//-----------------------------------------------------------------------------
// Filename: STUNMessage.cs
//
// Description: Implements STUN Message as defined in RFC3489.
//
// Author(s):
// Aaron Clauson
//
// History:
// 27 Dec 2006	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class STUNMessage
    {
        private static ILogger logger = Log.Logger;

        public STUNHeader Header = new STUNHeader();
        public List<STUNAttribute> Attributes = new List<STUNAttribute>();

        public STUNMessage()
        { }

        public STUNMessage(STUNMessageTypesEnum stunMessageType)
        {
            Header = new STUNHeader(stunMessageType);
        }

        public static STUNMessage ParseSTUNMessage(byte[] buffer, int bufferLength)
        {
            if (buffer != null && buffer.Length > 0 && buffer.Length >= bufferLength)
            {
                STUNMessage stunMessage = new STUNMessage();
                stunMessage.Header = STUNHeader.ParseSTUNHeader(buffer);

                if (stunMessage.Header.MessageLength > 0)
                {
                    stunMessage.Attributes = STUNAttribute.ParseMessageAttributes(buffer, STUNHeader.STUN_HEADER_LENGTH, bufferLength);
                }

                return stunMessage;
            }

            return null;
        }

        public byte[] ToByteBuffer()
        {
            UInt16 attributesLength = 0;
            foreach (STUNAttribute attribute in Attributes)
            {
                attributesLength += Convert.ToUInt16(STUNAttribute.STUNATTRIBUTE_HEADER_LENGTH + attribute.Length);
            }

            int messageLength = STUNHeader.STUN_HEADER_LENGTH + attributesLength;
            byte[] buffer = new byte[messageLength];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian((UInt16)Header.MessageType)), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Utility.ReverseEndian(attributesLength)), 0, buffer, 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((UInt16)Header.MessageType), 0, buffer, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(attributesLength), 0, buffer, 2, 2);
            }

            Buffer.BlockCopy(Header.TransactionId, 0, buffer, 4, STUNHeader.TRANSACTION_ID_LENGTH);

            int attributeIndex = 20;
            foreach (STUNAttribute attr in Attributes)
            {
                attributeIndex += attr.ToByteBuffer(buffer, attributeIndex);
            }

            return buffer;
        }

        public new string ToString()
        {
            string messageDescr = "STUN Message: " + Header.MessageType.ToString() + ", length=" + Header.MessageLength;

            foreach (STUNAttribute attribute in Attributes)
            {
                messageDescr += "\n " + attribute.ToString();
            }

            return messageDescr;
        }

        public void AddUsernameAttribute(string username)
        {
            byte[] usernameBytes = Encoding.UTF8.GetBytes(username);
            Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Username, usernameBytes));
        }
    }
}
