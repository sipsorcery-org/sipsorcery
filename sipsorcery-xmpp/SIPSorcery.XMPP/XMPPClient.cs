//-----------------------------------------------------------------------------
// Filename: XMPPClient.cs
//
// Description: Represents the top level abstraction that can be used to initiate
// an XMPP client connection.
// 
// History:
// 13 Nov 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), Hobart, Tasmania, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery. 
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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.XMPP
{
    public class XMPPClient
    {
        public const int SSL_READWRITE_TIMEOUT = 600000;

        private static ILog logger = AppState.logger;

        public Action IsBound;
        public Action Disconnected;

        private string m_host;
        private int m_port;
        private string m_server;
        private string m_fromUsername;
        private string m_saslToken;
        private TcpClient m_tcpClient;
        private SslStream m_sslStream;
        private WrappedStream m_tcpWrappedStream;
        private WrappedStream m_sslWrappedStream;
        private WrappedStream m_authWrappedStream;
        private XMPPAuthenticatedStream m_authenticatedStream;

        public XMPPClient(string host, int port, string server, string username, string password)
        {
            m_host = host;
            m_port = port;
            m_server = server;
            m_fromUsername = username;
            m_saslToken = GetPlainSASLResponse(username, password);
        }

        public void Connect()
        {
            try
            {
                m_tcpClient = new TcpClient(m_host, m_port);

                logger.Debug("XMPP client is connected to " + m_host + ":" + m_port + ".");

                m_tcpWrappedStream = new WrappedStream(m_tcpClient.GetStream());
                XMPPInitialStream initialStream = new XMPPInitialStream(m_tcpWrappedStream);
                initialStream.Start(m_server, null, null);

                m_tcpWrappedStream.BlockIO();
                XMPPStreamTLSRequired();
                m_sslWrappedStream.BlockIO();
                XMPPStreamAuthenticated();
            }
            catch (Exception excp)
            {
                logger.Error("Exception XMPPClient.Connect. " + excp.Message);
            }
            finally
            {
                if (Disconnected != null)
                {
                    Disconnected();
                }
            }
        }

        public XMPPJingleRequest GetJingleRequest(string to)
        {
            return new XMPPJingleRequest(m_authenticatedStream, to);
        }

        public XMPPMessageRequest GetMessageRequest(string to)
        {
            return new XMPPMessageRequest(m_authenticatedStream, to);
        }
        public XMPPPresenceRequest GetPresenceRequest()
        {
            return new XMPPPresenceRequest(m_authenticatedStream);
        }

        public XMPPRosterRequest GetRosterRequest()
        {
            return new XMPPRosterRequest(m_authenticatedStream);
        }

        public XMPPServiceDiscoveryRequest GetServiceDiscoveryRequest(string to)
        {
            return new XMPPServiceDiscoveryRequest(m_authenticatedStream, to);
        }

        public XMPPPhoneSession GetPhoneSession()
        {
            return new XMPPPhoneSession(m_authenticatedStream.JID, m_authenticatedStream);
        }

        private void XMPPStreamTLSRequired()
        {
            logger.Debug("XMPP client initiating TLS connection.");

            m_sslStream = new SslStream(m_tcpClient.GetStream(), false, ValidateServerCertificate, null);
            m_sslStream.ReadTimeout = SSL_READWRITE_TIMEOUT;
            m_sslStream.WriteTimeout = SSL_READWRITE_TIMEOUT;
            m_sslStream.AuthenticateAsClient(m_server);
            m_sslWrappedStream = new WrappedStream(m_sslStream);
            XMPPEncryptedStream encryptedStream = new XMPPEncryptedStream(m_sslWrappedStream);
            encryptedStream.Start(m_server, m_saslToken, m_fromUsername);
        }

        private void XMPPStreamAuthenticated()
        {
            logger.Debug("XMPP client now authenticated as " + m_fromUsername + ", initiating binding.");

            m_authWrappedStream = new WrappedStream(m_sslStream);
            m_authenticatedStream = new XMPPAuthenticatedStream(m_authWrappedStream);
            m_authenticatedStream.IsBound = IsBound;
            m_authenticatedStream.Start(m_server, null, m_fromUsername);
        }

        public static bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            logger.Debug(String.Format("XMPP Server Certificate: {0}", certificate.Subject));
            return true;
        }

        private static string GetPlainSASLResponse(string username, string password)
        {
            StringBuilder respnose = new StringBuilder();

            respnose.Append((char)0);
            respnose.Append(username);
            respnose.Append((char)0);
            respnose.Append(password);

            byte[] encode = Encoding.Default.GetBytes(respnose.ToString());
            return Convert.ToBase64String(encode, 0, encode.Length);
        }
    }
}
