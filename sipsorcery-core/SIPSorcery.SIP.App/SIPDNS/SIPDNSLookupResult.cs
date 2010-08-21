//-----------------------------------------------------------------------------
// Filename: SIPDNSLocation.cs
//
// Description: Used to hold the results of the various DNS lookups required to resolve a SIP hostname.
//
// History:
// 10 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
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
using SIPSorcery.Sys;
using Heijden.DNS;
using log4net;

namespace SIPSorcery.SIP.App
{
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

    public class SIPDNSServiceResult
    {
        public SIPServicesEnum SIPService;                  // The type of SIP NAPTR/SRV record that was resolved.
        public int Order;                                   // The order value assigned to the NAPTR/SRV record.
        public int TTL;                                     // The time-to-live in seconds for this record.
        public string Data;                                 // The SRV record for the NAPTR record and target for a SRV record.
        public int Port;                                    // The SRV record port.
        public DateTime? ResolvedAt;                        // Time this record was created.
        public DateTime? EndPointsResolvedAt;               // The time an attempt was made to resolve the A records for the SRV.

        public SIPDNSServiceResult(SIPServicesEnum sipService, int order, int ttl, string data, int port, DateTime resolvedAt)
        {
            SIPService = sipService;
            Order = order;
            TTL = ttl;
            Data = data;
            Port = port;
            ResolvedAt = resolvedAt;
        }
    }

    public class SIPDNSLookupEndPoint
    {
        public SIPEndPoint LookupEndPoint;
        public int TTL;
        public DateTime ResolvedAt;
        public DateTime FailedAt;
        public string FailureReason;

        public SIPDNSLookupEndPoint(SIPEndPoint sipEndPoint, int ttl)
        {
            LookupEndPoint = sipEndPoint;
            TTL = ttl;
            ResolvedAt = DateTime.Now;
        }
    }

    public class SIPDNSLookupResult
    {
        private static ILog logger = AssemblyState.logger;

        public SIPURI URI;
        public string LookupError;
        public DateTime? NAPTRTimedoutAt;
        public DateTime? SRVTimedoutAt;
        public DateTime? ATimedoutAt;
        public DateTime Inserted = DateTime.Now;
        public Dictionary<SIPServicesEnum, SIPDNSServiceResult> SIPNAPTRResults;
        public List<SIPDNSServiceResult> SIPSRVResults;
        public List<SIPDNSLookupEndPoint> EndPointResults;
        public bool Pending;                // If an aysnc lookup request is made this will be set to true if no immediate result is available.

        public SIPDNSLookupResult(SIPURI uri)
        {
            URI = uri;
        }

        public SIPDNSLookupResult(SIPURI uri, string lookupError)
        {
            URI = uri;
            LookupError = lookupError;
        }

        public void AddLookupResult(SIPDNSLookupEndPoint lookupEndPoint)
        {
            //logger.Debug(" adding SIP end point result for " + URI.ToString() + " of " + lookupEndPoint.LookupEndPoint + ".");

            if(EndPointResults == null)
            {
                EndPointResults = new List<SIPDNSLookupEndPoint>() { lookupEndPoint };
            }
            else
            {
                EndPointResults.Add(lookupEndPoint);
            }
        }

        public void AddNAPTRResult(RecordNAPTR naptrRecord)
        {
            //logger.Debug("Checking NAPTR record for " + URI.ToString() + " " + naptrRecord.ToString() + ".");

            SIPServicesEnum sipServicesEnum = SIPServicesEnum.none;

            if (naptrRecord.Service == SIPDNSManager.NAPTR_SIP_UDP_SERVICE)
            {
                sipServicesEnum = SIPServicesEnum.sipudp;
            }
            else if (naptrRecord.Service == SIPDNSManager.NAPTR_SIP_TCP_SERVICE)
            {
                sipServicesEnum = SIPServicesEnum.siptcp;
            }
            else if (naptrRecord.Service == SIPDNSManager.NAPTR_SIPS_TCP_SERVICE)
            {
                sipServicesEnum = SIPServicesEnum.sipstcp;
            }

            if(sipServicesEnum != SIPServicesEnum.none)
            {
                //logger.Debug(" adding NAPTR lookup result for " + URI.ToString() + " of " + naptrRecord.ToString() + ".");
                SIPDNSServiceResult sipNAPTRResult = new SIPDNSServiceResult(sipServicesEnum, naptrRecord.Order, naptrRecord.RR.TTL, naptrRecord.Replacement, 0, DateTime.Now);

                if (SIPNAPTRResults == null)
                {
                    SIPNAPTRResults = new Dictionary<SIPServicesEnum, SIPDNSServiceResult>() { { sipServicesEnum, sipNAPTRResult } };
                }
                else
                {
                    SIPNAPTRResults.Add(sipServicesEnum, sipNAPTRResult);
                }
            }
        }

        public void AddSRVResult(SIPServicesEnum service, RecordSRV srvRecord)
        {
            //logger.Debug("Adding record for " + URI.ToString() + " " + srvRecord.ToString() + ".");
            SIPDNSServiceResult sipSRVResult = new SIPDNSServiceResult(service, srvRecord.Priority, srvRecord.RR.TTL, srvRecord.Target, srvRecord.Port, DateTime.Now);

            if (SIPSRVResults == null)
            {
                SIPSRVResults = new List<SIPDNSServiceResult>() { sipSRVResult };
            }
            else
            {
                SIPSRVResults.Add(sipSRVResult);
            }
        }

        public SIPDNSServiceResult GetNextUnusedSRV()
        {
            if (SIPSRVResults != null && SIPSRVResults.Count > 0)
            {
                SIPDNSServiceResult nextSRVRecord = null;
                foreach (SIPDNSServiceResult sipSRVResult in SIPSRVResults)
                {
                    if (sipSRVResult.EndPointsResolvedAt == null)
                    {
                        if (nextSRVRecord == null || nextSRVRecord.Order < sipSRVResult.Order)
                        {
                            nextSRVRecord = sipSRVResult;
                        }
                    }
                }

                return nextSRVRecord;
            }

            return null;
        }
    }
}
