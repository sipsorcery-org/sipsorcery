// ============================================================================
// FileName: StatelessProxyScriptHelper.cs
//
// Description:
// A class that contains helper methods for use in a stateless prxoy runtime script.
//
// Author(s):
// Aaron Clauson
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	  
    public class StatelessProxyScriptHelper
    {
        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        //private static string m_channelNameKey = StatelessProxyCore.CHANNEL_NAME_KEY;

        private SIPMonitorLogDelegate m_proxyLogger;
        private SIPTransport m_sipTransport;

        public StatelessProxyScriptHelper(
            SIPMonitorLogDelegate proxyLogger,
            SIPTransport sipTransport)
        {
            m_proxyLogger = proxyLogger;
            m_sipTransport = sipTransport;
        }

        public void Log(string message)
        {
            m_proxyLogger(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.DialPlan, message, null));
        }

        public void Send(string dstSocket, SIPRequest sipRequest, string proxyBranch)
        {
            SIPEndPoint dstSIPEndPoint =  SIPEndPoint.ParseSIPEndPoint(dstSocket);
            Send(dstSIPEndPoint, sipRequest, proxyBranch, null, null);
        }

        public void Send(string dstSocket, SIPRequest sipRequest, string proxyBranch, string localSocket) {
            SIPEndPoint dstSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(dstSocket);
            SIPEndPoint localSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(localSocket);
            Send(dstSIPEndPoint, sipRequest, proxyBranch, localSIPEndPoint, null);
        }

        public void Send(SIPEndPoint dstSIPEndPoint, SIPRequest sipRequest, string proxyBranch, SIPEndPoint localSIPEndPoint) {
            Send(dstSIPEndPoint, sipRequest, proxyBranch, localSIPEndPoint, null);
        }

        /// <summary>
        /// Forwards a SIP request through the Proxy. If the request enters and leaves on different sockets then two Via headers
        /// will be added to allow the response to travel back along an identical path.
        /// </summary>
        /// <param name="dstSIPEndPoint">The destination SIP socket to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        /// <param name="proxyBranch">The branchid to set on the Via header to be added for this forwarding leg.</param>
        /// <param name="localSIPEndPoint">The proxy socket that the request should be sent from. If null the 
        /// default channel that matches the destination end point should be used.</param>
        /// <param name="setContactToLocal">If true the Contact header URI will be overwritten with the URI of the
        /// proxy socket the request is being sent from. This should only be used for INVITE requests.</param>
        public void Send(SIPEndPoint dstSIPEndPoint, SIPRequest sipRequest, string proxyBranch, SIPEndPoint localSIPEndPoint, SIPURI contactURI)
        {
            if (dstSIPEndPoint == null) {
                Log("Send was passed an emtpy destination for " + sipRequest.URI.ToString() + ", returning unresolvable.");
                Respond(sipRequest, SIPResponseStatusCodesEnum.NotFound, "DNS lookup unresolvable");
                return;
            }

            localSIPEndPoint = localSIPEndPoint ?? m_sipTransport.GetDefaultTransportContact(dstSIPEndPoint.SIPProtocol);

            // If the request is being forwarded on a different proxy socket to the one it was received on then two Via headers
            // need to be added, one for the proxy socket the request was received on and one for the socket it is being sent on.
            if (localSIPEndPoint != sipRequest.LocalSIPEndPoint) {
                SIPViaHeader receiveSocketVia = new SIPViaHeader(sipRequest.LocalSIPEndPoint, CallProperties.CreateBranchId());
                sipRequest.Header.Vias.PushViaHeader(receiveSocketVia);
            }
            
            SIPViaHeader via = new SIPViaHeader(localSIPEndPoint, proxyBranch);
            sipRequest.Header.Vias.PushViaHeader(via);

            sipRequest.LocalSIPEndPoint = localSIPEndPoint;

            if (contactURI != null && sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count > 0) {
                sipRequest.Header.Contact[0].ContactURI = contactURI;
            }

            m_sipTransport.SendRequest(dstSIPEndPoint, sipRequest);
        }

        /// <summary>
        /// Forwards a SIP request through the Proxy. This method differs from the standard Send in that irrespective of whether the Proxy is
        /// receiving and sending on different sockets only a single Via header will ever be allowed on the request. It is then up to the
        /// response processing logic to determine from which Proxy socket to forward the request and to add back on the Via header for the 
        /// end agent.
        /// </summary>
        /// <param name="dstSIPEndPoint">The destination SIP socket to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        /// <param name="proxyBranch">The branchid to set on the Via header to be added for this forwarding leg.</param>
        /// <param name="localSIPEndPoint">The proxy socket that the request should be sent from. If null the 
        /// default channel that matches the destination end point should be used.</param>
        /// <param name="setContactToLocal">If true the Contact header URI will be overwritten with the URI of the
        /// proxy socket the request is being sent from. This should only be used for INVITE requests.</param>
        public void SendTransparent(SIPEndPoint dstSIPEndPoint, SIPRequest sipRequest, string proxyBranch, SIPEndPoint localSIPEndPoint, SIPURI contactURI, IPAddress publicIPAddress)
        {
            if (dstSIPEndPoint == null)
            {
                Log("Send was passed an emtpy destination for " + sipRequest.URI.ToString() + ", returning unresolvable.");
                Respond(sipRequest, SIPResponseStatusCodesEnum.NotFound, "DNS lookup unresolvable");
                return;
            }

            localSIPEndPoint = localSIPEndPoint ?? m_sipTransport.GetDefaultTransportContact(dstSIPEndPoint.SIPProtocol);

            sipRequest.Header.Vias = new SIPViaSet();
            SIPViaHeader via = new SIPViaHeader(localSIPEndPoint, proxyBranch);
            if (publicIPAddress != null) {
                via.Host = publicIPAddress.ToString();
            }
            sipRequest.Header.Vias.PushViaHeader(via);

            sipRequest.LocalSIPEndPoint = localSIPEndPoint;

            if (contactURI != null && sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count > 0)
            {
                sipRequest.Header.Contact[0].ContactURI = contactURI;
            }

            m_sipTransport.SendRequest(dstSIPEndPoint, sipRequest);
        }

        public void Send(SIPResponse sipResponse)
        {
            m_sipTransport.SendResponse(sipResponse);
        }

        public void Send(SIPResponse sipResponse, string localSIPEndPoint) {
            Send(sipResponse, SIPEndPoint.ParseSIPEndPoint(localSIPEndPoint), null);
        }

        public void Send(SIPResponse sipResponse, SIPEndPoint localSIPEndPoint) {
            Send(sipResponse, localSIPEndPoint, null);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sipResponse"></param>
        /// <param name="localSIPEndPoint"></param>
        /// <param name="setContactURIToLocal">If true will set the Contact URI to the URI of the proxy socket the response
        /// is being sent from. This should only be used for INVITE responses where the contact header needs to be updated
        /// to make sure the correct proxy socket is used for in-dialogue requests.</param>
        public void Send(SIPResponse sipResponse, SIPEndPoint localSIPEndPoint, SIPURI contactURI) {
            try {
                sipResponse.LocalSIPEndPoint = localSIPEndPoint;

                if (contactURI != null && sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0) {
                    sipResponse.Header.Contact[0].ContactURI = contactURI;
                }

                m_sipTransport.SendResponse(sipResponse);
            }
            catch (Exception excp) {
                logger.Error("Exception StatelessProxyScriptHelper Send SIPResponse. " + excp);
            }
        }

        /// <summary>
        /// Helper method for dynamic proxy runtime script.
        /// </summary>
        /// <param name="responseCode"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="sipRequest"></param>
        public void Respond(SIPRequest sipRequest, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            SIPResponse response = SIPTransport.GetResponse(sipRequest, responseCode, reasonPhrase);
            m_sipTransport.SendResponse(response);
        }

        public SIPEndPoint Resolve(SIPRequest sipRequest)
        {
            return m_sipTransport.GetRequestEndPoint(sipRequest, null, false);
        }

        public SIPEndPoint Resolve(SIPURI sipURI)
        {
            return m_sipTransport.GetURIEndPoint(sipURI, false);
        }

        public SIPEndPoint Resolve(SIPResponse sipResponse) {
            SIPViaHeader topVia = sipResponse.Header.Vias.TopViaHeader;
            SIPEndPoint dstEndPoint = m_sipTransport.GetHostEndPoint(topVia.ReceivedFromAddress, true);
            dstEndPoint.SIPProtocol = topVia.Transport;
            return dstEndPoint;
        }

        public SIPEndPoint GetDefaultSIPEndPoint(SIPProtocolsEnum protocol) {
            return m_sipTransport.GetDefaultSIPEndPoint(protocol);
        }
    }
}
