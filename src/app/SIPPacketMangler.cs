// ============================================================================
// FileName: SIPPacketMangler.cs
//
// Description:
// A class containing functionality to mangle the Contact header and SDP payloads
// of SIP messages.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 14 Sep 2008	    Aaron Clauson   Created, Hobart, Australia 
//                                  (most methods extracted from StatefulProxyCore).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPPacketMangler
    {
        private static ILogger logger = Log.Logger;

        public static string MangleSDP(string sdpBody, string publicIPAddress, out bool wasMangled)
        {
            wasMangled = false;

            try
            {
                if (sdpBody != null && publicIPAddress != null)
                {
                    IPAddress addr = SDP.GetSDPRTPEndPoint(sdpBody).Address;
                    //rj2: need to consider publicAddress and IPv6 for mangling
                    IPAddress pubaddr = IPAddress.Parse(publicIPAddress);
                    string sdpAddress = addr.ToString();

                    // Only mangle if there is something to change. For example the server could be on the same private subnet in which case it can't help.
                    if (IPSocket.IsPrivateAddress(sdpAddress) && publicIPAddress != sdpAddress
                        && pubaddr.AddressFamily == AddressFamily.InterNetworkV6
                        && addr.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        string mangledSDP = Regex.Replace(sdpBody, @"c=IN IP6 (?<ipaddress>([:a-fA-F0-9]+))", "c=IN IP6" + publicIPAddress, RegexOptions.Singleline);
                        wasMangled = true;

                        return mangledSDP;
                    }
                    else if (IPSocket.IsPrivateAddress(sdpAddress) && publicIPAddress != sdpAddress
                        && pubaddr.AddressFamily == AddressFamily.InterNetwork
                        && addr.AddressFamily == AddressFamily.InterNetwork)
                    {
                        //logger.LogDebug("MangleSDP replacing private " + sdpAddress + " with " + publicIPAddress + ".");
                        string mangledSDP = Regex.Replace(sdpBody, @"c=IN IP4 (?<ipaddress>(\d+\.){3}\d+)", "c=IN IP4 " + publicIPAddress, RegexOptions.Singleline);
                        wasMangled = true;

                        return mangledSDP;
                    }
                }
                else
                {
                    logger.LogWarning("Mangle SDP was called with an empty body or public IP address.");
                }

                return sdpBody;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception MangleSDP. " + excp.Message);
                return sdpBody;
            }
        }

        /// <summary>
        /// Mangles private IP addresses in a SIP request replacing them with the IP address the packet was received on. 
        /// </summary>
        /// <param name="sipRequest">The unmangled SIP request.</param>
        /// <returns>The mangled SIP request</returns>
        public static void MangleSIPRequest(SIPRequest sipRequest)
        {
            try
            {
                string bottomViaIPAddress = sipRequest.Header.Vias.BottomViaHeader.ReceivedFromIPAddress;

                if (sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count == 1 && bottomViaIPAddress != null)
                {
                    string contactHost = sipRequest.Header.Contact[0].ContactURI.Host;

                    // Only mangle if the host is a private IP address and there is something to change. 
                    // For example the server could be on the same private subnet in which case it can't help.
                    if (IPSocket.IsPrivateAddress(contactHost) && contactHost != bottomViaIPAddress)
                    {
                        string origContact = sipRequest.Header.Contact[0].ContactURI.Host;
                        sipRequest.Header.Contact[0].ContactURI.Host = sipRequest.Header.Vias.BottomViaHeader.ReceivedFromAddress;

                        //logger.LogDebug("Contact URI identified as containing private address for " + sipRequest.Method + " " + origContact + " adjusting to use bottom via " + bottomViaHost + ".");
                        //FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorServerTypesEnum.ContactRegisterInProgress, "Contact on " + sipRequest.Method + " " + origContact + " had private address adjusted to " + bottomViaHost + ".", username));
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

                        logger.LogDebug("SDP mangled for " + sipRequest.Method.ToString() + " request from " + sipRequest.RemoteSIPEndPoint.ToString() + ", adjusted address " + bottomViaIPAddress + ".");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception MangleSIPRequest. " + excp.Message);
            }
        }

        /// <summary>
        /// Mangles private IP addresses in a SIP response replacing them with the IP address the packet was received on. 
        /// </summary>
        /// <param name="sipResponse">The unmangled SIP response.</param>
        /// <returns>The mangled SIP response</returns>
        public static void MangleSIPResponse(SIPResponse sipResponse, SIPEndPoint remoteEndPoint)
        {
            try
            {
                if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
                {
                    string contactHost = sipResponse.Header.Contact[0].ContactURI.Host;

                    // Only mangle if the host is a private IP address and there is something to change. 
                    // For example the server could be on the same private subnet in which case it can't help.
                    if (IPSocket.IsPrivateAddress(contactHost) && contactHost != remoteEndPoint.Address.ToString())
                    {
                        SIPURI origContact = sipResponse.Header.Contact[0].ContactURI;
                        sipResponse.Header.Contact[0].ContactURI = new SIPURI(origContact.Scheme, remoteEndPoint);

                        //logger.LogDebug("INVITE response Contact URI identified as containing private address, original " + origContact + " adjusted to " + remoteEndPoint.ToString() + ".");
                        //FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorServerTypesEnum.ContactRegisterInProgress, "INVITE Response contact adjusted from " + origContact + " to " + remoteEndPoint.ToString() + ".", username));
                    }
                }

                if (sipResponse.Body != null)
                {
                    bool wasMangled = false;
                    string mangledSDP = MangleSDP(sipResponse.Body, remoteEndPoint.Address.ToString(), out wasMangled);

                    if (wasMangled)
                    {
                        sipResponse.Body = mangledSDP;
                        sipResponse.Header.ContentLength = sipResponse.Body.Length;

                        logger.LogDebug("SDP mangled for " + sipResponse.Status.ToString() + " response from " + sipResponse.RemoteSIPEndPoint.ToString() + ", adjusted address " + remoteEndPoint.Address.ToString() + ".");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception MangleSIPResponse. " + excp.Message);
            }
        }

        public static IPAddress GetRequestIPAddress(SIPRequest sipRequest)
        {
            IPAddress requestIPAddress = null;
            string remoteUAStr = sipRequest.Header.ProxyReceivedFrom;
            if (!remoteUAStr.IsNullOrBlank())
            {
                requestIPAddress = SIPEndPoint.ParseSIPEndPoint(remoteUAStr).Address;
            }
            else if (sipRequest.RemoteSIPEndPoint != null)
            {
                requestIPAddress = sipRequest.RemoteSIPEndPoint.Address;
            }
            return requestIPAddress;
        }
    }
}
