//-----------------------------------------------------------------------------
// Filename: SIPTransportConfig.cs
//
// Description: Provides functions to configure the SIP Transport channels from 
// an XML configuration node.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 25 Mar 2009	Aaron Clauson	Created, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public static class SIPTransportConfig
    {
        private const string CERTIFICATE_PATH_PARAMETER = "certificatepath";
        private const string CERTIFICATE_TYPE_PARAMETER = "certificatetype";    // Can be file or store, defaults to store.
        private const string CERTIFICATE_KEY_PASSWORD_PARAMETER = "certificatekeypassword";
        private const string SIP_PROTOCOL_PARAMETER = "protocol";
        private const string ALL_LOCAL_IPADDRESSES_KEY = "*";

        private const int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private const int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;

        private static readonly ILogger logger = LogFactory.CreateLogger("SIPSorcery.Core");

        public static List<SIPChannel> ParseSIPChannelsNode(XmlNode sipChannelsNode, int port = 0)
        {
            var sipChannels = new List<SIPChannel>();

            foreach (XmlNode sipSocketNode in sipChannelsNode.ChildNodes)
            {
                logger.LogSipChannelCreating(sipSocketNode.OuterXml);

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
                                    var endPoint = sipEndPoint.GetIPEndPoint();
                                    logger.LogSipChannelCreateAttempt("UDP", endPoint);
                                    var udpChannel = new SIPUDPChannel(endPoint);
                                    sipChannels.Add(udpChannel);
                                }
                                break;
                            case SIPProtocolsEnum.tcp:
                                {
                                    var endPoint = sipEndPoint.GetIPEndPoint();
                                    logger.LogSipChannelCreateAttempt("TCP", endPoint);
                                    var tcpChannel = new SIPTCPChannel(endPoint);
                                    sipChannels.Add(tcpChannel);
                                }
                                break;
                            case SIPProtocolsEnum.tls:
                                if (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER) == null)
                                {
                                    logger.LogSipTlsCertificatePathMissing(CERTIFICATE_PATH_PARAMETER);
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
                                    var endPoint = sipEndPoint.GetIPEndPoint();
                                    logger.LogSipTlsChannelCreateAttempt(endPoint, certificateType, certificatePath);
                                    var certificate = LoadCertificate(certificateType, certificatePath, certificateKeyPassword);
                                    if (certificate != null)
                                    {
                                        var tlsChannel = new SIPTLSChannel(certificate, endPoint);
                                        sipChannels.Add(tlsChannel);
                                    }
                                    else
                                    {
                                        logger.LogSipTlsChannelNotCreated();
                                    }
                                }
                                break;
                            default:
                                logger.LogUnknownProtocolWarning(protocol);
                                break;
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogExceptionAddingSipChannel(sipEndPoint, excp);
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
#if NET9_0_OR_GREATER
                    var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(certifcateLocation, certKeyPassword);
#else
                    var serverCertificate = new X509Certificate2(certifcateLocation, certKeyPassword);
#endif
                    //DisplayCertificateChain(m_serverCertificate);
                    var verifyCert = serverCertificate.Verify();
                    logger.LogServerCertificateLoaded(serverCertificate.Subject, verifyCert);
                    return serverCertificate;
                }

                var store = (certificateType == "machinestore") ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
                return Crypto.LoadCertificate(store, certifcateLocation, true);
            }
            catch (Exception excp)
            {
                logger.LogExceptionLoadCertificate(excp.Message, excp);
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

            if (sipSocketString.StartsWith(ALL_LOCAL_IPADDRESSES_KEY))
            {
                return new List<SIPEndPoint> { new SIPEndPoint(sipProtocol, new IPEndPoint(IPAddress.Any, port)) };
            }
            else
            {
                var ipAddress = IPAddress.Parse(IPSocket.ParseHostFromSocket(sipSocketString));
                return new List<SIPEndPoint> { new SIPEndPoint(sipProtocol, new IPEndPoint(ipAddress, port)) };
            }
        }
    }
}
