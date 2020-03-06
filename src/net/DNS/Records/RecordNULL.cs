// ============================================================================
// FileName: RecordNULL.cs
//
// Description:
// 
//
// Author(s):
// Alphons van der Heijden
//
// History:
// 28 Mar 2008	Aaron Clauson   Added to sipswitch code base based on http://www.codeproject.com/KB/library/DNS.NET_Resolver.aspx.
// 14 Oct 2019  Aaron Clauson   Synchronised with latest version of source from at https://www.codeproject.com/Articles/23673/DNS-NET-Resolver-C.
//
// License:
// The Code Project Open License (CPOL) https://www.codeproject.com/info/cpol10.aspx
// ===========================================================================

/*
3.3.10. NULL RDATA format (EXPERIMENTAL)

    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
    /                  <anything>                   /
    /                                               /
    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

Anything at all may be in the RDATA field so long as it is 65535 octets
or less.

NULL records cause no additional section processing.  NULL RRs are not
allowed in master files.  NULLs are used as placeholders in some
experimental extensions of the DNS.
*/
namespace Heijden.DNS
{
    public class RecordNULL : Record
    {
        public byte[] ANYTHING;

        public RecordNULL(RecordReader rr)
        {
            rr.Position -= 2;
            // re-read length
            ushort RDLENGTH = rr.ReadUInt16();
            ANYTHING = new byte[RDLENGTH];
            ANYTHING = rr.ReadBytes(RDLENGTH);
        }

        public override string ToString()
        {
            return string.Format("...binary data... ({0}) bytes", ANYTHING.Length);
        }

    }
}

