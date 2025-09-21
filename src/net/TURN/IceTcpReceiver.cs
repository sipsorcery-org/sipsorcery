//-----------------------------------------------------------------------------
// Filename: IceTcpReceiver.cs
//
// Description: TBD.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Aug 2025  Aaron Clauson   Refactored from RtpIceChannel .
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net;

public class IceTcpReceiver : UdpReceiver
{
    protected const int RECEIVE_TCP_BUFFER_SIZE = RECEIVE_BUFFER_SIZE * 2;

    protected int m_recvOffset;

    public IceTcpReceiver(Socket socket, int mtu = RECEIVE_TCP_BUFFER_SIZE) : base(socket, mtu)
    {
        m_recvOffset = 0;
    }

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
            logger.LogWarning(sockExcp, "Socket error {SocketErrorCode} in IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
            //Close(sockExcp.Message);
        }
        catch (Exception excp)
        {
            m_isRunningReceive = false;
            // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
            // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
            // case the socket can be considered to be unusable and there's no point trying another receive.
            logger.LogError(excp, "Exception IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}", excp.Message);
            Close(excp.Message);
        }
    }

    /// <summary>
    /// Handler for end of the begin receive call.
    /// </summary>
    /// <param name="ar">Contains the results of the receive.</param>
    protected override void EndReceiveFrom(IAsyncResult ar)
    {
        try
        {
            // When socket is closed the object will be disposed of in the middle of a receive.
            if (!m_isClosed)
            {
                EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                int bytesRead = m_socket.EndReceiveFrom(ar, ref remoteEP);

                if (bytesRead > 0)
                {
                    ProcessRawBuffer(bytesRead + m_recvOffset, remoteEP as IPEndPoint);
                }
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
                    EndPoint remoteEP = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
                    var recvLength = m_recvBuffer.Length - m_recvOffset;
                    //Discard fragmentation buffer as seems that we will have an incorrect result based in cached values
                    if (recvLength <= 0 || m_recvOffset < 0)
                    {
                        m_recvOffset = 0;
                        recvLength = m_recvBuffer.Length;
                    }
                    int bytesReadSync = m_socket.ReceiveFrom(m_recvBuffer, m_recvOffset, recvLength, SocketFlags.None, ref remoteEP);

                    if (bytesReadSync > 0)
                    {
                        if (ProcessRawBuffer(bytesReadSync + m_recvOffset, remoteEP as IPEndPoint) == 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (SocketException resetSockExcp) when (resetSockExcp.SocketErrorCode == SocketError.ConnectionReset)
        {
            // Thrown when close is called on a socket from this end. Safe to ignore.
        }
        catch (SocketException sockExcp)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
            // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in 
            // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP 
            // error since the remote party may not have initialised their socket yet.
            logger.LogWarning(sockExcp, "SocketException IceTcpReceiver.EndReceiveFrom ({SocketErrorCode}). {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
        }
        catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
        { }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception IceTcpReceiver.EndReceiveFrom. {ErrorMessage}", excp.Message);
            Close(excp.Message);
        }
        finally
        {
            m_isRunningReceive = false;
            if (!m_isClosed)
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
                    int stunMsgBytes = STUNHeader.STUN_HEADER_LENGTH + header.MessageLength;
                    if (stunMsgBytes % 4 != 0)
                    {
                        stunMsgBytes = stunMsgBytes - (stunMsgBytes % 4) + 4;
                    }

                    //We have the packet count all inside current receiving buffer
                    if (recvRemainingSegment.Count >= stunMsgBytes)
                    {
                        extractCount++;
                        m_recvOffset = recvRemainingSegment.Offset + recvRemainingSegment.Count;

                        byte[] packetBuffer = new byte[stunMsgBytes];
                        Buffer.BlockCopy(recvRemainingSegment.Array, recvRemainingSegment.Offset, packetBuffer, 0, stunMsgBytes);

                        CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP, packetBuffer);

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
                m_socket?.Disconnect(false);
            }
            base.Close(reason);
        }
    }
}
