//-----------------------------------------------------------------------------
// Filename: UdpReceiver.cs
//
// Description: A UDP socket manager that encapsulates the common logic for managing UDP sockets.
// Original use case for managing RTP communications..
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Sep 2025	Aaron Clauson	Refactored from RTPChannel class.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public delegate void PacketReceivedDelegate(UdpReceiver receiver, int localPort, IPEndPoint remoteEndPoint, byte[] packet);

/// <summary>
/// A basic UDP socket manager. The RTP channel may need both an RTP and Control socket. This class encapsulates
/// the common logic for UDP socket management.
/// </summary>
/// <remarks>
/// .NET Framework Socket source:
/// https://referencesource.microsoft.com/#system/net/system/net/Sockets/Socket.cs
/// .NET Core Socket source:
/// https://github.com/dotnet/runtime/blob/master/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.cs
/// Mono Socket source:
/// https://github.com/mono/mono/blob/master/mcs/class/System/System.Net.Sockets/Socket.cs
/// </remarks>
public class UdpReceiver
{
    /// <summary>
    /// MTU is 1452 bytes so this should be heaps [AC 03 Nov 2024: turns out it's not when considering UDP fragmentation can
    /// result in a max UDP payload of 65535 - 8 (header) = 65527 bytes].
    /// An issue was reported with a real World WeBRTC implementation producing UDP packet sizes of 2144 byes #1045. Consequently
    /// updated from 2048 to 3000.
    /// </summary>
    protected const int RECEIVE_BUFFER_SIZE = 3000;

    protected static ILogger logger = Log.Logger;

    protected readonly Socket m_socket;
    protected byte[] m_recvBuffer;
    protected bool m_isClosed, m_isClosing;
    protected bool m_isRunningReceive;
    protected IPEndPoint m_localEndPoint;
    protected AddressFamily m_addressFamily;

    public virtual bool IsClosed
    {
        get
        {
            return m_isClosed;
        }
        protected set
        {
            if (m_isClosed == value)
            {
                return;
            }
            m_isClosed = value;
        }
    }

    public virtual bool IsRunningReceive
    {
        get
        {
            return m_isRunningReceive;
        }
        protected set
        {
            if (m_isRunningReceive == value)
            {
                return;
            }
            m_isRunningReceive = value;
        }
    }

    /// <summary>
    /// Fires when a new packet has been received on the UDP socket.
    /// </summary>
    public event PacketReceivedDelegate OnPacketReceived;

    /// <summary>
    /// Fires when there is an error attempting to receive on the UDP socket.
    /// </summary>
    public event Action<string> OnClosed;

    public UdpReceiver(Socket socket, int mtu = RECEIVE_BUFFER_SIZE)
    {
        m_socket = socket;
        m_localEndPoint = m_socket.LocalEndPoint as IPEndPoint;
        m_recvBuffer = new byte[mtu];
        m_addressFamily = m_socket.LocalEndPoint.AddressFamily;
    }

    /// <summary>
    /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
    /// return any data received.
    /// </summary>
    public virtual void BeginReceiveFrom()
    {
        //Prevent call BeginReceiveFrom if it is already running
        if (m_isClosed && m_isRunningReceive)
        {
            m_isRunningReceive = false;
        }
        if (m_isRunningReceive || m_isClosed || m_isClosing)
        {
            return;
        }

        try
        {
            m_isRunningReceive = true;
            EndPoint recvEndPoint = m_addressFamily == AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any, 0) : new IPEndPoint(IPAddress.IPv6Any, 0);
            m_socket.BeginReceiveFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref recvEndPoint, EndReceiveFrom, null);
        }
        catch (ObjectDisposedException)
        {
            // Thrown when socket is closed. Can be safely ignored.
            m_isRunningReceive = false;
        }
        catch (SocketException sockExcp)
        {
            // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
            // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
            // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.

            m_isRunningReceive = false;
            logger.LogWarning("Socket error {SocketErrorCode} in UdpReceiver.BeginReceiveFrom. {Message}", sockExcp.SocketErrorCode, sockExcp.Message);
            //Close(sockExcp.Message);
        }
        catch (Exception excp)
        {
            m_isRunningReceive = false;
            // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
            // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
            // case the socket can be considered to be unusable and there's no point trying another receive.
            logger.LogError(excp, "Exception UdpReceiver.BeginReceiveFrom. {ErrorMessage}", excp.Message);
            Close(excp.Message);
        }
    }

    /// <summary>
    /// Handler for end of the begin receive call.
    /// </summary>
    /// <param name="ar">Contains the results of the receive.</param>
    protected virtual void EndReceiveFrom(IAsyncResult ar)
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
                    // During experiments IPPacketInformation wasn't getting set on Linux. Without it the local IP address
                    // cannot be determined when a listener was bound to IPAddress.Any (or IPv6 equivalent). If the caller
                    // is relying on getting the local IP address on Linux then something may fail.
                    //if (packetInfo != null && packetInfo.Address != null)
                    //{
                    //    localEndPoint = new IPEndPoint(packetInfo.Address, localEndPoint.Port);
                    //}

                    byte[] packetBuffer = new byte[bytesRead];
                    // TODO: When .NET Framework support is dropped switch to using a slice instead of a copy.
                    Buffer.BlockCopy(m_recvBuffer, 0, packetBuffer, 0, bytesRead);
                    CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP as IPEndPoint, packetBuffer);
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
                    int bytesReadSync = m_socket.ReceiveFrom(m_recvBuffer, 0, m_recvBuffer.Length, SocketFlags.None, ref remoteEP);

                    if (bytesReadSync > 0)
                    {
                        byte[] packetBufferSync = new byte[bytesReadSync];
                        // TODO: When .NET Framework support is dropped switch to using a slice instead of a copy.
                        Buffer.BlockCopy(m_recvBuffer, 0, packetBufferSync, 0, bytesReadSync);
                        CallOnPacketReceivedCallback(m_localEndPoint.Port, remoteEP as IPEndPoint, packetBufferSync);
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
            // Thrown when close is called on a socket
            m_isClosing = true;
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
            logger.LogWarning(sockExcp, "SocketException UdpReceiver.EndReceiveFrom ({SocketErrorCode}). {ErrorMessage}", sockExcp.SocketErrorCode, sockExcp.Message);
        }
        catch (ObjectDisposedException) // Thrown when socket is closed. Can be safely ignored.
        { }
        catch (Exception excp)
        {
            logger.LogError(excp, "Exception UdpReceiver.EndReceiveFrom. {ErrorMessage}", excp.Message);
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

    /// <summary>
    /// Closes the socket and stops any new receives from being initiated.
    /// </summary>
    public virtual void Close(string reason)
    {
        if (!m_isClosed)
        {
            m_isClosed = true;
            m_socket?.Close();

            OnClosed?.Invoke(reason);
        }
    }

    protected virtual void CallOnPacketReceivedCallback(int localPort, IPEndPoint remoteEndPoint, byte[] packet)
    {
        OnPacketReceived?.Invoke(this, localPort, remoteEndPoint, packet);
    }
}
