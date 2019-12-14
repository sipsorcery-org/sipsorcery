//-----------------------------------------------------------------------------
// Filename: SIPTransportManager.cs
//
// Description: Manages the SIP tranpsort layer. For example
// for incoming calls needs to manage which client can accept it. 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 14 Dec 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Xml;
using log4net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.Sys;

namespace SIPSorcery.SoftPhone
{
    public class SIPTransportManager
    {
        private static int SIP_DEFAULT_PORT = SIPConstants.DEFAULT_SIP_PORT;

        private ILog logger = AppState.logger;

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;    // Optional XML node that can be used to configure the SIP channels used with the SIP transport layer.
        private string m_DnsServer = SIPSoftPhoneState.DnsServer;

        private bool _isInitialised = false;
        public SIPTransport SIPTransport { get; private set; }

        /// <summary>
        /// Event to notify the application of a new incoming call request. The event handler
        /// needs to return true if it is prepared to accept the call. If it returns false
        /// then a Busy response will be sent to the caller.
        /// </summary>
        public event Func<SIPRequest, bool> IncomingCall;

        public SIPTransportManager()
        { }

        /// <summary>
        /// Shutdown the SIP tranpsort layer and any other resources. Should only be called when the application exits.
        /// </summary>
        public void Shutdown()
        {
            if (SIPTransport != null)
            {
                SIPTransport.Shutdown();
            }

            DNSManager.Stop();
        }

        /// <summary>
        /// Initialises the SIP transport layer.
        /// </summary>
        public async Task InitialiseSIP()
        {
            if (_isInitialised == false)
            {
                await Task.Run(() =>
                {
                    _isInitialised = true;

                    if (String.IsNullOrEmpty(m_DnsServer) == false)
                    {
                        // Use a custom DNS server.
                        m_DnsServer = m_DnsServer.Contains(":") ? m_DnsServer : m_DnsServer + ":53";
                        DNSManager.SetDNSServers(new List<IPEndPoint> { IPSocket.ParseSocketString(m_DnsServer) });
                    }

                    // Configure the SIP transport layer.
                    SIPTransport = new SIPTransport();
                    bool sipChannelAdded = false;

                    if (m_sipSocketsNode != null)
                    {
                        // Set up the SIP channels based on the app.config file.
                        List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipSocketsNode);
                        if (sipChannels?.Count > 0)
                        {
                            SIPTransport.AddSIPChannel(sipChannels);
                            sipChannelAdded = true;
                        }
                    }

                    if (sipChannelAdded == false)
                    {
                        // Use default options to set up a SIP channel.
                        SIPUDPChannel udpChannel = null;
                        try
                        {
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_DEFAULT_PORT));
                        }
                        catch (SocketException bindExcp)
                        {
                            logger.Warn($"Socket exception attempting to bind UDP channel to port {SIP_DEFAULT_PORT}, will use random port. {bindExcp.Message}.");
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
                        }
                        var tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, udpChannel.Port));
                        SIPTransport.AddSIPChannel(new List<SIPChannel> { udpChannel, tcpChannel });
                    }
                });

                // Wire up the transport layer so incoming SIP requests have somewhere to go.
                SIPTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

                // Log all SIP packets received to a log file.
                SIPTransport.SIPRequestInTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { logger.Debug("Request Received : " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipRequest.ToString()); };
                SIPTransport.SIPRequestOutTraceEvent += (localSIPEndPoint, endPoint, sipRequest) => { logger.Debug("Request Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipRequest.ToString()); };
                SIPTransport.SIPResponseInTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { logger.Debug("Response Received: " + localSIPEndPoint + "<-" + endPoint + "\r\n" + sipResponse.ToString()); };
                SIPTransport.SIPResponseOutTraceEvent += (localSIPEndPoint, endPoint, sipResponse) => { logger.Debug("Response Sent: " + localSIPEndPoint + "->" + endPoint + "\r\n" + sipResponse.ToString()); };
            }
        }

        /// <summary>
        /// Handler for processing incoming SIP requests.
        /// </summary>
        /// <param name="localSIPEndPoint">The end point the request was received on.</param>
        /// <param name="remoteEndPoint">The end point the request came from.</param>
        /// <param name="sipRequest">The SIP request received.</param>
        private void SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Header.From != null &&
                sipRequest.Header.From.FromTag != null &&
                sipRequest.Header.To != null &&
                sipRequest.Header.To.ToTag != null)
            {
                // This is an in-dialog request that will be handled directly by a user agent instance.
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                bool? callAccepted = IncomingCall?.Invoke(sipRequest);

                if(callAccepted == false)
                {
                    // All user agents were already on a call return a busy response.
                    UASInviteTransaction uasTransaction = new UASInviteTransaction(SIPTransport, sipRequest, null);
                    SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                    uasTransaction.SendFinalResponse(busyResponse);
                }
            }
            else
            {
                logger.Debug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                SIPTransport.SendResponse(notAllowedResponse);
            }
        }
    }
}
