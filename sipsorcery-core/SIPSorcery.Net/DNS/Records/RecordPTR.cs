// ============================================================================
// FileName: RecordPTR.cs
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
 3.3.12. PTR RDATA format

    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
    /                   PTRDNAME                    /
    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

where:

PTRDNAME        A <domain-name> which points to some location in the
                domain name space.

PTR records cause no additional section processing.  These RRs are used
in special domains to point to some other location in the domain space.
These records are simple data, and don't imply any special processing
similar to that performed by CNAME, which identifies aliases.  See the
description of the IN-ADDR.ARPA domain for an example.
 */

namespace Heijden.DNS
{
	public class RecordPTR : Record
	{
		public string PTRDNAME;

		public RecordPTR(RecordReader rr)
		{
			PTRDNAME = rr.ReadDomainName();
		}

		public override string ToString()
		{
			return PTRDNAME;
		}

	}
}
