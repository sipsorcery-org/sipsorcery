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
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    public delegate bool SIPTLSChannelInboundCertificateValidationCallback(SIPTLSChannel channel, IPEndPoint remoteEndpoint, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors);
    public delegate bool SIPTLSChannelOutboundCertificateValidationCallback(SIPTLSChannel channel, IPEndPoint remoteEndpoint, string serverFQDN, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors);

    public class SIPTLSChannel : SIPChannel
    {
        private const string ACCEPT_THREAD_NAME = "siptls-";
        private const string PRUNE_THREAD_NAME = "siptlsprune-";

        /// <summary>
        /// Maximum number of connections for the TLS listener.
        /// </summary>
        private const int MAX_TLS_CONNECTIONS = 1000;              
        //private const int MAX_TLS_CONNECTIONS_PER_IPADDRESS = 10;   // Maximum number of connections allowed for a single remote IP address.
        private static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;

        private TcpListener m_tlsServerListener;
        //private bool m_closed = false;

        private readonly Dictionary<string, SIPConnection> m_connectedSockets = new Dictionary<string, SIPConnection>();
        /// <summary>
        /// List of connecting sockets to avoid SIP re-transmits initiating multiple connect attempts.
        /// </summary>
        private readonly List<string> m_connectingSockets = new List<string>();

        /// <summary>
        /// Je Verbindung (Key) gibt es einen WaitHandle (Value). Hierdurch wird gewährleistet, dass sich mehrere Threads beim (1.) Verbindungsaufbau und (2.) beim Versand nicht überholen
        /// und hierdurch Telegramm auf einer noch nicht vollständig geöffneten Verbindung versendet werden.
        /// </summary>
        private readonly Dictionary<string, AutoResetEvent> m_endpointsToEvent;
        private readonly object m_connectAndSendSync;

        /// <summary>
        /// Um zu gewährleisten, dass das verbindungsspezifische WaitHandle erst dann entfernt (Disposed) wird, wenn kein Thread auf dieses wartet, wurde die Anzahl an wartetenden Threads verwaltet.
        /// </summary>
        private readonly Dictionary<string, int> m_endpointsToCountOfWaitingThreads;

        //private string m_certificatePath;
        private readonly X509Certificate2 m_serverCertificate;
        private readonly SslProtocols m_sslProtocols;
        private readonly bool m_clientCertificateRequired;
        private readonly bool m_checkCertificateRevocation;
        private readonly bool m_useAnyAvailablePortForSend;

        private SIPTLSChannelInboundCertificateValidationCallback m_inboundCertificateValidationCallback;
        private SIPTLSChannelOutboundCertificateValidationCallback m_outboundCertificateValidationCallback;
        
        private new ILog logger = AppState.GetLogger("siptls-channel");

        public SIPTLSChannel(X509Certificate2 a_serverCertificate, SslProtocols a_sslProtocols, bool a_clientCertificateRequired, bool a_checkCertificateRevocation,
            SIPTLSChannelInboundCertificateValidationCallback inboundCertificateValidationCallback,
            SIPTLSChannelOutboundCertificateValidationCallback outboundCertificateValidationCallback,
            IPEndPoint a_endPoint, bool a_useAnyAvailablePortForSend)
        {
            if (a_serverCertificate == null)
            {
                throw new ArgumentNullException(nameof(a_serverCertificate), "An X509 certificate must be supplied for a SIP TLS channel.");
            }

            if (a_endPoint == null)
            {
                throw new ArgumentNullException(nameof(a_endPoint), "An IP end point must be supplied for a SIP TLS channel.");
            }

            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tls, a_endPoint);
            m_isReliable = true;
            m_isTLS = true;
            m_serverCertificate = a_serverCertificate;
            m_sslProtocols = a_sslProtocols;
            m_clientCertificateRequired = a_clientCertificateRequired;
            m_checkCertificateRevocation = a_checkCertificateRevocation;
            m_useAnyAvailablePortForSend = a_useAnyAvailablePortForSend;

            m_connectAndSendSync = new object();
            m_endpointsToEvent = new Dictionary<string, AutoResetEvent>();
            m_endpointsToCountOfWaitingThreads = new Dictionary<string, int>();

            Initialise();
        }

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPEndPoint endPoint) : 
            this(serverCertificate, SslProtocols.Default, false, false, null, null, endPoint, true)
        {
        }

        private void Initialise()
        {
            try
            {
                if (m_inboundCertificateValidationCallback == null)
                    m_inboundCertificateValidationCallback = InboundCertificateValidation;
                if (m_outboundCertificateValidationCallback == null)
                    m_outboundCertificateValidationCallback = OutboundCertificateValidation;

                m_tlsServerListener = new TcpListener(m_localSIPEndPoint.GetIPEndPoint());
                m_tlsServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                m_tlsServerListener.Start(MAX_TLS_CONNECTIONS);

                if (m_localSIPEndPoint.Port == 0)
                {
                    m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tls, (IPEndPoint)m_tlsServerListener.Server.LocalEndPoint);
                }

                LocalTCPSockets.Add(((IPEndPoint)m_tlsServerListener.Server.LocalEndPoint).ToString());

                ThreadPool.QueueUserWorkItem(delegate { AcceptConnections(ACCEPT_THREAD_NAME + m_localSIPEndPoint.Port); });
                ThreadPool.QueueUserWorkItem(delegate { PruneConnections(PRUNE_THREAD_NAME + m_localSIPEndPoint.Port); });

                logger.Debug("SIP TLS Channel listener created " + m_localSIPEndPoint.GetIPEndPoint() + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel Initialise. " + excp);
                throw;
            }
        }

        private void AcceptConnections(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                logger.Debug("SIPTLSChannel socket on " + m_localSIPEndPoint + " accept connections thread started.");

                while (!Closed)
                {
                    IPEndPoint remoteEndPoint = null;
                    try
                    {
                        // Blocking call - Waiting for connection ...
                        var tcpClient = m_tlsServerListener.AcceptTcpClient();

                        // Connected
                        tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;

                        var endpointKey = remoteEndPoint.ToString();

                        AutoResetEvent ev;
                        lock (m_connectAndSendSync)
                        {
                            if (!m_endpointsToEvent.ContainsKey(endpointKey))
                            {
                                m_endpointsToEvent.Add(endpointKey, new AutoResetEvent(initialState: true));
                            }

                            if (!m_endpointsToCountOfWaitingThreads.ContainsKey(endpointKey))
                            {
                                m_endpointsToCountOfWaitingThreads.Add(endpointKey, 0);
                            }

                            ev = m_endpointsToEvent[endpointKey];
                            m_endpointsToCountOfWaitingThreads[endpointKey] = m_endpointsToCountOfWaitingThreads[endpointKey] + 1;
                        }

                        //
                        //  Wait until the last async operation (BeginAuthenticateAsServer => EndAuthenticateAsServer) and
                        //  the modifications of the containers (m_connectingSockets, m_connectedSockets) are finished.
                        //
                        ev.WaitOne();

                        bool startAuthentication = true;
                        lock (m_connectedSockets)
                        {
                            if (m_connectingSockets.Contains(endpointKey) || m_connectedSockets.ContainsKey(endpointKey))
                            {
                                logger.Debug($"SIP TLS Channel refused from {endpointKey} because of existing or pending connection.");
                                startAuthentication = false;
                            }
                            else
                            {
                                m_connectingSockets.Add(endpointKey);
                            }
                        }

                        if (startAuthentication)
                        {
                            try
                            {
                                logger.Debug( "SIP TLS Channel connection accepted from " + remoteEndPoint + "." );

                                var sslStream = new SslStream( tcpClient.GetStream(), false, ( sender, certificate, chain, errors ) => m_inboundCertificateValidationCallback( this, remoteEndPoint, certificate, chain, errors ) );

                                var sipTlsConnection = new SIPConnection( this, tcpClient, sslStream, remoteEndPoint, SIPProtocolsEnum.tls, SIPConnectionsEnum.Listener );

                                sslStream.BeginAuthenticateAsServer( m_serverCertificate, m_clientCertificateRequired, m_sslProtocols, m_checkCertificateRevocation, EndAuthenticateAsServer, sipTlsConnection );
                            }
                            finally
                            {
                                lock (m_connectedSockets)
                                {
                                    m_connectingSockets.Remove(remoteEndPoint.ToString());
                                }
                            }
                        }
                        else
                        {
                            tcpClient.Close();
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error("SIPTLSChannel Accept Connection Exception. " + e);
                        if (null != remoteEndPoint)
                        {
                            SignalNextWaitingThreadAndPossibleRemoveWaitHandle(remoteEndPoint);
                        }
                    }
                }

                logger.Debug("SIPTLSChannel socket on " + m_localSIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel Listen. " + excp);
            }
        }

        public void EndAuthenticateAsServer(IAsyncResult ar)
        {
            SIPConnection sipTlsConnection = (SIPConnection)ar.AsyncState;
            try
            {
                var sslStream = (SslStream) sipTlsConnection.SIPStream;

                sslStream.EndAuthenticateAsServer(ar);

                const int fiveSeconds = 5000;
                sslStream.ReadTimeout = fiveSeconds;
                sslStream.WriteTimeout = fiveSeconds;

                lock (m_connectedSockets)
                {
                    m_connectingSockets.Remove(sipTlsConnection.RemoteEndPoint.ToString());
                    m_connectedSockets.Add(sipTlsConnection.RemoteEndPoint.ToString(), sipTlsConnection);
                }
            }
            catch (Exception excp)
            {
                lock (m_connectedSockets)
                {
                    m_connectingSockets.Remove(sipTlsConnection.RemoteEndPoint.ToString());
                }

                logger.Error("Exception SIPTLSChannel EndAuthenticateAsServer. " + excp);

                return;
            }
            finally
            {
                SignalNextWaitingThreadAndPossibleRemoveWaitHandle(sipTlsConnection.RemoteEndPoint);
            }

            try
            {
                sipTlsConnection.SIPSocketDisconnected += SIPTLSSocketDisconnected;
                sipTlsConnection.SIPMessageReceived += SIPTLSMessageReceived;
                sipTlsConnection.SIPStream.BeginRead(sipTlsConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, ReceiveCallback, sipTlsConnection);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel EndAuthenticateAsServer. SIPStream.BeginRead: " + excp);
            }
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            var sipTlsConnection = (SIPConnection)ar.AsyncState;

            if (sipTlsConnection?.SIPStream != null && sipTlsConnection.SIPStream.CanRead)
            {
                try
                {
                    int bytesRead = sipTlsConnection.SIPStream.EndRead(ar);

                    if (sipTlsConnection.SocketReadCompleted(bytesRead))
                    {
                        sipTlsConnection.SIPStream.BeginRead(sipTlsConnection.SocketBuffer, sipTlsConnection.SocketBufferEndPosition, MaxSIPTCPMessageSize - sipTlsConnection.SocketBufferEndPosition, ReceiveCallback, sipTlsConnection);
                    }
                }
                catch (SocketException sockExcp)  // Occurs if the remote end gets disconnected.
                {
                    logger.Warn("SocketException SIPTLSChannel ReceiveCallback. " + sockExcp);
                }
                catch (Exception excp)
                {
                    logger.Warn("Exception SIPTLSChannel ReceiveCallback. " + excp);
                    SIPTLSSocketDisconnected(sipTlsConnection.RemoteEndPoint);
                }
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

                var endpointKey = dstEndPoint.ToString();

                if (LocalTCPSockets.Contains(endpointKey))
                {
                    logger.Error("SIPTLSChannel blocked Send to " + endpointKey + " as it was identified as a locally hosted TCP socket.\r\n" + Encoding.UTF8.GetString(buffer));
                    throw new ApplicationException("A Send call was made in SIPTLSChannel to send to another local TCP socket.");
                }

                SIPConnection sipTLSClient = null;

                AutoResetEvent ev;
                lock (m_connectAndSendSync)
                {
                    if (!m_endpointsToEvent.ContainsKey(endpointKey))
                    {
                        m_endpointsToEvent.Add(endpointKey, new AutoResetEvent(initialState:true));
                    }

                    if (!m_endpointsToCountOfWaitingThreads.ContainsKey(endpointKey))
                    {
                        m_endpointsToCountOfWaitingThreads.Add(endpointKey, 0);
                    }

                    ev = m_endpointsToEvent[endpointKey];
                    m_endpointsToCountOfWaitingThreads[endpointKey] = m_endpointsToCountOfWaitingThreads[endpointKey] + 1;
                }

                //
                // Warten, bis die letzte asynchrone Operationen (BeginConnect bzw. BeginWrite) und
                // die Modifizierungen an den Containern (m_connectedSockets, m_connectingSockets) abgeschlossen ist.
                //
                ev.WaitOne();

                lock (m_connectedSockets)
                {
                    if (m_connectedSockets.ContainsKey(endpointKey))
                    {
                        sipTLSClient = m_connectedSockets[endpointKey];
                    }
                }

                //
                // Verbindung ist bereits aufgebaut => Telegramm senden.
                //
                bool isSent = false;
                if (null != sipTLSClient) 
                {
                    try
                    {
                        if (sipTLSClient.SIPStream != null && sipTLSClient.SIPStream.CanWrite)
                        {
                            //sipTLSClient.SIPStream.Write(buffer, 0, buffer.Length);
                            sipTLSClient.SIPStream.BeginWrite(buffer, 0, buffer.Length, EndSend, sipTLSClient);
                            isSent = true;
                            sipTLSClient.LastTransmission = DateTime.Now;
                        }
                        else
                        {
                            logger.Warn("A SIPTLSChannel write operation to " + dstEndPoint + " was dropped as the stream was null or could not be written to.");
                        }
                    }
                    catch (SocketException)
                    {
                        logger.Warn("Could not send to TLS socket " + dstEndPoint + ", closing and removing.");

                        lock (m_connectedSockets)
                        {
                            m_connectedSockets.Remove(endpointKey);
                        }

                        sipTLSClient.SIPStream?.Close();
                    }
                }

                if (isSent || null != sipTLSClient)
                {
                    return;
                }
                
                if (serverCertificateName.IsNullOrBlank())
                {
                    throw new ApplicationException("The SIP TLS Channel must be provided with the name of the expected server certificate, please use alternative method.");
                }

                bool tryConnect = false;
                lock (m_connectedSockets)
                {
                    if (!m_connectingSockets.Contains(endpointKey))
                    {
                        tryConnect = true;
                        m_connectingSockets.Add(endpointKey);
                    }
                }

                if (tryConnect)
                {
                    logger.Debug("Attempting to establish TLS connection to " + dstEndPoint + ".");
                    try
                    {
                        TcpClient tcpClient = new TcpClient();
                        tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                        tcpClient.Client.Bind(CreateEndpoint());
                        tcpClient.BeginConnect(dstEndPoint.Address, dstEndPoint.Port, EndConnect, new object[] {tcpClient, dstEndPoint, buffer, serverCertificateName});
                    }
                    catch (Exception e)
                    {
                        logger.Error("Exception (" + e.GetType() + ") SIPTLSChannel Send (sendto=>" + dstEndPoint + "); TcpClient.Bind or TcpClient.BeginConnect. " + e);

                        SignalNextWaitingThreadAndPossibleRemoveWaitHandle(dstEndPoint);
                    }
                    finally
                    {
                        lock (m_connectedSockets)
                        {
                            m_connectingSockets.Remove(endpointKey);
                        }
                    }
                }
                else
                {
                    logger.Warn("Could not send SIP packet to TLS " + dstEndPoint + " and another connection was already in progress so dropping message.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception (" + excp.GetType() + ") SIPTLSChannel Send (sendto=>" + dstEndPoint + "). " + excp);
                throw;
            }
        }

        private IPEndPoint CreateEndpoint()
        {
            if (m_useAnyAvailablePortForSend)
            {
                const int useAnyAvailablePort = 0;
                return new IPEndPoint(m_localSIPEndPoint.Address, useAnyAvailablePort);
            }

            return m_localSIPEndPoint.GetIPEndPoint();
        }

        private void EndSend(IAsyncResult ar)
        {
            IPEndPoint dstEndpoint = null;
            try
            {
                SIPConnection sipConnection = (SIPConnection) ar.AsyncState;
                dstEndpoint = sipConnection.RemoteEndPoint;
                sipConnection.SIPStream.EndWrite(ar);

                OnSendComplete(EventArgs.Empty);
            }
            catch (Exception excp)
            {
                logger.Error("Exception EndSend. " + excp);
            }
            finally
            {
                SignalNextWaitingThreadAndPossibleRemoveWaitHandle(dstEndpoint);
            }
        }

        protected override void OnSendComplete(EventArgs args)
        {
            base.OnSendComplete(args);
        }

        private void EndConnect(IAsyncResult ar)
        {
            object[] stateObj = (object[])ar.AsyncState;
            TcpClient tcpClient = (TcpClient)stateObj[0];
            IPEndPoint dstEndPoint = (IPEndPoint)stateObj[1];
            byte[] buffer = (byte[])stateObj[2];
            string serverCN = (string)stateObj[3];

            try
            {
                tcpClient.EndConnect(ar);

                SslStream sslStream = new SslStream(tcpClient.GetStream(), false, (sender, certificate, chain, errors) => m_outboundCertificateValidationCallback(this, dstEndPoint, serverCN, certificate, chain, errors), null);
                //DisplayCertificateInformation(sslStream);

                SIPConnection callerConnection = new SIPConnection(this, tcpClient, sslStream, dstEndPoint, SIPProtocolsEnum.tls, SIPConnectionsEnum.Caller);

                sslStream.BeginAuthenticateAsClient(serverCN, new X509Certificate2Collection() { m_serverCertificate }, m_sslProtocols, m_checkCertificateRevocation, EndAuthenticateAsClient, new object[] { tcpClient, dstEndPoint, buffer, callerConnection });

                #region old
                //sslStream.AuthenticateAsClient(serverCN);

                //if (tcpClient != null && tcpClient.Connected)
                //{
                //    SIPConnection callerConnection = new SIPConnection(this, sslStream, dstEndPoint, SIPProtocolsEnum.tls, SIPConnectionsEnum.Caller);
                //    m_connectedSockets.Add(dstEndPoint.ToString(), callerConnection);

                //    callerConnection.SIPSocketDisconnected += SIPTLSSocketDisconnected;
                //    callerConnection.SIPMessageReceived += SIPTLSMessageReceived;
                //    //byte[] receiveBuffer = new byte[MaxSIPTCPMessageSize];
                //    callerConnection.SIPStream.BeginRead(callerConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), callerConnection);

                //    logger.Debug("Established TLS connection to " + dstEndPoint + ".");

                //    callerConnection.SIPStream.BeginWrite(buffer, 0, buffer.Length, EndSend, callerConnection);
                //}
                //else
                //{
                //    logger.Warn("Could not establish TLS connection to " + dstEndPoint + ".");
                //}
                #endregion
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel EndConnect. " + excp);

                lock (m_connectedSockets)
                {
                    m_connectingSockets.Remove(dstEndPoint.ToString());
                }

                if (tcpClient != null)
                {
                    try
                    {
                        tcpClient.Close();
                    }
                    catch(Exception closeExcp)
                    {
                        logger.Warn("Exception SIPTLSChannel EndConnect Close TCP Client. " + closeExcp);
                    }
                }

                SignalNextWaitingThreadAndPossibleRemoveWaitHandle(dstEndPoint);
            }
        }

        private void EndAuthenticateAsClient( IAsyncResult ar )
        {
            object[] stateObj = (object[]) ar.AsyncState;
            TcpClient tcpClient = (TcpClient) stateObj[0];
            IPEndPoint dstEndPoint = (IPEndPoint) stateObj[1];
            byte[] buffer = (byte[]) stateObj[2];
            SIPConnection callerConnection = (SIPConnection) stateObj[3];
            try
            {
                SslStream sslStream = (SslStream) callerConnection.SIPStream;

                sslStream.EndAuthenticateAsClient(ar);

                if (tcpClient != null && tcpClient.Connected)
                {
                    //SIPConnection callerConnection = new SIPConnection(this, sslStream, dstEndPoint, SIPProtocolsEnum.tls, SIPConnectionsEnum.Caller);
                    lock (m_connectedSockets)
                    {
                        m_connectingSockets.Remove(dstEndPoint.ToString());
                        m_connectedSockets.Add(callerConnection.RemoteEndPoint.ToString(), callerConnection);
                    }
                }
                else
                {
                    logger.Warn("Could not establish TLS connection to " + callerConnection.RemoteEndPoint + ".");
                    return;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSChannel EndAuthenticateAsClient. " + excp);

                lock (m_connectedSockets)
                {
                    m_connectingSockets.Remove(dstEndPoint.ToString());
                }

                SignalNextWaitingThreadAndPossibleRemoveWaitHandle(dstEndPoint);
                return;
            }

            try
            {
                callerConnection.SIPSocketDisconnected += SIPTLSSocketDisconnected;
                callerConnection.SIPMessageReceived += SIPTLSMessageReceived;
                //byte[] receiveBuffer = new byte[MaxSIPTCPMessageSize];
                callerConnection.SIPStream.BeginRead(callerConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), callerConnection);

                logger.Debug("Established TLS connection to " + callerConnection.RemoteEndPoint + ".");

                callerConnection.SIPStream.BeginWrite(buffer, 0, buffer.Length, EndSend, callerConnection);
            }
            catch (Exception excp)
            {
                SignalNextWaitingThreadAndPossibleRemoveWaitHandle(dstEndPoint);
                logger.Error("Exception SIPTLSChannel EndAuthenticateAsClient. BeginRead/BeginWrite" + excp);
            }
        }

        private void SignalNextWaitingThreadAndPossibleRemoveWaitHandle(IPEndPoint a_dstEndPoint, [CallerMemberName] string a_memberName = "")
        {
            try
            {
                lock (m_connectAndSendSync)
                {
                    var key = a_dstEndPoint.ToString();

                    int waitingThreadsCount = DecrementWaitingThreadsCounter(key);

                    m_endpointsToEvent[key].Set();

                    if (AreThreadsWaiting(waitingThreadsCount))
                    {
                        return;
                    }

                    lock (m_connectedSockets)
                    {
                        if (IsSocketInUse(key))
                        {
                            return;
                        }

                        DisposeWaitHandle(key);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error($"SIPTLSChannel.{a_memberName} Signaling next Thread Exception: {e}");
            }
        }

        private int DecrementWaitingThreadsCounter(string a_key)
        {
            m_endpointsToCountOfWaitingThreads[a_key] = m_endpointsToCountOfWaitingThreads[a_key] - 1;
            return m_endpointsToCountOfWaitingThreads[a_key];
        }

        private bool AreThreadsWaiting(int a_countWaitingThreads)
        {
            return a_countWaitingThreads > 0;
        }

        private bool IsSocketInUse(string a_key)
        {
            bool isSocketConnecting = m_connectingSockets.Any(x => x == a_key);
            bool isSocketConnected = m_connectedSockets.ContainsKey(a_key);

            return isSocketConnecting || isSocketConnected;
        }

        private void DisposeWaitHandle(string a_key)
        {
            if (!m_endpointsToEvent.TryGetValue(a_key, out var e))
            {
                return;
            }
            e.Dispose();
            m_endpointsToEvent.Remove(a_key);
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

                var k = remoteEndPoint.ToString();

                lock (m_connectedSockets)
                {
                    m_connectedSockets.Remove(k);
                    m_connectingSockets.Remove(k);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTLSClientDisconnected. " + excp);
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

        private bool InboundCertificateValidation(
            SIPTLSChannel channel,
            IPEndPoint remotEndPoint,
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

        private bool OutboundCertificateValidation(
            SIPTLSChannel channel,
            IPEndPoint remotEndPoint,
            string serverFQDN,
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
            if (!Closed == true)
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

                lock (m_connectedSockets)
                {
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
    }
}
