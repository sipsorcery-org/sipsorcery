//-----------------------------------------------------------------------------
// Filename: SIPTLSChannel.cs
//
// Description: SIP transport for TLS over TCP.
// 
// History:
// 13 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public class SIPTLSChannel : SIPChannel
    {
        private const string ACCEPT_THREAD_NAME = "siptls-";
        private const string PRUNE_THREAD_NAME = "siptlsprune-";

        private const int MAX_TLS_CONNECTIONS = 1000;               // Maximum number of connections for the TLS listener.
        //private const int MAX_TLS_CONNECTIONS_PER_IPADDRESS = 10;   // Maximum number of connections allowed for a single remote IP address.
        private static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;

        private TcpListener m_tlsServerListener;
        //private bool m_closed = false;
        private Dictionary<string, SIPConnection> m_connectedSockets = new Dictionary<string, SIPConnection>();
        private List<IPEndPoint> m_connectingSockets = new List<IPEndPoint>(); // List of connecting sockets to avoid SIP re-transmits initiating multiple connect attempts.

        //private string m_certificatePath;
        private X509Certificate2 m_serverCertificate;

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPEndPoint endPoint)
        {
            if (serverCertificate == null)
            {
                throw new ArgumentNullException("serverCertificate", "An X509 certificate must be supplied for a SIP TLS channel.");
            }

            if (endPoint == null)
            {
                throw new ArgumentNullException("endPoint", "An IP end point must be supplied for a SIP TLS channel.");
            }

            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tls, endPoint);
            m_isReliable = true;
            m_isTLS = true;
            //m_certificatePath = certificateFileName;
            ///base.Name = "s" + Crypto.GetRandomInt(4);
            m_serverCertificate = serverCertificate;
            Initialise();
        }

        private void Initialise()
        {
            try
            {
                m_tlsServerListener = new TcpListener(m_localSIPEndPoint.SocketEndPoint);
                m_tlsServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                ThreadPool.QueueUserWorkItem(delegate { AcceptConnections(ACCEPT_THREAD_NAME + m_localSIPEndPoint.SocketEndPoint.Port); });
                ThreadPool.QueueUserWorkItem(delegate { PruneConnections(PRUNE_THREAD_NAME + m_localSIPEndPoint.SocketEndPoint.Port); });

                logger.Debug("SIP TLS Channel listener created " + m_localSIPEndPoint.SocketEndPoint + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel Initialise. " + excp.Message);
                throw;
            }
        }

        private void AcceptConnections(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                //m_sipConn.Listen(MAX_TCP_CONNECTIONS);
                m_tlsServerListener.Start(MAX_TLS_CONNECTIONS);

                logger.Debug("SIPTLSChannel socket on " + m_localSIPEndPoint + " listening started.");

                while (!Closed)
                {
                    TcpClient tcpClient = m_tlsServerListener.AcceptTcpClient();
                    tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    IPEndPoint remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                    logger.Debug("SIP TLS Channel connection accepted from " + remoteEndPoint + ".");

                    SslStream sslStream = new SslStream(tcpClient.GetStream(), false);

                    try
                    {
                        sslStream.AuthenticateAsServer(m_serverCertificate, false, SslProtocols.Tls, false);
                        // Display the properties and settings for the authenticated stream.
                        //DisplaySecurityLevel(sslStream);
                        //DisplaySecurityServices(sslStream);
                        //DisplayCertificateInformation(sslStream);
                        //DisplayStreamProperties(sslStream);

                        // Set timeouts for the read and write to 5 seconds.
                        sslStream.ReadTimeout = 5000;
                        sslStream.WriteTimeout = 5000;

                        SIPConnection sipTLSConnection = new SIPConnection(this, sslStream, remoteEndPoint, SIPProtocolsEnum.tls, SIPConnectionsEnum.Listener);
                        m_connectedSockets.Add(remoteEndPoint.ToString(), sipTLSConnection);

                        sipTLSConnection.SIPSocketDisconnected += SIPTLSSocketDisconnected;
                        sipTLSConnection.SIPMessageReceived += SIPTLSMessageReceived;
                        //byte[] receiveBuffer = new byte[MaxSIPTCPMessageSize];
                        sipTLSConnection.SIPStream.BeginRead(sipTLSConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), sipTLSConnection);
                    }
                    catch (Exception e)
                    {
                        logger.Error("SIPTLSChannel AuthenticationException. " + e.Message);
                        sslStream.Close();
                        tcpClient.Close();
                    }
                }

                logger.Debug("SIPTLSChannel socket on " + m_localSIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel Listen. " + excp.Message);
                //throw excp;
            }
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            SIPConnection sipTLSConnection = (SIPConnection)ar.AsyncState;

            try
            {
                int bytesRead = sipTLSConnection.SIPStream.EndRead(ar);
                if (sipTLSConnection.SocketReadCompleted(bytesRead))
                {
                    sipTLSConnection.SIPStream.BeginRead(sipTLSConnection.SocketBuffer, sipTLSConnection.SocketBufferEndPosition, MaxSIPTCPMessageSize - sipTLSConnection.SocketBufferEndPosition, new AsyncCallback(ReceiveCallback), sipTLSConnection);
                }
            }
            catch (SocketException)  // Occurs if the remote end gets disconnected.
            { }
            catch (Exception excp)
            {
                logger.Warn("Exception SIPTLSChannel ReceiveCallback. " + excp.Message);
                SIPTLSSocketDisconnected(sipTLSConnection.RemoteEndPoint);
            }
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer)
        {
            Send(dstEndPoint, buffer, null);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            try
            {
                if (buffer == null)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTLSChannel.");
                }
                else
                {
                    bool sent = false;

                    // Lookup a client socket that is connected to the destination.
                    //m_sipConn(buffer, buffer.Length, destinationEndPoint);
                    if (m_connectedSockets.ContainsKey(dstEndPoint.ToString()))
                    {
                        SIPConnection sipTLSClient = m_connectedSockets[dstEndPoint.ToString()];

                        try
                        {
                            sipTLSClient.SIPStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(EndSend), sipTLSClient);
                            sent = true;
                            sipTLSClient.LastTransmission = DateTime.Now;
                        }
                        catch (SocketException)
                        {
                            logger.Warn("Could not send to TLS socket " + dstEndPoint + ", closing and removing.");
                            sipTLSClient.SIPStream.Close();
                            m_connectedSockets.Remove(dstEndPoint.ToString());
                        }
                    }

                    if (!sent)
                    {
                        if (serverCertificateName.IsNullOrBlank())
                        {
                            throw new ApplicationException("The SIP TLS Channel must be provided with the name of the expected server certificate, please use alternative method.");
                        }

                        if (!m_connectingSockets.Contains(dstEndPoint))
                        {

                            logger.Debug("Attempting to establish TLS connection to " + dstEndPoint + ".");
                            TcpClient tcpClient = new TcpClient();
                            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            tcpClient.Client.Bind(m_localSIPEndPoint.SocketEndPoint);

                            m_connectingSockets.Add(dstEndPoint);
                            tcpClient.BeginConnect(dstEndPoint.Address, dstEndPoint.Port, EndConnect, new object[] { tcpClient, dstEndPoint, buffer, serverCertificateName });
                        }
                        else
                        {
                            logger.Warn("Could not send SIP packet to TLS " + dstEndPoint + " and another connection was already in progress so dropping message.");
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception (" + excp.GetType().ToString() + ") SIPTLSChannel Send (sendto=>" + dstEndPoint + "). " + excp.Message);
                throw excp;
            }
        }

        private void EndSend(IAsyncResult ar)
        {
            try
            {
                SIPConnection sipConnection = (SIPConnection)ar.AsyncState;
                sipConnection.SIPStream.EndWrite(ar);
            }
            catch (Exception excp)
            {
                logger.Error("Exception EndSend. " + excp.Message);
            }
        }

        private void EndConnect(IAsyncResult ar)
        {
            try
            {
                object[] stateObj = (object[])ar.AsyncState;
                TcpClient tcpClient = (TcpClient)stateObj[0];
                IPEndPoint dstEndPoint = (IPEndPoint)stateObj[1];
                byte[] buffer = (byte[])stateObj[2];
                string serverCN = (string)stateObj[3];

                m_connectedSockets.Remove(dstEndPoint.ToString());

                SslStream sslStream = new SslStream(tcpClient.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                //DisplayCertificateInformation(sslStream);
                sslStream.AuthenticateAsClient(serverCN);

                tcpClient.EndConnect(ar);

                if (tcpClient != null && tcpClient.Connected)
                {

                    SIPConnection callerConnection = new SIPConnection(this, sslStream, dstEndPoint, SIPProtocolsEnum.tls, SIPConnectionsEnum.Caller);
                    m_connectedSockets.Add(dstEndPoint.ToString(), callerConnection);

                    callerConnection.SIPSocketDisconnected += SIPTLSSocketDisconnected;
                    callerConnection.SIPMessageReceived += SIPTLSMessageReceived;
                    //byte[] receiveBuffer = new byte[MaxSIPTCPMessageSize];
                    callerConnection.SIPStream.BeginRead(callerConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), callerConnection);

                    logger.Debug("Established TLS connection to " + dstEndPoint + ".");

                    callerConnection.SIPStream.BeginWrite(buffer, 0, buffer.Length, EndSend, callerConnection);
                }
                else
                {
                    logger.Warn("Could not establish TLS connection to " + dstEndPoint + ".");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel EndConnect. " + excp.Message);
            }
        }

        protected override Dictionary<string, SIPConnection> GetConnectionsList()
        {
            return m_connectedSockets;
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            lock (m_connectedSockets)
            {
                return m_connectedSockets.ContainsKey(remoteEndPoint.ToString());
            }
        }

        private void SIPTLSSocketDisconnected(IPEndPoint remoteEndPoint)
        {
            try
            {
                logger.Debug("TLS socket from " + remoteEndPoint + " disconnected.");

                lock (m_connectedSockets)
                {
                    m_connectedSockets.Remove(remoteEndPoint.ToString());
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSClientDisconnected. " + excp.Message);
            }
        }

        private void SIPTLSMessageReceived(SIPChannel channel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (SIPMessageReceived != null)
            {
                SIPMessageReceived(channel, remoteEndPoint, buffer);
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
            logger.Debug(String.Format("Cipher: {0} strength {1}", stream.CipherAlgorithm, stream.CipherStrength));
            logger.Debug(String.Format("Hash: {0} strength {1}", stream.HashAlgorithm, stream.HashStrength));
            logger.Debug(String.Format("Key exchange: {0} strength {1}", stream.KeyExchangeAlgorithm, stream.KeyExchangeStrength));
            logger.Debug(String.Format("Protocol: {0}", stream.SslProtocol));
        }

        private void DisplaySecurityServices(SslStream stream)
        {
            logger.Debug(String.Format("Is authenticated: {0} as server? {1}", stream.IsAuthenticated, stream.IsServer));
            logger.Debug(String.Format("IsSigned: {0}", stream.IsSigned));
            logger.Debug(String.Format("Is Encrypted: {0}", stream.IsEncrypted));
        }

        private void DisplayStreamProperties(SslStream stream)
        {
            logger.Debug(String.Format("Can read: {0}, write {1}", stream.CanRead, stream.CanWrite));
            logger.Debug(String.Format("Can timeout: {0}", stream.CanTimeout));
        }

        private void DisplayCertificateInformation(SslStream stream)
        {
            logger.Debug(String.Format("Certificate revocation list checked: {0}", stream.CheckCertRevocationStatus));

            X509Certificate localCertificate = stream.LocalCertificate;
            if (stream.LocalCertificate != null)
            {
                logger.Debug(String.Format("Local cert was issued to {0} and is valid from {1} until {2}.",
                     localCertificate.Subject,
                     localCertificate.GetEffectiveDateString(),
                     localCertificate.GetExpirationDateString()));
            }
            else
            {
                logger.Warn("Local certificate is null.");
            }
            // Display the properties of the client's certificate.
            X509Certificate remoteCertificate = stream.RemoteCertificate;
            if (stream.RemoteCertificate != null)
            {
                logger.Debug(String.Format("Remote cert was issued to {0} and is valid from {1} until {2}.",
                    remoteCertificate.Subject,
                    remoteCertificate.GetEffectiveDateString(),
                    remoteCertificate.GetExpirationDateString()));
            }
            else
            {
                logger.Warn("Remote certificate is null.");
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
                return true;
            }
            else
            {
                logger.Warn(String.Format("Certificate error: {0}", sslPolicyErrors));
                return true;
            }
        }

        public override void Close()
        {
            logger.Debug("Closing SIP TLS Channel " + SIPChannelEndPoint + ".");

            Closed = true;

            try
            {
                m_tlsServerListener.Stop();
            }
            catch (Exception listenerCloseExcp)
            {
                logger.Warn("Exception SIPTLSChannel Close (shutting down listener). " + listenerCloseExcp.Message);
            }

            foreach (SIPConnection tcpConnection in m_connectedSockets.Values)
            {
                try
                {
                    tcpConnection.SIPStream.Close();
                }
                catch (Exception connectionCloseExcp)
                {
                    logger.Warn("Exception SIPTLSChannel Close (shutting down connection to " + tcpConnection.RemoteEndPoint + "). " + connectionCloseExcp.Message);
                }
            }
        }

        private void Dispose(bool disposing)
        {
            try
            {
                this.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception Disposing SIPTLSChannel. " + excp.Message);
            }
        }

        #region Unit testing.

#if UNITTEST
	
		[TestFixture]
		public class SIPTLSChannelUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);	
				Assert.IsTrue(true, "True was false.");
			}
		}

#endif

        #endregion
    }
}
