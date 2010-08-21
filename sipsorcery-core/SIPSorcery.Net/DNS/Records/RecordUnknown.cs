// ============================================================================
// FileName: RecordUnknown.cs
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

namespace Heijden.DNS
{
	public class RecordUnknown : Record
	{
		public RecordUnknown(RecordReader rr)
		{
			rr.Position -=2;
			// re-read length
			ushort RDLENGTH = rr.ReadShort();
			// skip bytes
			rr.ReadBytes(RDLENGTH);
		}
	}
}
