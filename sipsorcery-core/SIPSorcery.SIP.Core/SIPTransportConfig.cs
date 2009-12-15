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
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    public static class SIPTransportConfig
    {
        private const string CERTIFICATE_PATH_PARAMETER = "certificatepath";
        private const string CERTIFICATE_TYPE_PARAMETER = "certificatetype";    // Can be file or store, defaults to store.
        private const string SIP_PROTOCOL_PARAMETER = "protocol";

        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPTLSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;
        private static List<IPAddress> m_localIPAddresses = LocalIPConfig.GetLocalIPv4Addresses();

        private static ILog logger = AppState.logger;

        private static readonly string m_allIPAddresses = LocalIPConfig.ALL_LOCAL_IPADDRESSES_KEY;

        public static List<SIPChannel> ParseSIPChannelsNode(XmlNode sipChannelsNode)
        {
            List<SIPChannel> sipChannels = new List<SIPChannel>();

            foreach (XmlNode sipSocketNode in sipChannelsNode.ChildNodes)
            {
                logger.Debug("Creating SIP Channel for " + sipSocketNode.OuterXml + ".");
                
                string localSocket = sipSocketNode.InnerText;

                SIPProtocolsEnum protocol = SIPProtocolsEnum.udp;
                if (sipSocketNode.Attributes.GetNamedItem(SIP_PROTOCOL_PARAMETER) != null)
                {
                    protocol = SIPProtocolsType.GetProtocolType(sipSocketNode.Attributes.GetNamedItem(SIP_PROTOCOL_PARAMETER).Value);
                }
                
                List<SIPEndPoint> nodeSIPEndPoints = GetSIPEndPoints(localSocket, protocol);

                foreach (SIPEndPoint sipEndPoint in nodeSIPEndPoints)
                {
                    try
                    {
                        if (protocol == SIPProtocolsEnum.udp)
                        {
                            logger.Debug(" attempting to create SIP UDP channel for " + sipEndPoint.SocketEndPoint + ".");
                            SIPUDPChannel udpChannel = new SIPUDPChannel(sipEndPoint.SocketEndPoint);
                            sipChannels.Add(udpChannel);
                        }
                        else if (protocol == SIPProtocolsEnum.tcp)
                        {
                            logger.Debug(" attempting to create SIP TCP channel for " + sipEndPoint.SocketEndPoint + ".");

                            SIPTCPChannel tcpChannel = new SIPTCPChannel(sipEndPoint.SocketEndPoint);
                            sipChannels.Add(tcpChannel);
                        }
                        else if (protocol == SIPProtocolsEnum.tls)
                        {
                            if (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER) == null)
                            {
                                logger.Warn("Could not create SIPTLSChannel from XML configuration node as no " + CERTIFICATE_PATH_PARAMETER + " attribute was present.");
                            }
                            else
                            {
                                string certificateType = "machinestore";
                                if (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_TYPE_PARAMETER) != null) {
                                    certificateType = sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_TYPE_PARAMETER).Value;
                                }

                                string certificatePath = (sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER) != null) ? sipSocketNode.Attributes.GetNamedItem(CERTIFICATE_PATH_PARAMETER).Value : null;
                                logger.Debug(" attempting to create SIP TLS channel for " + sipEndPoint.SocketEndPoint + " and certificate type of " + certificateType + " at " + certificatePath + ".");
                                X509Certificate2 certificate = LoadCertificate(certificateType, certificatePath);
                                if(certificate != null) {
                                    SIPTLSChannel tlsChannel = new SIPTLSChannel(certificate, sipEndPoint.SocketEndPoint);
                                    sipChannels.Add(tlsChannel);
                                }
                                else {
                                    logger.Warn("A SIP TLS channel was not created because the certificate could not be loaded.");
                                }
                            }
                        }
                        else
                        {
                            logger.Warn("Could not create a SIP channel for protocol " + protocol + ".");
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Warn("Exception SIPTransportConfig Adding SIP Channel for " + sipEndPoint.ToString() + ". " + excp.Message);
                    }
                }
            }

            return sipChannels;
        }

        private static X509Certificate2 LoadCertificate(string certificateType, string certifcateLocation) {
            try {

                if (certificateType == "file") {
                    X509Certificate2 serverCertificate = new X509Certificate2(certifcateLocation, String.Empty);
                    //DisplayCertificateChain(m_serverCertificate);
                    bool verifyCert = serverCertificate.Verify();
                    logger.Debug("Server Certificate loaded from file, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");
                    return serverCertificate;
                }
                else {
                    X509Store store = (certificateType == "machinestore") ? new X509Store(StoreLocation.LocalMachine) : new X509Store(StoreLocation.CurrentUser);
                    logger.Debug("Certificate store " + store.Location + " opened");
                    store.Open(OpenFlags.OpenExistingOnly);
                    X509Certificate2Collection collection = store.Certificates.Find(X509FindType.FindBySubjectName, certifcateLocation, true);
                    if (collection != null && collection.Count > 0) {
                        X509Certificate2 serverCertificate = collection[0];
                        bool verifyCert = serverCertificate.Verify();
                        logger.Debug("Server Certificate loaded from current user store, Subject=" + serverCertificate.Subject + ", valid=" + verifyCert + ".");
                        return serverCertificate;
                    }
                    else {
                        logger.Warn("Certificate with subject name=" + certifcateLocation + ", not found in " + store.Location + " store.");
                        return null;
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception LoadCertificate. " + excp.Message);
                return null;
            }
        }

        private static List<SIPEndPoint> GetSIPEndPoints(string sipSocketString, SIPProtocolsEnum sipProtocol)
        {
            if (sipSocketString == null)
            {
                return null;
            }
            else
            {
                int port = IPSocket.ParsePortFromSocket(sipSocketString);
                if (port == 0)
                {
                    port = (sipProtocol == SIPProtocolsEnum.tls) ? m_defaultSIPTLSPort : m_defaultSIPPort;
                }

                if (sipSocketString.StartsWith(m_allIPAddresses))
                {
                    List<SIPEndPoint> sipEndPoints = new List<SIPEndPoint>();

                    foreach (IPAddress ipAddress in m_localIPAddresses)
                    {
                        sipEndPoints.Add(new SIPEndPoint(sipProtocol, new IPEndPoint(ipAddress, port)));
                    }

                    return sipEndPoints;
                }
                else
                {
                    IPAddress ipAddress = IPAddress.Parse(IPSocket.ParseHostFromSocket(sipSocketString));
                    return new List<SIPEndPoint>() { new SIPEndPoint(sipProtocol, new IPEndPoint(ipAddress, port)) };
                }
            }
        }
    }
}
