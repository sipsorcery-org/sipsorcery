using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

public class SocketTcpConnection : SocketConnection
{
    private readonly Pipe m_pipe = new(new PipeOptions(useSynchronizationContext: false));

    public SocketTcpConnection(Socket socket, int mtu = RECEIVE_BUFFER_SIZE) : base(socket, mtu)
    {
    }

    public override void BeginReceiveFrom()
    {
        if (IsClosed || IsClosing || !Socket.Connected)
        {
            return;
        }

        if (IsRunningReceive)
        {
            return;
        }

        IsRunningReceive = true;

        try
        {
            _ = BeginReceiveFromCoreAsync();

            async Task BeginReceiveFromCoreAsync()
            {
                try
                {
                    while (!IsClosed)
                    {
                        try
                        {
                            var buffer = m_pipe.Writer.GetMemory(Mtu);

                            var bytesRead = await ReadBytesAsync(buffer).ConfigureAwait(false);

                            if (bytesRead > 0)
                            {
                                m_pipe.Writer.Advance(bytesRead);
                                await m_pipe.Writer.FlushAsync().ConfigureAwait(false);

                                var localEndPoint = Socket.LocalEndPoint as IPEndPoint;
                                var remoteEndPoint = Socket.RemoteEndPoint as IPEndPoint;
                                Debug.Assert(localEndPoint is { });
                                Debug.Assert(remoteEndPoint is { });
                                ProcessRawBuffer(m_pipe.Reader, localEndPoint, remoteEndPoint);
                            }
                        }
                        catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
                        {
                            // Ignore cancelled operations when the connection is closed.
                        }
                        catch (IOException ex) when (ex.InnerException is SocketException sockEx)
                        {
                            logger.LogIceSocketWarning(sockEx.SocketErrorCode, sockEx.Message, sockEx);
                        }
                        catch (Exception excp)
                        {
                            logger.LogIceSocketReceiveError(excp.Message, excp);
                            Close(excp.Message);
                            break;
                        }
                    }
                }
                finally
                {
                    IsRunningReceive = false;
                }
            }
        }
        catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
        {
            // Ignore cancelled operations when the connection is closed.
        }
        catch (Exception excp)
        {
            Close(excp.Message);
        }
    }

    protected override async Task SendToCoreAsync(IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner)
    {
        if (!Socket.Connected
            || Socket.RemoteEndPoint is not IPEndPoint remoteEndPoint
            || remoteEndPoint.Port != dstEndPoint.Port
            || !remoteEndPoint.Address.Equals(dstEndPoint.Address))
        {
            if (Socket.Connected)
            {
                logger.LogTcpDisconnectRequest();
                await Socket.DisconnectAsync(true).ConfigureAwait(false);
            }

            await Socket.ConnectAsync(dstEndPoint).ConfigureAwait(false);

            logger.LogTcpSendStatus(Socket.Connected, dstEndPoint);
        }

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
        catch (SocketException ex)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            logger.LogRtpChannelSocketError(ex.SocketErrorCode);
        }
        catch (Exception ex)
        {
            logger.LogRtpChannelGeneralException(ex);
        }
        finally
        {
            memoryOwner?.Dispose();
        }
    }

    protected virtual async Task<int> ReadBytesAsync(Memory<byte> buffer)
    {
        var result = await Socket.ReceiveFromAsync(
            buffer,
            SocketFlags.None,
            Socket.RemoteEndPoint!,
            CancellationTokenSource.Token).ConfigureAwait(false);

        var bytesRead = result.ReceivedBytes;
        return bytesRead;
    }

    protected void ProcessRawBuffer(PipeReader reader, IPEndPoint localEndPoint, IPEndPoint? remoteEndPoint)
    {
        var rentedBuffer = default(byte[]);

        try
        {
            Span<byte> stunMessageLengthBytes = stackalloc byte[2];

            while (reader.TryRead(out var readResult) && readResult.Buffer.Length > STUNHeader.STUN_HEADER_LENGTH)
            {
                // TODO: If we miss any package because slow internet connection
                // and initial byte in buffer is not a STUNHeader (starts with 0x00 0x00)
                // and our receive buffer is full, we need a way to discard whole buffer
                // or check for 0x00 0x00 start again.

                readResult.Buffer.Slice(2, 2).CopyTo(stunMessageLengthBytes);
                var stunMessageLength = BinaryPrimitives.ReadUInt16BigEndian(stunMessageLengthBytes);

                var stunMsgBytes = (STUNHeader.STUN_HEADER_LENGTH + stunMessageLength + 3) & ~3;

                //We have the packet count all inside current receiving buffer
                if (readResult.Buffer.Length >= stunMsgBytes)
                {
                    var messageSequence = readResult.Buffer.Slice(0, stunMsgBytes);
                    var stunMessageBuffer = ReadBuffer(messageSequence, ref rentedBuffer);

                    CallOnPacketReceivedCallback(localEndPoint.Port, remoteEndPoint, stunMessageBuffer);

                    reader.AdvanceTo(messageSequence.End);
                }

                static ReadOnlyMemory<byte> ReadBuffer(ReadOnlySequence<byte> buffer, ref byte[]? rentedBuffer)
                {
                    if (buffer.IsSingleSegment)
                    {
                        return buffer.First.Slice(0, (int)buffer.Length);
                    }

                    if (rentedBuffer is { } && rentedBuffer.Length < buffer.Length)
                    {
                        ArrayPool<byte>.Shared.Return(rentedBuffer);
                        rentedBuffer = null;
                    }

                    if (rentedBuffer is null)
                    {
                        rentedBuffer = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                    }

                    buffer.CopyTo(rentedBuffer);

                    return rentedBuffer.AsMemory(0, (int)buffer.Length);
                }
            }
        }
        finally
        {
            if (rentedBuffer is { })
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }
}
