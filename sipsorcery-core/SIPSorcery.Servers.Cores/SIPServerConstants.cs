// ============================================================================
// FileName: StatefulProxyConstants.cs
//
// Description:
// Constants used by mutliple servers.
//
// Author(s):
// Aaron Clauson
//
// History:
// 05 Sep 2006	Aaron Clauson	Created.
// ============================================================================

using System;

namespace SIPSorcery.Servers
{	
	public class StatefulProxyConstants
	{	
		public const int MAX_USERAGENT_LENGTH = 128;
		public const string PROXY_REALM = "sip.mysipswitch.com";

		public const int MAX_EXPIRY_TIME = 3600;	// This is the maximum length of time NAT KeepAlives will be sent for. After this period keep alives will not start again until the agent registers.
		public const int MIN_EXPIRY_TIME = 1800;	// This is the time a registration will be allowed to stay in the cache without requiring a DB lookup.
	}
}
