using System;
using System.Collections.Generic;
using System.Text;

namespace SIPSorcery.Net.Sctp
{
    public static class Utils
    {
        // Serial Number Arithmetic (RFC 1982)
        public static bool sna32LT(uint i1, uint i2)
        {
            return i1 < i2;
            //return (i1 < i2 && i2 - i1 < 1 << 31) || (i1 > i2 && i1 - i2 > 1 << 31);
        }

        public static bool sna32LTE(uint i1, uint i2)
        {
            return i1 <= i2;
            //return i1 == i2 || sna32LT(i1, i2);
        }

        public static bool sna32GT(uint i1, uint i2)
        {
            return i1 > i2;
            ///return (i1 < i2 && (i2 - i1) >= 1 << 31) || (i1 > i2 && (i1 - i2) <= 1 << 31);
        }

        public static bool sna32GTE(uint i1, uint i2)
        {
            return i1 >= i2;
            //return i1 == i2 || sna32GT(i1, i2);
        }

        internal static bool sna16LT(int streamSequenceNumber, ushort nextSSN)
        {
            return streamSequenceNumber < nextSSN;
        }

        internal static bool sna16LTE(ushort ssn, ushort nextSSN)
        {
            return ssn <= nextSSN;
        }

        internal static bool sna16GT(ushort ssn, ushort nextSSN)
        {
            return ssn > nextSSN;
        }
    }
}
