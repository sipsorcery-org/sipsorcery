// ============================================================================
// FileName: RR.cs
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

using System;
using System.Net;

namespace Heijden.DNS
{
    #region RFC info
    /*
	3.2. RR definitions

	3.2.1. Format

	All RRs have the same top level format shown below:

										1  1  1  1  1  1
		  0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
		|                                               |
		/                                               /
		/                      NAME                     /
		|                                               |
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
		|                      TYPE                     |
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
		|                     CLASS                     |
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
		|                      TTL                      |
		|                                               |
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
		|                   RDLENGTH                    |
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--|
		/                     RDATA                     /
		/                                               /
		+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+


	where:

	NAME            an owner name, i.e., the name of the node to which this
					resource record pertains.

	TYPE            two octets containing one of the RR TYPE codes.

	CLASS           two octets containing one of the RR CLASS codes.

	TTL             a 32 bit signed integer that specifies the time interval
					that the resource record may be cached before the source
					of the information should again be consulted.  Zero
					values are interpreted to mean that the RR can only be
					used for the transaction in progress, and should not be
					cached.  For example, SOA records are always distributed
					with a zero TTL to prohibit caching.  Zero values can
					also be used for extremely volatile data.

	RDLENGTH        an unsigned 16 bit integer that specifies the length in
					octets of the RDATA field.

	RDATA           a variable length string of octets that describes the
					resource.  The format of this information varies
					according to the TYPE and CLASS of the resource record.
	*/
    #endregion

    /// <summary>
    /// Resource Record (rfc1034 3.6.)
    /// </summary>
    public class RR
    {
        /// <summary>
        /// The name of the node to which this resource record pertains
        /// </summary>
        public string NAME;

        /// <summary>
        /// Specifies type of resource record
        /// </summary>
        public DnsType Type;

        /// <summary>
        /// Specifies type class of resource record, mostly IN but can be CS, CH or HS 
        /// </summary>
        public Class Class;

        /// <summary>
        /// Time to live, the time interval that the resource record may be cached
        /// </summary>
        public uint TTL
        {
            get
            {
                return (uint)Math.Max(0, m_TTL - TimeLived);
            }
            set
            {
                m_TTL = value;
            }
        }
        private uint m_TTL;

        /// <summary>
        /// 
        /// </summary>
        public ushort RDLENGTH;

        /// <summary>
        /// One of the Record* classes
        /// </summary>
        public Record RECORD;

        public int TimeLived;

        public RR(IPAddress address)
        {
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                RECORD = new RecordAAAA(address);
            }
            else
            {
                RECORD = new RecordA(address);
            }
        }

        public RR(RecordReader rr)
        {
            TimeLived = 0;
            NAME = rr.ReadDomainName();
            Type = (DnsType)rr.ReadUInt16();
            Class = (Class)rr.ReadUInt16();
            TTL = rr.ReadUInt32();
            RDLENGTH = rr.ReadUInt16();
            RECORD = rr.ReadRecord(Type, RDLENGTH);
            RECORD.RR = this;
        }

        public override string ToString()
        {
            return string.Format("{0,-32} {1}\t{2}\t{3}\t{4}",
                NAME,
                TTL,
                Class,
                Type,
                RECORD);
        }
    }

    public class AnswerRR : RR
    {
        public AnswerRR(RecordReader br)
            : base(br)
        {
        }

        public AnswerRR(IPAddress address)
            : base(address)
        { }
    }

    public class AuthorityRR : RR
    {
        public AuthorityRR(RecordReader br)
            : base(br)
        {
        }
    }

    public class AdditionalRR : RR
    {
        public AdditionalRR(RecordReader br)
            : base(br)
        {
        }
    }
}

