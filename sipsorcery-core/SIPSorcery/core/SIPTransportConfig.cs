//-----------------------------------------------------------------------------
// Filename: SIPTransportConfig.cs
//
// Description: Provides functions to configure the SIP Transport channels from an XML configuration node.
//
// History:
// 25 Mar 2009	Aaron Clauson	Created.
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

using System.Linq;
using log4net;
using SIPSorcery.Sys;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace SIPSorcery.SIP
{
    public static class SIPTransportConfig
    {
        private const string CERTIFICATE_PATH_PARAMETER = "certificatepath";
        private const string CERTIFICATE_TYPE_PARAMETER = "certificatetype";    // Can be file or store, defaults to store.
        private const string CERTIFICATE_KEY_PASSWORD_PARAMETER = "certificatekeypassword";
        private const string SIP_PROTOCOL_PARAMETER = "protocol";

        private const int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private const int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;
        private static readonly List<IPAddress> m_localIPAddresses = LocalIPConfig.GetLocalIPv4Addresses();

        private static readonly ILog logger = AppState.logger;

        private const string m_allIPAddresses = LocalIPConfig.ALL_LOCAL_IPADDRESSES_KEY;

        public static List<SIPChannel> ParseSIPChannelsNode(XmlNode sipChannelsNode, int port = 0)
        {
            var sipChannels = new List<SIPChannel>();

            foreach (XmlNode sipSocketNode in sipChannelsNode.ChildNodes)
            {
                logger.Debug("Creating SIP Channel for " + sipSocketNode.OuterXml + ".");

                var localSocket = sipSocketNode.InnerText;

                var protocol = SIPProtocolsEnum.udp;
                if (sipSocketNode.Attributes.GetNamedItem(SIP_PROTOCOL_PARAMETER) != null)
                {
                    protocol = SIPProtocolsType.GetProtocolType(sipSocketNode.Attributes.GetNamedItem(SIP_PROTOCOL_PARAMETER).Value);
                }

                var nodeSIPEndPoints = GetSIPEndPoints(localSocket, protocol, port);

                foreach (var sipEndPoint in nodeSIPEndPoints)
                {
                    try
                    {
                        switch (protocol)
                        {
                            case SIPProtocolsEnum.udp:
                            {
                                logger.Debug(" attempting to create SIP UDP channel for " + sipEndPoint.GetIPEndPoint() + ".");
                                var udpChannel = new SIPUDPChannel(sipEndPoint.GetIPEndPoint());
                                sipChannels.Add(udpChannel);
                            }
                                break;
                            case SIPProtocolsEnum.tcp:
                            {
                                logger.Debug(" attempting to create SIP TCP channel for " + sipEndPoint.GetIPEndPoint() + ".");

                                var tcpChannel = new SIPTCPChannel(sipEndPoint.GetIPEndPoint());
                                sipChannels.Add(tcpChannel);
                            }
                                break;
                            case SIPProtocolsEnum.tls:
                                if (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER) == null)
                                {
                                    logger.Warn("Could not create SIPTLSChannel from XML configuration node as no " + CERTIFICATE_PATH_PARAMETER + " attribute was present.");
                                }
                                else
                                {
                                    var certificateType = "machinestore";
                                    if (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_TYPE_PARAMETER) != null)
                                    {
                                        certificateType = sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_TYPE_PARAMETER).Value;
                                    }

                                    var certificatePath = (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER) != null) ? sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER).Value : null;
                                    var certificateKeyPassword = (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_KEY_PASSWORD_PARAMETER) != null) ? sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_KEY_PASSWORD_PARAMETER).Value : String.Empty;
                                    logger.Debug(" attempting to create SIP TLS channel for " + sipEndPoint.GetIPEndPoint() + " and certificate type of " + certificateType + " at " + certificatePath + ".");
                                    var certificate = LoadCertificate(certificateType, certificatePath, certificateKeyPassword);
                                    if (certificate != null)
                                    {
                                        var tlsChannel = new SIPTLSChannel(certificate, sipEndPoint.GetIPEndPoint());
                                        sipChannels.Add(tlsChannel);
                                    }
                                    else
                                    {
                                        logger.Warn("A SIP TLS channel was not created because the certificate could not be loaded.");
                                    }
                                }
                                break;
                            default:
                                logger.Warn("Could not create a SIP channel for protocol " + protocol + ".");
                                break;
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Warn("Exception SIPTransportConfig Adding SIP Channel for " + sipEndPoint + ". " + excp.Message);
                    }
                }
            }

            return sipChannels;
        }

        private static X509Certificate2 LoadCertificate(string certificateType, string certifcateLocation, string certKeyPassword)
        {
            try
            {

                if (certificateType == "file")
                {
                    var serverCertificate = new X509Certificate2(certifcateLocation, certKeyPassword);
                    //DisplayCertificateChain(m_serverCertificate);
                    var verifyCert = serverCertificate.Verify();
                    logger.Debug("Server Certificate loaded from file, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");
                    return serverCertificate;
                }
                
                var store = (certificateType == "machinestore") ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
                return AppState.LoadCertificate(store, certifcateLocation, true);
            }
            catch (Exception excp)
            {
                logger.Error("Exception LoadCertificate. " + excp.Message);
                return null;
            }
        }

        private static IEnumerable<SIPEndPoint> GetSIPEndPoints(string sipSocketString, SIPProtocolsEnum sipProtocol, int overridePort)
        {
            if (sipSocketString == null)
            {
                return null;
            }
            
            int port;
            if (overridePort > 0)
            {
                port = overridePort;
            }
            else
            {
                port = IPSocket.ParsePortFromSocket(sipSocketString);
                if (port == 0)
                {
                    port = (sipProtocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort;
                }
            }

            if (sipSocketString.StartsWith(m_allIPAddresses))
            {
                return m_localIPAddresses.Select(x => new SIPEndPoint(sipProtocol, new IPEndPoint(x, port)));
            }
            
            var ipAddress = IPAddress.Parse(IPSocket.ParseHostFromSocket(sipSocketString));
            return new List<SIPEndPoint> { new SIPEndPoint(sipProtocol, new IPEndPoint(ipAddress, port)) };
        }
    }
}
