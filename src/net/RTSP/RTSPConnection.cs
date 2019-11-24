//-----------------------------------------------------------------------------
// Filename: RTSPConnection.cs
//
// Description: Encapsulates the information for a client RTSP connection.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Jan 2014	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class RTSPConnection
    {
        public static int MaxMessageSize = RTSPConstants.RTSP_MAXIMUM_LENGTH;
        private static string m_rtspEOL = RTSPConstants.CRLF;
        private static string m_rtspMessageDelimiter = RTSPConstants.CRLF + RTSPConstants.CRLF;

        private static ILogger logger = Log.Logger;

        public RTSPServer Server { get; private set; }
        public NetworkStream Stream { get; private set; }
        public IPEndPoint RemoteEndPoint { get; private set; }

        public DateTime LastTransmission;           // Records when a SIP packet was last sent or received.
        public byte[] SocketBuffer = new byte[2 * MaxMessageSize];
        public int SocketBufferEndPosition = 0;

        public event Action<RTSPConnection, IPEndPoint, byte[]> RTSPMessageReceived;
        public event Action<IPEndPoint> RTSPSocketDisconnected = (ep) => { };

        public RTSPConnection(RTSPServer server, NetworkStream stream, IPEndPoint remoteEndPoint)
        {
            Server = server;
            Stream = stream;
            RemoteEndPoint = remoteEndPoint;
        }

        public void Close()
        {
            try
            {
                if (Stream != null)
                {
                    Stream.Close();
                }
            }
            catch (Exception closeExcp)
            {
                logger.LogWarning("Exception closing socket in RTSPConnection Close. " + closeExcp.Message);
            }
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

                    // Attempt to extract an RTSP message from the receive buffer.
                    byte[] rtspMsgBuffer = ProcessReceive(SocketBuffer, 0, SocketBufferEndPosition, out bytesSkipped);

                    while (rtspMsgBuffer != null)
                    {
                        // An RTSP message is available.
                        if (RTSPMessageReceived != null)
                        {
                            LastTransmission = DateTime.Now;
                            RTSPMessageReceived(this, RemoteEndPoint, rtspMsgBuffer);
                        }

                        SocketBufferEndPosition -= (rtspMsgBuffer.Length + bytesSkipped);

                        if (SocketBufferEndPosition == 0)
                        {
                            break;
                        }
                        else
                        {
                            // Do a left shift on the receive array.
                            Array.Copy(SocketBuffer, rtspMsgBuffer.Length + bytesSkipped, SocketBuffer, 0, SocketBufferEndPosition);

                            // Try and extract another RTSP message from the receive buffer.
                            rtspMsgBuffer = ProcessReceive(SocketBuffer, 0, SocketBufferEndPosition, out bytesSkipped);
                        }
                    }

                    return true;
                }
                else
                {
                    Close();
                    RTSPSocketDisconnected(RemoteEndPoint);

                    return false;
                }
            }
            catch (ObjectDisposedException)
            {
                // Will occur if the owning channel closed the connection.
                RTSPSocketDisconnected(RemoteEndPoint);
                return false;
            }
            catch (SocketException)
            {
                // Will occur if the owning channel closed the connection.
                RTSPSocketDisconnected(RemoteEndPoint);
                return false;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RTSPConnection SocketReadCompleted. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Processes a buffer from a TCP read operation to extract the first full RTSP message. If no full RTSP 
        /// messages are available it returns null which indicates the next read should be appended to the current
        /// buffer and the process re-attempted.
        /// </summary>
        /// <param name="receiveBuffer">The buffer to check for the RTSP message in.</param>
        /// <param name="start">The position in the buffer to start parsing for an RTSP message.</param>
        /// <param name="length">The position in the buffer that indicates the end of the received bytes.</param>
        /// <returns>A byte array holding a full RTSP message or if no full RTSP messages are avialble null.</returns>
        public static byte[] ProcessReceive(byte[] receiveBuffer, int start, int length, out int bytesSkipped)
        {
            // NAT keep-alives can be interspersed between RTSP messages. Treat any non-letter character
            // at the start of a receive as a non RTSPtransmission and skip over it.
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
                int endMessageIndex = ByteBufferInfo.GetStringPosition(receiveBuffer, start, length, m_rtspMessageDelimiter, null);
                if (endMessageIndex != -1)
                {
                    int contentLength = GetContentLength(receiveBuffer, start, endMessageIndex);
                    int messageLength = endMessageIndex - start + m_rtspMessageDelimiter.Length + contentLength;

                    if (length - start >= messageLength)
                    {
                        byte[] rtspMsgBuffer = new byte[messageLength];
                        Buffer.BlockCopy(receiveBuffer, start, rtspMsgBuffer, 0, messageLength);
                        return rtspMsgBuffer;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Attempts to find the Content-Length header in an RTSP header and extract it.
        /// </summary>
        /// <param name="buffer">The buffer to search in.</param>
        /// <param name="start">The position in the buffer to start the serach from.</param>
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
                byte[] contentHeaderBytes = Encoding.UTF8.GetBytes(m_rtspEOL + RTSPHeaders.RTSP_HEADER_CONTENTLENGTH.ToUpper());

                int inContentHeaderPosn = 0;
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
    }
}
