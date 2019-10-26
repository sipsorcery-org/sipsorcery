//-----------------------------------------------------------------------------
// Filename: SIPStreamConnection.cs
//
// Description: Represents an established socket connection on a connection oriented SIP 
// TCP or TLS.
//
// History:
// 31 Mar 2009	Aaron Clauson	Created.
// 25 Oct 2019  Aaron Clauson   Renamed from SIPConnection to SIPStreamConnection as part of major TCP and TLS
//                              channel refactor. Moved message parsing logic to SIPMessage class.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
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
