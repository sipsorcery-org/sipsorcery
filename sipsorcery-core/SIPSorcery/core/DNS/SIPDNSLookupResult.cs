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
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using log4net;

namespace SIPSorcery.SIP
{
    public class SIPDNSServiceResult
    {
        public SIPServicesEnum SIPService;                  // The type of SIP NAPTR/SRV record that was resolved.
        public int Priority;                                // The priority value assigned to the NAPTR/SRV record. A client MUST attempt to contact the target host with the lowest-numbered priority it can
                                                            // reach; target hosts with the same priority SHOULD be tried in an order defined by the weight field.
        public int Weight;                                  // The weight value assigned to the SRV record. Larger weights SHOULD be given a proportionately higher probability of being selected.
        public int TTL;                                     // The time-to-live in seconds for this record.
        public string Data;                                 // The SRV record for the NAPTR record and target for a SRV record.
        public int Port;                                    // The SRV record port.
        public DateTime? ResolvedAt;                        // Time this record was created.
        public DateTime? EndPointsResolvedAt;               // The time an attempt was made to resolve the A records for the SRV.

        public SIPDNSServiceResult(SIPServicesEnum sipService, int priority, int weight, int ttl, string data, int port, DateTime resolvedAt)
        {
            SIPService = sipService;
            Priority = priority;
            Weight = weight;
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

        /// <summary>
        /// Used when the result is already known such as when the lookup is for an IP address but a DNS lookup
        /// object still needs to be returned.
        /// </summary>
        /// <param name="uri">The URI being looked up.</param>
        /// <param name="resultEndPoint">The known result SIP end point.</param>
        public SIPDNSLookupResult(SIPURI uri, SIPEndPoint resultEndPoint)
        {
            URI = uri;
            EndPointResults = new List<SIPDNSLookupEndPoint>() { new SIPDNSLookupEndPoint(resultEndPoint, Int32.MaxValue) };
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

        public void AddNAPTRResult(SIPDNSServiceResult sipNAPTRResult)
        {
                if (SIPNAPTRResults == null)
                {
                    SIPNAPTRResults = new Dictionary<SIPServicesEnum, SIPDNSServiceResult>() { { sipNAPTRResult.SIPService, sipNAPTRResult } };
                }
                else
                {
                    SIPNAPTRResults.Add(sipNAPTRResult.SIPService, sipNAPTRResult);
                }
        }

        public void AddSRVResult(SIPDNSServiceResult sipSRVResult)
        {
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
                return (from srv in SIPSRVResults where srv.EndPointsResolvedAt == null orderby srv.Priority select srv).FirstOrDefault();
            }

            return null;
        }

        public SIPEndPoint GetSIPEndPoint()
        {
            if (EndPointResults != null && EndPointResults.Count > 0)
            {
                return EndPointResults[0].LookupEndPoint;
            }
            else
            {
                return null;
            }
        }
    }
}
