//-----------------------------------------------------------------------------
// Filename: SIPTransportManager.cs
//
// Description: Manages the SIP transport layer. For example
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
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.Sys;

namespace SIPSorcery.SoftPhone
{
    public class SIPTransportManager
    {
        private static int SIP_DEFAULT_PORT = SIPConstants.DEFAULT_SIP_PORT;

        // If set will mirror SIP packets to a Homer (sipcapture.org) logging and analysis server.
        private static string HOMER_SERVER_ADDRESS = null; //"192.168.11.49";
        private static int HOMER_SERVER_PORT = 9060;

        private ILogger logger = Log.Logger;

        private XmlNode m_sipSocketsNode = SIPSoftPhoneState.SIPSocketsNode;    // Optional XML node that can be used to configure the SIP channels used with the SIP transport layer.

        private bool _isInitialised = false;
        public SIPTransport SIPTransport { get; private set; }
        private UdpClient _homerSIPClient;

        /// <summary>
        /// Event to notify the application of a new incoming call request. The event handler
        /// needs to return true if it is prepared to accept the call. If it returns false
        /// then a Busy response will be sent to the caller.
        /// </summary>
        public event Func<SIPRequest, bool> IncomingCall;

        public SIPTransportManager()
        {
            if(HOMER_SERVER_ADDRESS != null)
            {
                _homerSIPClient = new UdpClient(0, AddressFamily.InterNetwork);
            }
        }

        /// <summary>
        /// Shutdown the SIP transport layer and any other resources. Should only be called when the application exits.
        /// </summary>
        public void Shutdown()
        {
            if (SIPTransport != null)
            {
                SIPTransport.Shutdown();
            }
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
                            logger.LogWarning($"Socket exception attempting to bind UDP channel to port {SIP_DEFAULT_PORT}, will use random port. {bindExcp.Message}.");
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
                        }

                        SIPTCPChannel tcpChannel = null;
                        try
                        {
                            tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, udpChannel.Port));
                        }
                        catch (SocketException bindExcp)
                        {
                            logger.LogWarning($"Socket exception attempting to bind TCP channel to port {udpChannel.Port}, will use random port. {bindExcp.Message}.");
                            tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, 0));
                        }

                        SIPTransport.AddSIPChannel(new List<SIPChannel> { udpChannel, tcpChannel });
                    }
                });

                // Wire up the transport layer so incoming SIP requests have somewhere to go.
                SIPTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

                // Log all SIP packets received to a log file.
                SIPTransport.SIPRequestInTraceEvent += SIPRequestInTraceEvent;
                SIPTransport.SIPRequestOutTraceEvent += SIPRequestOutTraceEvent;
                SIPTransport.SIPResponseInTraceEvent += SIPResponseInTraceEvent;
                SIPTransport.SIPResponseOutTraceEvent += SIPResponseOutTraceEvent;
            }
        }

        /// <summary>
        /// Handler for processing incoming SIP requests.
        /// </summary>
        /// <param name="localSIPEndPoint">The end point the request was received on.</param>
        /// <param name="remoteEndPoint">The end point the request came from.</param>
        /// <param name="sipRequest">The SIP request received.</param>
        private Task SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.INFO)
            {
                logger.LogDebug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                return SIPTransport.SendResponseAsync(notAllowedResponse);
            }
            else if (sipRequest.Header.From != null &&
                sipRequest.Header.From.FromTag != null &&
                sipRequest.Header.To != null &&
                sipRequest.Header.To.ToTag != null)
            {
                // This is an in-dialog request that will be handled directly by a user agent instance.
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                bool? callAccepted = IncomingCall?.Invoke(sipRequest);

                if (callAccepted == false)
                {
                    // All user agents were already on a call return a busy response.
                    UASInviteTransaction uasTransaction = new UASInviteTransaction(SIPTransport, sipRequest, null);
                    SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                    uasTransaction.SendFinalResponse(busyResponse);
                }
            }
            else
            {
                logger.LogDebug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                return SIPTransport.SendResponseAsync(notAllowedResponse);
            }

            return Task.FromResult(0);
        }

        private void SIPRequestInTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest sipRequest)
        {
            logger.LogDebug($"Request Received {localEP}<-{remoteEP}: {sipRequest.StatusLine}.");

            if (_homerSIPClient != null)
            {
                var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, 333, "myHep", sipRequest.ToString());
                _homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
            }
        }

        private void SIPRequestOutTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest sipRequest)
        {
            logger.LogDebug($"Request Sent {localEP}<-{remoteEP}: {sipRequest.StatusLine}.");

            if (_homerSIPClient != null)
            {
                var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now, 333, "myHep", sipRequest.ToString());
                _homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
            }
        }

        private void SIPResponseInTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPResponse sipResponse)
        {
            logger.LogDebug($"Response Received {localEP}<-{remoteEP}: {sipResponse.ShortDescription}.");

            if (_homerSIPClient != null)
            {
                var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, 333, "myHep", sipResponse.ToString());
                _homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
            }
        }

        private void SIPResponseOutTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPResponse sipResponse)
        {
            logger.LogDebug($"Response Sent {localEP}<-{remoteEP}: {sipResponse.ShortDescription}.");

            if (_homerSIPClient != null)
            {
                var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now, 333, "myHep", sipResponse.ToString());
                _homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
            }
        }
    }
}
