// ============================================================================
// FileName: RecordNAPTR.cs
//
// Description:
// 
//
// Author(s):
// Alphons van der Heijden
//
// History:
// 28 Mar 2008	Aaron Clauson   Added to sipwitch code base.
// 14 Oct 2019  Aaron Clauson   Synchronised with latest version of source from at https://www.codeproject.com/Articles/23673/DNS-NET-Resolver-C.
//
// License:
// The Code Project Open License (CPOL) https://www.codeproject.com/info/cpol10.aspx
// ===========================================================================


#region RFC Specifications
/*
    NAPTR for ENUM TESTS !!!!
    RFC 2915 : 
    The DNS type code [1] for NAPTR is 35.
    
   Depending on the value of the
   flags field of the resource record, the resulting domain label or URI
   may be used in subsequent queries for the Naming Authority Pointer
   (NAPTR) resource records (to delegate the name lookup) or as the
   output of the entire process for which this system is used (a
   resolution server for URI resolution, a service URI for ENUM style
   e.164 number to URI mapping, etc).
  
    Points on the Flag field : RFC 2915, section 2 

      A <character-string> containing flags to control aspects of the
      rewriting and interpretation of the fields in the record.  Flags
      are single characters from the set [A-Z0-9].  The case of the
      alphabetic characters is not significant.

      At this time only four flags, "S", "A", "U", and "P", are
      defined.  The "S", "A" and "U" flags denote a terminal lookup.
      This means that this NAPTR record is the last one and that the
      flag determines what the next stage should be.  The "S" flag
      means that the next lookup should be for SRV records [4].  See
      Section 5 for additional information on how NAPTR uses the SRV
      record type.  "A" means that the next lookup should be for either
      an A, AAAA, or A6 record.  The "U" flag means that the next step
      is not a DNS lookup but that the output of the Regexp field is an
      URI that adheres to the 'absoluteURI' production found in the
      ABNF of RFC 2396 [9].
 
 */

/*
* http://www.faqs.org/rfcs/rfc2915.html
* 
8. DNS Packet Format

     The packet format for the NAPTR record is:

                                      1  1  1  1  1  1
        0  1  2  3  4  5  6  7  8  9  0  1  2  3  4  5
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
      |                     ORDER                     |
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
      |                   PREFERENCE                  |
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
      /                     FLAGS                     /
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
      /                   SERVICES                    /
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
      /                    REGEXP                     /
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+
      /                  REPLACEMENT                  /
      /                                               /
      +--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+--+

where:

FLAGS A <character-string> which contains various flags.

SERVICES A <character-string> which contains protocol and service
  identifiers.

REGEXP A <character-string> which contains a regular expression.

REPLACEMENT A <domain-name> which specifies the new value in the
  case where the regular expression is a simple replacement
  operation.

<character-string> and <domain-name> as used here are defined in
RFC1035 [1].

*/
#endregion

namespace Heijden.DNS
{
    public class RecordNAPTR : Record
    {
        public const string SIP_SERVICE_KEY = "E2U+SIP";
        public const string EMAIL_SERVICE_KEY = "E2U+EMAIL";
        public const string WEB_SERVICE_KEY = "E2U+WEB";

        public const char NAPTR_A_FLAG = 'A';
        public const char NAPTR_P_FLAG = 'P';
        public const char NAPTR_S_FLAG = 'S';
        public const char NAPTR_U_FLAG = 'U';

        public ushort ORDER;
        public ushort PREFERENCE;
        public string FLAGS;
        public string SERVICES;
        public string REGEXP;
        public string REPLACEMENT;

        public RecordNAPTR(RecordReader rr)
        {
            ORDER = rr.ReadUInt16();
            PREFERENCE = rr.ReadUInt16();
            FLAGS = rr.ReadString();
            SERVICES = rr.ReadString();
            REGEXP = rr.ReadString();
            REPLACEMENT = rr.ReadDomainName();
        }

        public override string ToString()
        {
            return string.Format("{0} {1} \"{2}\" \"{3}\" \"{4}\" {5}",
                ORDER,
                PREFERENCE,
                FLAGS,
                SERVICES,
                REGEXP,
                REPLACEMENT);
        }
    }
}
