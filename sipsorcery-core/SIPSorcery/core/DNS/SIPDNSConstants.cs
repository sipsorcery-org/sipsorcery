//-----------------------------------------------------------------------------
// Filename: SIPDNSConstants.cs
//
// Description: Holds constant fields related to SIP DNS resolution.
//
// History:
// 19 Jun 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIPSorcery.SIP
{
    public class SIPDNSConstants
    {
        public const string NAPTR_SIP_UDP_SERVICE = "SIP+D2U";
        public const string NAPTR_SIP_TCP_SERVICE = "SIP+D2T";
        public const string NAPTR_SIPS_TCP_SERVICE = "SIPS+D2T";

        public const string SRV_SIP_TCP_QUERY_PREFIX = "_sip._tcp.";
        public const string SRV_SIP_UDP_QUERY_PREFIX = "_sip._udp.";
        public const string SRV_SIP_TLS_QUERY_PREFIX = "_sip._tls.";
        public const string SRV_SIPS_TCP_QUERY_PREFIX = "_sips._tcp.";
    }

    /// <summary>
    /// A list of the different combinations of SIP schemes and transports. 
    /// </summary>
    public enum SIPServicesEnum
    {
        none = 0,
        sipudp = 1,     // sip over udp. SIP+D2U and _sip._udp
        siptcp = 2,     // sip over tcp. SIP+D2T and _sip._tcp
        sipsctp = 3,    // sip over sctp. SIP+D2S and _sip._sctp
        siptls = 4,     // sip over tls. _sip._tls.
        sipstcp = 5,    // sips over tcp. SIPS+D2T and _sips._tcp
        sipssctp = 6,   // sips over sctp. SIPS+D2S and _sips._sctp
    }

    public class SIPServices
    {
        public static SIPServicesEnum GetService(string service)
        {
            if (service == SIPDNSConstants.NAPTR_SIP_UDP_SERVICE)
            {
                return SIPServicesEnum.sipudp;
            }
            else if (service == SIPDNSConstants.NAPTR_SIP_TCP_SERVICE)
            {
                return SIPServicesEnum.siptcp;
            }
            else if (service == SIPDNSConstants.NAPTR_SIPS_TCP_SERVICE)
            {
                return SIPServicesEnum.sipstcp;
            }
            else
            {
                return SIPServicesEnum.none;
            }
        }
    }
}
