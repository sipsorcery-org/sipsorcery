//-----------------------------------------------------------------------------
// Filename: RTPChannel.cs
//
// Description: Communications channel to send and receive RTP and RTCP packets
// and whatever else happens to be multiplexed.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 27 Feb 2012	Aaron Clauson	Created, Hobart, Australia.
// 06 Dec 2019  Aaron Clauson   Simplify by removing all frame logic and reduce responsibility
//                              to only managing sending and receiving of packets.
// 28 Dec 2019  Aaron Clauson   Added RTCP reporting as per RFC3550.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

public abstract class SocketConnection
{
    /// <summary>
    /// MTU is 1452 bytes so this should be heaps [AC 03 Nov 2024: turns out it's not when considering UDP fragmentation can
    /// result in a max UDP payload of 65535 - 8 (header) = 65527 bytes].
    /// An issue was reported with a real World WeBRTC implementation producing UDP packet sizes of 2144 byes #1045. Consequently
    /// updated from 2048 to 3000.
    /// </summary>
    protected const int RECEIVE_BUFFER_SIZE = 3000;

    protected static ILogger logger = Log.Logger;

    protected SocketConnection(Socket socket, int mtu = RECEIVE_BUFFER_SIZE)
    {
        Mtu = mtu;

        Socket = socket;
        if (Socket.LocalEndPoint is not IPEndPoint localEndPoint)
        {
            throw new InvalidOperationException($"The socket is required to have a LocalEndPoint of type IPEndpoint and it was {(Socket.LocalEndPoint is null ? "<null>" : Socket.LocalEndPoint.GetType().FullName)}");
        }

        LocalEndPoint = localEndPoint;
        AddressFamily = localEndPoint.AddressFamily;
    }

    public Socket Socket { get; }

    public virtual bool IsClosed => CancellationTokenSource.IsCancellationRequested;

    public virtual bool IsRunningReceive { get; protected set; }

    protected virtual bool IsClosing { get; set; }

    /// <summary>
    /// Returns true if the RTP socket supports dual mode IPv4 and IPv6. If the control
    /// socket exists it will be the same.
    /// </summary>
    public bool IsDualMode => Socket is { AddressFamily: AddressFamily.InterNetworkV6 } && Socket.DualMode;

    protected int Mtu { get; }

    protected IPEndPoint LocalEndPoint { get; }

    protected AddressFamily AddressFamily { get; }

    protected CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

    /// <summary>
    /// Fires when a new packet has been received on the UDP socket.
    /// </summary>
    public event Action<SocketConnection, int, IPEndPoint?, ReadOnlyMemory<byte>>? OnPacketReceived;

    /// <summary>
    /// Fires when there is an error attempting to receive on the UDP socket.
    /// </summary>
    public event Action<string?>? OnClosed;

    public abstract void BeginReceiveFrom();

    public SocketError SendTo(IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner)
    {
        if (IsClosed)
        {
            return SocketError.Disconnecting;
        }
        else if (dstEndPoint is null)
        {
            throw new ArgumentException("An empty destination was specified to Send in RTP connection.", nameof(dstEndPoint));
        }
        else if (buffer.IsEmpty)
        {
            throw new ArgumentException("The buffer must be set and non empty for Send in RTP connection.", nameof(buffer));
        }
        else if (IPAddress.Any.Equals(dstEndPoint.Address) || IPAddress.IPv6Any.Equals(dstEndPoint.Address))
        {
            logger.LogRtpDestinationAddressInvalid(dstEndPoint.Address);
            return SocketError.DestinationAddressRequired;
        }

        //Prevent Send to IPV4 while socket is IPV6 (Mono Error)
        if (dstEndPoint.AddressFamily == AddressFamily.InterNetwork && Socket.AddressFamily != dstEndPoint.AddressFamily)
        {
            dstEndPoint = new IPEndPoint(dstEndPoint.Address.MapToIPv6(), dstEndPoint.Port);
        }

        try
        {
            _ = SendToCoreAsync(dstEndPoint, buffer, memoryOwner);

            BeginReceiveFrom();

            return SocketError.Success;
        }
        catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
        {
            // Ignore cancelled operations when the connection is closed.
            return SocketError.Disconnecting;
        }
        catch (ObjectDisposedException)
        {
            // Thrown when socket is closed. Can be safely ignored.
            return SocketError.Disconnecting;
        }
        catch (SocketException sockExcp)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            logger.LogRtpChannelSocketError(sockExcp.SocketErrorCode);
            return sockExcp.SocketErrorCode;
        }
        catch (Exception excp)
        {
            logger.LogRtpChannelGeneralException(excp);
            return SocketError.Fault;
        }
    }

    protected abstract Task SendToCoreAsync(IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner);

    /// <summary>
    /// Closes the socket and stops any new receives from being initiated.
    /// </summary>
    public virtual void Close(string? reason)
    {
        if (!IsClosed)
        {
            CancellationTokenSource.Cancel();

            if (Socket is { })
            {
                if (Socket.Connected)
                {
                    Socket.Disconnect(false);
                }

                Socket.Close();
            }

            OnClosed?.Invoke(reason);
        }
    }

    protected virtual void CallOnPacketReceivedCallback(int localPort, IPEndPoint? remoteEndPoint, ReadOnlyMemory<byte> packet)
    {
        OnPacketReceived?.Invoke(this, localPort, remoteEndPoint, packet);
    }
}
