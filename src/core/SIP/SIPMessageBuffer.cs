//-----------------------------------------------------------------------------
// Filename: SIPMessageBuffer.cs
//
// Description: Functionality to determine whether a SIP message is a request or
// a response and break a message up into its constituent parts.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 May 2006	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// Represents an incoming message before having determined whether it is a request or a response.
    /// </summary>
    public class SIPMessageBuffer
    {
        private const string SIP_RESPONSE_PREFIX = "SIP";
        private const string SIP_MESSAGE_IDENTIFIER = "SIP";    // String that must be in a message buffer to be recognised as a SIP message and processed.

        private static int m_sipFullVersionStrLen = SIPConstants.SIP_FULLVERSION_STRING.Length;
        private static int m_minFirstLineLength = 7;
        private static string m_CRLF = SIPConstants.CRLF;
        private static string m_sipMessageDelimiter = SIPConstants.CRLF + SIPConstants.CRLF;    // The delimiting character sequence for messages in a stream.

        private static ILogger logger = Log.Logger;

        public string RawMessage
        {
            get
            {
                if (RawBuffer != null)
                {
                    return Encoding.UTF8.GetString(RawBuffer);
                }
                else
                {
                    return null;
                }
            }
        }

        public SIPMessageTypesEnum SIPMessageType = SIPMessageTypesEnum.Unknown;
        public string FirstLine;
        public string[] SIPHeaders;
        public byte[] Body;
        public byte[] RawBuffer;

        public DateTime Created = DateTime.Now;
        public SIPEndPoint RemoteSIPEndPoint;               // The remote IP socket the message was received from or sent to.
        public SIPEndPoint LocalSIPEndPoint;                // The local SIP socket the message was received on or sent from.

        /// <summary>
        /// Attempts to parse a SIP message from a single buffer that can only contain a single message.
        /// </summary>
        /// <param name="buffer">The buffer that will be parsed for a SIP message.</param>
        /// <param name="localSIPEndPoint">The end point the message was received on.</param>
        /// <param name="remoteSIPEndPoint">The end point the message was received from.</param>
        /// <returns>If successful a SIP message or null if not.</returns>
        public static SIPMessageBuffer ParseSIPMessage(byte[] buffer, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint)
        {

            if (buffer == null || buffer.Length < m_minFirstLineLength)
            {
                // Ignore.
                return null;
            }
            else if (buffer.Length > SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH)
            {
                throw new ApplicationException("SIP message received that exceeded the maximum allowed message length, ignoring.");
            }
            else if (!BufferUtils.HasString(buffer, 0, buffer.Length, SIP_MESSAGE_IDENTIFIER, m_CRLF))
            {
                // Message does not contain "SIP" anywhere on the first line, ignore.
                return null;
            }
            else
            {
                var sipMessage = new SIPMessageBuffer();

                sipMessage.RawBuffer = buffer;
                sipMessage.LocalSIPEndPoint = localSIPEndPoint;
                sipMessage.RemoteSIPEndPoint = remoteSIPEndPoint;

                // For connection oriented transports the same connection should be used for responses and subsequent requests.
                if (sipMessage.LocalSIPEndPoint != null && remoteSIPEndPoint.ConnectionID != null)
                {
                    sipMessage.LocalSIPEndPoint.ConnectionID = remoteSIPEndPoint.ConnectionID;
                }

                string message = Encoding.UTF8.GetString(buffer);
                int endFistLinePosn = message.IndexOf(m_CRLF);

                if (endFistLinePosn != -1)
                {
                    sipMessage.FirstLine = message.Substring(0, endFistLinePosn);

                    if (sipMessage.FirstLine.Substring(0, 3) == SIP_RESPONSE_PREFIX)
                    {
                        sipMessage.SIPMessageType = SIPMessageTypesEnum.Response;
                    }
                    else
                    {
                        sipMessage.SIPMessageType = SIPMessageTypesEnum.Request;
                    }

                    int endHeaderPosn = message.IndexOf(m_CRLF + m_CRLF);
                    if (endHeaderPosn == -1)
                    {
                        // Assume flakey implementation if message does not contain the required CRLFCRLF sequence and treat the message as having no body.
                        string headerString = message.Substring(endFistLinePosn + 2, message.Length - endFistLinePosn - 2);
                        sipMessage.SIPHeaders = SIPHeader.SplitHeaders(headerString);
                    }
                    else
                    {
                        string headerString = message.Substring(endFistLinePosn + 2, endHeaderPosn - endFistLinePosn - 2);
                        sipMessage.SIPHeaders = SIPHeader.SplitHeaders(headerString);

                        if (message.Length > endHeaderPosn + 4)
                        {
                            sipMessage.Body = new byte[buffer.Length - (endHeaderPosn + 4)];
                            Buffer.BlockCopy(buffer, endHeaderPosn + 4, sipMessage.Body, 0, buffer.Length - (endHeaderPosn + 4));
                        }
                    }

                    return sipMessage;
                }
                else
                {
                    logger.LogWarning("Error ParseSIPMessage, there were no end of line characters in the string being parsed.");
                    return null;
                }
            }
        }

        /// <summary>
        /// Attempts to parse a SIP message from a string containing a single SIP request or response.
        /// </summary>
        /// <param name="message">The string to parse.</param>
        /// <param name="localSIPEndPoint">The end point the message was received on.</param>
        /// <param name="remoteSIPEndPoint">The end point the message was received from.</param>
        /// <returns>If successful a SIP message or null if not.</returns>
        public static SIPMessageBuffer ParseSIPMessage(string message, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint)
        {
            return ParseSIPMessage(Encoding.UTF8.GetBytes(message), localSIPEndPoint, remoteSIPEndPoint);
        }

        //rj2: check if message could be "well"known Ping message
        public static bool IsPing(byte[] buffer)
        {
            if (buffer != null)
            {
                int bufLen = buffer.Length;
                if (bufLen == 2 && buffer[0] == '\r' && buffer[1] == '\n')
                {
                    //only cr/lf for ping, return NULL and no error msg
                    return true;
                }
                if (bufLen == 4 && buffer[0] == '\r' && buffer[1] == '\n' && buffer[2] == '\r' && buffer[3] == '\n')
                {
                    //only cr/lf for ping, return NULL and no error msg
                    return true;
                }
                if (bufLen == 4 && buffer[0] == 'j' && buffer[1] == 'a' && buffer[2] == 'K' && buffer[3] == '\0')
                {
                    // linphones keep alive message sucks, ping w/o error msg 
                    return true;
                }
                if (bufLen == 3 && buffer[0] == 'p' && buffer[1] == 'n' && buffer[2] == 'g')
                {
                    //only cr/lf for ping, return NULL and no error msg
                    return true;
                }
                if (bufLen == 4 && buffer[0] == '\0' && buffer[1] == '\0' && buffer[2] == '\0' && buffer[3] == '\0')
                {
                    //4x byte 0 used as ping
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Processes a buffer from a TCP read operation to extract the first full SIP message. If no full SIP 
        /// messages are available it returns null which indicates the next read should be appended to the current
        /// buffer and the process re-attempted.
        /// </summary>
        /// <param name="receiveBuffer">The buffer to check for the SIP message in.</param>
        /// <param name="start">The position in the buffer to start parsing for a SIP message.</param>
        /// <param name="length">The position in the buffer that indicates the end of the received bytes.</param>
        /// <returns>A byte array holding a full SIP message or if no full SIP messages are available null.</returns>
        public static byte[] ParseSIPMessageFromStream(byte[] receiveBuffer, int start, int length, out int bytesSkipped)
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
                int endMessageIndex = BufferUtils.GetStringPosition(receiveBuffer, start, length, m_sipMessageDelimiter, null);
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
        /// <returns>If the Content-Length header is found the value if contains otherwise 0.</returns>
        public static int GetContentLength(byte[] buffer, int start, int end)
        {
            if (buffer == null || start > end || buffer.Length < end)
            {
                return 0;
            }
            else
            {
                byte[] contentHeaderBytes = Encoding.UTF8.GetBytes(m_CRLF + SIPSorcery.SIP.SIPHeaders.SIP_HEADER_CONTENTLENGTH.ToUpper());
                byte[] compactContentHeaderBytes = Encoding.UTF8.GetBytes(m_CRLF + SIPSorcery.SIP.SIPHeaders.SIP_COMPACTHEADER_CONTENTLENGTH.ToUpper());

                int inContentHeaderPosn = 0;
                int inCompactContentHeaderPosn = 0;
                bool possibleHeaderFound = false;
                int contentLengthValueStartPosn = 0;

                for (int index = start; index < end; index++)
                {
                    if (possibleHeaderFound)
                    {
                        // A possible match has been found for the Content-Length header. The next characters can only be whitespace or colon.
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
    }
}
