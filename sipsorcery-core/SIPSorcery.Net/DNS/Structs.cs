// ============================================================================
// FileName: Structs.cs
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

namespace Heijden.DNS
{
	/*
	 * 3.2.2. TYPE values
	 *
	 * TYPE fields are used in resource records.
	 * Note that these types are a subset of QTYPEs.
	 *
	 *		TYPE		value			meaning
	 */
	public enum DNSType : ushort
	{
		A = 1,				// a IPV4 host address
		NS = 2,				// an authoritative name server
		MD = 3,				// a mail destination (Obsolete - use MX)
		MF = 4,				// a mail forwarder (Obsolete - use MX)
		CNAME = 5,			// the canonical name for an alias
		SOA = 6,			// marks the start of a zone of authority
		MB = 7,				// a mailbox domain name (EXPERIMENTAL)
		MG = 8,				// a mail group member (EXPERIMENTAL)
		MR = 9,				// a mail rename domain name (EXPERIMENTAL)
		NULL = 10,			// a null RR (EXPERIMENTAL)
		WKS = 11,			// a well known service description
		PTR = 12,			// a domain name pointer
		HINFO = 13,			// host information
		MINFO = 14,			// mailbox or mail list information
		MX = 15,			// mail exchange
		TXT = 16,			// text strings
        AAAA = 28,			// a IPV6 host address
        SRV = 33,           // Service records.
        NAPTR = 35,         // NAPTR for ENUM lookups
	}

	/*
	 * 3.2.3. QTYPE values
	 *
	 * QTYPE fields appear in the question part of a query.  QTYPES are a
	 * superset of TYPEs, hence all TYPEs are valid QTYPEs.  In addition, the
	 * following QTYPEs are defined:
	 *
	 *		QTYPE		value			meaning
	 */
    public enum DNSQType : ushort
	{
        A = DNSType.A,			// a IPV4 host address
        NS = DNSType.NS,		// an authoritative name server
        MD = DNSType.MD,		// a mail destination (Obsolete - use MX)
        MF = DNSType.MF,		// a mail forwarder (Obsolete - use MX)
        CNAME = DNSType.CNAME,	// the canonical name for an alias
        SOA = DNSType.SOA,		// marks the start of a zone of authority
        MB = DNSType.MB,		// a mailbox domain name (EXPERIMENTAL)
        MG = DNSType.MG,		// a mail group member (EXPERIMENTAL)
        MR = DNSType.MR,		// a mail rename domain name (EXPERIMENTAL)
        NULL = DNSType.NULL,	// a null RR (EXPERIMENTAL)
        WKS = DNSType.WKS,		// a well known service description
        PTR = DNSType.PTR,		// a domain name pointer
        HINFO = DNSType.HINFO,	// host information
        MINFO = DNSType.MINFO,	// mailbox or mail list information
        MX = DNSType.MX,		// mail exchange
        TXT = DNSType.TXT,		// text strings
        SRV = DNSType.SRV,
        NAPTR = DNSType.NAPTR, // for ENUM Lookups

        AAAA = DNSType.AAAA,	// a IPV6 host address

		AXFR = 252,			// A request for a transfer of an entire zone
		MAILB = 253,		// A request for mailbox-related records (MB, MG or MR)
		MAILA = 254,		// A request for mail agent RRs (Obsolete - see MX)
		ANY = 255			// A request for all records
	}
	/*
	 * 3.2.4. CLASS values
	 *
	 * CLASS fields appear in resource records.  The following CLASS mnemonics
	 *and values are defined:
	 *
	 *		CLASS		value			meaning
	 */
	public enum Class : ushort
	{
		IN = 1,				// the Internet
		CS = 2,				// the CSNET class (Obsolete - used only for examples in some obsolete RFCs)
		CH = 3,				// the CHAOS class
		HS = 4				// Hesiod [Dyer 87]
	}
	/*
	 * 3.2.5. QCLASS values
	 *
	 * QCLASS fields appear in the question section of a query.  QCLASS values
	 * are a superset of CLASS values; every CLASS is a valid QCLASS.  In
	 * addition to CLASS values, the following QCLASSes are defined:
	 *
	 *		QCLASS		value			meaning
	 */
	public enum QClass : ushort
	{
		IN = Class.IN,		// the Internet
		CS = Class.CS,		// the CSNET class (Obsolete - used only for examples in some obsolete RFCs)
		CH = Class.CH,		// the CHAOS class
		HS = Class.HS,		// Hesiod [Dyer 87]

		ANY = 255			// any class
	}

	/*
RCODE           Response code - this 4 bit field is set as part of
                responses.  The values have the following
                interpretation:

                0               No error condition

                1               Format error - The name server was
                                unable to interpret the query.

                2               Server failure - The name server was
                                unable to process this query due to a
                                problem with the name server.

                3               Name Error - Meaningful only for
                                responses from an authoritative name
                                server, this code signifies that the
                                domain name referenced in the query does
                                not exist.

                4               Not Implemented - The name server does
                                not support the requested kind of query.

                5               Refused - The name server refuses to
                                perform the specified operation for
                                policy reasons.  For example, a name
                                server may not wish to provide the
                                information to the particular requester,
                                or a name server may not wish to perform
                                a particular operation (e.g., zone
                                transfer) for particular data.

                6-15            Reserved for future use.
	 */
	public enum RCode
	{
		NOERROR = 0,			// No error condition
		FORMATERROR = 1,		// Format error
		SERVERFAILURE = 2,		// Server failure
		NAMERROR = 3,			// Name Error
		NOTIMPLEMENTED = 4,		// Not Implemented
		REFUSED = 5,			// Refused

		RESERVED6 = 6,			// Reserved
		RESERVED7 = 7,			// Reserved
		RESERVED8 = 8,			// Reserved
		RESERVED9 = 9,			// Reserved
		RESERVED10 = 10,		// Reserved
		RESERVED11 = 11,		// Reserved
		RESERVED12 = 12,		// Reserved
		RESERVED13 = 13,		// Reserved
		RESERVED14 = 14,		// Reserved
		RESERVED15 = 15,		// Reserved
	}

	/*
OPCODE          A four bit field that specifies kind of query in this
                message.  This value is set by the originator of a query
                and copied into the response.  The values are:

                0               a standard query (QUERY)

                1               an inverse query (IQUERY)

                2               a server status request (STATUS)

                3-15            reserved for future use
	 */
	public enum OPCode
	{
		QUERY = 0,				// a standard query (QUERY)
		IQUERY = 1,				// an inverse query (IQUERY)
		STATUS = 2,				// a server status request (STATUS)
		RESERVED3 = 3,
		RESERVED4 = 4,
		RESERVED5 = 5,
		RESERVED6 = 6,
		RESERVED7 = 7,
		RESERVED8 = 8,
		RESERVED9 = 9,
		RESERVED10 = 10,
		RESERVED11 = 11,
		RESERVED12 = 12,
		RESERVED13 = 13,
		RESERVED14 = 14,
		RESERVED15 = 15,
	}

	public enum TransportType
	{
		Udp,
		Tcp
	}
}
