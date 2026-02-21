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
using System.Text;

namespace SIPSorcery.Net
{
    public class STUNErrorCodeAttribute : STUNAttribute
    {
        public byte ErrorClass;             // The hundreds value of the error code must be between 3 and 6.
        public byte ErrorNumber;            // The units value of the error code must be between 0 and 99.
        public string ReasonPhrase;

        public int ErrorCode
        {
            get
            {
                return ErrorClass * 100 + ErrorNumber;
            }
        }

        public STUNErrorCodeAttribute(byte[] attributeValue)
            : base(STUNAttributeTypesEnum.ErrorCode, attributeValue)
        {
            ErrorClass = attributeValue[2];
            ErrorNumber = attributeValue[3];
            ReasonPhrase = Encoding.UTF8.GetString(attributeValue, 4, attributeValue.Length - 4);
        }

        public STUNErrorCodeAttribute(int errorCode, string reasonPhrase)
            : base(STUNAttributeTypesEnum.ErrorCode, BuildValue(errorCode, reasonPhrase))
        {
            ErrorClass = (byte)(errorCode / 100);
            ErrorNumber = (byte)(errorCode % 100);
            ReasonPhrase = reasonPhrase;
        }

        private static byte[] BuildValue(int errorCode, string reasonPhrase)
        {
            byte[] reasonBytes = Encoding.UTF8.GetBytes(reasonPhrase ?? string.Empty);
            byte[] value = new byte[4 + reasonBytes.Length];
            value[2] = (byte)(errorCode / 100);
            value[3] = (byte)(errorCode % 100);
            Buffer.BlockCopy(reasonBytes, 0, value, 4, reasonBytes.Length);
            return value;
        }

        public override string ToString()
        {
            string attrDescrStr = "STUN ERROR_CODE_ADDRESS Attribute: error code=" + ErrorCode + ", reason phrase=" + ReasonPhrase + ".";

            return attrDescrStr;
        }
    }
}
