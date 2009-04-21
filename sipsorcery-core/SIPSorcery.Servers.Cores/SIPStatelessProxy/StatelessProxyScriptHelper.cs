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
using System.Scripting;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using IronPython.Compiler;
using IronPython.Hosting;
using log4net;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Ruby;
using Ruby.Compiler;
using Ruby.Hosting;
//using IronRuby.Compiler;
//using IronRuby.Hosting;
//using IronRuby;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{	  
    public class StatelessProxyScriptHelper
    {
        private const string CHANNEL_NAME_KEY = "cn";

        private static ILog logger = log4net.LogManager.GetLogger("sipproxy");

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

        public void Send(string dstSocket, SIPRequest sipRequest, string proxyBranch, string channelName, bool addRR)
        {
            SIPEndPoint dstSIPEndPoint =  SIPEndPoint.ParseSIPEndPoint(dstSocket);
            Send(dstSIPEndPoint, sipRequest, proxyBranch, channelName, addRR);
        }

        public void Send(SIPEndPoint dstSIPEndPoint, SIPRequest sipRequest, string proxyBranch, string channelName, bool addRR)
        {
            SIPEndPoint localSIPEndPoint = (sipRequest.LocalSIPEndPoint != null) ? sipRequest.LocalSIPEndPoint : m_sipTransport.GetDefaultTransportContact(dstSIPEndPoint.SIPProtocol);
            
            SIPViaHeader via = new SIPViaHeader(localSIPEndPoint, proxyBranch);
            if(!channelName.IsNullOrBlank()) {
                via.ViaParameters.Set(CHANNEL_NAME_KEY, channelName);
            }

            if (addRR && sipRequest.Method == SIPMethodsEnum.INVITE) {
                SIPSchemesEnum scheme = (localSIPEndPoint.SIPProtocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                SIPRoute sipRoute = new SIPRoute(new SIPURI(scheme, localSIPEndPoint), true);
                if (!channelName.IsNullOrBlank()) {
                    sipRoute.URI.Parameters.Set(CHANNEL_NAME_KEY, channelName);
                }
                sipRequest.Header.RecordRoutes.PushRoute(sipRoute);
            }

            sipRequest.Header.Vias.PushViaHeader(via);
            m_sipTransport.SendRequest(dstSIPEndPoint, sipRequest);
        }

        public void Send(SIPResponse sipResponse)
        {
            m_sipTransport.SendResponse(sipResponse);
        }

        public void Send(SIPResponse sipResponse, SIPEndPoint recvdSIPEndPoint, SIPEndPoint sendSIPEndPoint, string channelName) {
            if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE && sipResponse.Header.RecordRoutes.Length > 0) {
                // Iterate through the record-route headers and replace the one set by this proxy.
                for (int index = 0; index < sipResponse.Header.RecordRoutes.Length; index++) {
                    SIPRoute recordRoute = sipResponse.Header.RecordRoutes.GetAt(index);
                    
                    if (recordRoute.URI.ToSIPEndPoint() == recvdSIPEndPoint) {
                        // A route was found matching the received SIPEndPoint. Replace with the sending SIPEndPoint.
                        SIPSchemesEnum scheme = (sendSIPEndPoint.SIPProtocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                        SIPRoute sipRoute = new SIPRoute(new SIPURI(scheme, sendSIPEndPoint), true);
                        if (!channelName.IsNullOrBlank()) {
                            sipRoute.URI.Parameters.Set(CHANNEL_NAME_KEY, channelName);
                        }
                        sipResponse.Header.RecordRoutes.SetAt(index, sipRoute);
                        break;
                    }
                }
            }

            m_sipTransport.SendResponse(sipResponse);
        }

        /// <summary>
        /// Helper method for dynamic proxy runtime script.
        /// </summary>
        /// <param name="responseCode"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="sipRequest"></param>
        public void Respond(SIPResponseStatusCodesEnum responseCode, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            string fromUser = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.User : null;
            string toUser = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToURI.User : null;
            string summaryStr = "req " + sipRequest.Method + " from=" + fromUser + ", to=" + toUser + ", " + IPSocket.GetSocketString(remoteEndPoint);

            //string respondMessage = "Proxy responding with method " + responseCode + " for " + summaryStr + ".";
            //SIPMonitorEvent respondEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorServerTypesEnum.ProxyForward, respondMessage, localEndPoint, remoteEndPoint, remoteEndPoint);
            //SendMonitorEvent(respondEvent);

            SIPResponse response = SIPTransport.GetResponse(sipRequest, responseCode, null);
            m_sipTransport.SendResponse(response);
        }

        public void Mangle(SIPResponse sipResponse, string remoteEndPoint)
        {
            Mangle(sipResponse, IPSocket.ParseSocketString(remoteEndPoint));
        }

        public void Mangle(SIPResponse sipResponse, IPEndPoint remoteEndPoint)
        {
            if (sipResponse != null)
            {
                SIPPacketMangler.MangleSIPResponse(SIPMonitorServerTypesEnum.StatelessProxy, sipResponse, remoteEndPoint, null, m_proxyLogger);
            }
        }

        public void Mangle(SIPRequest sipRequest)
        {
            if (sipRequest != null && sipRequest.Body != null)
            {
                SIPPacketMangler.MangleSIPRequest(SIPMonitorServerTypesEnum.StatelessProxy, sipRequest, null, m_proxyLogger);
            }
        }

        public SIPEndPoint Resolve(SIPRequest sipRequest)
        {
            return m_sipTransport.GetRequestEndPoint(sipRequest, false);
        }

        public SIPEndPoint Resolve(SIPURI sipURI)
        {
            return m_sipTransport.GetURIEndPoint(sipURI, false);
        }

        public SIPEndPoint GetChannelSIPEndPoint(string channelName) {
            SIPChannel channel = m_sipTransport.FindSIPChannel(channelName);
            if (channel != null) {
                return channel.SIPChannelEndPoint;
            }
            return null;
        }
    }
}
