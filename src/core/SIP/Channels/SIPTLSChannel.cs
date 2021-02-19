//-----------------------------------------------------------------------------
// Filename: SIPTLSChannel.cs
//
// Description: SIP transport for TLS over TCP.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Mar 2009	Aaron Clauson	Created, Hobart, Australia.
// 16 Oct 2019  Aaron Clauson   Added IPv6 support.
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPTLSChannel : SIPTCPChannel
    {
        private const int CLOSE_CONNECTION_TIMEOUT = 500;
        private const int TLS_ATTEMPT_CONNECT_TIMEOUT = 5000;

        private X509Certificate2 m_serverCertificate;

        override protected string ProtDescr { get; } = "TLS";

        public bool BypassCertificateValidation { get; set; } = true;

        public SIPTLSChannel(IPEndPoint endPoint, bool useDualMode = false)
            : base(endPoint, SIPProtocolsEnum.tls, false, useDualMode)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint), "An IP end point must be supplied for a SIP TLS channel.");
            }

            IsSecure = true;
        }

        public SIPTLSChannel(X509Certificate2 serverCertificate, IPEndPoint endPoint, bool useDualMode = false)
            : base(endPoint, SIPProtocolsEnum.tls, serverCertificate != null, useDualMode)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException(nameof(endPoint), "An IP end point must be supplied for a SIP TLS channel.");
            }

            IsSecure = true;
            m_serverCertificate = serverCertificate;

            if (m_serverCertificate != null)
            {
                logger.LogInformation($"SIP TLS Channel ready for {ListeningSIPEndPoint} and certificate {m_serverCertificate.Subject}.");
            }
            else
            {
                logger.LogInformation($"SIP TLS client only channel ready.");
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
            NetworkStream networkStream = new NetworkStream(streamConnection.StreamSocket, true);
            SslStream sslStream = new SslStream(networkStream, false);

            //await sslStream.AuthenticateAsServerAsync(m_serverCertificate).ConfigureAwait(false);
            sslStream.AuthenticateAsServer(m_serverCertificate);

            logger.LogDebug($"SIP TLS Channel successfully upgraded accepted client to SSL stream for {ListeningSIPEndPoint}<-{streamConnection.RemoteSIPEndPoint}.");

            //// Display the properties and settings for the authenticated stream.
            ////DisplaySecurityLevel(sslStream);
            ////DisplaySecurityServices(sslStream);
            ////DisplayCertificateInformation(sslStream);
            ////DisplayStreamProperties(sslStream);

            //// Set timeouts for the read and write to 5 seconds.
            //sslStream.ReadTimeout = 5000;
            //sslStream.WriteTimeout = 5000;

            streamConnection.SslStream = sslStream;
            streamConnection.SslStreamBuffer = new byte[2 * SIPStreamConnection.MaxSIPTCPMessageSize];

            sslStream.BeginRead(streamConnection.SslStreamBuffer, 0, SIPStreamConnection.MaxSIPTCPMessageSize, new AsyncCallback(OnReadCallback), streamConnection);
        }

        /// <summary>
        /// For the TLS channel once the TCP client socket is connected it needs to be wrapped up in an SSL stream.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly connected client socket.</param>
        /// <param name="serverCertificateName">The expected common name on the SSL certificate supplied by the server.</param>
        protected override async Task<SocketError> OnClientConnect(SIPStreamConnection streamConnection, string serverCertificateName)
        {
            NetworkStream networkStream = new NetworkStream(streamConnection.StreamSocket, true);
            SslStream sslStream = new SslStream(networkStream, false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
            //DisplayCertificateInformation(sslStream);

            var timeoutTask = Task.Delay(TLS_ATTEMPT_CONNECT_TIMEOUT);
            var sslStreamTask = sslStream.AuthenticateAsClientAsync(serverCertificateName);
            await Task.WhenAny(sslStreamTask, timeoutTask).ConfigureAwait(false);

            if(sslStreamTask.IsCompleted)
            {
                if (!sslStream.IsAuthenticated)
                {
                    logger.LogWarning($"SIP TLS channel failed to establish SSL stream with {streamConnection.RemoteSIPEndPoint}.");
                    networkStream.Close(CLOSE_CONNECTION_TIMEOUT);
                    return SocketError.ProtocolNotSupported;
                }
                else
                {
                    streamConnection.SslStream = sslStream;
                    streamConnection.SslStreamBuffer = new byte[2 * SIPStreamConnection.MaxSIPTCPMessageSize];

                    logger.LogDebug($"SIP TLS Channel successfully upgraded client connection to SSL stream for {ListeningSIPEndPoint}->{streamConnection.RemoteSIPEndPoint}.");

                    sslStream.BeginRead(streamConnection.SslStreamBuffer, 0, SIPStreamConnection.MaxSIPTCPMessageSize, new AsyncCallback(OnReadCallback), streamConnection);

                    return SocketError.Success;
                }
            }
            else
            {
                logger.LogWarning($"SIP TLS channel timed out attempting to establish SSL stream with {streamConnection.RemoteSIPEndPoint}.");
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
                    logger.LogDebug($"TLS socket disconnected by {sipStreamConnection.RemoteSIPEndPoint}.");
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
                    logger.LogWarning(ioExcp, $"IOException SIPTLSChannel OnReadCallback. {ioExcp.Message}");
                    OnSIPStreamDisconnected(sipStreamConnection, SocketError.Fault);
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning(excp, $"Exception SIPTLSChannel OnReadCallback. {excp.Message}");
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
                logger.LogWarning(sockExcp, $"SocketException SIP TLS Channel sending to {sipStreamConn.RemoteSIPEndPoint}. ErrorCode {sockExcp.SocketErrorCode}. {sockExcp}");
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
                logger.LogDebug($"Successfully validated X509 certificate for {certificate.Subject}.");
                return true;
            }
            else
            {
                logger.LogWarning(String.Format("Certificate error: {0}", sslPolicyErrors));
                return BypassCertificateValidation;
            }
        }

        #region Certificate verbose logging.

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

        #endregion
    }
}
