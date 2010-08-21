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
// 28 Mar 2008	Aaron Clauson   Added to sipwitch code base based on http://www.codeproject.com/KB/library/DNS.NET_Resolver.aspx.
//
// License:
// http://www.opensource.org/licenses/gpl-license.php
// ===========================================================================

using System;
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
			ushort RDLENGTH = rr.ReadShort();
			ANYTHING = new byte[RDLENGTH];
			ANYTHING = rr.ReadBytes(RDLENGTH);
		}

		public override string ToString()
		{
			return string.Format("...binary data... ({0}) bytes",ANYTHING.Length);
		}

	}
}
