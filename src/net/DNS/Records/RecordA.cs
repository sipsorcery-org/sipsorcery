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
// 14 Oct 2019  Aaron Clauson   Synchronised with latest version of source from at https://www.codeproject.com/Articles/23673/DNS-NET-Resolver-C.
//
// License:
// The Code Project Open License (CPOL) https://www.codeproject.com/info/cpol10.aspx
// ============================================================================

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
        public System.Net.IPAddress Address;

        public RecordA(RecordReader rr)
        {
            Address = new System.Net.IPAddress(rr.ReadBytes(4));
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
