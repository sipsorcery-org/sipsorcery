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
// Copyright (c) 2006-2011 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Australia (www.sipsorcery.com)
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
    public class SIPProxyScriptFacade
    {
        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

        private SIPMonitorLogDelegate m_proxyLogger;
        private SIPTransport m_sipTransport;
        private SIPProxyDispatcher m_dispatcher;

        private GetAppServerDelegate GetAppServer_External;

        public SIPProxyScriptFacade(
            SIPMonitorLogDelegate proxyLogger,
            SIPTransport sipTransport,
            SIPProxyDispatcher dispatcher,
            GetAppServerDelegate getAppServer)
        {
            m_proxyLogger = proxyLogger;
            m_sipTransport = sipTransport;
            m_dispatcher = dispatcher;
            GetAppServer_External = getAppServer;
        }

        public void Log(string message)
        {
            m_proxyLogger(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.SIPProxy, SIPMonitorEventTypesEnum.DialPlan, message, null));
        }

        /// <summary>
        /// Used to send a SIP request received from an external user agent to an internal SIP server agent.
        /// </summary>
        /// <param name="receivedFromEP">The SIP end point the proxy received the request from.</param>
        /// <param name="receivedOnEP">The SIP end point the proxy received the request on.</param>
        /// <param name="dstSocket">The internal socket to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        /// <param name="proxyBranch">The branch to set on the Via header when sending the request. The branch should be calculated
        /// by the proxy core so that looped requests can be detected.</param>
        /// <param name="sendFromSocket">The proxy socket to send the request from.</param>
        public void SendInternal(SIPEndPoint receivedFromEP, SIPEndPoint receivedOnEP, string dstSocket, SIPRequest sipRequest, string proxyBranch, string sendFromSocket)
        {
            try
            {
                if (!IsDestinationValid(sipRequest, dstSocket))
                {
                    logger.Debug("SendInternal failed destination check.");
                    return;
                }

                sipRequest.Header.ProxyReceivedFrom = receivedFromEP.ToString();
                sipRequest.Header.ProxyReceivedOn = receivedOnEP.ToString();

                SIPEndPoint dstSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(dstSocket);
                SIPEndPoint localSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(sendFromSocket);

                if (receivedOnEP != localSIPEndPoint)
                {
                    // The proxy is being requested to send the request on a different socket to the one it was received on.
                    // A second Via header is added to ensure the response can navigate back the same path. The calculated branch
                    // parameter needs to go on the top Via header so that whichever internal socket the request is being sent to can
                    // determine re-transmits.
                    SIPViaHeader via = new SIPViaHeader(receivedOnEP, CallProperties.CreateBranchId());
                    sipRequest.Header.Vias.PushViaHeader(via);

                    SIPViaHeader topVia = new SIPViaHeader(localSIPEndPoint, proxyBranch);
                    sipRequest.Header.Vias.PushViaHeader(topVia);
                }
                else
                {
                    // Only a single Via header is required as any response to this request will be sent from the same socket it gets received on.
                    SIPViaHeader via = new SIPViaHeader(localSIPEndPoint, proxyBranch);
                    sipRequest.Header.Vias.PushViaHeader(via);
                }

                sipRequest.LocalSIPEndPoint = localSIPEndPoint;

                m_sipTransport.SendRequest(dstSIPEndPoint, sipRequest);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRequest SendInternal. " + excp.Message);
                logger.Error(sipRequest.ToString());
                throw;
            }
        }

        /// <summary>
        /// Used to send a request from an internal server agent to an external SIP user agent. The difference between this method and
        /// the SendTransparent method is that this one will set Via headers in accordance with RFC3261.
        /// </summary>
        /// <param name="receivedOnEP">The proxy SIP end point the request was received on.</param>
        /// <param name="dstSocket">The SIP end point the request is being sent to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        /// <param name="proxyBranch">The branch parameter for the top Via header that has been pre-calculated by the proxy core.</param>
        /// <param name="sendFromSocket">The proxy SIP end point to send this request from. If the SIP request has its ProxySendFrom header
        /// value set that will overrule this parameter.</param>
        public void SendExternal(SIPEndPoint receivedOnEP, SIPEndPoint dstSIPEndPoint, SIPRequest sipRequest, string proxyBranch, IPAddress publicIPAddress)
        {
            try
            {
                if (!IsDestinationValid(sipRequest, dstSIPEndPoint))
                {
                    logger.Debug("SendExternal failed destination check.");
                    return;
                }

                // Determine the external SIP endpoint that the proxy will use to send this request.
                SIPEndPoint localSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(dstSIPEndPoint);
                if (!sipRequest.Header.ProxySendFrom.IsNullOrBlank())
                {
                    SIPChannel proxyChannel = m_sipTransport.FindSIPChannel(SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxySendFrom));
                    if (proxyChannel == null)
                    {
                        logger.Warn("No SIP channel could be found for\n" + sipRequest.ToString());
                    }
                    localSIPEndPoint = (proxyChannel != null) ? proxyChannel.SIPChannelEndPoint : localSIPEndPoint;
                }

                if (receivedOnEP != localSIPEndPoint)
                {
                    // The proxy is being requested to send the request on a different socket to the one it was received on.
                    // A second Via header is added to ensure the response can navigate back the same path. The calculated branch
                    // parameter needs to go on the top Via header so that whichever internal socket the request is being sent to can
                    // determine re-transmits.
                    SIPViaHeader via = new SIPViaHeader(receivedOnEP, CallProperties.CreateBranchId());
                    sipRequest.Header.Vias.PushViaHeader(via);

                    SIPViaHeader externalVia = new SIPViaHeader(localSIPEndPoint, proxyBranch);
                    sipRequest.Header.Vias.PushViaHeader(externalVia);
                }
                else
                {
                    // Only a single Via header is required as any response to this request will be sent from the same socket it gets received on.
                    SIPViaHeader via = new SIPViaHeader(localSIPEndPoint, proxyBranch);
                    sipRequest.Header.Vias.PushViaHeader(via);
                }

                if (sipRequest.Method != SIPMethodsEnum.REGISTER)
                {
                    AdjustContactHeader(sipRequest.Header, localSIPEndPoint, publicIPAddress);
                }

                sipRequest.LocalSIPEndPoint = localSIPEndPoint;

                // Proxy sepecific headers that don't need to be seen by external UAs.
                sipRequest.Header.ProxyReceivedOn = null;
                sipRequest.Header.ProxyReceivedFrom = null;
                sipRequest.Header.ProxySendFrom = null;

                m_sipTransport.SendRequest(dstSIPEndPoint, sipRequest);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRequest SendExternal. " + excp.Message);
                logger.Error(sipRequest.ToString());
                throw;
            }
        }

        /// <summary>
        /// Forwards a SIP request through the Proxy. This method differs from the standard Send in that irrespective of whether the Proxy is
        /// receiving and sending on different sockets only a single Via header will ever be allowed on the request. It is then up to the
        /// response processing logic to determine from which Proxy socket to forward the request and to add back on the Via header for the 
        /// end agent. This method is only ever used for requests destined for EXTERNAL SIP end points. 
        /// </summary>
        /// <param name="dstSIPEndPoint">The destination SIP socket to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        /// default channel that matches the destination end point should be used.</param>
        public void SendTransparent(SIPEndPoint dstSIPEndPoint, SIPRequest sipRequest, IPAddress publicIPAddress)
        {
            try
            {
                if (!IsDestinationValid(sipRequest, dstSIPEndPoint))
                {
                    logger.Debug("SendTransparent failed destination check.");
                    return;
                }

                // Determine the external SIP endpoint that the proxy will use to send this request.
                SIPEndPoint localSIPEndPoint = null;
                if (!sipRequest.Header.ProxySendFrom.IsNullOrBlank())
                {
                    SIPChannel proxyChannel = m_sipTransport.FindSIPChannel(SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxySendFrom));
                    localSIPEndPoint = (proxyChannel != null) ? proxyChannel.SIPChannelEndPoint : null;
                }
                localSIPEndPoint = localSIPEndPoint ?? m_sipTransport.GetDefaultSIPEndPoint(dstSIPEndPoint);

                // Create the single Via header for the outgoing request. It uses the passed in branchid which has been taken from the
                // request that's being forwarded. If this proxy is behind a NAT and the public IP is known that's also set on the Via.
                string proxyBranch = sipRequest.Header.Vias.PopTopViaHeader().Branch;
                sipRequest.Header.Vias = new SIPViaSet();
                SIPViaHeader via = new SIPViaHeader(localSIPEndPoint, proxyBranch);
                if (publicIPAddress != null)
                {
                    via.Host = publicIPAddress.ToString();
                }
                sipRequest.Header.Vias.PushViaHeader(via);

                if (sipRequest.Method != SIPMethodsEnum.REGISTER)
                {
                    AdjustContactHeader(sipRequest.Header, localSIPEndPoint, publicIPAddress);
                }

                // If dispatcher is being used record the transaction so responses are sent to the correct internal socket.
                if (m_dispatcher != null && sipRequest.Method != SIPMethodsEnum.REGISTER && sipRequest.Method != SIPMethodsEnum.ACK && sipRequest.Method != SIPMethodsEnum.NOTIFY)
                {
                    //Log("RecordDispatch for " + sipRequest.Method + " " + sipRequest.URI.ToString() + " to " + sipRequest.RemoteSIPEndPoint.ToString() + ".");
                    m_dispatcher.RecordDispatch(sipRequest, sipRequest.RemoteSIPEndPoint);
                }

                // Proxy sepecific headers that don't need to be seen by external UAs.
                sipRequest.Header.ProxyReceivedOn = null;
                sipRequest.Header.ProxyReceivedFrom = null;
                sipRequest.Header.ProxySendFrom = null;

                sipRequest.LocalSIPEndPoint = localSIPEndPoint;
                m_sipTransport.SendRequest(dstSIPEndPoint, sipRequest);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendTransparent. " + excp.Message);
                logger.Error(sipRequest.ToString());
                throw;
            }
        }

        public void SendInternal(SIPEndPoint receivedFromEP, SIPEndPoint receivedOnEP, SIPResponse sipResponse, SIPEndPoint localSIPEndPoint)
        {
            try
            {
                sipResponse.Header.ProxyReceivedFrom = receivedFromEP.ToString();
                sipResponse.Header.ProxyReceivedOn = receivedOnEP.ToString();

                sipResponse.LocalSIPEndPoint = localSIPEndPoint;
                m_sipTransport.SendResponse(sipResponse);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPResponse SendInternal. " + excp.Message);
                logger.Error(sipResponse.ToString());
                throw;
            }
        }

        public void SendTransparent(SIPEndPoint receivedFromEP, SIPEndPoint receivedOnEP, SIPResponse sipResponse, SIPEndPoint localSIPEndPoint, string dstSIPEndPoint, string proxyBranch)
        {
            SendTransparent(receivedFromEP, receivedOnEP, sipResponse, localSIPEndPoint, SIPEndPoint.ParseSIPEndPoint(dstSIPEndPoint), proxyBranch);
        }

        /// <summary>
        /// This method is the equivalent to the same named method for sending SIP requests. The two methods are used to allow the Proxy to
        /// deliver requests to external SIP agents with only a SINGLE Via header due to a small number of providers rejecting requests with
        /// more than one Via header.
        /// </summary>
        /// <param name="receivedFromEP">The socket the response was received from.</param>
        /// <param name="receivedOnEP">The proxy socket the response was received on.</param>
        /// <param name="sipResponse">The response being forwarded.</param>
        /// <param name="localSIPEndPoint">The proxy socket to forward the request from.</param>
        /// <param name="dstSIPEndPoint">The internal destination socket to forward the response to.</param>
        /// <param name="proxyBranch">The branch parameter from the top Via header that needs to be reused when forwarding the response.</param>
        public void SendTransparent(SIPEndPoint receivedFromEP, SIPEndPoint receivedOnEP, SIPResponse sipResponse, SIPEndPoint localSIPEndPoint, SIPEndPoint dstSIPEndPoint, string proxyBranch)
        {
            try
            {
                sipResponse.Header.ProxyReceivedFrom = receivedFromEP.ToString();
                sipResponse.Header.ProxyReceivedOn = receivedOnEP.ToString();

                sipResponse.Header.Vias.PushViaHeader(new SIPViaHeader(dstSIPEndPoint, proxyBranch));

                sipResponse.LocalSIPEndPoint = localSIPEndPoint;
                m_sipTransport.SendResponse(sipResponse);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPResponse SendInternal. " + excp.Message);
                logger.Error(sipResponse.ToString());
                throw;
            }
        }

        public void SendExternal(SIPResponse sipResponse, SIPEndPoint localSIPEndPoint)
        {
            try
            {
                sipResponse.Header.ProxyReceivedOn = null;
                sipResponse.Header.ProxyReceivedFrom = null;
                sipResponse.Header.ProxySendFrom = null;

                sipResponse.LocalSIPEndPoint = localSIPEndPoint;

                m_sipTransport.SendResponse(sipResponse);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPResponse SendExternal. " + excp.Message);
                logger.Error(sipResponse.ToString());
                throw;
            }
        }

        public void SendExternal(SIPResponse sipResponse, SIPEndPoint localSIPEndPoint, IPAddress publicIPAddress)
        {
            AdjustContactHeader(sipResponse.Header, localSIPEndPoint, publicIPAddress);
            SendExternal(sipResponse, localSIPEndPoint);
        }

        private bool IsDestinationValid(SIPRequest sipRequest, string dstSIPEndPoint)
        {
            if (dstSIPEndPoint.IsNullOrBlank())
            {
                Log("Request with URI " + sipRequest.URI.ToString() + " from " + sipRequest.RemoteSIPEndPoint + " was unresolvable.");
                Respond(sipRequest, SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "Destination unresolvable");
                return false;
            }
            else
            {
                return IsDestinationValid(sipRequest, SIPEndPoint.ParseSIPEndPoint(dstSIPEndPoint));
            }
        }

        private bool IsDestinationValid(SIPRequest sipRequest, SIPEndPoint dstSIPEndPoint)
        {
            if (dstSIPEndPoint == null)
            {
                Log("Request with URI " + sipRequest.URI.ToString() + " from " + sipRequest.RemoteSIPEndPoint + " was unresolvable.");
                Respond(sipRequest, SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "Destination unresolvable");
                return false;
            }
            else if (SIPTransport.BlackholeAddress.Equals(dstSIPEndPoint.Address))
            {
                Log("Request with URI " + sipRequest.URI.ToString() + " from " + sipRequest.RemoteSIPEndPoint + " resolved to a blackhole IP address.");
                Respond(sipRequest, SIPResponseStatusCodesEnum.BadRequest, "Resolved to blackhole");
                return false;
            }

            return true;
        }

        private void AdjustContactHeader(SIPHeader sipHeader, SIPEndPoint localSIPEndPoint, IPAddress publicIPAddress)
        {
            try
            {
                // Set the Contact URI on the outgoing requests depending on which SIP socket the request is being sent on and whether
                // the request is going to an external network.
                if (sipHeader.Contact != null && sipHeader.Contact.Count == 1)
                {
                    SIPEndPoint proxyContact = localSIPEndPoint.CopyOf();
                    if (publicIPAddress != null)
                    {
                        proxyContact = new SIPEndPoint(proxyContact.Protocol, publicIPAddress, proxyContact.Port);
                    }

                    sipHeader.Contact[0].ContactURI.Host = proxyContact.GetIPEndPoint().ToString();
                    sipHeader.Contact[0].ContactURI.Protocol = proxyContact.Protocol;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AdjustContactHeader. " + excp.Message);
                throw;
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

        public SIPDNSLookupResult Resolve(SIPRequest sipRequest)
        {
            if (sipRequest.Header.Routes != null && sipRequest.Header.Routes.Length > 0 && !sipRequest.Header.Routes.TopRoute.IsStrictRouter)
            {
                return SIPDNSManager.ResolveSIPService(sipRequest.Header.Routes.TopRoute.URI, true);
            }
            else
            {
                return SIPDNSManager.ResolveSIPService(sipRequest.URI, true);
            }
        }

        public SIPDNSLookupResult Resolve(SIPURI sipURI)
        {
            return SIPDNSManager.ResolveSIPService(sipURI, true);
        }

        public SIPEndPoint Resolve(SIPResponse sipResponse)
        {
            SIPViaHeader topVia = sipResponse.Header.Vias.TopViaHeader;
            SIPEndPoint dstEndPoint = new SIPEndPoint(topVia.Transport, m_sipTransport.GetHostEndPoint(topVia.ReceivedFromAddress, true).GetSIPEndPoint().GetIPEndPoint());
            return dstEndPoint;
        }

        public SIPEndPoint GetDefaultSIPEndPoint(SIPProtocolsEnum protocol)
        {
            return m_sipTransport.GetDefaultSIPEndPoint(protocol);
        }

        public SIPEndPoint DispatcherLookup(SIPRequest sipRequest)
        {
            if (m_dispatcher != null &&
                (sipRequest.Method == SIPMethodsEnum.ACK || sipRequest.Method == SIPMethodsEnum.CANCEL || sipRequest.Method == SIPMethodsEnum.INVITE))
            {
                return m_dispatcher.LookupTransactionID(sipRequest);
            }

            return null;
        }

        public SIPEndPoint DispatcherLookup(SIPResponse sipResponse)
        {
            if (m_dispatcher != null &&
                (sipResponse.Header.CSeqMethod == SIPMethodsEnum.ACK || sipResponse.Header.CSeqMethod == SIPMethodsEnum.CANCEL || sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE))
            {
                return m_dispatcher.LookupTransactionID(sipResponse);
            }

            return null;
        }

        public SIPEndPoint GetAppServer()
        {
            return GetAppServer_External();
        }
    }
}
