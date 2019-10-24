//-----------------------------------------------------------------------------
// Filename: SIPTLSChannel.cs
//
// Description: SIP transport for TLS over TCP.
// 
// History:
// 13 Mar 2009	Aaron Clauson	Created.
// 16 Oct 2019  Aaron Clauson   Added IPv6 support.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPTLSChannel : SIPTCPChannel
    {
        private const string ACCEPT_THREAD_NAME = "siptls-";
        private const string PRUNE_THREAD_NAME = "siptlsprune-";

        private const int MAX_TLS_CONNECTIONS = 1000;              // Maximum number of connections for the TLS listener.

        private X509Certificate2 m_serverCertificate;
        private static object m_writeLock = new object();

        private new ILogger logger = Log.Logger;

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPEndPoint endPoint)
            : base(endPoint, SIPProtocolsEnum.tls)
        {
            if (serverCertificate == null)
            {
                throw new ArgumentNullException("serverCertificate", "An X509 certificate must be supplied for a SIP TLS channel.");
            }

            if (endPoint == null)
            {
                throw new ArgumentNullException("endPoint", "An IP end point must be supplied for a SIP TLS channel.");
            }

            m_isTLS = true;
            m_serverCertificate = serverCertificate;
        }

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPAddress listenAddress, int listenPort) :
            this(serverCertificate, new IPEndPoint(listenAddress, listenPort))
        { }

        /// <summary>
        /// For the TLS channel the SSL stream must be created and any authentication actions undertaken.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly accepted client socket.</param>
        protected override async void OnAccept(SIPStreamConnection streamConnection)
        {
            NetworkStream networkStream = new NetworkStream(streamConnection.StreamSocket, true);
            SslStream sslStream = new SslStream(networkStream, false);

            await sslStream.AuthenticateAsServerAsync(m_serverCertificate);

            logger.LogDebug($"SIP TLS Channel successfully upgraded accepted client to SSL stream for {m_localSIPEndPoint.GetIPEndPoint()}->{streamConnection.StreamSocket.RemoteEndPoint}.");

            //// Display the properties and settings for the authenticated stream.
            ////DisplaySecurityLevel(sslStream);
            ////DisplaySecurityServices(sslStream);
            ////DisplayCertificateInformation(sslStream);
            ////DisplayStreamProperties(sslStream);

            //// Set timeouts for the read and write to 5 seconds.
            //sslStream.ReadTimeout = 5000;
            //sslStream.WriteTimeout = 5000;

            streamConnection.SslStream = sslStream;
            streamConnection.SslStreamBuffer = new byte[2 * SIPConnection.MaxSIPTCPMessageSize];

            sslStream.BeginRead(streamConnection.SslStreamBuffer, 0, SIPConnection.MaxSIPTCPMessageSize, new AsyncCallback(OnReadCallback), streamConnection);
        }

        /// <summary>
        /// For the TLS channel once the TCP client socket is connected it needs to be wrapped up in an SSL stream.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly connected client socket.</param>
        protected override async void OnClientConnect(SIPStreamConnection streamConnection, string serverCertificateName, byte[] buffer)
        {
            try
            {
                NetworkStream networkStream = new NetworkStream(streamConnection.StreamSocket, true);
                SslStream sslStream = new SslStream(networkStream, false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                //DisplayCertificateInformation(sslStream);

                await sslStream.AuthenticateAsClientAsync(serverCertificateName);
                streamConnection.SslStream = sslStream;
                streamConnection.SslStreamBuffer = new byte[2 * SIPConnection.MaxSIPTCPMessageSize];

                logger.LogDebug($"SIP TLS Channel successfully upgraded client connection to SSL stream for {m_localSIPEndPoint.GetIPEndPoint()}->{streamConnection.StreamSocket.RemoteEndPoint}.");

                sslStream.BeginRead(streamConnection.SslStreamBuffer, 0, SIPConnection.MaxSIPTCPMessageSize, new AsyncCallback(OnReadCallback), streamConnection);

                await sslStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPTLSChannel OnClientConnect. {excp.Message}");
            }
        }

        private void OnReadCallback(IAsyncResult ar)
        {
            SIPStreamConnection sipStreamConnection = (SIPStreamConnection)ar.AsyncState;

            try
            {
                int bytesRead = sipStreamConnection.SslStream.EndRead(ar);

                Console.WriteLine($"{bytesRead} for TLS channel.");

                if (sipStreamConnection.ConnectionProps.SocketReadCompleted(bytesRead, sipStreamConnection.SslStreamBuffer))
                {
                    sipStreamConnection.SslStream.BeginRead(sipStreamConnection.SslStreamBuffer, sipStreamConnection.ConnectionProps.RecvEndPosition, sipStreamConnection.SslStreamBuffer.Length - sipStreamConnection.ConnectionProps.RecvEndPosition, new AsyncCallback(OnReadCallback), sipStreamConnection);
                }
            }
            catch (SocketException sockExcp)  // Occurs if the remote end gets disconnected.
            {
                logger.LogWarning($"SocketException SIPTLSChannel ReceiveCallback. Error code {sockExcp.SocketErrorCode}. {sockExcp}");
                SIPTCPSocketDisconnected(sipStreamConnection.ConnectionProps.RemoteEndPoint);
            }
            catch (Exception excp)
            {
                logger.LogWarning($"Exception SIPTLSChannel ReceiveCallback. ${excp.Message}");
                SIPTCPSocketDisconnected(sipStreamConnection.ConnectionProps.RemoteEndPoint);
            }
        }

        protected override async void DoSend(SIPStreamConnection sipStreamConn, byte[] buffer)
        {
            IPEndPoint dstEndPoint = sipStreamConn.ConnectionProps.RemoteEndPoint;

            Console.WriteLine($"SIP TLS Channel sending {buffer.Length} bytes to {dstEndPoint}.");

            try
            {
                await sipStreamConn.SslStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (SocketException sockExcp)
            {
                logger.LogWarning($"SocketException SIP TCP Channel sending to {dstEndPoint}. ErrorCode {sockExcp.SocketErrorCode}. {sockExcp}");
                SIPTCPSocketDisconnected(dstEndPoint);
                throw;
            }
        }

        private X509Certificate GetServerCert()
        {
            //X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            X509Store store = new X509Store(StoreName.CertificateAuthority, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            X509CertificateCollection cert = store.Certificates.Find(X509FindType.FindBySubjectName, "10.0.0.100", true);
            return cert[0];
        }

        private void DisplayCertificateChain(X509Certificate2 certificate)
        {
            X509Chain ch = new X509Chain();
            ch.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            ch.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
            ch.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            ch.Build(certificate);
            Console.WriteLine("Chain Information");
            Console.WriteLine("Chain revocation flag: {0}", ch.ChainPolicy.RevocationFlag);
            Console.WriteLine("Chain revocation mode: {0}", ch.ChainPolicy.RevocationMode);
            Console.WriteLine("Chain verification flag: {0}", ch.ChainPolicy.VerificationFlags);
            Console.WriteLine("Chain verification time: {0}", ch.ChainPolicy.VerificationTime);
            Console.WriteLine("Chain status length: {0}", ch.ChainStatus.Length);
            Console.WriteLine("Chain application policy count: {0}", ch.ChainPolicy.ApplicationPolicy.Count);
            Console.WriteLine("Chain certificate policy count: {0} {1}", ch.ChainPolicy.CertificatePolicy.Count, Environment.NewLine);
            //Output chain element information.
            Console.WriteLine("Chain Element Information");
            Console.WriteLine("Number of chain elements: {0}", ch.ChainElements.Count);
            Console.WriteLine("Chain elements synchronized? {0} {1}", ch.ChainElements.IsSynchronized, Environment.NewLine);

            foreach (X509ChainElement element in ch.ChainElements)
            {
                Console.WriteLine("Element issuer name: {0}", element.Certificate.Issuer);
                Console.WriteLine("Element certificate valid until: {0}", element.Certificate.NotAfter);
                Console.WriteLine("Element certificate is valid: {0}", element.Certificate.Verify());
                Console.WriteLine("Element error status length: {0}", element.ChainElementStatus.Length);
                Console.WriteLine("Element information: {0}", element.Information);
                Console.WriteLine("Number of element extensions: {0}{1}", element.Certificate.Extensions.Count, Environment.NewLine);

                if (ch.ChainStatus.Length > 1)
                {
                    for (int index = 0; index < element.ChainElementStatus.Length; index++)
                    {
                        Console.WriteLine(element.ChainElementStatus[index].Status);
                        Console.WriteLine(element.ChainElementStatus[index].StatusInformation);
                    }
                }
            }
        }

        private void DisplaySecurityLevel(SslStream stream)
        {
            logger.LogDebug(String.Format("Cipher: {0} strength {1}", stream.CipherAlgorithm, stream.CipherStrength));
            logger.LogDebug(String.Format("Hash: {0} strength {1}", stream.HashAlgorithm, stream.HashStrength));
            logger.LogDebug(String.Format("Key exchange: {0} strength {1}", stream.KeyExchangeAlgorithm, stream.KeyExchangeStrength));
            logger.LogDebug(String.Format("Protocol: {0}", stream.SslProtocol));
        }

        private void DisplaySecurityServices(SslStream stream)
        {
            logger.LogDebug(String.Format("Is authenticated: {0} as server? {1}", stream.IsAuthenticated, stream.IsServer));
            logger.LogDebug(String.Format("IsSigned: {0}", stream.IsSigned));
            logger.LogDebug(String.Format("Is Encrypted: {0}", stream.IsEncrypted));
        }

        private void DisplayStreamProperties(SslStream stream)
        {
            logger.LogDebug(String.Format("Can read: {0}, write {1}", stream.CanRead, stream.CanWrite));
            logger.LogDebug(String.Format("Can timeout: {0}", stream.CanTimeout));
        }

        private void DisplayCertificateInformation(SslStream stream)
        {
            logger.LogDebug(String.Format("Certificate revocation list checked: {0}", stream.CheckCertRevocationStatus));

            X509Certificate localCertificate = stream.LocalCertificate;
            if (stream.LocalCertificate != null)
            {
                logger.LogDebug(String.Format("Local cert was issued to {0} and is valid from {1} until {2}.",
                     localCertificate.Subject,
                     localCertificate.GetEffectiveDateString(),
                     localCertificate.GetExpirationDateString()));
            }
            else
            {
                logger.LogWarning("Local certificate is null.");
            }
            // Display the properties of the client's certificate.
            X509Certificate remoteCertificate = stream.RemoteCertificate;
            if (stream.RemoteCertificate != null)
            {
                logger.LogDebug(String.Format("Remote cert was issued to {0} and is valid from {1} until {2}.",
                    remoteCertificate.Subject,
                    remoteCertificate.GetEffectiveDateString(),
                    remoteCertificate.GetExpirationDateString()));
            }
            else
            {
                logger.LogWarning("Remote certificate is null.");
            }
        }

        private bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                logger.LogDebug($"Successfully validated X509 certificate for {certificate.Subject}.");
                return true;
            }
            else
            {
                logger.LogWarning(String.Format("Certificate error: {0}", sslPolicyErrors));
                return true;
            }
        }
    }
}
