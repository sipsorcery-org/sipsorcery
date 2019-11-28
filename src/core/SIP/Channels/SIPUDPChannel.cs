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
// This class is using the "Asychronous Programming Model" (APM)* BeginReceiveMessageFrom/EndReceiveMessageFrom approach. 
// The motivation for te decision is that it's the only one of the UDP socket receives methods that provides access to 
// the received on IP address when listening on IPAddress.Any.
//
// * https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPUDPChannel : SIPChannel
    {
        private readonly Socket m_udpSocket;
        private readonly UdpClient m_udpClientWrapper;  // Wrapper around the socket. Allows access to SendAsync methods.
        private byte[] m_recvBuffer;

        /// <summary>
        /// Keep a list of transient send failures to remote end points. With UDP a failure is detected if an ICMP packet is received 
        /// on a receive.
        /// TODO: Keep looking for a way to access the raw ICMP packet that causes the receive exception.
        /// </summary>
        //private static ConcurrentDictionary<IPEndPoint, Tuple<IPEndPoint, DateTime>> m_sendFailures = new ConcurrentDictionary<IPEndPoint, Tuple<IPEndPoint, DateTime>>();

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
            IsReliable = true;

            m_udpSocket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            m_udpSocket.Bind(endPoint);
            if (endPoint.Port == 0)
            {
                Port = (m_udpSocket.LocalEndPoint as IPEndPoint).Port;
            }
            m_udpClientWrapper = new UdpClient();
            m_udpClientWrapper.Client = m_udpSocket;
            m_recvBuffer = new byte[SIPConstants.SIP_MAXIMUM_UDP_SEND_LENGTH * 2];

            logger.LogInformation($"SIP UDP Channel created for {ListeningEndPoint}.");

            Receive();
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
                // case the sopcket can be considered to be unusable and there's no point trying another receive.
                logger.LogError($"Exception Receive. {excp.Message}");
                logger.LogDebug($"SIPUDPChannel socket on {ListeningEndPoint} listening halted.");
                Closed = true;
            }
        }

        private void EndReceiveMessageFrom(IAsyncResult ar)
        {
            try
            {
                SocketFlags flags = SocketFlags.None;
                EndPoint remoteEP = (ListeningIPAddress.AddressFamily == AddressFamily.InterNetwork) ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);

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
            catch (SocketException sockExcp)
            {
                // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                // so that we know to stop sending.
                logger.LogWarning($"SocketException SIPUDPChannel Receive ({sockExcp.ErrorCode}). {sockExcp.Message}");
            }
            catch (ObjectDisposedException) { } // Thrown when socket is closed. Can be safely ignored.
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPUDPChannel.EndReceiveMessageFrom. {excp.Message}");
            }
            finally
            {
                if (!Closed)
                {
                    Receive();
                }
            }
        }

        public override async void Send(IPEndPoint dstEndPoint, byte[] buffer, string connectionIDHint)
        {
            await SendAsync(dstEndPoint, buffer, connectionIDHint);
        }

        public override async Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer, string connectionIDHint)
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
                int bytesSent = await m_udpClientWrapper.SendAsync(buffer, buffer.Length, dstEndPoint);
                return (bytesSent > 0) ? SocketError.Success : SocketError.ConnectionReset;
            }
            catch (ObjectDisposedException) { return SocketError.NotConnected; } // Thrown when socket is closed. Can be safely ignored.
            catch (SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
        }

        /// <summary>
        /// This method is not implemented for the SIP UDP channel.
        /// </summary>
        public override void SendSecure(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName, string connectionIDHint)
        {
            throw new NotImplementedException("This Send method is not available in the SIP UDP channel, please use an alternative overload.");
        }

        /// <summary>
        /// This method is not implemented for the SIP UDP channel.
        /// </summary>
        public override Task<SocketError> SendSecureAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName, string connectionIDHint)
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
        public override bool HasConnection(IPEndPoint remoteEndPoint)
        {
            return false;
        }

        public override void Close()
        {
            try
            {
                logger.LogDebug($"Closing SIP UDP Channel {ListeningEndPoint}.");

                Closed = true;
                m_udpClientWrapper.Close();
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
    }
}
