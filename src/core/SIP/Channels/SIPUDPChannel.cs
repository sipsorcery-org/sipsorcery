//-----------------------------------------------------------------------------
// Filename: SIPUDPChannel.cs
//
// Description: SIP transport for UDP.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Oct 2005	Aaron Clauson	Created, Dublin, Ireland.
// 14 Oct 2019  Aaron Clauson   Added IPv6 support.
// 17 Nov 2019  Aaron Clauson   Added IPAddress.Any support, see https://github.com/sipsorcery/sipsorcery/issues/97.
//
// Notes:
// This class is using the "Asynchronous Programming Model" (APM*) BeginReceiveMessageFrom/EndReceiveMessageFrom approach. 
// The motivation for the decision is that it's the only one of the UDP socket receives methods that provides access to 
// the received on IP address when listening on IPAddress.Any.
//
// * https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPUDPChannel : SIPChannel
    {
        private const int FAILED_DESTINATION_PERIOD_SECONDS = 30;       // How long a failed send should prevent subsequent sends for.
        private const int EXPIRED_FAILED_PERIOD_SECONDS = 5;            // Period at which to check the failed send list and remove expired items.

        private readonly Socket m_udpSocket;
        private byte[] m_recvBuffer;
        private CancellationTokenSource m_cts;

        /// <summary>
        /// Keep a list of transient send failures to remote end points. With UDP a failure is detected if an ICMP packet is received 
        /// on a receive.
        /// </summary>
        private static ConcurrentDictionary<IPEndPoint, DateTime> m_sendFailures = new ConcurrentDictionary<IPEndPoint, DateTime>();

        /// <summary>
        /// Creates a SIP channel to listen for and send SIP messages over UDP.
        /// </summary>
        /// <param name="endPoint">The IP end point to listen on and send from.</param>
        public SIPUDPChannel(IPEndPoint endPoint) : base()
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException("endPoint", "The end point must be specified when creating a SIPUDPChannel.");
            }

            ListeningIPAddress = endPoint.Address;
            Port = endPoint.Port;
            SIPProtocol = SIPProtocolsEnum.udp;
            IsReliable = false;
            m_cts = new CancellationTokenSource();

            m_udpSocket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            m_udpSocket.Bind(endPoint);
            if (endPoint.Port == 0)
            {
                Port = (m_udpSocket.LocalEndPoint as IPEndPoint).Port;
            }

            m_recvBuffer = new byte[SIPConstants.SIP_MAXIMUM_UDP_SEND_LENGTH * 2];

            logger.LogInformation($"SIP UDP Channel created for {ListeningEndPoint}.");

            Receive();

            Task.Run(ExpireFailedSends);
        }

        public SIPUDPChannel(IPAddress listenAddress, int listenPort) : this(new IPEndPoint(listenAddress, listenPort))
        { }

        private void Receive()
        {
            try
            {
                EndPoint recvEndPoint = (ListeningIPAddress.AddressFamily == AddressFamily.InterNetwork) ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                m_udpSocket.BeginReceiveMessageFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref recvEndPoint, EndReceiveMessageFrom, null);
            }
            catch (ObjectDisposedException) { } // Thrown when socket is closed. Can be safely ignored.
            catch (Exception excp)
            {
                // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3056
                // the BeginReceiveMessageFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
                // case the socket can be considered to be unusable and there's no point trying another receive.
                logger.LogError($"Exception Receive. {excp.Message}");
                logger.LogDebug($"SIPUDPChannel socket on {ListeningEndPoint} listening halted.");
                Closed = true;
            }
        }

        private void EndReceiveMessageFrom(IAsyncResult ar)
        {
            EndPoint remoteEP = (ListeningIPAddress.AddressFamily == AddressFamily.InterNetwork) ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);

            try
            {
                if (!Closed)
                {
                    SocketFlags flags = SocketFlags.None;

                    int bytesRead = m_udpSocket.EndReceiveMessageFrom(ar, ref flags, ref remoteEP, out var packetInfo);

                    if (bytesRead > 0)
                    {
                        SIPEndPoint remoteEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, remoteEP as IPEndPoint, ID, null);
                        SIPEndPoint localEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(packetInfo.Address, Port), ID, null);
                        byte[] sipMsgBuffer = new byte[bytesRead];
                        Buffer.BlockCopy(m_recvBuffer, 0, sipMsgBuffer, 0, bytesRead);
                        SIPMessageReceived?.Invoke(this, localEndPoint, remoteEndPoint, sipMsgBuffer);
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                // This exception can occur as the result of a Send operation. It's caused by an ICMP packet from a remote host
                // rejecting an incoming UDP packet. If that happens we want to stop further sends to the socket for a short period.
                logger.LogWarning($"SocketException SIPUDPChannel EndReceiveMessageFrom from {remoteEP} ({sockExcp.ErrorCode}). {sockExcp.Message}");
                if (remoteEP != null)
                {
                    m_sendFailures.TryAdd(remoteEP as IPEndPoint, DateTime.Now);
                }
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPUDPChannel EndReceiveMessageFrom. {excp.Message}");
            }
            finally
            {
                if (!Closed)
                {
                    Receive();
                }
            }
        }

        public override Task<SocketError> SendAsync(SIPEndPoint dstEndPoint, byte[] buffer, string connectionIDHint)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in SIPUDPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPUDPChannel.");
            }

            try
            {
                IPEndPoint dstIPEndPoint = dstEndPoint.GetIPEndPoint();

                if (m_sendFailures.ContainsKey(dstEndPoint.GetIPEndPoint()))
                {
                    return Task.FromResult(SocketError.ConnectionRefused);
                }
                else
                {
                    m_udpSocket.BeginSendTo(buffer, 0, buffer.Length, SocketFlags.None, dstIPEndPoint, EndSendTo, dstEndPoint);
                    return Task.FromResult(SocketError.Success);
                }
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            {
                return Task.FromResult(SocketError.Disconnecting);
            }
            catch (SocketException sockExcp)
            {
                return Task.FromResult(sockExcp.SocketErrorCode);
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPUDPChannel.SendAsync. {excp}");
                return Task.FromResult(SocketError.Fault);
            }
        }

        private void EndSendTo(IAsyncResult ar)
        {
            try
            {
                int bytesSent = m_udpSocket.EndSendTo(ar);
            }
            catch (SocketException sockExcp)
            {
                // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                // so that we know to stop sending.
                logger.LogWarning($"SocketException SIPUDPChannel EndSendTo ({sockExcp.ErrorCode}). {sockExcp.Message}");
            }
            catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
            { }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPUDPChannel EndSendTo. {excp.Message}");
            }
        }

        /// <summary>
        /// This method is not implemented for the SIP UDP channel.
        /// </summary>
        public override Task<SocketError> SendSecureAsync(SIPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName, string connectionIDHint)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        /// <summary>
        /// The UDP channel does not support connections. Always returns false.
        /// </summary>
        public override bool HasConnection(string connectionID)
        {
            return false;
        }

        /// <summary>
        /// The UDP channel does not support connections. Always returns false.
        /// </summary>
        public override bool HasConnection(SIPEndPoint remoteEndPoint)
        {
            return false;
        }

        /// <summary>
        /// The UDP channel does not support connections. Always returns false.
        /// </summary>
        public override bool HasConnection(Uri serverUri)
        {
            return false;
        }

        /// <summary>
        /// Checks whether the specified address family is supported.
        /// </summary>
        /// <param name="addresFamily">The address family to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsAddressFamilySupported(AddressFamily addresFamily)
        {
             return addresFamily == ListeningIPAddress.AddressFamily;
        }

        /// <summary>
        /// Checks whether the specified protocol is supported.
        /// </summary>
        /// <param name="protocol">The protocol to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsProtocolSupported(SIPProtocolsEnum protocol)
        {
            return protocol == SIPProtocolsEnum.udp;
        }

        /// <summary>
        /// Closes the channel's UDP socket.
        /// </summary>
        public override void Close()
        {
            try
            {
                logger.LogDebug($"Closing SIP UDP Channel {ListeningEndPoint}.");

                Closed = true;
                m_cts.Cancel();
                m_udpSocket.Close();
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception SIPUDPChannel Close. " + excp.Message);
            }
        }

        public override void Dispose()
        {
            this.Close();
        }

        /// <summary>
        /// Removed end points from the send failures list after the timeout period.
        /// </summary>
        private async void ExpireFailedSends()
        {
            try
            {
                while (!Closed)
                {
                    var expireds = m_sendFailures.Where(x => DateTime.Now.Subtract(x.Value).TotalSeconds > FAILED_DESTINATION_PERIOD_SECONDS).Select(x => x.Key).ToList();

                    foreach (var expired in expireds)
                    {
                        m_sendFailures.TryRemove(expired, out _);
                    }

                    await Task.Delay(EXPIRED_FAILED_PERIOD_SECONDS * 1000, m_cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPUDPChannel.ExpireFailedSends. {excp.Message}");
            }
        }
    }
}
