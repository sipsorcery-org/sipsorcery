using System;
using System.Net;

namespace SIPSorcery.Sys
{
    public class DNSManager
    {
        public static IPAddress[] LookupSIPServer(string hostname, bool synchronous)
        {
            throw new ApplicationException("DNS functionality is not implemented in the Silverlight version of the SIP stack.");
        }
    }
}
