//-----------------------------------------------------------------------------
// Filename: SIPStreamConnection.cs
//
// Description: Represents an established socket connection on a connection oriented SIP 
// TCP or TLS.
//
// Author(s):
// Aaron Clauson
//
// History:
// 31 Mar 2009	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
// 25 Oct 2019  Aaron Clauson   Renamed from SIPConnection to SIPStreamConnection as part of major TCP and TLS
//                              channel refactor. Moved message parsing logic to SIPMessage class.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace SIPSorcery.SIP
{
    public enum SIPConnectionsEnum
    {
        Listener = 1,   // Indicates the connection was initiated by the remote client to a local server socket.
        Caller = 2,     // Indicated the connection was initiated locally to a remote server socket.
    }

    /// <summary>
    /// Represents a reliable stream connection (e.g. TCP or TLS) between two end points. Stream connections have a lot more
    /// overhead than UDP. The state of the connection has to be monitored and messages on the stream can be spread across
    /// multiple packets.
    /// </summary>
    public class SIPStreamConnection
    {
        public static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;

        // The underlying TCP socket for the stream connection. To take adavantage of newer async TCP IO operations the
        // RecvSocketArgs is used for TCP channel receives. 
        public Socket StreamSocket;
        public SocketAsyncEventArgs RecvSocketArgs;

        // For secure streams the TCP connection will be upgraded to an SSL stream and the SslStreamBuffer will
        // be used for receives.
        public SslStream SslStream;
        public byte[] SslStreamBuffer;

        public IPEndPoint RemoteEndPoint;           // The remote end point for the stream.
        public SIPProtocolsEnum ConnectionProtocol; // The stream protocol.
        public SIPConnectionsEnum ConnectionType;   // Indicates whether the connection was initiated by us or the remote party.
        public DateTime LastTransmission;           // Records when a SIP packet was last sent or received.

        public int RecvStartPosn = 0;               // The current start position of unprocessed data in the recceive buffer.
        public int RecvEndPosn = 0;                 // The current end position of unprocessed data in the recceive buffer.
        
        public event SIPMessageReceivedDelegate SIPMessageReceived; // Event for new SIP requests or responses becoming available.

        public SIPStreamConnection(Socket streamSocket, IPEndPoint remoteEndPoint, SIPProtocolsEnum connectionProtocol, SIPConnectionsEnum connectionType)
        {
            StreamSocket = streamSocket;
            LastTransmission = DateTime.Now;
            RemoteEndPoint = remoteEndPoint;
            ConnectionProtocol = connectionProtocol;
            ConnectionType = connectionType;

            if (ConnectionProtocol == SIPProtocolsEnum.tcp)
            {
                RecvSocketArgs = new SocketAsyncEventArgs();
                RecvSocketArgs.SetBuffer(new byte[2 * MaxSIPTCPMessageSize], 0, 2 * MaxSIPTCPMessageSize);
            }
        }

        /// <summary>
        /// Attempts to extract SIP messages from the data that has been received on the SIP stream connection.
        /// </summary>
        /// <param name="recvChannel">The receiving SIP channel.</param>
        /// <param name="buffer">The buffer holding the current data from the stream. Note that the buffer can 
        /// stretch over multiple receives.</param>
        /// <param name="bytesRead">The bytes that were read by the latest receive operation (the new bytes available).</param>
        public void ExtractSIPMessages(SIPChannel recvChannel, byte[] buffer, int bytesRead)
        {
            RecvEndPosn += bytesRead;

            int bytesSkipped = 0;
            byte[] sipMsgBuffer = SIPMessage.ParseSIPMessageFromStream(buffer, RecvStartPosn, RecvEndPosn, out bytesSkipped);

            while (sipMsgBuffer != null)
            {
                // A SIP message is available.
                if (SIPMessageReceived != null)
                {
                    LastTransmission = DateTime.Now;
                    SIPMessageReceived(recvChannel, new SIPEndPoint(ConnectionProtocol, RemoteEndPoint), sipMsgBuffer);
                }

                RecvStartPosn += (sipMsgBuffer.Length + bytesSkipped);

                if (RecvStartPosn == RecvEndPosn)
                {
                    // All data has been successfully extracted from the receive buffer.
                    RecvStartPosn = RecvEndPosn = 0;
                    break;
                }
                else
                {
                    // Try and extract another SIP message from the receive buffer.
                    sipMsgBuffer = SIPMessage.ParseSIPMessageFromStream(buffer, RecvStartPosn, RecvEndPosn, out bytesSkipped);
                }
            }
        }
    }
}
