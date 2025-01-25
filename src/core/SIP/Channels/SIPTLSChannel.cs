//-----------------------------------------------------------------------------
// Filename: SIPTLSChannel.cs
//
// Description: SIP transport for TLS over TCP.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Mar 2009	Aaron Clauson             Created, Hobart, Australia.
// 16 Oct 2019  Aaron Clauson             Added IPv6 support.
// 4 July 2022  Jean-Christophe Grondin   Added custom server certificate callback
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPTLSChannel : SIPTCPChannel
    {
        private const int CLOSE_CONNECTION_TIMEOUT = 500;
        private const int TLS_ATTEMPT_CONNECT_TIMEOUT = 5000;

        private readonly X509Certificate2 m_serverCertificate;
        private readonly X509Certificate2Collection m_clientCertificates;
        private readonly RemoteCertificateValidationCallback m_remoteCertificateValidation;

        override protected string ProtDescr { get; } = "TLS";

        /// <summary>
        /// Allows to ignore any ssl policy errors regarding the received certificate.
        /// Only applicable when no custom remote certificate validation is provided.
        /// </summary>
        public bool BypassCertificateValidation { get; set; } = true;

        public SIPTLSChannel(IPEndPoint endPoint, bool useDualMode = false, RemoteCertificateValidationCallback remoteCertificateValidation = null)
            : base(endPoint, SIPProtocolsEnum.tls, false, useDualMode)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint), "An IP end point must be supplied for a SIP TLS channel.");
            }

            IsSecure = true;
            m_serverCertificate = null;
            m_clientCertificates = null;
            m_remoteCertificateValidation = remoteCertificateValidation ?? new RemoteCertificateValidationCallback(ValidateServerCertificate);
        }

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPEndPoint endPoint, bool useDualMode = false, X509Certificate2Collection clientCertificates = null, RemoteCertificateValidationCallback remoteCertificateValidation = null)
            : base(endPoint, SIPProtocolsEnum.tls, serverCertificate != null, useDualMode)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint), "An IP end point must be supplied for a SIP TLS channel.");
            }

            IsSecure = true;
            m_serverCertificate = serverCertificate;
            m_clientCertificates = clientCertificates;
            m_remoteCertificateValidation = remoteCertificateValidation ?? new RemoteCertificateValidationCallback(ValidateServerCertificate);

            if (m_serverCertificate != null)
            {
                logger.LogTlsChannelReady(ListeningSIPEndPoint, m_serverCertificate.Subject);
            }
            else
            {
                logger.LogTlsClientOnlyChannelReady();
            }
        }

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPAddress listenAddress, int listenPort) :
            this(serverCertificate, new IPEndPoint(listenAddress, listenPort))
        { }

        /// <summary>
        /// For the TLS channel the SSL stream must be created and any authentication actions undertaken.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly accepted client socket.</param>
        protected override void OnAccept(SIPStreamConnection streamConnection)
        {
            OnAcceptAsync(streamConnection).ConfigureAwait(false);
        }

        /// <summary>
        /// For the TLS channel the SSL stream must be created and any authentication actions undertaken.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly accepted client socket.</param>
        protected async Task OnAcceptAsync(SIPStreamConnection streamConnection)
        {
            NetworkStream networkStream = new NetworkStream(streamConnection.StreamSocket, true);
            SslStream sslStream = null;

            try
            {
                sslStream = new SslStream(networkStream, false);
                using (var cts = new CancellationTokenSource())
                {
                    var authTask = sslStream.AuthenticateAsServerAsync(m_serverCertificate);
                    var timeoutTask = Task.Delay(TLS_ATTEMPT_CONNECT_TIMEOUT, cts.Token);

                    var resultTask = await Task.WhenAny(authTask, timeoutTask);
                    if (resultTask == timeoutTask)
                    {
                        logger.LogTlsAuthenticateTimeout();
                        sslStream.Close();
                        return;
                    }
                    cts.Cancel();

                    logger.LogTlsServerUpgraded(ListeningSIPEndPoint, streamConnection.RemoteSIPEndPoint);


                    //// Display the properties and settings for the authenticated stream.
                    ////DisplaySecurityLevel(sslStream);
                    ////DisplaySecurityServices(sslStream);
                    ////DisplayCertificateInformation(sslStream);
                    ////DisplayStreamProperties(sslStream);

                    //// Set timeouts for the read and write to 5 seconds.
                    //sslStream.ReadTimeout = 5000;
                    //sslStream.WriteTimeout = 5000;

                    streamConnection.SslStream = new SIPStreamWrapper(sslStream);
                    streamConnection.SslStreamBuffer = new byte[2 * SIPStreamConnection.MaxSIPTCPMessageSize];
                }

                sslStream.BeginRead(streamConnection.SslStreamBuffer, 0, SIPStreamConnection.MaxSIPTCPMessageSize, new AsyncCallback(OnReadCallback), streamConnection);
            }
            catch(Exception ex) 
            {
                logger.LogTlsConnectionError(ex.Message, ex);
                sslStream?.Close();
            }
        }

        /// <summary>
        /// For the TLS channel once the TCP client socket is connected it needs to be wrapped up in an SSL stream.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly connected client socket.</param>
        /// <param name="serverCertificateName">The expected common name on the SSL certificate supplied by the server.</param>
        protected override async Task<SocketError> OnClientConnect(SIPStreamConnection streamConnection, string serverCertificateName)
        {
            NetworkStream networkStream = new NetworkStream(streamConnection.StreamSocket, true);
            SslStream sslStream = new SslStream(networkStream, false, m_remoteCertificateValidation, null);
            //DisplayCertificateInformation(sslStream);

            var timeoutTask = Task.Delay(TLS_ATTEMPT_CONNECT_TIMEOUT);
            var sslStreamTask = m_clientCertificates != null ? sslStream.AuthenticateAsClientAsync(serverCertificateName, m_clientCertificates, System.Security.Authentication.SslProtocols.None, false) : sslStream.AuthenticateAsClientAsync(serverCertificateName);
            await Task.WhenAny(sslStreamTask, timeoutTask).ConfigureAwait(false);

            if(sslStreamTask.IsCompleted)
            {
                if (!sslStream.IsAuthenticated)
                {
                    logger.LogTlsStreamAuthenticateFailure(streamConnection.RemoteSIPEndPoint);
                    networkStream.Close(CLOSE_CONNECTION_TIMEOUT);
                    return SocketError.ProtocolNotSupported;
                }
                else
                {
                    streamConnection.SslStream = new SIPStreamWrapper(sslStream);
                    streamConnection.SslStreamBuffer = new byte[2 * SIPStreamConnection.MaxSIPTCPMessageSize];

                    logger.LogTlsClientUpgraded(ListeningSIPEndPoint, streamConnection.RemoteSIPEndPoint);

                    sslStream.BeginRead(streamConnection.SslStreamBuffer, 0, SIPStreamConnection.MaxSIPTCPMessageSize, new AsyncCallback(OnReadCallback), streamConnection);

                    return SocketError.Success;
                }
            }
            else
            {
                logger.LogTlsStreamConnectTimeout(streamConnection.RemoteSIPEndPoint);
                networkStream.Close(CLOSE_CONNECTION_TIMEOUT);
                return SocketError.TimedOut;
            }
        }

        /// <summary>
        /// Callback for read operations on the SSL stream. 
        /// </summary>
        private void OnReadCallback(IAsyncResult ar)
        {
            SIPStreamConnection sipStreamConnection = (SIPStreamConnection)ar.AsyncState;

            try
            {
                int bytesRead = sipStreamConnection.SslStream.EndRead(ar);

                if (bytesRead == 0)
                {
                    // SSL stream was disconnected by the remote end point sending a FIN or RST.
                    logger.LogTlsRemoteDisconnection(sipStreamConnection.RemoteSIPEndPoint);
                    OnSIPStreamDisconnected(sipStreamConnection, SocketError.ConnectionReset);
                }
                else
                {
                    sipStreamConnection.ExtractSIPMessages(this, sipStreamConnection.SslStreamBuffer, bytesRead);
                    sipStreamConnection.SslStream.BeginRead(sipStreamConnection.SslStreamBuffer, sipStreamConnection.RecvEndPosn, sipStreamConnection.SslStreamBuffer.Length - sipStreamConnection.RecvEndPosn, new AsyncCallback(OnReadCallback), sipStreamConnection);
                }
            }
            catch (SocketException sockExcp)  // Occurs if the remote end gets disconnected.
            {
                OnSIPStreamDisconnected(sipStreamConnection, sockExcp.SocketErrorCode);
            }
            catch (IOException ioExcp)
            {
                if (ioExcp.InnerException is SocketException)
                {
                    OnSIPStreamDisconnected(sipStreamConnection, (ioExcp.InnerException as SocketException).SocketErrorCode);
                }
                else if (ioExcp.InnerException is ObjectDisposedException)
                {
                    // This exception is expected when the TLS connection is closed and this method is waiting for a receive.
                    OnSIPStreamDisconnected(sipStreamConnection, SocketError.Disconnecting);
                }
                else
                {
                    logger.LogTlsCloseError(ioExcp.Message, ioExcp);
                    OnSIPStreamDisconnected(sipStreamConnection, SocketError.Fault);
                }
            }
            catch (Exception excp)
            {
                logger.LogTlsStreamError(excp.Message, excp);
                OnSIPStreamDisconnected(sipStreamConnection, SocketError.Fault);
            }
        }

        /// <summary>
        /// Sends data using the connected SSL stream.
        /// </summary>
        /// <param name="sipStreamConn">The stream connection object that holds the SSL stream.</param>
        /// <param name="buffer">The data to send.</param>
        protected override Task SendOnConnected(SIPStreamConnection sipStreamConn, byte[] buffer)
        {
            try
            {
                return sipStreamConn.SslStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (SocketException sockExcp)
            {
                logger.LogTlsStreamSendError(sipStreamConn.RemoteSIPEndPoint, sockExcp.SocketErrorCode, sockExcp.Message);
                OnSIPStreamDisconnected(sipStreamConn, sockExcp.SocketErrorCode);
                throw;
            }
        }

        /// <summary>
        /// Checks whether the specified protocol is supported.
        /// </summary>
        /// <param name="protocol">The protocol to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsProtocolSupported(SIPProtocolsEnum protocol)
        {
            return protocol == SIPProtocolsEnum.tls;
        }

        /// <summary>
        /// Attempt to retrieve a certificate from the Windows local machine certificate store.
        /// </summary>
        /// <param name="subjName">The subject name of the certificate to retrieve.</param>
        /// <returns>If found an X509 certificate or null if not.</returns>
        private X509Certificate GetServerCert(string subjName)
        {
            //X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            X509Store store = new X509Store(StoreName.CertificateAuthority, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            X509CertificateCollection cert = store.Certificates.Find(X509FindType.FindBySubjectName, subjName, true);
            return cert[0];
        }

        /// <summary>
        /// Hook to do any validation required on the server certificate.
        /// </summary>
        private bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                logger.LogTlsCertificateValidated(certificate.Subject);
                return true;
            }
            else
            {
                logger.LogTlsCertificateError(sslPolicyErrors);
                return BypassCertificateValidation;
            }
        }
    }
}
