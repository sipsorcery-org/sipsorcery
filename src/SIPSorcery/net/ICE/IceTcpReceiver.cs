//-----------------------------------------------------------------------------
// Filename: IceTcpReceiver.cs
//
// Description: TBD.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Aug 2025  Aaron Clauson   Refactored from RtpIceChannel.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

#nullable disable

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public class IceTcpReceiver : UdpReceiver
{
    protected const int REVEIVE_TCP_BUFFER_SIZE = RECEIVE_BUFFER_SIZE * 2;

    protected int m_recvOffset;

    public IceTcpReceiver(Socket socket, int mtu = REVEIVE_TCP_BUFFER_SIZE) : base(socket, mtu)
    {
        m_recvOffset = 0;
    }

    /// <summary>
    /// True once a receive has completed with zero bytes, which on a stream socket means the remote end
    /// has closed the connection. Once set it stays set: the connection is finished and this receiver's
    /// socket cannot serve another one.
    /// </summary>
    /// <remarks>
    /// This is the reliable signal that the connection is gone. <see cref="Socket.Connected"/> is not: it
    /// reports the state as of the last I/O operation and stays true after the remote end's FIN, so a half
    /// closed connection still looks connected to any caller that checks it. Callers that need to know
    /// whether the connection is usable should consult this as well.
    /// <para>
    /// There is no recovery in place. A connected socket cannot be reused once disconnected
    /// (<see cref="Socket.Connect(EndPoint)"/> throws <see cref="InvalidOperationException"/> after
    /// <see cref="Socket.Disconnect(bool)"/>, whatever endpoint is passed), so resuming means a new socket
    /// and a new receiver.
    /// </para>
    /// </remarks>
    public virtual bool IsEndOfStream { get; protected set; }

    /// <summary>
    /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
    /// return any data received.
    /// </summary>
    public override void BeginReceiveFrom()
    {
        //Prevent call BeginReceiveFrom if it is already running or into invalid state
        if ((m_isClosed || !m_socket.Connected) && m_isRunningReceive)
        {
            m_isRunningReceive = false;
        }
        if (m_isRunningReceive || m_isClosed || !m_socket.Connected)
        {
            return;
        }

        try
        {
            m_isRunningReceive = true;
            EndPoint recvEndPoint = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
            var recvLength = m_recvBuffer.Length - m_recvOffset;
            //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
            if (recvLength <= 0 || m_recvOffset < 0)
            {
                m_recvOffset = 0;
                recvLength = m_recvBuffer.Length;
            }
            m_socket.BeginReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref recvEndPoint, EndReceiveFrom, null);
        }
        // Thrown when socket is closed. Can be safely ignored.
        // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
        // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
        // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.
        catch (ObjectDisposedException)
        {
            m_isRunningReceive = false;
        }
        catch (SocketException sockExcp)
        {
            m_isRunningReceive = false;
            logger.LogIceSocketWarning(sockExcp.SocketErrorCode, sockExcp.Message, sockExcp);
            //Close(sockExcp.Message);
        }
        catch (Exception excp)
        {
            m_isRunningReceive = false;
            // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
            // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
            // case the socket can be considered to be unusable and there's no point trying another receive.
            logger.LogIceSocketReceiveError(excp.Message, excp);
            Close(excp.Message);
        }
    }

    /// <summary>
    /// Handler for end of the begin receive call.
    /// </summary>
    /// <param name="ar">Contains the results of the receive.</param>
    protected override void EndReceiveFrom(IAsyncResult ar)
    {
        // Set when a receive completes with zero bytes. On a stream socket that is the end of stream
        // indication, not an empty datagram as it would be on the UDP receive path, so the receive loop
        // must not be re-armed. Socket.Connected remains true after the peer's FIN, so the guard in
        // BeginReceiveFrom does not stop it: the re-arm would complete synchronously with zero bytes and
        // call straight back into this method, recursing until the process died with a StackOverflowException.
        // It is also surfaced on IsEndOfStream so the send path can tell a half closed connection from a
        // live one, which Socket.Connected cannot.
        var endOfStream = false;

        try
        {
            EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
            // When socket is closed the object will be disposed of in the middle of a receive.
            if (!m_isClosed)
            {
                var bytesRead = m_socket.EndReceiveFrom(ar, ref remoteEP);

                if (bytesRead > 0)
                {
                    ProcessRawBuffer(bytesRead + m_recvOffset, remoteEP as IPEndPoint);
                }
                else
                {
                    endOfStream = true;
                }
            }
            else
            {
                m_socket.EndReceiveFromClosed(ar, ref remoteEP);
            }

            // If there is still data available it should be read now. This is more efficient than calling
            // BeginReceiveFrom which will incur the overhead of creating the callback and then immediately firing it.
            // It also avoids the situation where if the application cannot keep up with the network then BeginReceiveFrom
            // will be called synchronously (if data is available it calls the callback method immediately) which can
            // create a very nasty stack.
            if (!m_isClosed && m_socket.Available > 0)
            {
                while (!m_isClosed && m_socket.Available > 0)
                {
                    remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                    var recvLength = m_recvBuffer.Length - m_recvOffset;
                    //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
                    if (recvLength <= 0 || m_recvOffset < 0)
                    {
                        m_recvOffset = 0;
                        recvLength = m_recvBuffer.Length;
                    }
                    var bytesReadSync = m_socket.ReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref remoteEP);

                    if (bytesReadSync > 0)
                    {
                        if (ProcessRawBuffer(bytesReadSync + m_recvOffset, remoteEP as IPEndPoint) == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        endOfStream = true;
                        break;
                    }
                }
            }
        }
        catch (SocketException resetSockExcp) when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset)
        {
            // This is a connected TCP socket to a STUN/TURN server, so unlike the UDP case this is a real
            // RST from the peer rather than a possibly spurious ICMP "port unreachable": the connection is
            // genuinely gone. It is still not a reason to close the receiver. The socket is reconnected
            // lazily by RtpIceChannel.SendOverTCP the next time a message needs to go to that ICE server,
            // and that reconnect only re-arms this receive loop while the receiver has not been closed.
            // Closing here would make the reconnect permanently unable to resume receiving. Re-arming is
            // safe because BeginReceiveFrom returns immediately while the socket is not connected.
            logger.LogIceSocketEndReceiveWarning(resetSockExcp.SocketErrorCode, resetSockExcp.Message, resetSockExcp);
        }
        catch (SocketException sockExcp)
        {
            // Other socket errors are handled the same way and for the same reason: the reconnect in
            // SendOverTCP is what recovers the connection, and it needs this receiver left open to do it.
            logger.LogIceSocketEndReceiveWarning(sockExcp.SocketErrorCode, sockExcp.Message, sockExcp);
        }
        catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
        { }
        catch (Exception excp)
        {
            // A non-socket exception here almost always originates from processing a single inbound packet
            // (e.g. a malformed STUN/TURN packet throwing in the packet-received handler). Closing the
            // receiver on one bad packet permanently kills the ICE-over-TCP path for the session because
            // Close sets m_isClosed and the re-arm in the finally block below then becomes a no-op. Log and
            // drop the offending packet instead and let the receive loop continue, mirroring the
            // drop-and-continue behaviour of the base UdpReceiver.EndReceiveFrom. Genuine socket failures
            // are handled by the SocketException/ObjectDisposedException catches above.
            logger.LogIceTcpReceiveError(excp.Message, excp);
        }
        finally
        {
            m_isRunningReceive = false;
            // On end of stream the receiver is left open but idle rather than closed. Closing here would
            // fire OnClosed and tear down state for what is a normal way for a connection to end. The flag
            // is published after m_isRunningReceive is cleared so a caller that observes the receiver as
            // idle also sees why it went idle, which is what RtpIceChannel.SendOverTCP checks before it
            // tries to send anything else to that ICE server.
            if (endOfStream)
            {
                IsEndOfStream = true;
            }

            if (!m_isClosed && !endOfStream)
            {
                BeginReceiveFrom();
            }
        }
    }

    // TODO: If we miss any package because slow internet connection
    // and initial byte in buffer is not a STUNHeader (starts with 0x00 0x00)
    // and our receive buffer is full, we need a way to discard whole buffer
    // or check for 0x00 0x00 start again.
    protected virtual int ProcessRawBuffer(int bytesRead, IPEndPoint remoteEP)
    {
        var extractCount = 0;
        if (bytesRead > 0)
        {
            // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
            // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
            // is relying on getting the local IP address on Linux then something may fail.
            //if (packetInfo != null && packetInfo.Address != null)
            //{
            //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
            //}

            //Try extract all StunMessages from current receive buffer
            var isFragmented = true;
            var recvRemainingSegment = new ArraySegment<byte>(m_recvBuffer, 0, bytesRead);

            while (recvRemainingSegment.Count > STUNHeader.STUN_HEADER_LENGTH)
            {
                isFragmented = false;
                STUNHeader header = null;
                try
                {
                    header = STUNHeader.ParseSTUNHeader(recvRemainingSegment);
                }
                catch
                {
                    header = null;
                }
                if (header != null)
                {
                    var stunMsgBytes = STUNHeader.STUN_HEADER_LENGTH + header.MessageLength;
                    if (stunMsgBytes % 4 != 0)
                    {
                        stunMsgBytes = stunMsgBytes - (stunMsgBytes % 4) + 4;
                    }

                    //We have the packet count all inside current receiving buffer
                    if (recvRemainingSegment.Count >= stunMsgBytes)
                    {
                        extractCount++;
                        m_recvOffset = recvRemainingSegment.Offset + recvRemainingSegment.Count;

                        var packetBuffer = new byte[stunMsgBytes];
                        Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, packetBuffer, 0, stunMsgBytes);

                        // A throw from the packet handler must not escape the framing loop. Unwinding here
                        // would leave m_recvOffset at the value set above and skip the fragmentation
                        // bookkeeping at the end of the method, so the next read would be written at a stale
                        // offset and every subsequent message on the stream would be mis-framed. Log the bad
                        // packet, keep the framing state consistent and carry on with the rest of the buffer.
                        try
                        {
                            CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP, packetBuffer);
                        }
                        catch (Exception excp)
                        {
                            logger.LogError(excp, "Exception IceTcpReceiver processing a received packet from {RemoteEndPoint}. {ErrorMessage}", remoteEP, excp.Message);
                        }

                        var newOffset = recvRemainingSegment.Offset + stunMsgBytes;
                        var newCount = recvRemainingSegment.Count - stunMsgBytes;
                        if (newCount > STUNHeader.STUN_HEADER_LENGTH && newOffset >= 0)
                        {
                            recvRemainingSegment = new ArraySegment<byte>(recvRemainingSegment.Array, newOffset, newCount);
                        }
                        else
                        {
                            if (newCount > 0 && newOffset >= 0)
                            {
                                recvRemainingSegment = new ArraySegment<byte>(recvRemainingSegment.Array, newOffset, newCount);
                                isFragmented = true;
                            }
                            else
                            {
                                recvRemainingSegment = new ArraySegment<byte>();
                                isFragmented = false;
                            }
                            break;
                        }
                    }
                    //We have a fragmentation but the header is intact, we need to cache the fragmentation for the next receive cycle
                    else
                    {
                        isFragmented = true;
                        break;
                    }
                }
                //Save Remaining Buffer in start of m_recvBuffer
                else
                {
                    isFragmented = true;
                    break;
                }
            }

            if (isFragmented)
            {
                m_recvOffset = recvRemainingSegment.Count;
                Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, m_recvBuffer, 0, recvRemainingSegment.Count);
            }
            else
            {
                m_recvOffset = 0;
            }
        }

        return extractCount;
    }

    /// <summary>
    /// Closes the socket and stops any new receives from being initiated.
    /// </summary>
    public override void Close(string reason)
    {
        if (!m_isClosed)
        {
            if (m_socket != null && m_socket.Connected)
            {
                m_socket.Disconnect(false);
            }
            base.Close(reason);
        }
    }
}
