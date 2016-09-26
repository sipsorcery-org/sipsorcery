//-----------------------------------------------------------------------------
// Filename: SIPConnection.cs
//
// Description: Represents an established socket connection on a connection oriented SIP 
// TCL or TLS.
//
// History:
// 31 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    public delegate void SIPConnectionDisconnectedDelegate(IPEndPoint remoteEndPoint);

    public enum SIPConnectionsEnum
    {
        Listener = 1,   // Indicates the connection was initiated by the remote client to a local server socket.
        Caller = 2,     // Indicated the connection was initiated locally to a remote server socket.
    }

    public class SIPConnection
    {
        private static ILog logger = AssemblyState.logger;

        public static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;
        private static string m_sipEOL = SIPConstants.CRLF;
        private static string m_sipMessageDelimiter = SIPConstants.CRLF + SIPConstants.CRLF;

        public Stream SIPStream;
        //public Socket SIPSocket;
        public IPEndPoint RemoteEndPoint;
        public SIPProtocolsEnum ConnectionProtocol;
        public SIPConnectionsEnum ConnectionType;
        public DateTime LastTransmission;           // Records when a SIP packet was last sent or received.
        public byte[] SocketBuffer = new byte[2 * MaxSIPTCPMessageSize];
        public int SocketBufferEndPosition = 0;

        private SIPChannel m_owningChannel;
        private TcpClient _tcpClient;

        public event SIPMessageReceivedDelegate SIPMessageReceived;
        public event SIPConnectionDisconnectedDelegate SIPSocketDisconnected = (ep) => { };

        //public SIPConnection(SIPChannel channel, Socket sipSocket, IPEndPoint remoteEndPoint, SIPProtocolsEnum connectionProtocol, SIPConnectionsEnum connectionType)
        //{
        //    LastTransmission = DateTime.Now;
        //    m_owningChannel = channel;
        //    SIPSocket = sipSocket;
        //    RemoteEndPoint = remoteEndPoint;
        //    ConnectionProtocol = connectionProtocol;
        //    ConnectionType = connectionType;
        //}

        public SIPConnection(SIPChannel channel, TcpClient tcpClient, Stream sipStream, IPEndPoint remoteEndPoint, SIPProtocolsEnum connectionProtocol, SIPConnectionsEnum connectionType)
        {
            LastTransmission = DateTime.Now;
            m_owningChannel = channel;
            _tcpClient = tcpClient;
            SIPStream = sipStream;
            RemoteEndPoint = remoteEndPoint;
            ConnectionProtocol = connectionProtocol;
            ConnectionType = connectionType;
        }

        /// <summary>
        /// Processes the receive buffer after a read from the connected socket.
        /// </summary>
        /// <param name="bytesRead">The number of bytes that were read into the receive buffer.</param>
        /// <returns>True if the receive was processed correctly, false if the socket returned 0 bytes or was disconnected.</returns>
        public bool SocketReadCompleted(int bytesRead)
        {
            try
            {
                if (bytesRead > 0)
                {
                    SocketBufferEndPosition += bytesRead;
                    int bytesSkipped = 0;

                    // Attempt to extract a SIP message from the receive buffer.
                    byte[] sipMsgBuffer = SIPConnection.ProcessReceive(SocketBuffer, 0, SocketBufferEndPosition, out bytesSkipped);

                    while (sipMsgBuffer != null)
                    {
                        // A SIP message is available.
                        if (SIPMessageReceived != null)
                        {
                            LastTransmission = DateTime.Now;
                            SIPMessageReceived(m_owningChannel, new SIPEndPoint(SIPProtocolsEnum.tcp, RemoteEndPoint), sipMsgBuffer);
                        }

                        SocketBufferEndPosition -= (sipMsgBuffer.Length + bytesSkipped);

                        if (SocketBufferEndPosition == 0)
                        {
                            //Array.Clear(SocketBuffer, 0, SocketBuffer.Length);
                            break;
                        }
                        else
                        {
                            // Do a left shift on the receive array.
                            Array.Copy(SocketBuffer, sipMsgBuffer.Length + bytesSkipped, SocketBuffer, 0, SocketBufferEndPosition);
                            //Array.Clear(SocketBuffer, SocketBufferEndPosition, SocketBuffer.Length - SocketBufferEndPosition);

                            // Try and extract another SIP message from the receive buffer.
                            sipMsgBuffer = SIPConnection.ProcessReceive(SocketBuffer, 0, SocketBufferEndPosition, out bytesSkipped);
                        }
                    }

                    return true;
                }
                else
                {
                    //logger.Debug("SIP " + ConnectionProtocol + " socket to " + RemoteEndPoint + " was disconnected, closing.");
                    //SIPStream.Close();
                    Close();
                    SIPSocketDisconnected(RemoteEndPoint);

                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                // Will occur if the owning channel closed the connection.
                SIPSocketDisconnected(RemoteEndPoint);
                return false;
            }
            catch (SocketException)
            {
                // Will occur if the owning channel closed the connection.
                SIPSocketDisconnected(RemoteEndPoint);
                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPConnection SocketReadCompleted. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Processes a buffer from a TCP read operation to extract the first full SIP message. If no full SIP 
        /// messages are available it returns null which indicates the next read should be appended to the current
        /// buffer and the process re-attempted.
        /// </summary>
        /// <param name="receiveBuffer">The buffer to check for the SIP message in.</param>
        /// <param name="start">The position in the buffer to start parsing for a SIP message.</param>
        /// <param name="length">The position in the buffer that indicates the end of the received bytes.</param>
        /// <returns>A byte array holding a full SIP message or if no full SIP messages are avialble null.</returns>
        public static byte[] ProcessReceive(byte[] receiveBuffer, int start, int length, out int bytesSkipped)
        {
            // NAT keep-alives can be interspersed between SIP messages. Treat any non-letter character
            // at the start of a receive as a non SIP transmission and skip over it.
            bytesSkipped = 0;
            bool letterCharFound = false;
            while (!letterCharFound && start < length)
            {
                if ((int)receiveBuffer[start] >= 65)
                {
                    break;
                }
                else
                {
                    start++;
                    bytesSkipped++;
                }
            }

            if (start < length)
            {
                int endMessageIndex = ByteBufferInfo.GetStringPosition(receiveBuffer, start, length, m_sipMessageDelimiter, null);
                if (endMessageIndex != -1)
                {
                    int contentLength = GetContentLength(receiveBuffer, start, endMessageIndex);
                    int messageLength = endMessageIndex - start + m_sipMessageDelimiter.Length + contentLength;

                    if (length - start >= messageLength)
                    {
                        byte[] sipMsgBuffer = new byte[messageLength];
                        Buffer.BlockCopy(receiveBuffer, start, sipMsgBuffer, 0, messageLength);
                        return sipMsgBuffer;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to find the Content-Length header is a SIP header and extract it.
        /// </summary>
        /// <param name="buffer">The buffer to search in.</param>
        /// <param name="start">The position in the buffer to start the search from.</param>
        /// <param name="end">The position in the buffer to stop the search at.</param>
        /// <returns></returns>
        public static int GetContentLength(byte[] buffer, int start, int end)
        {
            if (buffer == null || start > end || buffer.Length < end)
            {
                return 0;
            }
            else
            {
                byte[] contentHeaderBytes = Encoding.UTF8.GetBytes(m_sipEOL + SIPHeaders.SIP_HEADER_CONTENTLENGTH.ToUpper());
                byte[] compactContentHeaderBytes = Encoding.UTF8.GetBytes(m_sipEOL + SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH.ToUpper());

                int inContentHeaderPosn = 0;
                int inCompactContentHeaderPosn = 0;
                bool possibleHeaderFound = false;
                int contentLengthValueStartPosn = 0;

                for (int index = start; index < end; index++)
                {
                    if (possibleHeaderFound)
                    {
                        // A possilbe match has been found for the Content-Length header. The next characters can only be whitespace or colon.
                        if (buffer[index] == ':')
                        {
                            // The Content-Length header has been found.
                            contentLengthValueStartPosn = index + 1;
                            break;
                        }
                        else if (buffer[index] == ' ' || buffer[index] == '\t')
                        {
                            // Skip any whitespace between the header and the colon.
                            continue;
                        }
                        else
                        {
                            // Additional characters indicate this is not the Content-Length header.
                            possibleHeaderFound = false;
                            inContentHeaderPosn = 0;
                            inCompactContentHeaderPosn = 0;
                        }
                    }

                    if (buffer[index] == contentHeaderBytes[inContentHeaderPosn] || buffer[index] == contentHeaderBytes[inContentHeaderPosn] + 32)
                    {
                        inContentHeaderPosn++;

                        if (inContentHeaderPosn == contentHeaderBytes.Length)
                        {
                            possibleHeaderFound = true;
                        }
                    }
                    else
                    {
                        inContentHeaderPosn = 0;
                    }

                    if (buffer[index] == compactContentHeaderBytes[inCompactContentHeaderPosn] || buffer[index] == compactContentHeaderBytes[inCompactContentHeaderPosn] + 32)
                    {
                        inCompactContentHeaderPosn++;

                        if (inCompactContentHeaderPosn == compactContentHeaderBytes.Length)
                        {
                            possibleHeaderFound = true;
                        }
                    }
                    else
                    {
                        inCompactContentHeaderPosn = 0;
                    }
                }

                if (contentLengthValueStartPosn != 0)
                {
                    // The Content-Length header has been found, this block extracts the value of the header.
                    string contentLengthValue = null;

                    for (int index = contentLengthValueStartPosn; index < end; index++)
                    {
                        if (contentLengthValue == null && (buffer[index] == ' ' || buffer[index] == '\t'))
                        {
                            // Skip any whitespace at the start of the header value.
                            continue;
                        }
                        else if (buffer[index] >= '0' && buffer[index] <= '9')
                        {
                            contentLengthValue += ((char)buffer[index]).ToString();
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (!contentLengthValue.IsNullOrBlank())
                    {
                        return Convert.ToInt32(contentLengthValue);
                    }
                }

                return 0;
            }
        }

        public void Close()
        {
            try
            {
                if (_tcpClient.GetStream() != null)
                {
                    _tcpClient.GetStream().Close(0);
                }

                if (_tcpClient.Client != null && _tcpClient.Client.Connected == true)
                {
                    _tcpClient.Client.Shutdown(SocketShutdown.Both);
                    _tcpClient.Client.Close(0);
                }

                _tcpClient.Close();
            }
            catch (Exception closeExcp)
            {
                logger.Warn("Exception closing socket in SIPConnection Close. " + closeExcp.Message);
            }
        }
    }
}
