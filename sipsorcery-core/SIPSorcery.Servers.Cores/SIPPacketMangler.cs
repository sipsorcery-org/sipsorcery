// ============================================================================
// FileName: SIPPacketMangler.cs
//
// Description:
// A class containing functionality to mangle the Contact header and SDP payloads
// of SIP messages.
//
// History:
// 14 Sep 2008	    Aaron Clauson	    Created (most methods extractd from StatefulProxyCore).
//
// Author(s):
// Aaron Clauson
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPPacketMangler
    {
        private static ILog logger = AppState.GetLogger("sip.servers");

        public static string MangleSDP(string sdpBody, string publicIPAddress, out bool wasMangled)
        {
            wasMangled = false;

            try
            {
                if (sdpBody != null && publicIPAddress != null)
                {
                    string sdpAddress = Regex.Match(sdpBody, @"c=IN IP4 (?<ipaddress>(\d+\.){3}\d+)", RegexOptions.Singleline).Result("${ipaddress}");

                    // Only mangle if there is something to change. For example the server could be on the same private subnet in which case it can't help.
                    if (SIPTransport.IsPrivateAddress(sdpAddress) && publicIPAddress != sdpAddress)
                    {
                        string mangledSDP = Regex.Replace(sdpBody, @"c=IN IP4 (?<ipaddress>(\d+\.){3}\d+)", "c=IN IP4 " + publicIPAddress, RegexOptions.Singleline);
                        wasMangled = true;

                        return mangledSDP;
                    }
                }

                return sdpBody;
            }
            catch (Exception excp)
            {
                logger.Error("Exception MangleSDP. " + excp.Message);
                return sdpBody;
            }
        }

        /// <summary>
        /// Mangles private IP addresses in a SIP request replacing them with the IP address the packet was received on. 
        /// </summary>
        /// <param name="sipResponse">The unmangled SIP request.</param>
        /// <returns>The mangled SIP request</returns>
        public static void MangleSIPRequest(SIPMonitorServerTypesEnum server, SIPRequest sipRequest, string username, SIPMonitorLogDelegate logDelegate)
        {
            try
            {
                string bottomViaIPAddress = sipRequest.Header.Vias.BottomViaHeader.ReceivedFromIPAddress;

                if (sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count == 1 && bottomViaIPAddress != null)
                {
                    string contactHost = sipRequest.Header.Contact[0].ContactURI.Host;

                    // Only mangle if the host is a private IP address and there is something to change. 
                    // For example the server could be on the same private subnet in which case it can't help.
                    if (SIPTransport.IsPrivateAddress(contactHost) && contactHost != bottomViaIPAddress)
                    {
                        string origContact = sipRequest.Header.Contact[0].ContactURI.Host;
                        sipRequest.Header.Contact[0].ContactURI.Host = sipRequest.Header.Vias.BottomViaHeader.ReceivedFromAddress;

                        //logger.Debug("Contact URI identified as containing private address for " + sipRequest.Method + " " + origContact + " adjusting to use bottom via " + bottomViaHost + ".");
                        //FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorServerTypesEnum.ContactRegisterInProgress, "Contact on " + sipRequest.Method + " " + origContact + " had private address adjusted to " + bottomViaHost + ".", username));
                    }
                }

                if (sipRequest.Body != null && bottomViaIPAddress != null)
                {
                    bool wasMangled = false;
                    string mangledSDP = MangleSDP(sipRequest.Body, bottomViaIPAddress, out wasMangled);

                    if (wasMangled)
                    {
                        sipRequest.Body = mangledSDP;
                        sipRequest.Header.ContentLength = sipRequest.Body.Length;

                        if (logDelegate != null)
                        {
                            logDelegate(new SIPMonitorControlClientEvent(server, SIPMonitorEventTypesEnum.DialPlan, "SDP mangled for INVITE request from " + sipRequest.RemoteSIPEndPoint.ToString() + ", adjusted address " + bottomViaIPAddress + ".", username));
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MangleSIPRequest. " + excp.Message);
            }
        }

        /// <summary>
        /// Mangles private IP addresses in a SIP response replacing them with the IP address the packet was received on. 
        /// </summary>
        /// <param name="sipResponse">The unmangled SIP response.</param>
        /// <returns>The mangled SIP response</returns>
        public static void MangleSIPResponse(SIPMonitorServerTypesEnum server, SIPResponse sipResponse, SIPEndPoint remoteEndPoint, string username, SIPMonitorLogDelegate logDelegate)
        {
            try
            {
                if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
                {
                    string contactHost = sipResponse.Header.Contact[0].ContactURI.Host;

                    // Only mangle if the host is a private IP address and there is something to change. 
                    // For example the server could be on the same private subnet in which case it can't help.
                    if (SIPTransport.IsPrivateAddress(contactHost) && contactHost != remoteEndPoint.SocketEndPoint.Address.ToString())
                    {
                        SIPURI origContact = sipResponse.Header.Contact[0].ContactURI;
                        sipResponse.Header.Contact[0].ContactURI = new SIPURI(origContact.Scheme, remoteEndPoint);

                        //logger.Debug("INVITE response Contact URI identified as containing private address, original " + origContact + " adjustied to " + remoteEndPoint.ToString() + ".");
                        //FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorServerTypesEnum.ContactRegisterInProgress, "INVITE Response contact adjusted from " + origContact + " to " + remoteEndPoint.ToString() + ".", username));
                    }
                }

                if (sipResponse.Body != null)
                {
                    bool wasMangled = false;
                    string mangledSDP = MangleSDP(sipResponse.Body, remoteEndPoint.SocketEndPoint.Address.ToString(), out wasMangled);

                    if (wasMangled)
                    {
                        sipResponse.Body = mangledSDP;
                        sipResponse.Header.ContentLength = sipResponse.Body.Length;
        
                        if (logDelegate != null)
                        {
                            logDelegate(new SIPMonitorControlClientEvent(server, SIPMonitorEventTypesEnum.DialPlan, "SDP mangled for INVITE response from " + sipResponse.RemoteSIPEndPoint.ToString() + ", adjusted address " + remoteEndPoint.SocketEndPoint.Address.ToString() + ".", username));
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MangleSIPResponse. " + excp.Message);
            }
        }
    }
}
