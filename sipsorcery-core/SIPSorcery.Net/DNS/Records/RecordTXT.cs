// ============================================================================
// FileName: RecordTXT.cs
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

#region Rfc info
/*
3.3.14. TXT RDATA format

    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
    /                   TXT-DATA                    /
    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

where:

TXT-DATA        One or more <character-string>s.

TXT RRs are used to hold descriptive text.  The semantics of the text
depends on the domain where it is found.
 * 
*/
#endregion

namespace Heijden.DNS
{
	public class RecordTXT : Record
	{
		public string TXT;

		public RecordTXT(RecordReader rr)
		{
			TXT = rr.ReadString();
		}

		public override string ToString()
		{
			return string.Format("\"{0}\"",TXT);
		}

	}
}
