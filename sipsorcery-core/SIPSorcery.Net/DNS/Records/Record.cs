// ============================================================================
// FileName: Record.cs
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
// ============================================================================

// Stuff records are made of

namespace Heijden.DNS
{
	public abstract class Record
	{
		/// <summary>
		/// The Resource Record this RDATA record belongs to
		/// </summary>
		public RR RR;
	}
}
