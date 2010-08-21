// ============================================================================
// FileName: RecordA.cs
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

using System;
using System.Net;

/*
 3.4.1. A RDATA format

    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
    |                    ADDRESS                    |
    +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

where:

ADDRESS         A 32 bit Internet address.

Hosts that have multiple Internet addresses will have multiple A
records.
 * 
 */
namespace Heijden.DNS
{
	public class RecordA : Record
	{
		public IPAddress Address;

		public RecordA(RecordReader rr)
		{
			System.Net.IPAddress.TryParse(string.Format("{0}.{1}.{2}.{3}",
				rr.ReadByte(),
				rr.ReadByte(),
				rr.ReadByte(),
				rr.ReadByte()), out this.Address);
		}

        public RecordA(IPAddress address)
        {
            Address = address;
        }

		public override string ToString()
		{
			return Address.ToString();
		}
	}
}
