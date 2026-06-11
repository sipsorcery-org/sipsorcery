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
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

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
public class SocketUdpConnection : SocketConnection
{
    public SocketUdpConnection(Socket socket, int mtu = RECEIVE_BUFFER_SIZE) : base(socket, mtu)
    {
    }

    protected override async Task SendToCoreAsync(IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner)
    {
        try
        {
            await Socket.SendToAsync(buffer, SocketFlags.None, dstEndPoint).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
        {
            // Ignore cancelled operations when the connection is closed.
        }
        catch (ObjectDisposedException)
        {
            // Thrown when socket is closed. Can be safely ignored.
        }
        catch (SocketException sockExcp)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            logger.LogRtpChannelSocketError(sockExcp.SocketErrorCode);
        }
        catch (Exception excp)
        {
            logger.LogRtpChannelGeneralException(excp);
        }
        finally
        {
            memoryOwner?.Dispose();
        }
    }

    /// <summary>
    /// Starts the receive. This method returns immediately. An event will be fired in the corresponding "End" event to
    /// return any data received.
    /// </summary>
    public override void BeginReceiveFrom()
    {
        //Prevent call BeginReceiveFrom if it is already running
        if (IsRunningReceive || IsClosed || IsClosing)
        {
            return;
        }

        IsRunningReceive = true;

        _ = BeginReceiveFromCoreAsync();

        async Task BeginReceiveFromCoreAsync()
        {
            var buffer = ArrayPool<byte>.Shared.Rent(Mtu);

            try
            {
                while (!IsClosed)
                {
                    try
                    {
                        EndPoint remoteEP = AddressFamily == AddressFamily.InterNetwork
                            ? new IPEndPoint(IPAddress.Any, 0)
                            : new IPEndPoint(IPAddress.IPv6Any, 0);

                        var result = await Socket.ReceiveFromAsync(
                            buffer,
                            SocketFlags.None,
                            remoteEP,
                            CancellationTokenSource.Token
                        ).ConfigureAwait(false);

                        if (result.ReceivedBytes > 0)
                        {
                            CallOnPacketReceivedCallback(
                                LocalEndPoint.Port,
                                result.RemoteEndPoint as IPEndPoint,
                                buffer.AsMemory(0, result.ReceivedBytes)
                            );
                        }
                    }
                    catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
                    {
                        // Ignore cancelled operations when the connection is closed.
                    }
                    catch (ObjectDisposedException)
                    {
                        // Thrown when socket is closed. Can be safely ignored.
                    }
                    catch (SocketException sockExcp)
                    {
                        // This exception can be thrown in response to an ICMP packet. The problem is the ICMP packet can be a false positive.
                        // For example if the remote RTP socket has not yet been opened the remote host could generate an ICMP packet for the 
                        // initial RTP packets. Experience has shown that it's not safe to close an RTP connection based solely on ICMP packets.

                        // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
                        // normal RTP operation. For example:
                        // - the RTP connection may start sending before the remote socket starts listening,
                        // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
                        //   or new socket during the transition.
                        // It also seems that once a UDP socket pair have exchanged packets and the remote party closes the socket exception will occur
                        // in the BeginReceive method (very handy). Follow-up, this doesn't seem to be the case, the socket exception can occur in 
                        // BeginReceive before any packets have been exchanged. This means it's not safe to close if BeginReceive gets an ICMP 
                        // error since the remote party may not have initialised their socket yet.

                        logger.LogRtpSocketException(sockExcp.SocketErrorCode, sockExcp.Message);
                    }
                    catch (Exception excp)
                    {
                        // From https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/Socket.cs#L3262
                        // the BeginReceiveFrom will only throw if there is an problem with the arguments or the socket has been disposed of. In that
                        // case the socket can be considered to be unusable and there's no point trying another receive.

                        logger.LogRtpChannelBeginReceiveError(excp.Message, excp);

                        IsRunningReceive = false;

                        Close(excp.Message);
                    }
                }
            }
            finally
            {
                IsRunningReceive = false;
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
