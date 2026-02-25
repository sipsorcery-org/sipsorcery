using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace SIPSorcery.Sys;

internal sealed class SocketTlsConnection : SocketTcpConnection
{
    private SslStream? m_sslStream;
    private readonly SslClientAuthenticationOptions m_sslClientAuthenticationOptions;
    private readonly SemaphoreSlim m_sslStreamLock = new(1);

    public SocketTlsConnection(Socket socket, string targetHost, SslClientAuthenticationOptions? sslClientAuthenticationOptions, int mtu = RECEIVE_BUFFER_SIZE) : base(socket, mtu)
    {
        m_sslClientAuthenticationOptions = SslClientAuthenticationOptions.CreateFrom(sslClientAuthenticationOptions);
        m_sslClientAuthenticationOptions.TargetHost ??= targetHost;
    }

    protected override async Task SendToCoreAsync(IPEndPoint dstEndPoint, ReadOnlyMemory<byte> buffer, IDisposable? memoryOwner)
    {
        await m_sslStreamLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (m_sslStream is null
                || !Socket.Connected
                || Socket.RemoteEndPoint is not IPEndPoint remoteEndPoint
                || remoteEndPoint.Port != dstEndPoint.Port
                || !remoteEndPoint.Address.Equals(dstEndPoint.Address))
            {
                if (m_sslStream is { })
                {
                    await m_sslStream.DisposeAsync().ConfigureAwait(false);
                }

                if (Socket.Connected)
                {
                    logger.LogTcpDisconnectRequest();
                    await Socket.DisconnectAsync(true, CancellationTokenSource.Token).ConfigureAwait(false);
                }

                await Socket.ConnectAsync(dstEndPoint, CancellationTokenSource.Token).ConfigureAwait(false);

                logger.LogTcpSendStatus(Socket.Connected, dstEndPoint);

                m_sslStream = new SslStream(new NetworkStream(Socket, ownsSocket: false), leaveInnerStreamOpen: false);

                await m_sslStream.AuthenticateAsClientAsync(m_sslClientAuthenticationOptions, CancellationTokenSource.Token).ConfigureAwait(false);
            }

            Debug.Assert(m_sslStream is { });

            await m_sslStream.WriteAsync(buffer, CancellationTokenSource.Token).ConfigureAwait(false);
            await m_sslStream.FlushAsync(CancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
        {
            // Ignore cancelled operations when the connection is closed.
        }
        catch (IOException ex) when (ex.InnerException is SocketException sockEx)
        {
            // Socket errors do not trigger a close. The reason being that there are genuine situations that can cause them during
            // normal RTP operation. For example:
            // - the RTP connection may start sending before the remote socket starts listening,
            // - an on hold, transfer, etc. operation can change the RTP end point which could result in socket errors from the old
            //   or new socket during the transition.
            logger.LogRtpChannelSocketError(sockEx.SocketErrorCode);
        }
        catch (Exception ex)
        {
            logger.LogRtpChannelGeneralException(ex);
        }
        finally
        {
            m_sslStreamLock.Release();
            memoryOwner?.Dispose();
        }
    }

    protected override async Task<int> ReadBytesAsync(Memory<byte> buffer)
    {
        await m_sslStreamLock.WaitAsync(CancellationTokenSource.Token).ConfigureAwait(false);

        try
        {
            Debug.Assert(m_sslStream is { });

            return await m_sslStream.ReadAsync(buffer, CancellationTokenSource.Token).ConfigureAwait(false);
        }
        finally
        {
            m_sslStreamLock.Release();
        }
    }

    public override void Close(string? reason)
    {
        if (!IsClosed)
        {
            m_sslStream?.Dispose();
            m_sslStream = null;

            base.Close(reason);
        }
    }
}
