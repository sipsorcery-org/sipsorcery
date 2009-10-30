using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Sockets
{
    public delegate void SocketDataReceivedDelegate(byte[] data, int bytesRead);
    public delegate void SocketConnectionChangeDelegate(SocketConnectionStatus connectionStatus);

    public struct SocketConnectionStatus
    {
        public ServiceConnectionStatesEnum ConnectionStatus;
        public string Message;

        public SocketConnectionStatus(ServiceConnectionStatesEnum connectionStatus, string message)
        {
            ConnectionStatus = connectionStatus;
            Message = message;
        }
    }

    /// <summary>
    /// A client to connect to a SIP Monitor Server to allow event notifications to be received.
    /// The client is designed to receive only with the only requirement to sned anything to the server being
    /// the authoirsation id if/when a user logs in which allows customisation of the events.
    /// </summary>
    public class SocketClient
    {
        private const int MAX_SOCKET_BUFFER_SIZE = 4096;        // Max amount of data that can be recived from the socket on a single read.

        public event SocketDataReceivedDelegate SocketDataReceived;
        public event SocketConnectionChangeDelegate SocketConnectionChange;
        
        public bool Initialised { get; private set; }

        private EndPoint m_serverEndPoint;
        private Socket m_socket;
        private byte[] m_socketBuffer = new byte[MAX_SOCKET_BUFFER_SIZE];
        private bool m_isConnected;
        public bool m_closeRequested;

        public SocketClient(EndPoint serverEndPoint)
        {
            m_serverEndPoint = serverEndPoint;
        }

        public void ConnectAsync()
        {
            Initialised = true;
            
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketAsyncEventArgs socketConnectionArgs = new SocketAsyncEventArgs();
            socketConnectionArgs.UserToken = m_serverEndPoint;
            socketConnectionArgs.RemoteEndPoint = m_serverEndPoint;
            socketConnectionArgs.Completed += SocketConnect_Completed;

            m_socket.ConnectAsync(socketConnectionArgs);
        }

        private void SocketConnect_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                m_isConnected = (e.SocketError == SocketError.Success);

                if (m_isConnected)
                {
                    if (SocketConnectionChange != null)
                    {
                        SocketConnectionChange(new SocketConnectionStatus(ServiceConnectionStatesEnum.Ok, "Successfully connected to SIP monitor."));
                    }

                    SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                    receiveArgs.SetBuffer(m_socketBuffer, 0, MAX_SOCKET_BUFFER_SIZE);
                    receiveArgs.Completed += SocketRead_Completed;
                    m_socket.ReceiveAsync(receiveArgs);
                }
                else
                {
                    if (SocketConnectionChange != null && m_closeRequested) {
                        SocketConnectionChange(new SocketConnectionStatus(ServiceConnectionStatesEnum.Error, "Connection to " + m_serverEndPoint + " failed."));
                    }
                }
            }
            catch (Exception excp)
            {
                if (SocketConnectionChange != null && m_closeRequested) {
                    SocketConnectionChange(new SocketConnectionStatus(ServiceConnectionStatesEnum.Error, "Exception connecting to " + m_serverEndPoint + ". " + excp.Message));
                }
            }
        }

        private void SocketRead_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                int bytesRead = e.BytesTransferred;
                if (bytesRead > 0)
                {
                    SocketDataReceived(m_socketBuffer, bytesRead);

                    SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                    receiveArgs.SetBuffer(m_socketBuffer, 0, MAX_SOCKET_BUFFER_SIZE);
                    receiveArgs.Completed += SocketRead_Completed;
                    m_socket.ReceiveAsync(receiveArgs);
                }
                else
                {
                    if (SocketConnectionChange != null && !m_closeRequested) {
                        SocketConnectionChange(new SocketConnectionStatus(ServiceConnectionStatesEnum.Error, "Connection has been closed."));
                    }
                }
            }
            catch(Exception excp) 
            {
                if (SocketConnectionChange != null && !m_closeRequested) {
                    SocketConnectionChange(new SocketConnectionStatus(ServiceConnectionStatesEnum.Error, "Exception on socket read. " + excp.Message));
                }
            }
        }

        public void Send(byte[] data) {
            try {
                if (data != null && data.Length > 0) {
                    SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                    sendArgs.SetBuffer(data, 0, data.Length);
                    m_socket.SendAsync(sendArgs);
                }
            }
            catch (Exception excp) {
                if (SocketConnectionChange != null && !m_closeRequested) {
                    SocketConnectionChange(new SocketConnectionStatus(ServiceConnectionStatesEnum.Error, "Exception sending to " + m_serverEndPoint + ". " + excp.Message));
                }
            }
        }

        public void Close() {
            try {
                m_closeRequested = true;

                if (m_socket != null && m_isConnected) {
                    m_socket.Close();
                }
            }
            catch { }
        }
    }
}
